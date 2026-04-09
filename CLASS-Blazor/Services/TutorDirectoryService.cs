using CLASS_Blazor.Models;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class TutorDirectoryService(
    HttpClient httpClient,
    TutorOfferStore tutorOfferStore,
    ILogger<TutorDirectoryService> logger)
{
    private const string TutorApiUrl = "https://pepf.net/api/class/user";

    public async Task<TutorDirectoryResult> GetTutorsAsync(CancellationToken cancellationToken = default)
    {
        var localTutors = tutorOfferStore.Tutors.ToList();

        try
        {
            var json = await httpClient.GetStringAsync(TutorApiUrl, cancellationToken);
            var apiUsers = ParseApiUsers(json);

            if (apiUsers.Count == 0)
            {
                return new TutorDirectoryResult(
                    [],
                    LoadedFromApi: false,
                    Message: "Die API hat keine Tutor-Daten geliefert.");
            }

            return new TutorDirectoryResult(
                BuildTutorsFromApi(apiUsers, localTutors),
                LoadedFromApi: true,
                Message: $"{apiUsers.Count} Tutor-Angebote wurden aus der API geladen.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor data could not be loaded from {TutorApiUrl}. Falling back to local store.", TutorApiUrl);

            return new TutorDirectoryResult(
                [],
                LoadedFromApi: false,
                Message: $"Die API konnte nicht erreicht werden: {exception.Message}");
        }
    }

    private static List<ClassUserDto> ParseApiUsers(string json)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClassUserDto>>(json, serializerOptions) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClassUserDto>>(valueElement.GetRawText(), serializerOptions) ?? [];
        }

        return [];
    }

    private static IReadOnlyList<TutorOffer> BuildTutorsFromApi(IEnumerable<ClassUserDto> apiUsers, IReadOnlyList<TutorOffer> localTutors)
    {
        var defaultExpiry = DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
        var localByName = localTutors.ToDictionary(
            tutor => NormalizeKey(tutor.Name),
            StringComparer.OrdinalIgnoreCase);

        return apiUsers
            .Select(apiUser =>
            {
                var fullName = $"{apiUser.FirstName} {apiUser.LastName}".Trim();
                localByName.TryGetValue(NormalizeKey(fullName), out var localTutor);

                return new TutorOffer(
                    Name: fullName,
                    Age: localTutor?.Age ?? 17,
                    Email: string.IsNullOrWhiteSpace(apiUser.Email) ? localTutor?.Email ?? string.Empty : apiUser.Email,
                    Description: GetDescription(apiUser, localTutor),
                    SchoolInfo: GetSchoolInfo(apiUser, localTutor),
                    Subjects: localTutor?.Subjects ?? [],
                    Rating: ConvertRating(apiUser.Rating),
                    ReviewCount: localTutor?.ReviewCount ?? GetReviewCount(apiUser.Rating),
                    PricePerHour: localTutor?.PricePerHour ?? 0,
                    ExpiresOn: localTutor?.ExpiresOn ?? defaultExpiry,
                    ImageUrl: localTutor?.ImageUrl ?? string.Empty);
            })
            .ToList();
    }

    private static decimal ConvertRating(int rating)
    {
        var normalized = Math.Clamp(rating, 0, 100) / 20m;
        return Math.Round(normalized, 1, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeKey(string value)
    {
        return string.Concat(value
            .Where(character => !char.IsWhiteSpace(character)))
            .Trim()
            .ToUpperInvariant();
    }

    private static string GetDescription(ClassUserDto apiUser, TutorOffer? localTutor)
    {
        if (!string.IsNullOrWhiteSpace(apiUser.Description))
        {
            return apiUser.Description.Trim();
        }

        return localTutor?.Description ?? "Für dieses Tutorprofil sind noch keine weiteren Angebotsdetails hinterlegt.";
    }

    private static string GetSchoolInfo(ClassUserDto apiUser, TutorOffer? localTutor)
    {
        var infoParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(apiUser.Grade))
        {
            infoParts.Add(apiUser.Grade.Trim());
        }

        if (!string.IsNullOrWhiteSpace(apiUser.SchoolType))
        {
            infoParts.Add(apiUser.SchoolType.Trim());
        }

        if (infoParts.Count > 0)
        {
            return string.Join(", ", infoParts);
        }

        return localTutor?.SchoolInfo ?? "Tutor bei CLASS";
    }

    private static int GetReviewCount(int rating)
    {
        return rating <= 0 ? 0 : Math.Max(1, rating / 10);
    }
}
