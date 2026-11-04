namespace HeimevernetInnlevering1.Models;

/// <summary>
/// En ressurs en ressursleverandør stiller til disposisjon, med posisjon på kartet.
/// Status lagres (bevisst unntak fra 3NF, se rapporten) slik at kart og lister kan filtrere raskt.
/// </summary>
public class Ressurs
{
    public int RessursID { get; set; }
    public int RessurstypeID { get; set; }
    public int EierID { get; set; }
    public string Navn { get; set; } = string.Empty;
    public string? Beskrivelse { get; set; }
    public DateOnly TilgjengeligFra { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? TilgjengeligTil { get; set; }
    public string Kontaktperson { get; set; } = string.Empty;
    public string Kontakttelefon { get; set; } = string.Empty;
    public decimal Breddegrad { get; set; }
    public decimal Lengdegrad { get; set; }
    /// <summary>Tillegg til ER-diagrammet: lesbart stedsnavn for punktet.</summary>
    public string Stedsnavn { get; set; } = string.Empty;
    public string Status { get; set; } = Statuser.Ressurs.VenterPåVerifisering;
    public DateTime Registrert { get; set; } = DateTime.UtcNow;

    public Ressurstype? Ressurstype { get; set; }
    public Bruker? Eier { get; set; }
    public ICollection<Verifisering> Verifiseringer { get; set; } = new List<Verifisering>();
    public ICollection<Forespørsel> Forespørsler { get; set; } = new List<Forespørsel>();
}

/// <summary>En kontroll av en ressurs. Siste godkjenning avgjør om ressursen kan forespørres.</summary>
public class Verifisering
{
    public int VerifiseringID { get; set; }
    public int RessursID { get; set; }
    public int VerifisertAvID { get; set; }
    public DateTime Tidspunkt { get; set; } = DateTime.UtcNow;
    public string Resultat { get; set; } = Statuser.Resultat.Godkjent;
    public string? Begrunnelse { get; set; }

    public Ressurs? Ressurs { get; set; }
    public Bruker? VerifisertAv { get; set; }
}
