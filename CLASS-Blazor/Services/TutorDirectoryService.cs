using CLASS_Blazor.Models;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class TutorDirectoryService(
    HttpClient httpClient,
    TutorOfferStore tutorOfferStore,
    ILogger<TutorDirectoryService> logger)
{
    private const string TutorApiUrl = "https://pepf.net/api/class/offer";

    public async Task<TutorDirectoryResult> GetTutorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await httpClient.GetStringAsync(TutorApiUrl, cancellationToken);
            var apiOffers = ParseApiOffers(json);

            if (apiOffers.Count == 0)
            {
                return new TutorDirectoryResult(
                    [],
                    LoadedFromApi: false,
                    Message: "Die API hat keine Nachhilfeangebote geliefert.");
            }

            return new TutorDirectoryResult(
                BuildTutorsFromApi(apiOffers),
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
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClassOfferDto>>(json, serializerOptions) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClassOfferDto>>(valueElement.GetRawText(), serializerOptions) ?? [];
        }

        return [];
    }

    private IReadOnlyList<TutorOffer> BuildTutorsFromApi(IEnumerable<ClassOfferDto> apiOffers)
    {
        return apiOffers
            .Select(apiOffer =>
            {
                var fullName = $"{apiOffer.FirstName} {apiOffer.LastName}".Trim();
                var isCurrentTutor = IsCurrentTutor(fullName);

                return new TutorOffer(
                    Name: fullName,
                    Age: GetAge(apiOffer.BirthDate),
                    Email: isCurrentTutor ? tutorOfferStore.CurrentTutor.Email : string.Empty,
                    Description: GetDescription(apiOffer),
                    SchoolInfo: GetSchoolInfo(apiOffer),
                    Subjects: GetSubjects(apiOffer.SubjectList),
                    Rating: ConvertRating(apiOffer.Rating),
                    ReviewCount: GetReviewCount(apiOffer.Rating),
                    PricePerHour: Math.Max(0, apiOffer.MinPrice),
                    ExpiresOn: GetExpiryDate(apiOffer.Until),
                    ImageUrl: isCurrentTutor ? tutorOfferStore.CurrentTutor.ImageUrl : string.Empty);
            })
            .Where(tutor => !string.IsNullOrWhiteSpace(tutor.Name))
            .ToList();
    }

    private bool IsCurrentTutor(string fullName)
    {
        return string.Equals(fullName, tutorOfferStore.CurrentTutor.Name, StringComparison.OrdinalIgnoreCase);
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

    private static string GetSchoolInfo(ClassOfferDto apiOffer)
    {
        var infoParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(apiOffer.Grade))
        {
            infoParts.Add(apiOffer.Grade.Trim());
        }

        if (!string.IsNullOrWhiteSpace(apiOffer.SchoolType))
        {
            infoParts.Add(apiOffer.SchoolType.Trim());
        }

        return infoParts.Count > 0
            ? string.Join(", ", infoParts)
            : "Tutor bei CLASS";
    }

    private static string[] GetSubjects(string subjectList)
    {
        if (string.IsNullOrWhiteSpace(subjectList))
        {
            return [];
        }

        return subjectList
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
