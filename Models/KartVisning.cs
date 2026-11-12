using HeimevernetInnlevering1.Services;

namespace HeimevernetInnlevering1.Models;

/// <summary>
/// Alt kartet skal vise, laget på serveren. Siden gjør dette om til JSON, og det lille
/// skriptet wwwroot/js/kart.js tegner det med Leaflet. Skriptet bestemmer ingenting selv.
/// </summary>
public sealed record KartVisning(double Lat, double Lng, int Zoom)
{
    /// <summary>Kristiansand, der kartet starter når ingenting annet er valgt.</summary>
    public static KartVisning Standard { get; } = new(58.1467, 7.9956, 12);

    /// <summary>Melding fra «Finn sted», f.eks. «Viser Arendal».</summary>
    public string? Melding { get; init; }

    /// <summary>Andre steder som passet søket, vist som lenker under søkefeltet.</summary>
    public IReadOnlyList<PlaceResult> Stedsforslag { get; init; } = [];
    public IReadOnlyList<KartMarkor> Markorer { get; init; } = [];

    /// <summary>Satt når brukeren skal klikke i kartet for å velge en plassering i et skjema.</summary>
    public KartPlassering? Plassering { get; init; }
}

/// <summary>En markør på kartet. <c>Popup</c> er ferdig HTML der all tekst allerede er kodet (encoded) på serveren.</summary>
public sealed record KartMarkor(double Lat, double Lng, string Ikon, string Tittel, string Popup, bool Apen = false,
    bool Akutt = false, string? LeveranseStatus = null, int? RessursId = null, bool KanForespørres = false);

/// <summary>Hvilke skjemafelt et klikk i kartet skal fylle ut, og eventuelt punktet som allerede er valgt.</summary>
public sealed record KartPlassering(string LatFelt, string LngFelt, string TekstFelt, double? Lat = null, double? Lng = null);
