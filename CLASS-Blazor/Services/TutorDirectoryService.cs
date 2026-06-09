using CLASS_Blazor.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class TutorDirectoryService(
    HttpClient httpClient,
    ILogger<TutorDirectoryService> logger,
    ProfileImageStorageService profileImageStorageService)
{
    private const string TutorApiUrl = "offer";
    private const string UserApiUrl = "user";
    private const string SubjectApiUrl = "subject";
    private static readonly TimeSpan CreateOfferTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TutorDirectoryResult> GetTutorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var offersJson = await httpClient.GetStringAsync(TutorApiUrl, cancellationToken);
            var usersJson = await httpClient.GetStringAsync(UserApiUrl, cancellationToken);
            var subjectsJson = await httpClient.GetStringAsync(SubjectApiUrl, cancellationToken);
            var apiOffers = ParseApiOffers(offersJson);
            var usersById = ParseUsers(usersJson).ToDictionary(user => user.Uid);
            var subjectsById = ParseSubjects(subjectsJson).ToDictionary(subject => subject.Suid);

            if (apiOffers.Count == 0)
            {
                return new TutorDirectoryResult(
                    [],
                    LoadedFromApi: false,
                    Message: "Die API hat keine Nachhilfeangebote geliefert.");
            }

            return new TutorDirectoryResult(
                BuildTutorsFromApi(apiOffers, usersById, subjectsById),
                LoadedFromApi: true,
                Message: $"{apiOffers.Count} Nachhilfeangebote wurden aus der API geladen.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor offers could not be loaded from {TutorApiUrl}.", TutorApiUrl);

            return new TutorDirectoryResult(
                [],
                LoadedFromApi: false,
                Message: $"Die API konnte nicht erreicht werden: {exception.Message}");
        }
    }

    public async Task<TutorOfferCreationResult> CreateTutorOfferAsync(
        UserProfile user,
        TutorOfferFormModel form,
        string authToken,
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (user.Uid <= 0)
        {
            return TutorOfferCreationResult.Failed("Du musst eingeloggt sein, um ein Angebot zu erstellen.");
        }

        if (!form.ExpiresOn.HasValue)
        {
            return TutorOfferCreationResult.Failed("Bitte gib ein Gueltigkeitsdatum an.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CreateOfferTimeout);

            var subjects = await GetSubjectsAsync(timeoutCts.Token);
            var normalizedSubjects = NormalizeSubjects(form.Subjects);
            var subjectIds = GetSubjectIds(normalizedSubjects, subjects);

            if (subjectIds.Length == 0)
            {
                return TutorOfferCreationResult.Failed(
                    "Bitte gib mindestens ein bekanntes Fach an, z. B. SEW, NWT, MEDT, AM oder Deutsch.");
            }

            var payload = new
            {
                min_price = form.PricePerHour,
                until = FormatApiDateTime(form.ExpiresOn.Value),
                description = form.Description.Trim(),
                subjects = subjectIds
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Post, TutorApiUrl, authToken);
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TutorOfferCreationResult.Failed("Bitte logge dich erneut ein, um dein Angebot zu erstellen.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return TutorOfferCreationResult.Failed("Das Angebot konnte mit diesen Daten nicht gespeichert werden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var apiError = await TryReadApiErrorAsync(response, timeoutCts.Token);
                return TutorOfferCreationResult.Failed(string.IsNullOrWhiteSpace(apiError)
                    ? $"Das Angebot konnte nicht erstellt werden ({(int)response.StatusCode})."
                    : $"Das Angebot konnte nicht erstellt werden ({(int)response.StatusCode}): {apiError}");
            }

            var createdOffer = await BuildCreatedTutorOfferAsync(
                response,
                user,
                form,
                normalizedSubjects,
                subjectIds,
                imageUrl,
                subjects,
                timeoutCts.Token);

            return TutorOfferCreationResult.Created(createdOffer);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tutor offer creation timed out at {TutorApiUrl}.", TutorApiUrl);
            return TutorOfferCreationResult.Failed("Das Speichern dauert zu lange. Bitte pruefe deine Verbindung und versuche es erneut.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor offer could not be created at {TutorApiUrl}.", TutorApiUrl);
            return TutorOfferCreationResult.Failed($"Das Angebot konnte nicht erstellt werden: {exception.Message}");
        }
    }

    public async Task<TutorOfferDeletionResult> DeleteTutorOfferAsync(
        TutorOffer offer,
        string authToken,
        CancellationToken cancellationToken = default)
    {
        if (offer.OfferId <= 0)
        {
            return TutorOfferDeletionResult.Failed("Das Angebot konnte keiner API-ID zugeordnet werden.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CreateOfferTimeout);

            var payload = new
            {
                until = FormatApiDateTime(DateTime.UtcNow.AddDays(-1))
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"{TutorApiUrl}/{offer.OfferId}", authToken);
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TutorOfferDeletionResult.Failed("Bitte logge dich erneut ein, um dein Angebot zu löschen.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TutorOfferDeletionResult.Failed("Du kannst nur dein eigenes Angebot löschen.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return TutorOfferDeletionResult.Failed("Das Angebot wurde nicht gefunden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var apiError = await TryReadApiErrorAsync(response, timeoutCts.Token);
                return TutorOfferDeletionResult.Failed(string.IsNullOrWhiteSpace(apiError)
                    ? $"Das Angebot konnte nicht gelöscht werden ({(int)response.StatusCode})."
                    : $"Das Angebot konnte nicht gelöscht werden ({(int)response.StatusCode}): {apiError}");
            }

            return TutorOfferDeletionResult.Deleted();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tutor offer {OfferId} deletion timed out at {TutorApiUrl}.", offer.OfferId, TutorApiUrl);
            return TutorOfferDeletionResult.Failed("Das Löschen dauert zu lange. Bitte prüfe deine Verbindung und versuche es erneut.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor offer {OfferId} could not be deleted at {TutorApiUrl}.", offer.OfferId, TutorApiUrl);
            return TutorOfferDeletionResult.Failed($"Das Angebot konnte nicht gelöscht werden: {exception.Message}");
        }
    }

    private static List<ClassOfferDto> ParseApiOffers(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClassOfferDto>>(json, SerializerOptions) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClassOfferDto>>(valueElement.GetRawText(), SerializerOptions) ?? [];
        }

        return [];
    }

    private static List<UserProfile> ParseUsers(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<UserProfile>>(json, SerializerOptions) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<UserProfile>>(valueElement.GetRawText(), SerializerOptions) ?? [];
        }

        return [];
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

    private async Task<TutorOffer> BuildCreatedTutorOfferAsync(
        HttpResponseMessage response,
        UserProfile user,
        TutorOfferFormModel form,
        string[] normalizedSubjects,
        int[] subjectIds,
        string imageUrl,
        IReadOnlyList<SubjectDto> subjects,
        CancellationToken cancellationToken)
    {
        var createdDto = await TryReadCreatedOfferAsync(response, cancellationToken);
        var subjectsById = subjects.ToDictionary(subject => subject.Suid);

        return new TutorOffer(
            Name: string.IsNullOrWhiteSpace(user.FullName) ? user.Email.Trim() : user.FullName,
            Age: GetAge(user.BirthDate),
            Email: user.Email.Trim(),
            Description: form.Description.Trim(),
            SchoolInfo: GetSchoolInfo(user),
            Subjects: createdDto is null
                ? normalizedSubjects
                : GetCreatedOfferSubjects(createdDto, normalizedSubjects, subjectIds, subjectsById),
            Rating: ConvertRating(user.Rating),
            ReviewCount: GetReviewCount(user.Rating),
            PricePerHour: createdDto?.MinPrice ?? form.PricePerHour,
            ExpiresOn: createdDto is null ? form.ExpiresOn!.Value : GetExpiryDate(createdDto.Until),
            ImageUrl: imageUrl,
            OffererUserId: createdDto?.OffererUid > 0 ? createdDto.OffererUid : user.Uid,
            OfferId: createdDto?.Oid ?? 0);
    }

    private static string[] GetCreatedOfferSubjects(
        ClassOfferDto createdDto,
        string[] normalizedSubjects,
        int[] subjectIds,
        IReadOnlyDictionary<int, SubjectDto> subjectsById)
    {
        var createdSubjectIds = string.IsNullOrWhiteSpace(createdDto.SubjectIds)
            ? string.Join(",", subjectIds)
            : createdDto.SubjectIds;
        var createdSubjects = GetSubjects(createdSubjectIds, subjectsById);

        return createdSubjects.Length == 0 ? normalizedSubjects : createdSubjects;
    }

    private static async Task<ClassOfferDto?> TryReadCreatedOfferAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<ClassOfferDto>(valueElement.GetRawText(), SerializerOptions);
        }

        return document.RootElement.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<ClassOfferDto>(json, SerializerOptions)
            : null;
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

    private static string FormatApiDateTime(DateOnly date)
    {
        return FormatApiDateTime(date.ToDateTime(TimeOnly.MinValue));
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
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
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

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string requestUri, string authToken)
    {
        var request = new HttpRequestMessage(method, requestUri);

        if (!string.IsNullOrWhiteSpace(authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        return request;
    }

    private IReadOnlyList<TutorOffer> BuildTutorsFromApi(
        IEnumerable<ClassOfferDto> apiOffers,
        IReadOnlyDictionary<int, UserProfile> usersById,
        IReadOnlyDictionary<int, SubjectDto> subjectsById)
    {
        return apiOffers
            .Select(apiOffer =>
            {
                usersById.TryGetValue(apiOffer.OffererUid, out var user);

                return new TutorOffer(
                    Name: user?.FullName ?? $"User {apiOffer.OffererUid}",
                    Age: GetAge(user?.BirthDate),
                    Email: user?.Email.Trim() ?? string.Empty,
                    Description: GetDescription(apiOffer),
                    SchoolInfo: GetSchoolInfo(user),
                    Subjects: GetSubjects(apiOffer.SubjectIds, subjectsById),
                    Rating: ConvertRating(user?.Rating ?? 0),
                    ReviewCount: GetReviewCount(user?.Rating ?? 0),
                    PricePerHour: Math.Max(0, apiOffer.MinPrice),
                    ExpiresOn: GetExpiryDate(apiOffer.Until),
                    ImageUrl: profileImageStorageService.GetProfileImageUrl(apiOffer.OffererUid),
                    OffererUserId: apiOffer.OffererUid,
                    OfferId: apiOffer.Oid);
            })
            .Where(tutor => !string.IsNullOrWhiteSpace(tutor.Name))
            .ToList();
    }

    private static decimal ConvertRating(int rating)
    {
        var normalized = Math.Clamp(rating, 0, 100) / 20m;
        return Math.Round(normalized, 1, MidpointRounding.AwayFromZero);
    }

    private static string GetDescription(ClassOfferDto apiOffer)
    {
        return string.IsNullOrWhiteSpace(apiOffer.Description)
            ? "Für dieses Angebot sind noch keine weiteren Details hinterlegt."
            : apiOffer.Description.Trim();
    }

    private static string GetSchoolInfo(UserProfile? user)
    {
        var infoParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(user?.Grade))
        {
            infoParts.Add(user.Grade.Trim());
        }

        if (!string.IsNullOrWhiteSpace(user?.SchoolType))
        {
            infoParts.Add(user.SchoolType.Trim());
        }

        return infoParts.Count > 0
            ? string.Join(", ", infoParts)
            : "Tutor bei CLASS";
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

    private static int GetAge(DateTimeOffset? birthDate)
    {
        if (!birthDate.HasValue)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var birthday = DateOnly.FromDateTime(birthDate.Value.DateTime);
        var age = today.Year - birthday.Year;

        if (birthday.AddYears(age) > today)
        {
            age--;
        }

        return Math.Max(0, age);
    }

    private static DateOnly GetExpiryDate(DateTimeOffset? until)
    {
        return until.HasValue
            ? DateOnly.FromDateTime(until.Value.DateTime)
            : DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
    }

    private static int GetReviewCount(int rating)
    {
        return rating <= 0 ? 0 : Math.Max(1, rating / 10);
    }
}

public sealed record TutorOfferCreationResult(
    TutorOffer? Offer,
    bool Success,
    string? Message)
{
    public static TutorOfferCreationResult Created(TutorOffer offer)
    {
        return new(offer, Success: true, Message: null);
    }

    public static TutorOfferCreationResult Failed(string message)
    {
        return new(Offer: null, Success: false, message);
    }
}

public sealed record TutorOfferDeletionResult(
    bool Success,
    string? Message)
{
    public static TutorOfferDeletionResult Deleted()
    {
        return new(Success: true, Message: null);
    }

    public static TutorOfferDeletionResult Failed(string message)
    {
        return new(Success: false, message);
    }
}
