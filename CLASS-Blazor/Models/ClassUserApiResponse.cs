using System.Text.Json.Serialization;

namespace CLASS_Blazor.Models;

public sealed class ClassUserApiResponse
{
    [JsonPropertyName("value")]
    public List<ClassUserDto> Value { get; set; } = [];
}

public sealed class ClassUserDto
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("grade")]
    public string Grade { get; set; } = string.Empty;

    [JsonPropertyName("school_type")]
    public string SchoolType { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public int Rating { get; set; }
}
