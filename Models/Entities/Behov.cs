namespace HeimevernetInnlevering1.Models;

/// <summary>Et behov meldt av en offentlig aktør, med posisjon på kartet.</summary>
public class Behov
{
    public int BehovID { get; set; }
    public int BehovstypeID { get; set; }
    public int RegistrertAvID { get; set; }
    public int? HendelseID { get; set; }
    public string Beskrivelse { get; set; } = string.Empty;
    public string Prioritet { get; set; } = Statuser.Prioritet.Planlagt;
    public int Antall { get; set; } = 1;
    public DateTime? Frist { get; set; }
    public string Kontaktperson { get; set; } = string.Empty;
    public string Kontakttelefon { get; set; } = string.Empty;
    public decimal Breddegrad { get; set; }
    public decimal Lengdegrad { get; set; }
    /// <summary>Tillegg til ER-diagrammet: lesbart stedsnavn for punktet (f.eks. «Hamar»).</summary>
    public string Stedsnavn { get; set; } = string.Empty;
    public string Status { get; set; } = Statuser.Behov.Ny;
    public DateTime Registrert { get; set; } = DateTime.UtcNow;

    public Behovstype? Behovstype { get; set; }
    public Bruker? RegistrertAv { get; set; }
    public Hendelse? Hendelse { get; set; }
    public ICollection<Forespørsel> Forespørsler { get; set; } = new List<Forespørsel>();
}
