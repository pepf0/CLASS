using System.ComponentModel.DataAnnotations;

namespace CLASS_Blazor.Models;

public sealed class LoginFormModel
{
    [Required(ErrorMessage = "Bitte gib deine E-Mail-Adresse ein.")]
    [EmailAddress(ErrorMessage = "Bitte gib eine gültige E-Mail-Adresse ein.")]
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
