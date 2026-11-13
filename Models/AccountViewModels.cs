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

public record AdminUserViewModel(int Id, string Navn, string Email, string? Organisasjon, string? Role, bool Active = true, DateTime? LastActive = null);

public record ProfilKontakt(int Id, string Navn, string Rolle, string? Organisasjon, string Epost, string? Telefon);

public record OvStat(string Label, int Value, string Ikon, string? Merke = null);

public class OvRad
{
    public string Tittel { get; init; } = "";
    public string Under { get; init; } = "";
    public string? Tekst { get; init; }
    public string? Chip { get; init; }
    public string? ChipK { get; init; }
    public string? Chip2 { get; init; }
    public string? Chip2K { get; init; }
    public DateTime? Tid { get; init; }
    public string? Knapp { get; init; }
    public string Url { get; init; } = "#";
    public string Ikon { get; init; } = "";
}

public class OversiktModel
{
    public List<OvStat> Stats { get; } = [];
    public OvRad? Hoved { get; set; }
    public int HovedBehov { get; set; }
    public List<OvRad> A { get; } = [];
    public List<OvRad> B { get; } = [];
    public List<OvRad> C { get; } = [];
    public List<OvRad> Hendelser { get; } = [];
    public List<OvRad> Aktivitet { get; } = [];
    public List<OvRad> Leveranser { get; } = [];
    public List<(string Navn, int Totalt, int Tilgjengelig)> Typer { get; } = [];
    public List<(string Tekst, bool Ferdig)> Oppgaver { get; } = [];
    public int AntA { get; set; }
    public int AntB { get; set; }
    public int AntC { get; set; }
    public int ResTilgjengelig { get; set; }
    public int ResAktive { get; set; }
    public int ResVenter { get; set; }
    public int Utnyttelse { get; set; }
}

public class ProfileViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Organisation { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool HasAvatar { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public List<(string Label, string Description, string Controller, string Action)> Shortcuts { get; } = [];
    public List<(string Label, int Value)> Stats { get; } = [];
    public List<ProfilKontakt> Kontakter { get; } = [];
    public OversiktModel Ov { get; } = new();
    public string Ansvar { get; init; } = string.Empty;
    public string Vakt { get; init; } = "pa";
    public Dictionary<string, bool> Varselvalg { get; init; } = [];
    public List<(string Handling, string Detalj, DateTime Tid)> Aktivitet { get; } = [];

    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(part => char.IsLetter(part[0])).Select(part => char.ToUpperInvariant(part[0])).Take(2)) is { Length: > 0 } initials
        ? initials : "?";
}

public class ProfileEditModel
{
    [Required, StringLength(100)] public string Fornavn { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Etternavn { get; set; } = string.Empty;
    [Required, Phone, StringLength(20)] public string Telefon { get; set; } = string.Empty;
    [StringLength(150)] public string? Organisasjon { get; set; }
}

public class ChangePasswordModel
{
    [Required, DataType(DataType.Password)] public string CurrentPassword { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string NewPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = string.Empty;
}