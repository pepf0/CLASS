using CLASS_Blazor.Models;
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
                    OffererUserId: apiOffer.OffererUid);
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

    private static string[] GetSubjects(string subjectIds, IReadOnlyDictionary<int, SubjectDto> subjectsById)
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
