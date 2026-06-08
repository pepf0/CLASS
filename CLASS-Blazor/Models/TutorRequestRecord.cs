namespace CLASS_Blazor.Models;

public sealed record TutorRequestRecord(
    int RequestId,
    int RequesterUserId,
    int OffererUserId,
    int OfferId,
    string RequesterName,
    string RequesterEmail,
    string Subject,
    int MaxPrice,
    DateTimeOffset? Until,
    string Description,
    DateTimeOffset CreatedAt,
    string Status);

public sealed record TutorRequestResult(
    bool Success,
    string? Message)
{
    public static TutorRequestResult Sent()
    {
        return new(Success: true, Message: null);
    }

    public static TutorRequestResult Failed(string message)
    {
        return new(Success: false, message);
    }
}
