using CLASS_Blazor.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CLASS_Blazor.Services;

public sealed class TutorRequestService(
    HttpClient httpClient,
    ILogger<TutorRequestService> logger)
{
    private const string RequestApiUrl = "request";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<TutorRequestRecord>> GetRequestsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await httpClient.GetStringAsync(RequestApiUrl, cancellationToken);

            return ParseRequests(json);
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

        var subject = tutor.Subjects.FirstOrDefault() ?? "Nachhilfe";
        var payload = new
        {
            requester_uid = requester.Uid,
            offerer_uid = tutor.OffererUserId,
            max_price = Math.Max(0, tutor.PricePerHour),
            until = DateTimeOffset.UtcNow.AddMonths(1).ToString("O"),
            subject,
            description = $"Anfrage von {requester.FullName} für {subject} bei {tutor.Name}."
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(RequestApiUrl, payload, SerializerOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return TutorRequestResult.Failed("Die Anfrage konnte nicht gespeichert werden.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return TutorRequestResult.Failed($"Die Anfrage konnte nicht gesendet werden ({(int)response.StatusCode}).");
            }

            return TutorRequestResult.Sent();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Tutor request could not be sent to {RequestApiUrl}.", RequestApiUrl);
            return TutorRequestResult.Failed($"Die Anfrage konnte nicht gesendet werden: {exception.Message}");
        }
    }

    private static IReadOnlyList<TutorRequestRecord> ParseRequests(string json)
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
                Subject: GetString(element, "subject", "subject_name", "subjectName"),
                MaxPrice: GetInt(element, "max_price", "price_per_hour", "price"),
                Until: GetDate(element, "until", "available_until"),
                Description: GetString(element, "description", "message"),
                CreatedAt: GetDate(element, "created_at", "createdAt", "requested_on", "requestedOn") ?? DateTimeOffset.Now,
                Status: GetString(element, "status")));
        }

        return requests;
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
}
