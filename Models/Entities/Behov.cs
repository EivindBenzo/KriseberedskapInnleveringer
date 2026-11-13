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

    /// <summary>Antall som allerede er dekket av ventende eller godtatte forespørsler (krever at Forespørsler er lastet).</summary>
    public decimal Dekket => Forespørsler.Where(f => f.Svar == Statuser.Svar.Venter || f.Svar == Statuser.Svar.Godtatt).Sum(f => f.Tildeling is { Fremdrift: Statuser.Fremdrift.Fullført, LevertAntall: not null } ? f.Tildeling.LevertAntall!.Value : f.Antall);
    /// <summary>Antall som faktisk er levert gjennom fullførte tildelinger.</summary>
    public decimal Levert => Forespørsler.Where(f => f.Tildeling is { Fremdrift: Statuser.Fremdrift.Fullført }).Sum(f => f.Tildeling!.LevertAntall ?? f.Antall);
    public decimal Mangler => Math.Max(0, Antall - Dekket);
    public Hendelse? Hendelse { get; set; }
    public ICollection<Forespørsel> Forespørsler { get; set; } = new List<Forespørsel>();
}
