namespace HeimevernetInnlevering1.Models;

/// <summary>
/// Dispatch spør en ressursleverandør om å bruke en ressurs på et behov.
/// Ressursen kan først tildeles når leverandøren har svart «godtatt».
/// </summary>
public class Forespørsel
{
    public int ForespørselID { get; set; }
    public int BehovID { get; set; }
    public int RessursID { get; set; }
    public int SendtAvID { get; set; }
    public decimal Antall { get; set; } = 1;
    public DateTime Sendt { get; set; } = DateTime.UtcNow;
    public string Svar { get; set; } = Statuser.Svar.Venter;
    public DateTime? Svart { get; set; }

    public Behov? Behov { get; set; }
    public Ressurs? Ressurs { get; set; }
    public Bruker? SendtAv { get; set; }
    public Tildeling? Tildeling { get; set; }
}

/// <summary>En godtatt forespørsel som er gjort om til et oppdrag (1–0..1, ForespørselID er unik).</summary>
public class Tildeling
{
    public int TildelingID { get; set; }
    public int ForespørselID { get; set; }
    public DateTime Tildelt { get; set; } = DateTime.UtcNow;
    public string Fremdrift { get; set; } = Statuser.Fremdrift.Bekreftet;
    public DateTime SistOppdatert { get; set; } = DateTime.UtcNow;
    public DateTime? Fullført { get; set; }
    public DateTime? ForventetFremme { get; set; }
    public string? Mottakskontakt { get; set; }
    public DateTime? OverleveringBekreftet { get; set; }
    public decimal? LevertAntall { get; set; }
    public string? Avvik { get; set; }

    public Forespørsel? Forespørsel { get; set; }
}

/// <summary>
/// Tillegg til ER-diagrammet: logg over viktige handlinger (hvem, hva, når og hvorfor),
/// brukt til historikken på ressurser, forespørsler og tildelinger.
/// </summary>
public class Hendelseslogg
{
    public long LoggID { get; set; }
    public int? HendelseID { get; set; }
    public int? RessursID { get; set; }
    public int? BehovID { get; set; }
    public int? ForespørselID { get; set; }
    public int UtførtAvID { get; set; }
    public DateTime Tidspunkt { get; set; } = DateTime.UtcNow;
    public string Handling { get; set; } = string.Empty;
    public string Begrunnelse { get; set; } = string.Empty;

    public Hendelse? Hendelse { get; set; }
}

/// <summary>Direkte melding mellom to registrerte operative brukere.</summary>
public class Melding
{
    public long MeldingID { get; set; }
    public int SendtAvID { get; set; }
    public int MottakerID { get; set; }
    public string Emne { get; set; } = string.Empty;
    public string Innhold { get; set; } = string.Empty;
    public DateTime Sendt { get; set; } = DateTime.UtcNow;
    public DateTime? Lest { get; set; }

    public Bruker? SendtAv { get; set; }
    public Bruker? Mottaker { get; set; }
}
