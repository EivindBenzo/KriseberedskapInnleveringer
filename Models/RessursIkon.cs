namespace HeimevernetInnlevering1.Models;

/// <summary>
/// Velger symbol (SVG-fil i wwwroot/images/resources) for en ressurstype.
/// De seks faste typene har hvert sitt symbol. Typer som brukerne har lagt til selv
/// (f.eks. «Snøscooter») havner i gruppen «Annet» og deler ett felles symbol.
/// </summary>
public static class RessursIkon
{
    public const string Annet = "Annet";

    // Rekkefølgen her er rekkefølgen i tegnforklaringen og filteret på kartet.
    private static readonly (string Type, string Fil)[] FasteSymboler =
    [
        ("Kjøretøy", "car"),
        ("Maskin", "excavator"),
        ("Drone", "drone"),
        ("Generator", "generator"),
        ("Personell", "personnel"),
        ("Utstyr", "toolbox")
    ];

    /// <summary>De seks typene som har eget symbol.</summary>
    public static IReadOnlyList<string> FasteTyper { get; } = FasteSymboler.Select(s => s.Type).ToList();

    /// <summary>Gruppene i tegnforklaringen: de faste typene pluss «Annet».</summary>
    public static IReadOnlyList<string> Grupper { get; } = [.. FasteTyper, Annet];

    /// <summary>Gruppen en ressurstype hører til: sin egen type, eller «Annet».</summary>
    public static string Gruppe(string? type) => FasteTyper.Contains(type) ? type! : Annet;

    /// <summary>Filnavnet uten endelse, f.eks. «car». Ingen type gir et spørsmålstegn («unknown»).</summary>
    public static string Fil(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "unknown";
        foreach (var (navn, fil) in FasteSymboler)
            if (navn == type) return fil;
        return "other";
    }

    /// <summary>
    /// Stien til symbolet. Teal symbol når ressursen er tilgjengelig, nøytralt blått når den ikke kan forespørres.
    /// Rødt er reservert for akutte behov på kartet.
    /// </summary>
    public static string Sti(string? type, ResourceOperationalStatus? status = null)
    {
        var fil = Fil(type);
        if (status?.Lifecycle == Statuser.Ressurs.Tilgjengelig)
            fil += "-available";
        return $"~/images/resources/{fil}.svg";
    }
}
