using CLASS_Blazor.Models;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace CLASS_Blazor.Services;

public sealed class DashboardDataService(
    HttpClient httpClient,
    UserSessionService userSessionService,
    UserProfileService userProfileService,
    TutorDirectoryService tutorDirectoryService,
    TutorRequestService tutorRequestService,
    TutorRequestStatusOverlayService requestStatusOverlay,
    IMemoryCache cache,
    ILogger<DashboardDataService> logger)
{
    private const string SessionApiUrl = "session";
    private const string SessionsCacheKey = "class:sessions";
    private static readonly TimeSpan SessionsCacheDuration = TimeSpan.FromSeconds(30);

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

        var tutorsTask = tutorDirectoryService.GetTutorsAsync(cancellationToken);
        var requestsTask = tutorRequestService.GetRequestsAsync(cancellationToken);
        var usersTask = userProfileService.GetUsersAsync(cancellationToken);
        var sessionsTask = GetSessionsAsync(cancellationToken);

        await Task.WhenAll(tutorsTask, requestsTask, usersTask, sessionsTask);

        var tutorsResult = await tutorsTask;
        var currentOffer = tutorsResult.Tutors.FirstOrDefault(tutor =>
            tutor.OffererUserId == currentUser.Uid
            || string.Equals(tutor.Email, currentUser.Email, StringComparison.OrdinalIgnoreCase));
        var requests = ApplyRequestStatusOverlays(await requestsTask);
        var usersResult = await usersTask;
        var usersById = usersResult.Users.ToDictionary(user => user.Uid);
        var sessions = await sessionsTask;
        var tutorSessions = sessions
            .Where(session => session.TutorUserId == currentUser.Uid)
            .ToList();
        tutorSessions.AddRange(BuildAcceptedRequestSessions(requests, currentUser, currentOffer));
        var dashboardRequests = GetDashboardRequests(requests, currentUser, currentOffer, tutorsResult.Tutors, usersById);
        var activities = BuildActivities(requests, currentUser, currentOffer, tutorsResult.Tutors, usersById, tutorSessions);
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
            OpenRequests: dashboardRequests,
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
                new(Guid.NewGuid(), 101, TutorRequestDirection.Incoming, TutorRequestStatus.Open, "Mira Novak", "INSY", 18, DateOnly.FromDateTime(DateTime.Today.AddDays(-1))),
                new(Guid.NewGuid(), 102, TutorRequestDirection.Outgoing, TutorRequestStatus.Open, "Jonas Berger", "MEDT", 20, DateOnly.FromDateTime(DateTime.Today.AddDays(-2))),
                new(Guid.NewGuid(), 103, TutorRequestDirection.Outgoing, TutorRequestStatus.Accepted, "Aylin Demir", "Deutsch", 16, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)))
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
        if (cache.TryGetValue(SessionsCacheKey, out IReadOnlyList<SessionRecord>? cachedSessions) && cachedSessions is not null)
        {
            return cachedSessions;
        }

        try
        {
            var json = await httpClient.GetStringAsync(SessionApiUrl, cancellationToken);
            var sessions = ParseSessions(json);
            cache.Set(SessionsCacheKey, sessions, SessionsCacheDuration);
            return sessions;
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Session data could not be loaded from {SessionApiUrl}.", SessionApiUrl);
            return [];
        }
    }

    private IReadOnlyList<TutorRequestRecord> ApplyRequestStatusOverlays(IEnumerable<TutorRequestRecord> requests)
    {
        return requests
            .Select(request =>
            {
                if (!requestStatusOverlay.TryGetStatus(request.RequestId, out var overlay))
                {
                    return request;
                }

                return request with
                {
                    Status = GetOverlayStatusValue(overlay.Status),
                    Description = ApplyOverlaySubjectMarker(request.Description, overlay)
                };
            })
            .ToList();
    }

    private static string GetOverlayStatusValue(TutorRequestStatus status)
    {
        return status switch
        {
            TutorRequestStatus.Accepted => "accepted",
            TutorRequestStatus.Declined => "declined",
            TutorRequestStatus.Cancelled => "cancelled",
            _ => string.Empty
        };
    }

    private static string ApplyOverlaySubjectMarker(string description, TutorRequestStatusOverlay overlay)
    {
        if (overlay.Status != TutorRequestStatus.Accepted || string.IsNullOrWhiteSpace(overlay.Subject))
        {
            return description;
        }

        const string subjectMarkerPrefix = "[CLASS_REQUEST_SUBJECT:";
        var cleanedDescription = (description ?? string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith(subjectMarkerPrefix, StringComparison.OrdinalIgnoreCase))
            .DefaultIfEmpty("Anfrage")
            .Aggregate((current, line) => $"{current}\n{line}");

        return $"{cleanedDescription}\n{subjectMarkerPrefix}{overlay.Subject.Trim()}]";
    }

    private static IReadOnlyList<TutorRequest> GetDashboardRequests(
        IEnumerable<TutorRequestRecord> requests,
        UserProfile currentUser,
        TutorOffer? currentOffer,
        IReadOnlyList<TutorOffer> tutorOffers,
        IReadOnlyDictionary<int, UserProfile> usersById)
    {
        return requests
            .Where(request => IsRequestRelevantForCurrentUser(request, currentUser, currentOffer))
            .OrderByDescending(request => request.CreatedAt)
            .Select(request => CreateTutorRequest(request, currentUser, tutorOffers, usersById))
            .Where(IsVisibleDashboardRequest)
            .ToList();
    }

    private static bool IsVisibleDashboardRequest(TutorRequest request)
    {
        return request.Direction switch
        {
            TutorRequestDirection.Incoming => request.Status == TutorRequestStatus.Open,
            TutorRequestDirection.Outgoing => request.Status is TutorRequestStatus.Open
                or TutorRequestStatus.Accepted
                or TutorRequestStatus.Declined,
            _ => false
        };
    }

    private static TutorRequest CreateTutorRequest(
        TutorRequestRecord request,
        UserProfile currentUser,
        IReadOnlyList<TutorOffer> tutorOffers,
        IReadOnlyDictionary<int, UserProfile> usersById)
    {
        var isOutgoing = request.RequesterUserId == currentUser.Uid;
        var name = isOutgoing
            ? GetOffererName(request, tutorOffers, usersById)
            : GetRequesterName(request, usersById);

        return new TutorRequest(
            Id: CreateStableGuid(request.RequestId, isOutgoing ? request.OffererUserId : request.RequesterUserId),
            RequestId: request.RequestId,
            Direction: isOutgoing ? TutorRequestDirection.Outgoing : TutorRequestDirection.Incoming,
            Status: NormalizeRequestStatus(request.Status, request.Until),
            Name: name,
            Subject: string.IsNullOrWhiteSpace(request.Subject) ? "Nachhilfe" : request.Subject,
            PricePerHour: request.MaxPrice,
            RequestedOn: DateOnly.FromDateTime(request.CreatedAt.LocalDateTime),
            Description: request.Description);
    }

    private static bool IsRequestRelevantForCurrentUser(TutorRequestRecord request, UserProfile currentUser, TutorOffer? currentOffer)
    {
        return request.RequesterUserId == currentUser.Uid
            || IsRequestForCurrentTutor(request, currentUser, currentOffer);
    }

    private static IReadOnlyList<SessionRecord> BuildAcceptedRequestSessions(
        IEnumerable<TutorRequestRecord> requests,
        UserProfile currentUser,
        TutorOffer? currentOffer)
    {
        return requests
            .Where(request => IsRequestForCurrentTutor(request, currentUser, currentOffer))
            .Where(request => NormalizeRequestStatus(request.Status, request.Until) == TutorRequestStatus.Accepted)
            .Select(request => new SessionRecord(
                SessionId: -Math.Abs(request.RequestId),
                TutorUserId: currentUser.Uid,
                RequesterUserId: request.RequesterUserId,
                Subject: GetSelectedRequestSubject(request),
                Hours: 1m,
                Revenue: 0m,
                OccurredAt: request.CreatedAt.LocalDateTime,
                Status: "completed"))
            .ToList();
    }

    private static string GetSelectedRequestSubject(TutorRequestRecord request)
    {
        var selectedSubject = GetDescriptionMarker(request.Description, "CLASS_REQUEST_SUBJECT");

        if (!string.IsNullOrWhiteSpace(selectedSubject))
        {
            return selectedSubject;
        }

        return string.IsNullOrWhiteSpace(request.Subject) ? "Nachhilfe" : request.Subject;
    }

    private static bool IsRequestForCurrentTutor(TutorRequestRecord request, UserProfile currentUser, TutorOffer? currentOffer)
    {
        if (request.OffererUserId == currentUser.Uid)
        {
            return true;
        }

        if (currentOffer is null)
        {
            return false;
        }

        if (request.OfferId > 0)
        {
            return currentOffer.OfferId == request.OfferId;
        }

        var targetOfferId = GetTargetOfferId(request.Description);

        if (targetOfferId > 0)
        {
            return currentOffer.OfferId == targetOfferId;
        }

        return HasMatchingSubject(request.Subject, currentOffer.Subjects);
    }

    private static int GetTargetOfferId(string description)
    {
        const string marker = "Angebot #";

        if (string.IsNullOrWhiteSpace(description))
        {
            return 0;
        }

        var markerIndex = description.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            return 0;
        }

        var start = markerIndex + marker.Length;
        var end = start;

        while (end < description.Length && char.IsDigit(description[end]))
        {
            end++;
        }

        return end > start && int.TryParse(description[start..end], out var offerId)
            ? offerId
            : 0;
    }

    private static string GetDescriptionMarker(string description, string markerName)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(markerName))
        {
            return string.Empty;
        }

        var markerPrefix = $"[{markerName}:";
        var markerIndex = description.IndexOf(markerPrefix, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var valueStart = markerIndex + markerPrefix.Length;
        var valueEnd = description.IndexOf(']', valueStart);

        return valueEnd > valueStart
            ? description[valueStart..valueEnd].Trim()
            : string.Empty;
    }

    private static bool HasMatchingSubject(string requestSubject, IEnumerable<string> tutorSubjects)
    {
        if (string.IsNullOrWhiteSpace(requestSubject))
        {
            return false;
        }

        var requestSubjects = requestSubject
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeSubjectKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tutorSubjects
            .Select(NormalizeSubjectKey)
            .Any(requestSubjects.Contains);
    }

    private static string NormalizeSubjectKey(string subject)
    {
        return subject
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

    private static string GetRequesterName(TutorRequestRecord request, IReadOnlyDictionary<int, UserProfile> usersById)
    {
        if (!string.IsNullOrWhiteSpace(request.RequesterName))
        {
            return request.RequesterName;
        }

        usersById.TryGetValue(request.RequesterUserId, out var requester);

        if (!string.IsNullOrWhiteSpace(requester?.FullName))
        {
            return requester.FullName;
        }

        return request.RequesterUserId > 0 ? $"User {request.RequesterUserId}" : "Neue Anfrage";
    }

    private static string GetOffererName(
        TutorRequestRecord request,
        IReadOnlyList<TutorOffer> tutorOffers,
        IReadOnlyDictionary<int, UserProfile> usersById)
    {
        if (request.OfferId > 0)
        {
            var offer = tutorOffers.FirstOrDefault(tutor => tutor.OfferId == request.OfferId);

            if (!string.IsNullOrWhiteSpace(offer?.Name))
            {
                return offer.Name;
            }
        }

        if (request.OffererUserId > 0 && usersById.TryGetValue(request.OffererUserId, out var offerer)
            && !string.IsNullOrWhiteSpace(offerer.FullName))
        {
            return offerer.FullName;
        }

        var targetOfferId = GetTargetOfferId(request.Description);

        if (targetOfferId > 0)
        {
            var offer = tutorOffers.FirstOrDefault(tutor => tutor.OfferId == targetOfferId);

            if (!string.IsNullOrWhiteSpace(offer?.Name))
            {
                return offer.Name;
            }
        }

        return request.OffererUserId > 0 ? $"User {request.OffererUserId}" : "Tutor";
    }

    private static IReadOnlyList<DashboardActivity> BuildActivities(
        IEnumerable<TutorRequestRecord> requests,
        UserProfile currentUser,
        TutorOffer? currentOffer,
        IReadOnlyList<TutorOffer> tutorOffers,
        IReadOnlyDictionary<int, UserProfile> usersById,
        IEnumerable<SessionRecord> sessions)
    {
        var requestActivities = requests
            .Where(request => IsRequestRelevantForCurrentUser(request, currentUser, currentOffer))
            .Select(request => CreateRequestActivity(request, currentUser, tutorOffers, usersById));
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

    private static DashboardActivity CreateRequestActivity(
        TutorRequestRecord request,
        UserProfile currentUser,
        IReadOnlyList<TutorOffer> tutorOffers,
        IReadOnlyDictionary<int, UserProfile> usersById)
    {
        var projected = CreateTutorRequest(request, currentUser, tutorOffers, usersById);
        var subject = projected.Subject;
        var name = projected.Name;
        var title = projected.Status switch
        {
            TutorRequestStatus.Accepted => "Anfrage angenommen",
            TutorRequestStatus.Declined => "Anfrage abgelehnt",
            TutorRequestStatus.Cancelled => "Anfrage zurückgezogen",
            TutorRequestStatus.Read => "Anfrage gelesen",
            _ => projected.Direction == TutorRequestDirection.Outgoing ? "Anfrage gesendet" : "Neue Anfrage"
        };
        var detail = (projected.Direction, projected.Status) switch
        {
            (TutorRequestDirection.Outgoing, TutorRequestStatus.Open) => $"Du hast {name} wegen {subject} angefragt.",
            (TutorRequestDirection.Outgoing, TutorRequestStatus.Accepted) => $"{name} hat deine Anfrage fuer {subject} angenommen.",
            (TutorRequestDirection.Outgoing, TutorRequestStatus.Declined) => $"{name} hat deine Anfrage fuer {subject} abgelehnt.",
            (TutorRequestDirection.Outgoing, TutorRequestStatus.Cancelled) => $"Du hast deine Anfrage an {name} zurückgezogen.",
            (TutorRequestDirection.Incoming, TutorRequestStatus.Accepted) => $"Du hast die Anfrage von {name} fuer {subject} angenommen.",
            (TutorRequestDirection.Incoming, TutorRequestStatus.Declined) => $"Du hast die Anfrage von {name} fuer {subject} abgelehnt.",
            (TutorRequestDirection.Incoming, TutorRequestStatus.Cancelled) => $"{name} hat die Anfrage fuer {subject} zurückgezogen.",
            _ => $"{name} fragt {subject} an."
        };
        var tone = projected.Status switch
        {
            TutorRequestStatus.Accepted => "success",
            TutorRequestStatus.Declined or TutorRequestStatus.Cancelled => "warning",
            TutorRequestStatus.Read => "neutral",
            _ => "info"
        };

        return new DashboardActivity(
            CreateStableGuid(request.RequestId, projected.Direction == TutorRequestDirection.Outgoing ? currentUser.Uid : request.RequesterUserId),
            title,
            detail,
            request.CreatedAt.LocalDateTime,
            tone);
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
        return NormalizeRequestStatus(status, until: null) == TutorRequestStatus.Open;
    }

    private static TutorRequestStatus NormalizeRequestStatus(string status, DateTimeOffset? until)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? TutorRequestStatus.Open
            : status.Trim().ToLowerInvariant() switch
            {
                "open" or "pending" or "new" or "created" or "requested" or "sent" => TutorRequestStatus.Open,
                "accepted" or "approved" or "confirmed" or "angenommen" => TutorRequestStatus.Accepted,
                "declined" or "rejected" or "denied" or "abgelehnt" => TutorRequestStatus.Declined,
                "cancelled" or "canceled" or "withdrawn" or "zurueckgezogen" => TutorRequestStatus.Cancelled,
                "read" or "seen" or "archived" or "gelesen" => TutorRequestStatus.Read,
                _ => TutorRequestStatus.Unknown
            };

        if (normalizedStatus == TutorRequestStatus.Open
            && until.HasValue
            && until.Value <= DateTimeOffset.Now)
        {
            return TutorRequestStatus.Cancelled;
        }

        return normalizedStatus;
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
