using CLASS_Blazor.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class TutorRequestService(
    HttpClient httpClient,
    ILogger<TutorRequestService> logger)
{
    private const string RequestApiUrl = "request";
    private const string SubjectApiUrl = "subject";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<TutorRequestRecord>> GetRequestsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            var json = await httpClient.GetStringAsync(RequestApiUrl, timeoutCts.Token);
            var subjects = await GetSubjectsAsync(timeoutCts.Token);
            var subjectsById = subjects.ToDictionary(subject => subject.Suid);

            return ParseRequests(json, subjectsById);
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
            var subjectIds = GetSubjectIds(normalizedSubjects, subjects);

            if (subjectIds.Length == 0)
            {
                return TutorRequestResult.Failed("Für dieses Angebot konnte kein bekanntes Fach gefunden werden.");
            }

            var subject = normalizedSubjects.FirstOrDefault() ?? "Nachhilfe";
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
                Status: GetString(element, "status")));
        }

        return requests;
    }

    private async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var subjectsJson = await httpClient.GetStringAsync(SubjectApiUrl, cancellationToken);
            return ParseSubjects(subjectsJson);
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
