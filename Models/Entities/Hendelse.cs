namespace HeimevernetInnlevering1.Models;

/// <summary>En krisehendelse (f.eks. flom). Behov kan knyttes til en hendelse (0..1 – 0..*).</summary>
public class Hendelse
{
    public int HendelseID { get; set; }
    public int OpprettetAvID { get; set; }
    public string Navn { get; set; } = string.Empty;
    public string? Beskrivelse { get; set; }
    public string Område { get; set; } = string.Empty;
    public DateTime Starttidspunkt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = Statuser.Hendelse.Aktiv;

    public Bruker? OpprettetAv { get; set; }
    public ICollection<Behov> Behov { get; set; } = new List<Behov>();
}
