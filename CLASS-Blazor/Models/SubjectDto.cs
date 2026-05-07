using System.Text.Json.Serialization;

namespace CLASS_Blazor.Models;

public sealed class SubjectDto
{
    [JsonPropertyName("suid")]
    public int Suid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("abbr")]
    public string Abbreviation { get; set; } = string.Empty;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Abbreviation) && !string.IsNullOrWhiteSpace(Name))
            {
                return $"{Abbreviation.Trim()} - {Name.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name.Trim();
            }

            return string.IsNullOrWhiteSpace(Abbreviation) ? $"Fach {Suid}" : Abbreviation.Trim();
        }
    }
}
