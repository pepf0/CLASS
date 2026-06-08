using CLASS_Blazor.Models;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class DashboardDataService(
    HttpClient httpClient,
    UserSessionService userSessionService,
    UserProfileService userProfileService,
    TutorDirectoryService tutorDirectoryService,
    TutorRequestService tutorRequestService,
    ILogger<DashboardDataService> logger)
{
    private const string SessionApiUrl = "session";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] MonthLabels =
    [
        "Jan", "Feb", "Mar", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"
    ];

    private static readonly string[] SubjectColors =
    [
        "#7c3aed", "#0ea5e9", "#10b981", "#f97316", "#e11d48", "#14b8a6"
    ];

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var currentUser = userSessionService.CurrentUser;

        if (IsClassTestUser(currentUser))
        {
            return GetTestDashboard();
        }

        if (currentUser is null)
        {
            return GetEmptyDashboard(hasOffer: false);
        }

        var tutorsResult = await tutorDirectoryService.GetTutorsAsync(cancellationToken);
        var currentOffer = tutorsResult.Tutors.FirstOrDefault(tutor =>
            tutor.OffererUserId == currentUser.Uid
            || string.Equals(tutor.Email, currentUser.Email, StringComparison.OrdinalIgnoreCase));
        var requests = await tutorRequestService.GetRequestsAsync(cancellationToken);
        var usersResult = await userProfileService.GetUsersAsync(cancellationToken);
        var usersById = usersResult.Users.ToDictionary(user => user.Uid);
        var sessions = await GetSessionsAsync(cancellationToken);
        var tutorSessions = sessions
            .Where(session => session.TutorUserId == currentUser.Uid)
            .ToList();
        var receivedRequests = GetReceivedRequests(requests, currentUser, currentOffer, usersById);
        var activities = BuildActivities(receivedRequests, tutorSessions);
        var completedSessions = tutorSessions
            .Where(session => IsCompletedStatus(session.Status))
            .ToList();

        return new DashboardSnapshot(
            BookedHours: (int)Math.Round(tutorSessions.Sum(session => session.Hours)),
            CompletedHours: (int)Math.Round(completedSessions.Sum(session => session.Hours)),
            ActiveOffers: currentOffer is null ? 0 : 1,
            TotalRevenue: completedSessions.Sum(session => session.Revenue),
            CurrentMonthRevenue: completedSessions
                .Where(session => session.OccurredAt.Year == DateTime.Today.Year && session.OccurredAt.Month == DateTime.Today.Month)
                .Sum(session => session.Revenue),
            HasOffer: currentOffer is not null,
            RevenueByMonth: BuildMonthlyChart(completedSessions, session => session.Revenue),
            HoursByMonth: BuildMonthlyChart(tutorSessions, session => session.Hours),
            SubjectHours: BuildSubjectHours(completedSessions),
            OpenRequests: receivedRequests,
            Activities: activities,
            IsTestData: false);
    }

    private static bool IsClassTestUser(UserProfile? user)
    {
        return user is not null
            && user.Email.EndsWith("@class.com", StringComparison.OrdinalIgnoreCase);
    }

    private static DashboardSnapshot GetTestDashboard()
    {
        return new DashboardSnapshot(
            BookedHours: 9,
            CompletedHours: 7,
            ActiveOffers: 1,
            TotalRevenue: 640m,
            CurrentMonthRevenue: 126m,
            HasOffer: true,
            RevenueByMonth:
            [
                new("Jan", 72m),
                new("Feb", 96m),
                new("Mar", 126m),
                new("Apr", 126m),
                new("Mai", 0m),
                new("Jun", 0m),
                new("Jul", 0m),
                new("Aug", 0m),
                new("Sep", 0m),
                new("Okt", 0m),
                new("Nov", 0m),
                new("Dez", 0m)
            ],
            HoursByMonth:
            [
                new("Nov", 3m),
                new("Dez", 6m),
                new("Jan", 4m),
                new("Feb", 5m),
                new("Mar", 7m),
                new("Apr", 7m)
            ],
            SubjectHours:
            [
                new("INSY", 3m, "#7c3aed"),
                new("MEDT", 2m, "#0ea5e9"),
                new("Deutsch", 1.5m, "#10b981"),
                new("SEW", 0.5m, "#f97316")
            ],
            OpenRequests:
            [
                new(Guid.NewGuid(), "Mira Novak", "INSY", 18, DateOnly.FromDateTime(DateTime.Today.AddDays(-1))),
                new(Guid.NewGuid(), "Jonas Berger", "MEDT", 20, DateOnly.FromDateTime(DateTime.Today.AddDays(-2))),
                new(Guid.NewGuid(), "Aylin Demir", "Deutsch", 16, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)))
            ],
            Activities:
            [
                new(Guid.NewGuid(), "Neue Anfrage", "Mira fragt INSY an.", DateTime.Now.AddMinutes(-2), "info"),
                new(Guid.NewGuid(), "Stunde abgeschlossen", "Deutsch mit Leon erledigt.", DateTime.Now.AddMinutes(-10), "success"),
                new(Guid.NewGuid(), "Anfrage angenommen", "MEDT-Termin bestätigt.", DateTime.Now.AddMinutes(-27), "success"),
                new(Guid.NewGuid(), "Anfrage aktualisiert", "Preis wurde gespeichert.", DateTime.Now.AddHours(-2), "neutral")
            ],
            IsTestData: true);
    }

    private static DashboardSnapshot GetEmptyDashboard(bool hasOffer)
    {
        return new DashboardSnapshot(
            BookedHours: 0,
            CompletedHours: 0,
            ActiveOffers: hasOffer ? 1 : 0,
            TotalRevenue: 0m,
            CurrentMonthRevenue: 0m,
            HasOffer: hasOffer,
            RevenueByMonth: MonthLabels.Select(month => new DashboardChartPoint(month, 0m)).ToList(),
            HoursByMonth: MonthLabels.Select(month => new DashboardChartPoint(month, 0m)).ToList(),
            SubjectHours: [],
            OpenRequests: [],
            Activities: [],
            IsTestData: false);
    }

    private async Task<IReadOnlyList<SessionRecord>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await httpClient.GetStringAsync(SessionApiUrl, cancellationToken);
            return ParseSessions(json);
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Session data could not be loaded from {SessionApiUrl}.", SessionApiUrl);
            return [];
        }
    }

    private static IReadOnlyList<TutorRequest> GetReceivedRequests(
        IEnumerable<TutorRequestRecord> requests,
        UserProfile currentUser,
        TutorOffer? currentOffer,
        IReadOnlyDictionary<int, UserProfile> usersById)
    {
        return requests
            .Where(request => IsRequestForCurrentTutor(request, currentUser, currentOffer))
            .Where(request => IsOpenStatus(request.Status))
            .OrderByDescending(request => request.CreatedAt)
            .Select(request =>
            {
                usersById.TryGetValue(request.RequesterUserId, out var requester);

                return new TutorRequest(
                    Id: CreateStableGuid(request.RequestId, request.RequesterUserId),
                    Name: GetRequesterName(request, requester),
                    Subject: string.IsNullOrWhiteSpace(request.Subject) ? "Nachhilfe" : request.Subject,
                    PricePerHour: request.MaxPrice,
                    RequestedOn: DateOnly.FromDateTime(request.CreatedAt.LocalDateTime));
            })
            .ToList();
    }

    private static bool IsRequestForCurrentTutor(TutorRequestRecord request, UserProfile currentUser, TutorOffer? currentOffer)
    {
        if (request.OffererUserId == currentUser.Uid)
        {
            return true;
        }

        return currentOffer is not null
            && request.OfferId > 0
            && currentOffer.OfferId == request.OfferId;
    }

    private static string GetRequesterName(TutorRequestRecord request, UserProfile? requester)
    {
        if (!string.IsNullOrWhiteSpace(request.RequesterName))
        {
            return request.RequesterName;
        }

        if (!string.IsNullOrWhiteSpace(requester?.FullName))
        {
            return requester.FullName;
        }

        return request.RequesterUserId > 0 ? $"User {request.RequesterUserId}" : "Neue Anfrage";
    }

    private static IReadOnlyList<DashboardActivity> BuildActivities(
        IEnumerable<TutorRequest> requests,
        IEnumerable<SessionRecord> sessions)
    {
        var requestActivities = requests.Select(request =>
            new DashboardActivity(
                CreateStableGuid(request.Id.GetHashCode(), request.PricePerHour),
                "Neue Anfrage",
                $"{request.Name} fragt {request.Subject} an.",
                request.RequestedOn.ToDateTime(TimeOnly.FromTimeSpan(DateTime.Now.TimeOfDay)),
                "info"));
        var sessionActivities = sessions
            .Where(session => IsCompletedStatus(session.Status))
            .Select(session =>
                new DashboardActivity(
                    CreateStableGuid(session.SessionId, session.TutorUserId),
                    "Stunde abgeschlossen",
                    $"{session.Subject} wurde als erledigt gespeichert.",
                    session.OccurredAt,
                    "success"));

        return requestActivities
            .Concat(sessionActivities)
            .OrderByDescending(activity => activity.OccurredAt)
            .Take(5)
            .ToList();
    }

    private static IReadOnlyList<DashboardChartPoint> BuildMonthlyChart(
        IReadOnlyList<SessionRecord> sessions,
        Func<SessionRecord, decimal> valueSelector)
    {
        return MonthLabels
            .Select((month, index) => new DashboardChartPoint(
                month,
                sessions
                    .Where(session => session.OccurredAt.Year == DateTime.Today.Year && session.OccurredAt.Month == index + 1)
                    .Sum(valueSelector)))
            .ToList();
    }

    private static IReadOnlyList<SubjectHoursStat> BuildSubjectHours(IReadOnlyList<SessionRecord> sessions)
    {
        return sessions
            .GroupBy(session => string.IsNullOrWhiteSpace(session.Subject) ? "Nachhilfe" : session.Subject)
            .Select((group, index) => new SubjectHoursStat(
                group.Key,
                group.Sum(session => session.Hours),
                SubjectColors[index % SubjectColors.Length]))
            .ToList();
    }

    private static bool IsOpenStatus(string status)
    {
        return string.IsNullOrWhiteSpace(status)
            || status.Equals("open", StringComparison.OrdinalIgnoreCase)
            || status.Equals("pending", StringComparison.OrdinalIgnoreCase)
            || status.Equals("new", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompletedStatus(string status)
    {
        return status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("done", StringComparison.OrdinalIgnoreCase)
            || status.Equals("finished", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SessionRecord> ParseSessions(string json)
    {
        using var document = JsonDocument.Parse(json);
        var sessionElements = GetArrayElements(document.RootElement);

        return sessionElements
            .Select(element => new SessionRecord(
                SessionId: GetInt(element, "sid", "session_id", "id"),
                TutorUserId: GetInt(element, "offerer_uid", "tutor_uid", "teacher_uid"),
                RequesterUserId: GetInt(element, "requester_uid", "student_uid"),
                Subject: GetString(element, "subject", "subject_name"),
                Hours: GetDecimal(element, "hours", "duration_hours", "duration") switch
                {
                    <= 0m => 1m,
                    var hours => hours
                },
                Revenue: GetDecimal(element, "revenue", "price", "price_per_hour"),
                OccurredAt: GetDate(element, "date", "held_at", "created_at")?.LocalDateTime ?? DateTime.Now,
                Status: GetString(element, "status")))
            .ToList();
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

    private static decimal GetDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0m;
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

    private static Guid CreateStableGuid(int first, int second)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..4], first);
        BitConverter.TryWriteBytes(bytes.Slice(4, 4), second);
        return new Guid(bytes);
    }

    private sealed record SessionRecord(
        int SessionId,
        int TutorUserId,
        int RequesterUserId,
        string Subject,
        decimal Hours,
        decimal Revenue,
        DateTime OccurredAt,
        string Status);
}
