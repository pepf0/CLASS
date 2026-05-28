using System.ComponentModel.DataAnnotations;

namespace CLASS_Blazor.Models;

public sealed class RegistrationFormModel
{
    [Required(ErrorMessage = "Bitte gib deinen Vornamen ein.")]
    [StringLength(100, ErrorMessage = "Der Vorname darf maximal 100 Zeichen lang sein.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte gib deinen Nachnamen ein.")]
    [StringLength(100, ErrorMessage = "Der Nachname darf maximal 100 Zeichen lang sein.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte gib deine E-Mail-Adresse ein.")]
    [EmailAddress(ErrorMessage = "Bitte gib eine gültige E-Mail-Adresse ein.")]
    [StringLength(100, ErrorMessage = "Die E-Mail-Adresse darf maximal 100 Zeichen lang sein.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte gib ein Passwort ein.")]
    [MinLength(8, ErrorMessage = "Das Passwort muss mindestens 8 Zeichen lang sein.")]
    [StringLength(255, ErrorMessage = "Das Passwort darf maximal 255 Zeichen lang sein.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte gib dein Geburtsdatum ein.")]
    public DateOnly? BirthDate { get; set; }

    [Required(ErrorMessage = "Bitte gib deine Klasse ein.")]
    [StringLength(40, ErrorMessage = "Die Klasse darf maximal 40 Zeichen lang sein.")]
    public string Grade { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte wähle deinen Schultyp aus.")]
    public string SchoolType { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Die Beschreibung darf maximal 500 Zeichen lang sein.")]
    public string Description { get; set; } = string.Empty;

    public string ProfileImageName { get; set; } = string.Empty;

    public string ProfileImageDataUrl { get; set; } = string.Empty;

    public string CroppedProfileImageDataUrl { get; set; } = string.Empty;
}
