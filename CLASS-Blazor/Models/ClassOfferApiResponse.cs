using System.Text.Json.Serialization;

namespace CLASS_Blazor.Models;

public sealed class ClassOfferApiResponse
{
    [JsonPropertyName("value")]
    public List<ClassOfferDto> Value { get; set; } = [];
}

public sealed class ClassOfferDto
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("grade")]
    public string Grade { get; set; } = string.Empty;

    [JsonPropertyName("school_type")]
    public string SchoolType { get; set; } = string.Empty;

    [JsonPropertyName("birth_date")]
    public DateTimeOffset? BirthDate { get; set; }

    [JsonPropertyName("rating")]
    public int Rating { get; set; }

    [JsonPropertyName("min_price")]
    public int MinPrice { get; set; }

    [JsonPropertyName("until")]
    public DateTimeOffset? Until { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("subject_list")]
    public string SubjectList { get; set; } = string.Empty;
}
