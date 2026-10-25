using System.ComponentModel.DataAnnotations;

namespace HeimevernetInnlevering1.Models;

public enum LoginAccess
{
    Bruker,
    Admin
}

public static class AccountLoginRules
{
    public static bool CanUseChoice(string choice, bool isAdmin) =>
        choice == nameof(LoginAccess.Bruker) || (choice == nameof(LoginAccess.Admin) && isAdmin);
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Oppgi e-postadresse."), EmailAddress(ErrorMessage = "Oppgi en gyldig e-postadresse.")]
    [Display(Name = "E-post")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi passord."), DataType(DataType.Password)]
    [Display(Name = "Passord")]
    public string Password { get; set; } = string.Empty;

    [EnumDataType(typeof(LoginAccess), ErrorMessage = "Velg en gyldig tilgang.")]
    public LoginAccess Access { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Oppgi e-postadresse."), EmailAddress(ErrorMessage = "Oppgi en gyldig e-postadresse."), MaxLength(254)]
    [Display(Name = "E-post")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi passord."), StringLength(128, MinimumLength = 12, ErrorMessage = "Passordet må være mellom 12 og 128 tegn."), DataType(DataType.Password)]
    [Display(Name = "Passord")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekreft passordet."), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passordene må være like.")]
    [Display(Name = "Bekreft passord")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public record AdminUserViewModel(string Id, string Email, bool IsAdmin);
