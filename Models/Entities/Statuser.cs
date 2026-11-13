namespace HeimevernetInnlevering1.Models;

/// <summary>
/// Gyldige verdier for status-feltene, slik de står i rapporten. Databasen lagrer dem som
/// VARCHAR, og appen sjekker at bare disse verdiene brukes (i stedet for ENUM).
/// </summary>
public static class Statuser
{
    public static class Hendelse
    {
        public const string Aktiv = "aktiv";
        public const string Avsluttet = "avsluttet";
        public const string Planlagt = "planlagt";
        public static readonly string[] Alle = [Planlagt, Aktiv, Avsluttet];

        /// <summary>«Pågår» i grensesnittet.</summary>
        public static string Etikett(string? status) => status == Aktiv ? "Pågår" : Visning(status);
    }

    public static class HendelsePrioritet
    {
        public const string Lav = "lav";
        public const string Medium = "medium";
        public const string Hoy = "høy";
        public static readonly string[] Alle = [Lav, Medium, Hoy];
    }

    public static class Behov
    {
        public const string Ny = "ny";
        public const string Vurderes = "vurderes";
        public const string Tildelt = "tildelt";
        public const string Løst = "løst";
        public static readonly string[] Alle = [Ny, Vurderes, Tildelt, Løst];
        public static readonly string[] Aktive = [Ny, Vurderes, Tildelt];
    }

    public static class Prioritet
    {
        public const string Akutt = "akutt";
        public const string Planlagt = "planlagt";
        public static readonly string[] Alle = [Akutt, Planlagt];
    }

    public static class Ressurs
    {
        public const string VenterPåVerifisering = "venter på verifisering";
        public const string Tilgjengelig = "tilgjengelig";
        public const string Forespurt = "forespurt";
        public const string Tildelt = "tildelt";
        public const string IBruk = "i bruk";
        public const string Returnert = "returnert";
        public const string Utilgjengelig = "utilgjengelig";
        public const string Avvist = "avvist";
        public static readonly string[] Alle =
            [VenterPåVerifisering, Tilgjengelig, Forespurt, Tildelt, IBruk, Returnert, Utilgjengelig, Avvist];
        /// <summary>Statuser der ressursen er bundet til et oppdrag og ikke kan endres manuelt.</summary>
        public static readonly string[] Opptatt = [Forespurt, Tildelt, IBruk];
    }

    public static class Svar
    {
        public const string Venter = "venter";
        public const string Godtatt = "godtatt";
        public const string Avslått = "avslått";
        /// <summary>Tillegg: dispatch trakk forespørselen tilbake før tildeling.</summary>
        public const string Avbrutt = "avbrutt";
        public static readonly string[] Alle = [Venter, Godtatt, Avslått, Avbrutt];
    }

    public static class Fremdrift
    {
        public const string Bekreftet = "bekreftet";
        public const string PåVei = "på vei";
        public const string Fremme = "fremme";
        public const string Fullført = "fullført";
        public static readonly string[] Rekkefølge = [Bekreftet, PåVei, Fremme, Fullført];
    }

    public static class Resultat
    {
        public const string Godkjent = "godkjent";
        public const string Avvist = "avvist";
    }

    /// <summary>Gjør en statusverdi pen for visning: «venter på verifisering» → «Venter på verifisering».</summary>
    public static string Visning(string? verdi) =>
        string.IsNullOrEmpty(verdi) ? "Ukjent" : char.ToUpperInvariant(verdi[0]) + verdi[1..];
}
