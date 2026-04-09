using System.ComponentModel.DataAnnotations;

namespace CLASS_Blazor.Models;

public sealed class TutorOfferFormModel : IValidatableObject
{
    [Required(ErrorMessage = "Bitte beschreibe dein Angebot.")]
    [StringLength(320, MinimumLength = 24, ErrorMessage = "Die Beschreibung muss zwischen 24 und 320 Zeichen lang sein.")]
    public string Description { get; set; } = string.Empty;

    [Range(5, 100, ErrorMessage = "Bitte gib einen Preis zwischen 5 und 100 EUR an.")]
    public int PricePerHour { get; set; } = 15;

    public List<string> Subjects { get; set; } = [];

    [Required(ErrorMessage = "Bitte gib ein Gültigkeitsdatum an.")]
    public DateOnly? ExpiresOn { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Subjects.Count == 0)
        {
            yield return new ValidationResult("Bitte wähle mindestens ein Fach.", [nameof(Subjects)]);
        }

        if (ExpiresOn.HasValue && ExpiresOn.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            yield return new ValidationResult("Das Gültigkeitsdatum darf nicht in der Vergangenheit liegen.", [nameof(ExpiresOn)]);
        }
    }
}
