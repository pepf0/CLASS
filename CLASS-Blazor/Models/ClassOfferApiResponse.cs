using System.Text.Json.Serialization;

namespace CLASS_Blazor.Models;

public sealed class ClassOfferApiResponse
{
    [JsonPropertyName("value")]
    public List<ClassOfferDto> Value { get; set; } = [];
}

public sealed class ClassOfferDto
{
    [JsonPropertyName("oid")]
    public int Oid { get; set; }

    [JsonPropertyName("offerer_uid")]
    public int OffererUid { get; set; }

    [JsonPropertyName("min_price")]
    public int MinPrice { get; set; }

    [JsonPropertyName("until")]
    public DateTimeOffset? Until { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("subject_ids")]
    public string? SubjectIds { get; set; }
}
