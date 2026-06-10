using CLASS_Blazor.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace CLASS_Blazor.Services;

public sealed class TutorRequestService(
    HttpClient httpClient,
    ILogger<TutorRequestService> logger,
    IMemoryCache cache)
{
    private const string RequestApiUrl = "request";
    private const string SubjectApiUrl = "subject";
    private const string RequestsCacheKey = "class:requests";
    private const string SubjectsCacheKey = "class:subjects";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RequestsCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SubjectCacheDuration = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<TutorRequestRecord>> GetRequestsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(RequestsCacheKey, out IReadOnlyList<TutorRequestRecord>? cachedRequests) && cachedRequests is not null)
        {
            return cachedRequests;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var json = await httpClient.GetStringAsync(RequestApiUrl, timeoutCts.Token);
            var subjects = await GetSubjectsAsync(timeoutCts.Token);
            var subjectsById = subjects.ToDictionary(subject => subject.Suid);

            var requests = ParseRequests(json, subjectsById);
            cache.Set(RequestsCacheKey, requests, RequestsCacheDuration);
            return requests;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor requests could not be loaded from {RequestApiUrl}.", RequestApiUrl);
            return [];
        }
    }

    public async Task<TutorRequestResult> SendRequestAsync(
        UserProfile requester,
        TutorOffer tutor,
        string authToken,
        string selectedSubject = "",
        CancellationToken cancellationToken = default)
    {
        if (requester.Uid <= 0)
        {
            return TutorRequestResult.Failed("Du musst eingeloggt sein, um eine Anfrage zu senden.");
        }

        if (tutor.OffererUserId <= 0)
        {
            return TutorRequestResult.Failed("Das Tutor-Angebot konnte keinem User zugeordnet werden.");
        }

        if (requester.Uid == tutor.OffererUserId)
        {
            return TutorRequestResult.Failed("Du kannst keine Anfrage an dein eigenes Angebot senden.");
        }

        if (string.IsNullOrWhiteSpace(authToken))
        {
            return TutorRequestResult.Failed("Bitte logge dich erneut ein, um eine Anfrage zu senden.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var subjects = await GetSubjectsAsync(timeoutCts.Token);
            var normalizedSubjects = NormalizeSubjects(tutor.Subjects);
            var subject = GetSelectedRequestSubject(selectedSubject, normalizedSubjects);
            var subjectIds = GetSubjectIds([subject], subjects);

            if (subjectIds.Length == 0)
            {
                return TutorRequestResult.Failed("Für dieses Angebot konnte kein bekanntes Fach gefunden werden.");
            }

            var payload = new
            {
                requester_uid = requester.Uid,
                max_price = Math.Max(0, tutor.PricePerHour),
                until = FormatApiDateTime(DateTime.UtcNow.AddMonths(1)),
                description = $"Anfrage an Angebot #{tutor.OfferId} von {tutor.Name}: {requester.FullName} fragt {subject} an.",
                subjects = subjectIds
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Post, RequestApiUrl, authToken);
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TutorRequestResult.Failed("Bitte logge dich erneut ein, um eine Anfrage zu senden.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TutorRequestResult.Failed("Du kannst diese Anfrage nicht mit diesem Login senden.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return TutorRequestResult.Failed("Die Anfrage konnte nicht gespeichert werden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var apiError = await TryReadApiErrorAsync(response, timeoutCts.Token);
                return TutorRequestResult.Failed(string.IsNullOrWhiteSpace(apiError)
                    ? $"Die Anfrage konnte nicht gesendet werden ({(int)response.StatusCode})."
                    : $"Die Anfrage konnte nicht gesendet werden ({(int)response.StatusCode}): {apiError}");
            }

            cache.Remove(RequestsCacheKey);
            cache.Remove(RequestsCacheKey);
            return TutorRequestResult.Sent();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tutor request creation timed out at {RequestApiUrl}.", RequestApiUrl);
            return TutorRequestResult.Failed("Das Senden dauert zu lange. Bitte prüfe deine Verbindung und versuche es erneut.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor request could not be sent to {RequestApiUrl}.", RequestApiUrl);
            return TutorRequestResult.Failed($"Die Anfrage konnte nicht gesendet werden: {exception.Message}");
        }
    }

    public async Task<TutorRequestResult> UpdateRequestStatusAsync(
        int requestId,
        string status,
        string authToken,
        string currentDescription = "",
        string selectedSubject = "",
        CancellationToken cancellationToken = default)
    {
        if (requestId <= 0)
        {
            return TutorRequestResult.Failed("Die Anfrage konnte keiner API-ID zugeordnet werden.");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            return TutorRequestResult.Failed("Der neue Anfrage-Status fehlt.");
        }

        if (string.IsNullOrWhiteSpace(authToken))
        {
            return TutorRequestResult.Failed("Bitte logge dich erneut ein, um die Anfrage zu aktualisieren.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var payload = new
            {
                status
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"{RequestApiUrl}/{requestId}", authToken);
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TutorRequestResult.Failed("Bitte logge dich erneut ein, um die Anfrage zu aktualisieren.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TutorRequestResult.Failed("Du kannst diese Anfrage nicht mit diesem Login aktualisieren.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return TutorRequestResult.Failed("Die Anfrage wurde nicht gefunden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var apiError = await TryReadApiErrorAsync(response, timeoutCts.Token);

                if (response.StatusCode == HttpStatusCode.BadRequest
                    && apiError.Contains("No fields to update", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsCancelledStatus(status))
                    {
                        return await ExpireRequestAsync(requestId, authToken, cancellationToken);
                    }

                    if (IsDecisionStatus(status))
                    {
                        return await UpdateRequestDescriptionStatusAsync(requestId, status, currentDescription, selectedSubject, authToken, cancellationToken);
                    }
                }

                return TutorRequestResult.Failed(string.IsNullOrWhiteSpace(apiError)
                    ? $"Die Anfrage konnte nicht aktualisiert werden ({(int)response.StatusCode})."
                    : $"Die Anfrage konnte nicht aktualisiert werden ({(int)response.StatusCode}): {apiError}");
            }

            cache.Remove(RequestsCacheKey);
            cache.Remove(RequestsCacheKey);
            return TutorRequestResult.Sent();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tutor request {RequestId} update timed out at {RequestApiUrl}.", requestId, RequestApiUrl);
            return TutorRequestResult.Failed("Das Aktualisieren dauert zu lange. Bitte prÃ¼fe deine Verbindung und versuche es erneut.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor request {RequestId} could not be updated at {RequestApiUrl}.", requestId, RequestApiUrl);
            return TutorRequestResult.Failed($"Die Anfrage konnte nicht aktualisiert werden: {exception.Message}");
        }
    }

    private async Task<TutorRequestResult> ExpireRequestAsync(
        int requestId,
        string authToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var payload = new
            {
                until = FormatApiDateTime(DateTime.UtcNow.AddDays(-1))
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"{RequestApiUrl}/{requestId}", authToken);
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TutorRequestResult.Failed("Bitte logge dich erneut ein, um die Anfrage zurückzuziehen.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TutorRequestResult.Failed("Du kannst diese Anfrage nicht mit diesem Login zurückziehen.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return TutorRequestResult.Failed("Die Anfrage wurde nicht gefunden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var apiError = await TryReadApiErrorAsync(response, timeoutCts.Token);
                return TutorRequestResult.Failed(string.IsNullOrWhiteSpace(apiError)
                    ? $"Die Anfrage konnte nicht zurückgezogen werden ({(int)response.StatusCode})."
                    : $"Die Anfrage konnte nicht zurückgezogen werden ({(int)response.StatusCode}): {apiError}");
            }

            return TutorRequestResult.Sent();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tutor request {RequestId} expiration timed out at {RequestApiUrl}.", requestId, RequestApiUrl);
            return TutorRequestResult.Failed("Das Zurückziehen dauert zu lange. Bitte prüfe deine Verbindung und versuche es erneut.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor request {RequestId} could not be expired at {RequestApiUrl}.", requestId, RequestApiUrl);
            return TutorRequestResult.Failed($"Die Anfrage konnte nicht zurückgezogen werden: {exception.Message}");
        }
    }

    private async Task<TutorRequestResult> UpdateRequestDescriptionStatusAsync(
        int requestId,
        string status,
        string currentDescription,
        string selectedSubject,
        string authToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var payload = new
            {
                description = ApplyStatusMarker(currentDescription, status, selectedSubject)
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"{RequestApiUrl}/{requestId}", authToken);
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TutorRequestResult.Failed("Bitte logge dich erneut ein, um die Anfrage zu aktualisieren.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TutorRequestResult.Failed("Du kannst diese Anfrage nicht mit diesem Login aktualisieren.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return TutorRequestResult.Failed("Die Anfrage wurde nicht gefunden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var apiError = await TryReadApiErrorAsync(response, timeoutCts.Token);
                return TutorRequestResult.Failed(string.IsNullOrWhiteSpace(apiError)
                    ? $"Die Anfrage konnte nicht aktualisiert werden ({(int)response.StatusCode})."
                    : $"Die Anfrage konnte nicht aktualisiert werden ({(int)response.StatusCode}): {apiError}");
            }

            return TutorRequestResult.Sent();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tutor request {RequestId} description status update timed out at {RequestApiUrl}.", requestId, RequestApiUrl);
            return TutorRequestResult.Failed("Das Aktualisieren dauert zu lange. Bitte prüfe deine Verbindung und versuche es erneut.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor request {RequestId} description status could not be updated at {RequestApiUrl}.", requestId, RequestApiUrl);
            return TutorRequestResult.Failed($"Die Anfrage konnte nicht aktualisiert werden: {exception.Message}");
        }
    }

    private static bool IsCancelledStatus(string status)
    {
        return status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("withdrawn", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDecisionStatus(string status)
    {
        return status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            || status.Equals("declined", StringComparison.OrdinalIgnoreCase)
            || status.Equals("rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyStatusMarker(string description, string status, string selectedSubject)
    {
        const string markerPrefix = "[CLASS_REQUEST_STATUS:";
        const string subjectMarkerPrefix = "[CLASS_REQUEST_SUBJECT:";
        var cleanedDescription = (description ?? string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith(markerPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith(subjectMarkerPrefix, StringComparison.OrdinalIgnoreCase))
            .DefaultIfEmpty("Anfrage")
            .Aggregate((current, line) => $"{current}\n{line}");

        var statusMarker = $"{markerPrefix}{NormalizeStatusMarker(status)}]";

        if (status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(selectedSubject))
        {
            return $"{cleanedDescription}\n{statusMarker}\n{subjectMarkerPrefix}{selectedSubject.Trim()}]";
        }

        return $"{cleanedDescription}\n{statusMarker}";
    }

    private static string NormalizeStatusMarker(string status)
    {
        return status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            ? "accepted"
            : "declined";
    }

    private static IReadOnlyList<TutorRequestRecord> ParseRequests(
        string json,
        IReadOnlyDictionary<int, SubjectDto> subjectsById)
    {
        using var document = JsonDocument.Parse(json);
        var requestElements = GetArrayElements(document.RootElement);
        var requests = new List<TutorRequestRecord>();

        foreach (var element in requestElements)
        {
            requests.Add(new TutorRequestRecord(
                RequestId: GetInt(element, "rid", "request_id", "id"),
                RequesterUserId: GetInt(element, "requester_uid", "requesterUserId", "requester_id"),
                OffererUserId: GetInt(element, "offerer_uid", "offererUserId", "tutor_uid", "receiver_uid"),
                OfferId: GetInt(element, "offer_id", "oid"),
                RequesterName: GetString(element, "requester_name", "requesterName", "name"),
                RequesterEmail: GetString(element, "requester_email", "requesterEmail", "email"),
                Subject: GetSubjectText(element, subjectsById),
                MaxPrice: GetInt(element, "max_price", "price_per_hour", "price"),
                Until: GetDate(element, "until", "available_until"),
                Description: GetString(element, "description", "message"),
                CreatedAt: GetDate(element, "created_at", "createdAt", "requested_on", "requestedOn") ?? DateTimeOffset.Now,
                Status: GetRequestStatus(element)));
        }

        return requests;
    }

    private async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(SubjectsCacheKey, out IReadOnlyList<SubjectDto>? cachedSubjects) && cachedSubjects is not null)
        {
            return cachedSubjects;
        }

        try
        {
            var subjectsJson = await httpClient.GetStringAsync(SubjectApiUrl, cancellationToken);
            var subjects = ParseSubjects(subjectsJson);
            cache.Set(SubjectsCacheKey, subjects, SubjectCacheDuration);
            return subjects;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Subjects could not be loaded from {SubjectApiUrl}.", SubjectApiUrl);
            return [];
        }
    }

    private static List<SubjectDto> ParseSubjects(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<SubjectDto>>(json, SerializerOptions) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<SubjectDto>>(valueElement.GetRawText(), SerializerOptions) ?? [];
        }

        return [];
    }

    private static IReadOnlyList<JsonElement> GetArrayElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return valueElement.EnumerateArray().ToList();
        }

        return [];
    }

    private static string GetSubjectText(JsonElement element, IReadOnlyDictionary<int, SubjectDto> subjectsById)
    {
        var subject = GetString(element, "subject", "subject_name", "subjectName");

        if (!string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        var subjectIds = GetString(element, "subject_ids", "subjectIds");
        var subjects = GetSubjects(subjectIds, subjectsById);

        return subjects.Length == 0 ? string.Empty : string.Join(", ", subjects);
    }

    private static string GetRequestStatus(JsonElement element)
    {
        var status = GetString(element, "status", "state", "request_status", "requestStatus");

        if (!string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        var description = GetString(element, "description", "message");
        const string markerPrefix = "[CLASS_REQUEST_STATUS:";
        var markerIndex = description.IndexOf(markerPrefix, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var statusStart = markerIndex + markerPrefix.Length;
        var statusEnd = description.IndexOf(']', statusStart);

        return statusEnd > statusStart
            ? description[statusStart..statusEnd].Trim()
            : string.Empty;
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property.ValueKind == JsonValueKind.String
                    ? property.GetString()?.Trim() ?? string.Empty
                    : property.ToString();
            }
        }

        return string.Empty;
    }

    private static int GetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static DateTimeOffset? GetDate(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static async Task<string> TryReadApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString() ?? string.Empty;
            }

            return json;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string[] NormalizeSubjects(IEnumerable<string> subjects)
    {
        return subjects
            .Where(subject => !string.IsNullOrWhiteSpace(subject))
            .Select(subject => subject.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetSelectedRequestSubject(string selectedSubject, IReadOnlyList<string> availableSubjects)
    {
        if (!string.IsNullOrWhiteSpace(selectedSubject))
        {
            var match = availableSubjects.FirstOrDefault(subject =>
                string.Equals(subject, selectedSubject.Trim(), StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return availableSubjects.FirstOrDefault() ?? "Nachhilfe";
    }

    private static string FormatApiDateTime(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static int[] GetSubjectIds(IEnumerable<string> selectedSubjects, IReadOnlyList<SubjectDto> subjects)
    {
        return selectedSubjects
            .Select(subject => FindSubjectId(subject, subjects))
            .Where(subjectId => subjectId > 0)
            .Distinct()
            .ToArray();
    }

    private static int FindSubjectId(string selectedSubject, IReadOnlyList<SubjectDto> subjects)
    {
        var normalized = NormalizeSubjectKey(selectedSubject);
        var aliasMatch = GetSubjectAliasId(normalized);

        if (aliasMatch > 0 && subjects.Any(subject => subject.Suid == aliasMatch))
        {
            return aliasMatch;
        }

        return subjects.FirstOrDefault(subject =>
                NormalizeSubjectKey(subject.DisplayName) == normalized
                || NormalizeSubjectKey(subject.Name) == normalized
                || NormalizeSubjectKey(subject.Abbreviation) == normalized
                || NormalizeSubjectKey($"{subject.Abbreviation} {subject.Name}") == normalized)
            ?.Suid ?? 0;
    }

    private static string NormalizeSubjectKey(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Aggregate(string.Empty, (current, character) => current + character)
            .Replace("ß", "ss", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private static int GetSubjectAliasId(string normalizedSubject)
    {
        return normalizedSubject switch
        {
            "softwareentwicklung" or "programmieren" or "coding" => 1,
            "netzwerktechnik" or "netzwerke" or "networking" => 2,
            "medientechnik" or "media" => 3,
            "systemtechniket" or "etechnik" or "elektrotechnik" or "gete" => 4,
            "systemtechnikit" or "informationstechnik" or "ginf" => 5,
            "geographiegeschichteundpolitischebildung" or "geographie" or "geschichte" or "politischebildung" => 6,
            "naturwissenschaften" or "naturwissenschaft" => 7,
            "deutsch" => 8,
            "angewandtemathematik" or "mathematik" or "mathe" or "math" => 9,
            _ => 0
        };
    }

    private static string[] GetSubjects(string? subjectIds, IReadOnlyDictionary<int, SubjectDto> subjectsById)
    {
        if (string.IsNullOrWhiteSpace(subjectIds))
        {
            return [];
        }

        return subjectIds
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(subjectId => GetSubjectName(subjectId, subjectsById))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetSubjectName(string subjectId, IReadOnlyDictionary<int, SubjectDto> subjectsById)
    {
        return int.TryParse(subjectId, out var parsedSubjectId)
               && subjectsById.TryGetValue(parsedSubjectId, out var subject)
            ? subject.DisplayName
            : $"Fach {subjectId}";
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string requestUri, string authToken)
    {
        var request = new HttpRequestMessage(method, requestUri);

        if (!string.IsNullOrWhiteSpace(authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        return request;
    }
}
