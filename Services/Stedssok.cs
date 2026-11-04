using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeimevernetInnlevering1.Services;

/// <summary>
/// Søker etter norske byer og tettsteder for «Finn sted» på kartet.
/// Søket skjer på serveren i en lokal liste (wwwroot/data/norway-localities.json, GeoNames),
/// så det som skrives sendes ikke til andre nettsteder. Finnes ikke stedet i listen,
/// spørres OpenStreetMap (Nominatim) via <see cref="IPlaceGeocoder"/>.
/// </summary>
public sealed class Stedssok(IWebHostEnvironment miljø, IPlaceGeocoder geocoder)
{
    private const int MaksTreff = 5;
    private readonly Lazy<IReadOnlyList<Sted>> _steder = new(() => LesSteder(miljø.WebRootPath));

    private sealed record Sted(string Label, double Lat, double Lng, string Nøkkel);

    public async Task<IReadOnlyList<PlaceResult>> SøkAsync(string? tekst, CancellationToken avbryt = default)
    {
        if (!PlaceGeocoder.ValidQuery(tekst)) return [];
        var nøkkel = Normaliser(tekst!);

        // Beste treff først: nøyaktig navn, så navn som starter med teksten, så navn som inneholder den.
        var treff = _steder.Value
            .Select(s => new { Sted = s, Navn = s.Nøkkel.Split(',')[0] })
            .Where(x => x.Sted.Nøkkel.Contains(nøkkel))
            .OrderBy(x => x.Navn == nøkkel ? 0 : x.Navn.StartsWith(nøkkel) ? 1 : 2)
            .Take(MaksTreff)
            .Select(x => new PlaceResult(x.Sted.Lat, x.Sted.Lng, x.Sted.Label))
            .ToList();

        return treff.Count > 0 ? treff : await geocoder.SearchAsync(tekst!, avbryt);
    }

    /// <summary>Små bokstaver uten aksenter, slik at «tromso» også finner «Tromsø».</summary>
    public static string Normaliser(string tekst)
    {
        var bokstaver = tekst.Trim().ToLower(CultureInfo.GetCultureInfo("nb-NO"))
            .Replace('ø', 'o').Replace('æ', 'a')
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(bokstaver.ToArray());
    }

    private static IReadOnlyList<Sted> LesSteder(string webroot)
    {
        var fil = Path.Combine(webroot, "data", "norway-localities.json");
        if (!File.Exists(fil)) return [];
        using var json = JsonDocument.Parse(File.ReadAllText(fil));
        return json.RootElement.EnumerateArray()
            .Select(e => new Sted(e.GetProperty("label").GetString() ?? "",
                e.GetProperty("lat").GetDouble(), e.GetProperty("lng").GetDouble(), ""))
            .Where(s => s.Label.Length > 0)
            .Select(s => s with { Nøkkel = Normaliser(s.Label) })
            .ToList();
    }
}
