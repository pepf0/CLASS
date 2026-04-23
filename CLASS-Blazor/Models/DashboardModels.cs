namespace CLASS_Blazor.Models;

public sealed record DashboardSnapshot(
    int BookedHours,
    int CompletedHours,
    int ActiveOffers,
    decimal TotalRevenue,
    decimal CurrentMonthRevenue,
    bool HasOffer,
    IReadOnlyList<DashboardChartPoint> RevenueByMonth,
    IReadOnlyList<DashboardChartPoint> HoursByMonth,
    IReadOnlyList<SubjectHoursStat> SubjectHours,
    IReadOnlyList<TutorRequest> OpenRequests,
    IReadOnlyList<DashboardActivity> Activities);

public sealed record DashboardChartPoint(
    string Label,
    decimal Value);

public sealed record SubjectHoursStat(
    string Subject,
    decimal Hours,
    string Color);

public sealed record SubjectDonutSegment(
    string Color,
    string DashArray,
    string DashOffset);

public sealed record TutorRequest(
    Guid Id,
    string Name,
    string Subject,
    int PricePerHour,
    DateOnly RequestedOn);

public sealed record DashboardActivity(
    Guid Id,
    string Title,
    string Detail,
    DateTime OccurredAt,
    string Tone);
