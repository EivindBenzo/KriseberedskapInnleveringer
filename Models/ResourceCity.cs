using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HeimevernetInnlevering1.Models;

public sealed record ResourceCity(string Name, int Count)
{
    public static string Normalize(string? value) =>
        Regex.Replace((value ?? "").Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ");

    public static string? FromLocation(string? location)
    {
        var label = Normalize(location);
        // Reverse geocoding stores "street house-number, locality", or just locality.
        // Its coordinate-only fallback is not a city.
        if (label.Length == 0 || label.Any(char.IsControl) || label.Contains('<') || label.Contains('>') ||
            Regex.IsMatch(label, @"^[+-]?\d+(?:\.\d+)?,\s*[+-]?\d+(?:\.\d+)?$")) return null;
        var city = Normalize(label.Split(',').Last());
        city = Regex.Replace(city, @"^\d{4}\s+", "");
        return city.Length is > 0 and <= 120 && city.Any(char.IsLetter) ? city : null;
    }

    public static IReadOnlyList<ResourceCity> Aggregate(IEnumerable<MapSubmission> resources) =>
        resources.Select(r => FromLocation(r.LocationName)).OfType<string>()
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResourceCity(g.OrderBy(c => c == c.ToLowerInvariant() || c == c.ToUpperInvariant())
                .ThenBy(c => c, StringComparer.Ordinal).First(), g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Create(CultureInfo.GetCultureInfo("nb-NO"), true))
            .ToList();
}
