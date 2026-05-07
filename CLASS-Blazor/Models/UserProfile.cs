using System.Text.Json.Serialization;

namespace CLASS_Blazor.Models;

public sealed class UserProfile
{
    [JsonPropertyName("uid")]
    public int Uid { get; set; }

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

    [JsonPropertyName("birth_date")]
    public DateTimeOffset? BirthDate { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public decimal StarRating => Math.Round(Math.Clamp(Rating, 0, 100) / 20m, 1, MidpointRounding.AwayFromZero);
}
