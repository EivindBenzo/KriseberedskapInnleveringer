using System.Text.RegularExpressions;

namespace HeimevernetInnlevering1.Services;

/// <summary>
/// Inneholder norske byer og tettsteder, slik at et punkt alltid kan vises med et stedsnavn («Kristiansand»)
/// i stedet for koordinater, også når oppslaget mot Nominatim feiler.
/// </summary>
public static partial class NorwegianPlaces
{
    private static readonly (string Name, double Lat, double Lng)[] Places =
    [
        ("Oslo", 59.9139, 10.7522), ("Bergen", 60.3913, 5.3221), ("Trondheim", 63.4305, 10.3951),
        ("Stavanger", 58.9700, 5.7331), ("Kristiansand", 58.1467, 7.9956), ("Tromsø", 69.6492, 18.9553),
        ("Drammen", 59.7441, 10.2045), ("Fredrikstad", 59.2181, 10.9298), ("Sarpsborg", 59.2839, 11.1096),
        ("Skien", 59.2096, 9.6090), ("Porsgrunn", 59.1405, 9.6561), ("Ålesund", 62.4722, 6.1549),
        ("Sandnes", 58.8524, 5.7352), ("Haugesund", 59.4138, 5.2680), ("Tønsberg", 59.2676, 10.4076),
        ("Moss", 59.4340, 10.6577), ("Sandefjord", 59.1312, 10.2167), ("Arendal", 58.4616, 8.7725),
        ("Bodø", 67.2804, 14.4049), ("Larvik", 59.0530, 10.0277), ("Halden", 59.1235, 11.3875),
        ("Lillehammer", 61.1153, 10.4662), ("Hamar", 60.7945, 11.0680), ("Gjøvik", 60.7957, 10.6916),
        ("Molde", 62.7375, 7.1591), ("Harstad", 68.7986, 16.5413), ("Kongsberg", 59.6689, 9.6502),
        ("Askøy", 60.4000, 5.2000), ("Kristiansund", 63.1103, 7.7280), ("Horten", 59.4172, 10.4833),
        ("Ringsaker", 60.8860, 10.8290), ("Elverum", 60.8819, 11.5623), ("Kongsvinger", 60.1904, 12.0014),
        ("Narvik", 68.4385, 17.4272), ("Alta", 69.9689, 23.2716), ("Hammerfest", 70.6634, 23.6821),
        ("Vadsø", 70.0744, 29.7490), ("Kirkenes", 69.7271, 30.0450), ("Mo i Rana", 66.3128, 14.1428),
        ("Mosjøen", 65.8365, 13.1936), ("Namsos", 64.4660, 11.4950), ("Steinkjer", 64.0149, 11.4954),
        ("Levanger", 63.7467, 11.2995), ("Stjørdal", 63.4690, 10.9250), ("Orkanger", 63.3050, 9.8490),
        ("Røros", 62.5745, 11.3840), ("Tynset", 62.2750, 10.7830), ("Otta", 61.7710, 9.5360),
        ("Dombås", 62.0740, 9.1230), ("Geilo", 60.5340, 8.2020), ("Hønefoss", 60.1680, 10.2570),
        ("Ski", 59.7190, 10.8350), ("Lillestrøm", 59.9560, 11.0490), ("Jessheim", 60.1410, 11.1750),
        ("Gardermoen", 60.1939, 11.1004), ("Asker", 59.8330, 10.4390), ("Bærum", 59.8940, 10.5450),
        ("Lørenskog", 59.9270, 10.9580), ("Sørumsand", 59.9830, 11.2370), ("Eidsvoll", 60.3290, 11.2610),
        ("Brumunddal", 60.8810, 10.9390), ("Moelv", 60.9310, 10.7020), ("Biri", 60.9490, 10.5990),
        ("Gran", 60.3590, 10.5620), ("Raufoss", 60.7240, 10.6170), ("Fagernes", 60.9860, 9.2330),
        ("Kongsvinger", 60.1904, 12.0014), ("Mosby", 58.1990, 8.0600), ("Vennesla", 58.2680, 7.9700),
        ("Lillesand", 58.2530, 8.3790), ("Grimstad", 58.3405, 8.5934), ("Mandal", 58.0290, 7.4600),
        ("Farsund", 58.0950, 6.8040), ("Flekkefjord", 58.2970, 6.6620), ("Egersund", 58.4510, 6.0010),
        ("Bryne", 58.7330, 5.6460), ("Sauda", 59.6490, 6.3540), ("Odda", 60.0690, 6.5460),
        ("Voss", 60.6280, 6.4150), ("Førde", 61.4520, 5.8570), ("Florø", 61.5990, 5.0330),
        ("Måløy", 61.9350, 5.1130), ("Sogndal", 61.2290, 7.1000), ("Lærdal", 61.1000, 7.4800),
        ("Stryn", 61.9060, 6.7190), ("Volda", 62.1470, 6.0700), ("Ørsta", 62.2000, 6.1300),
        ("Ulsteinvik", 62.3430, 5.8490), ("Sunndalsøra", 62.6740, 8.5620), ("Orkdal", 63.2980, 9.8470),
        ("Brønnøysund", 65.4740, 12.2130), ("Sandnessjøen", 66.0200, 12.6310), ("Fauske", 67.2590, 15.3930),
        ("Svolvær", 68.2340, 14.5680), ("Leknes", 68.1470, 13.6120), ("Sortland", 68.6930, 15.4130),
        ("Finnsnes", 69.2300, 17.9770), ("Bardufoss", 69.0560, 18.5400), ("Setermoen", 68.8650, 18.3480),
        ("Karasjok", 69.4720, 25.5110), ("Kautokeino", 69.0120, 23.0410), ("Honningsvåg", 70.9820, 25.9700),
        ("Vardø", 70.3700, 31.1090), ("Longyearbyen", 78.2232, 15.6267), ("Skjetten", 59.9620, 11.0280),
        ("Nesoddtangen", 59.8590, 10.6590), ("Drøbak", 59.6620, 10.6270), ("Holmestrand", 59.4890, 10.3140),
        ("Notodden", 59.5600, 9.2590), ("Rjukan", 59.8780, 8.5940), ("Kragerø", 58.8690, 9.4120),
        ("Risør", 58.7230, 9.2350), ("Åmli", 58.7640, 8.4850), ("Evje", 58.5880, 7.8040),
        ("Bø", 59.4110, 9.0640), ("Sandvika", 59.8900, 10.5240), ("Stathelle", 59.0440, 9.6850)
    ];

    private const double MaxNearbyKm = 30;
    private const double SameTownKm = 6;

    /// <summary>Stedsnavn for et punkt: byen/tettstedet hvis det er tett på, ellers «Nær …». Null hvis ingenting er i nærheten.</summary>
    public static string? Describe(double latitude, double longitude)
    {
        var best = Places.Select(p => (p.Name, Km: DistanceKm(latitude, longitude, p.Lat, p.Lng)))
            .OrderBy(p => p.Km).First();
        if (best.Km <= SameTownKm) return best.Name;
        return best.Km <= MaxNearbyKm ? $"Nær {best.Name}" : null;
    }

    /// <summary>Koordinater skrevet som tekst, f.eks. «60.99665, 10.25703», er ikke et stedsnavn.</summary>
    public static bool IsCoordinateLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) || CoordinatePattern().IsMatch(label.Trim());

    /// <summary>Tekst som vises for et sted: lagret stedsnavn, eller et navn funnet ut fra koordinatene.</summary>
    public static string Display(string? stored, decimal latitude, decimal longitude) =>
        !IsCoordinateLabel(stored) ? stored!.Trim()
            : Describe((double)latitude, (double)longitude) ?? stored?.Trim() ?? "";

    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }

    [GeneratedRegex(@"^[+-]?\d+(?:\.\d+)?,\s*[+-]?\d+(?:\.\d+)?$")]
    private static partial Regex CoordinatePattern();
}
