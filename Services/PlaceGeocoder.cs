using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HeimevernetInnlevering1.Services;

public sealed class GeocodingOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";
    public string UserAgent { get; set; } =
        "KriseberedskapInnleveringer/1.0 (+https://github.com/EivindBenzo/KriseberedskapInnleveringer)";
    public int TimeoutSeconds { get; set; } = 3;
}

public record PlaceResult(double Lat, double Lng, string Label);

public interface IPlaceGeocoder
{
    Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

// One singleton owns the cache and the zero-queue gate, including across users and endpoints.
public sealed class PlaceGeocoder : IPlaceGeocoder, IDisposable
{
    private readonly HttpClient _http;
    private readonly GeocodingOptions _options;
    private readonly Uri? _baseUri;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 512 });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastRequest;
    private bool _hasRequested;

    public PlaceGeocoder(HttpClient http, IOptions<GeocodingOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            _baseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static bool ValidPoint(double? latitude, double? longitude) =>
        latitude is double lat && longitude is double lng
        && double.IsFinite(lat) && double.IsFinite(lng)
        && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;

    public static string CoordinateLabel(double latitude, double longitude) =>
        $"{latitude.ToString("F5", CultureInfo.InvariantCulture)}, {longitude.ToString("F5", CultureInfo.InvariantCulture)}";

    public static bool ValidQuery(string? query) =>
        query is not null && query.Trim().Length is >= 2 and <= 120 && !query.Any(char.IsControl);

    public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        if (!ValidPoint(latitude, longitude)) throw new ArgumentOutOfRangeException(nameof(latitude));
        var fallback = CoordinateLabel(latitude, longitude);
        var point = $"lat={latitude.ToString("R", CultureInfo.InvariantCulture)}&lon={longitude.ToString("R", CultureInfo.InvariantCulture)}";
        var key = "reverse:" + point;
        if (_cache.TryGetValue<string>(key, out var cached)) return cached!;
        using var json = await RequestAsync($"reverse?format=jsonv2&addressdetails=1&zoom=18&{point}", cancellationToken);
        var label = json is null ? null : ReverseLabel(json.RootElement, latitude, longitude);
        var result = label ?? fallback;
        _cache.Set(key, result, new MemoryCacheEntryOptions().SetSize(1)
            .SetAbsoluteExpiration(label is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromHours(24)));
        return result;
    }

    public async Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!ValidQuery(query)) return Array.Empty<PlaceResult>();
        query = query.Trim();
        var key = "search:" + query.ToUpperInvariant();
        if (_cache.TryGetValue<PlaceResult[]>(key, out var cached)) return cached!;
        using var json = await RequestAsync(
            $"search?format=jsonv2&addressdetails=1&limit=5&q={Uri.EscapeDataString(query)}", cancellationToken);
        var results = new List<PlaceResult>();
        if (json?.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in json.RootElement.EnumerateArray().Take(5))
            {
                if (TryPoint(item, out var lat, out var lng) && Plain(Text(item, "display_name")) is { } label)
                    results.Add(new PlaceResult(lat, lng, label));
            }
        }
        var array = results.ToArray();
        _cache.Set(key, array, new MemoryCacheEntryOptions().SetSize(1)
            .SetAbsoluteExpiration(array.Length == 0 ? TimeSpan.FromSeconds(30) : TimeSpan.FromHours(24)));
        return array;
    }

    private async Task<JsonDocument?> RequestAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || _baseUri is null || !await _gate.WaitAsync(0, cancellationToken))
            return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 5)));
            // Space request starts by at least one second; no callers queue behind this work.
            if (_hasRequested)
            {
                var delay = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(_lastRequest);
                while (delay > TimeSpan.Zero)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(delay.TotalMilliseconds)), timeout.Token);
                    delay = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(_lastRequest);
                }
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relativeUrl));
            if (string.IsNullOrWhiteSpace(_options.UserAgent)
                || !request.Headers.UserAgent.TryParseAdd(_options.UserAgent)) return null;
            _lastRequest = Stopwatch.GetTimestamp();
            _hasRequested = true;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (body.Length + count > 65536) return null;
                body.Write(buffer, 0, count);
            }
            return JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? ReverseLabel(JsonElement root, double latitude, double longitude)
    {
        // Nominatim can return a distant city's nearest object for offshore points.
        if (!TryPoint(root, out var lat, out var lng) || DistanceKm(latitude, longitude, lat, lng) > 1
            || !root.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
            return null;
        var road = Text(address, "road") ?? Text(address, "pedestrian") ?? Text(address, "footway");
        var locality = Text(address, "village") ?? Text(address, "town") ?? Text(address, "city")
            ?? Text(address, "hamlet") ?? Text(address, "municipality");
        var street = road is null ? null : string.Join(" ", new[] { road, Text(address, "house_number") }.Where(x => x is not null));
        return Plain(string.Join(", ", new[] { street, locality }.Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }

    private static string? Text(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryPoint(JsonElement root, out double lat, out double lng)
    {
        lat = lng = 0;
        return double.TryParse(Text(root, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(Text(root, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out lng)
            && ValidPoint(lat, lng);
    }

    private static string? Plain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var insideTag = false;
        var characters = new List<char>(200);
        foreach (var character in value)
        {
            if (character == '<') insideTag = true;
            else if (character == '>') insideTag = false;
            else if (!insideTag && !char.IsControl(character)) characters.Add(character);
            if (characters.Count == 200) break;
        }
        var plain = new string(characters.ToArray()).Trim();
        return plain.Length == 0 ? null : plain[..Math.Min(plain.Length, 200)];
    }

    public void Dispose()
    {
        _http.Dispose();
        _cache.Dispose();
        _gate.Dispose();
    }
}
