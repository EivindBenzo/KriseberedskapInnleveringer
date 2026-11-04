using System.ComponentModel.DataAnnotations;

namespace HeimevernetInnlevering1.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Oppgi e-postadresse."), EmailAddress(ErrorMessage = "Oppgi en gyldig e-postadresse.")]
    [Display(Name = "E-post")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi passord."), DataType(DataType.Password)]
    [Display(Name = "Passord")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Oppgi fornavn."), StringLength(100)]
    [Display(Name = "Fornavn")]
    public string Fornavn { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi etternavn."), StringLength(100)]
    [Display(Name = "Etternavn")]
    public string Etternavn { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi telefonnummer."), Phone(ErrorMessage = "Oppgi et gyldig telefonnummer."), StringLength(20)]
    [Display(Name = "Telefon")]
    public string Telefon { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Organisasjon (valgfritt)")]
    public string? Organisasjon { get; set; }

    [Required(ErrorMessage = "Oppgi e-postadresse."), EmailAddress(ErrorMessage = "Oppgi en gyldig e-postadresse."), MaxLength(254)]
    [Display(Name = "E-post")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi passord."), StringLength(128, MinimumLength = 12, ErrorMessage = "Passordet må være mellom 12 og 128 tegn."), DataType(DataType.Password)]
    [Display(Name = "Passord")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Velg kontotype.")]
    [Display(Name = "Kontotype")]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekreft passordet."), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passordene må være like.")]
    [Display(Name = "Bekreft passord")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public record AdminUserViewModel(int Id, string Navn, string Email, string? Organisasjon, string? Role);
