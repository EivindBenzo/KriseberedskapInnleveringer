using System.ComponentModel.DataAnnotations;
using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Skjemaet for å registrere eller redigere en ressurs (tabellen Ressurs).</summary>
public class RessursSkjemaViewModel
{
    [Required(ErrorMessage = "Ressursnavn er påkrevd")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Ressursnavn må være mellom 2 og 150 tegn")]
    public string Navn { get; set; } = string.Empty;

    /// <summary>Verdien i nedtrekkslisten som betyr at brukeren skriver inn en ny type selv.</summary>
    public const string AnnenType = "__annen__";

    [Required(ErrorMessage = "Velg en ressurstype")]
    public string Ressurstype { get; set; } = string.Empty;

    /// <summary>Navn på en ny ressurstype når typen ikke finnes i listen (Ressurstype = AnnenType).</summary>
    [StringLength(100, ErrorMessage = "Ressurstypen kan ha høyst 100 tegn")]
    public string? NyRessurstype { get; set; }

    [StringLength(1000, ErrorMessage = "Beskrivelsen kan ha høyst 1000 tegn.")]
    public string? Beskrivelse { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Antall må være større enn 0.")]
    public decimal Antall { get; set; } = 1;

    [Required(ErrorMessage = "Oppgi enhet, for eksempel stk, L eller kg")]
    [StringLength(20, ErrorMessage = "Enheten kan ha høyst 20 tegn")]
    public string Enhet { get; set; } = "stk";

    public bool ErForbruksvare { get; set; }

    [Required(ErrorMessage = "Oppgi når ressursen er tilgjengelig fra")]
    [DataType(DataType.Date)]
    public DateOnly? TilgjengeligFra { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [DataType(DataType.Date)]
    public DateOnly? TilgjengeligTil { get; set; }

    [Required(ErrorMessage = "Oppgi kontaktperson")]
    [StringLength(150, ErrorMessage = "Kontaktperson kan ha høyst 150 tegn")]
    public string Kontaktperson { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi kontakttelefon")]
    [Phone(ErrorMessage = "Ugyldig telefonnummer")]
    [StringLength(20, ErrorMessage = "Telefonnummeret kan ha høyst 20 tegn")]
    public string Kontakttelefon { get; set; } = string.Empty;

    /// <summary>Settes av serveren ut fra punktet i kartet (stoler ikke på det nettleseren sender).</summary>
    public string Stedsnavn { get; set; } = string.Empty;

    [Required(ErrorMessage = "Klikk i kartet for å velge plassering")]
    [Range(-90, 90, ErrorMessage = "Breddegrad må være mellom -90 og 90")]
    public double? Breddegrad { get; set; }

    [Required(ErrorMessage = "Klikk i kartet for å velge plassering")]
    [Range(-180, 180, ErrorMessage = "Lengdegrad må være mellom -180 og 180")]
    public double? Lengdegrad { get; set; }
}

/// <summary>Skjemaet der en offentlig aktør melder et behov (tabellen Behov).</summary>
public class BehovSkjemaViewModel
{
    /// <summary>Verdien i nedtrekkslista for «Annen type …», der brukeren skriver inn behovstypen selv.</summary>
    public const int AnnenType = -1;

    [Range(AnnenType, int.MaxValue, ErrorMessage = "Velg en behovstype")]
    public int BehovstypeID { get; set; }

    [StringLength(100, ErrorMessage = "Behovstypen kan ha høyst 100 tegn")]
    public string? NyBehovstype { get; set; }

    public int? HendelseID { get; set; }

    [Required(ErrorMessage = "Beskriv behovet")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Beskrivelsen må være mellom 10 og 2000 tegn")]
    public string Beskrivelse { get; set; } = string.Empty;

    [Required]
    public string Prioritet { get; set; } = Statuser.Prioritet.Planlagt;

    [Range(1, 10000, ErrorMessage = "Antall må være mellom 1 og 10000")]
    public int Antall { get; set; } = 1;

    [DataType(DataType.DateTime)]
    public DateTime? Frist { get; set; }

    [Required(ErrorMessage = "Oppgi kontaktperson")]
    [StringLength(150)]
    public string Kontaktperson { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi kontakttelefon")]
    [Phone(ErrorMessage = "Ugyldig telefonnummer")]
    [StringLength(20)]
    public string Kontakttelefon { get; set; } = string.Empty;

    public string Stedsnavn { get; set; } = string.Empty;

    [Required(ErrorMessage = "Klikk i kartet for å velge hvor behovet er")]
    [Range(-90, 90)]
    public double? Breddegrad { get; set; }

    [Required(ErrorMessage = "Klikk i kartet for å velge hvor behovet er")]
    [Range(-180, 180)]
    public double? Lengdegrad { get; set; }
}

/// <summary>Skjemaet der kriseledelsen oppretter en hendelse (tabellen Hendelse).</summary>
public class HendelseSkjemaViewModel
{
    [Required(ErrorMessage = "Gi hendelsen et navn")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Navnet må være mellom 3 og 150 tegn")]
    public string Navn { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Beskrivelse { get; set; }

    [Required(ErrorMessage = "Oppgi område")]
    [StringLength(150)]
    public string Område { get; set; } = string.Empty;

    [Required(ErrorMessage = "Oppgi starttidspunkt")]
    [DataType(DataType.DateTime)]
    public DateTime? Starttidspunkt { get; set; } = DateTime.Now;

    public string Prioritet { get; set; } = Statuser.HendelsePrioritet.Medium;

    public string Status { get; set; } = Statuser.Hendelse.Aktiv;

    public decimal? Breddegrad { get; set; }
    public decimal? Lengdegrad { get; set; }
}
