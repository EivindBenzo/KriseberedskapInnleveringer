namespace HeimevernetInnlevering1.Models;

/// <summary>Oppslagstabell med typer behov (Transport, Strøm, ...).</summary>
public class Behovstype
{
    public int BehovstypeID { get; set; }
    public string Navn { get; set; } = string.Empty;
    public ICollection<BehovstypeRessurstype> PassendeRessurstyper { get; set; } = new List<BehovstypeRessurstype>();
}

/// <summary>Oppslagstabell med typer ressurser (Kjøretøy, Generator, ...).</summary>
public class Ressurstype
{
    public int RessurstypeID { get; set; }
    public string Navn { get; set; } = string.Empty;
    public ICollection<BehovstypeRessurstype> PassendeBehovstyper { get; set; } = new List<BehovstypeRessurstype>();
}

/// <summary>Koblingstabell (m:n): hvilke ressurstyper som kan dekke hvilke behovstyper.</summary>
public class BehovstypeRessurstype
{
    public int BehovstypeID { get; set; }
    public int RessurstypeID { get; set; }
    public Behovstype? Behovstype { get; set; }
    public Ressurstype? Ressurstype { get; set; }
}
