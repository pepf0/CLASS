using CLASS_Blazor.Models;
using System.Collections.Concurrent;

namespace CLASS_Blazor.Services;

public sealed class TutorRequestStatusOverlayService
{
    private readonly ConcurrentDictionary<int, TutorRequestStatusOverlay> statuses = new();

    public void SetStatus(int requestId, TutorRequestStatus status, string subject)
    {
        if (requestId <= 0)
        {
            return;
        }

        statuses[requestId] = new TutorRequestStatusOverlay(
            status,
            subject.Trim(),
            DateTimeOffset.Now);
    }

    public bool TryGetStatus(int requestId, out TutorRequestStatusOverlay status)
    {
        return statuses.TryGetValue(requestId, out status!);
    }
}

public sealed record TutorRequestStatusOverlay(
    TutorRequestStatus Status,
    string Subject,
    DateTimeOffset UpdatedAt);
