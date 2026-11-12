using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HeimevernetInnlevering1.Models;

public sealed record ResourceCity(string Name, int Count)
{
    private static readonly HashSet<string> AdministrativeSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Agder", "Innlandet", "Akershus", "Buskerud", "Finnmark", "Møre og Romsdal", "Nordland",
        "Østfold", "Rogaland", "Telemark", "Troms", "Trøndelag", "Vestfold", "Vestland", "Oslo", "Norge"
    };

    public static string Normalize(string? value) =>
        Regex.Replace((value ?? "").Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ");

    public static string? FromLocation(string? location)
    {
        var label = Regex.Replace(Normalize(location), @"^Nær\s+", "");
        // Reverse geocoding stores "street house-number, locality", or just locality.
        // Its coordinate-only fallback is not a city.
        if (label.Length == 0 || label.Any(char.IsControl) || label.Contains('<') || label.Contains('>') ||
            Regex.IsMatch(label, @"^[+-]?\d+(?:\.\d+)?,\s*[+-]?\d+(?:\.\d+)?$")) return null;
        var parts = label.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(part => part.Length > 0).ToArray();
        var city = parts.LastOrDefault() ?? string.Empty;
        if (parts.Length > 1 && AdministrativeSuffixes.Contains(city))
            city = parts[^2];
        city = Regex.Replace(city, @"^\d{4}\s+", "");
        return city.Length is > 0 and <= 120 && city.Any(char.IsLetter) ? city : null;
    }

    public static string? FromResource(Ressurs resource) =>
        FromLocation(HeimevernetInnlevering1.Services.NorwegianPlaces.Display(resource.Stedsnavn, resource.Breddegrad, resource.Lengdegrad));

    public static IReadOnlyList<ResourceCity> Aggregate(IEnumerable<Ressurs> resources) =>
        resources.Select(FromResource).OfType<string>()
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResourceCity(g.OrderBy(c => c == c.ToLowerInvariant() || c == c.ToUpperInvariant())
                .ThenBy(c => c, StringComparer.Ordinal).First(), g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Create(CultureInfo.GetCultureInfo("nb-NO"), true))
            .ToList();
}
