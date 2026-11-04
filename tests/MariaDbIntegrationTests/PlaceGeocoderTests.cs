using System.Diagnostics;
using System.Globalization;
using System.Net;
using HeimevernetInnlevering1.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace MariaDbIntegrationTests;

public class PlaceGeocoderTests
{
    private const string Address = """
        {"lat":"58.1467","lon":"7.9956","display_name":"Unnecessarily long display",
         "address":{"road":"Markens gate","house_number":"1","city":"Kristiansand"}}
        """;

    [Fact]
    public async Task ReverseUsesConciseAddressAndSharedCache()
    {
        var calls = 0;
        using var geocoder = Create((request, _) =>
        {
            calls++;
            Assert.Equal("nominatim.openstreetmap.org", request.RequestUri!.Host);
            Assert.Equal("/reverse", request.RequestUri.AbsolutePath);
            Assert.Contains("KriseberedskapInnleveringer/1.0", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json(Address));
        });
        Assert.Equal("Markens gate 1, Kristiansand", await geocoder.ReverseAsync(58.1467, 7.9956));
        Assert.Equal("Markens gate 1, Kristiansand", await geocoder.ReverseAsync(58.1467, 7.9956));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OffshorePointDoesNotBecomeDistantCityAndFallbackIsInvariant()
    {
        using var geocoder = Create((_, _) => Task.FromResult(Json(Address)));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
            Assert.Equal("57.00000, 7.00000", await geocoder.ReverseAsync(57, 7));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"lat":"NaN","lon":"0","address":{"city":"Invalid"}}""")]
    [InlineData("not json")]
    [InlineData("""{"error":"Unable to geocode"}""")]
    public async Task MalformedResponsesFallBack(string response)
    {
        using var geocoder = Create((_, _) => Task.FromResult(Json(response)));
        Assert.Equal("0.00000, 0.00000", await geocoder.ReverseAsync(0, 0));
    }

    [Fact]
    public async Task DisabledOrInsecureConfigurationDoesNotSendRequests()
    {
        var calls = 0;
        Task<HttpResponseMessage> Handler(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            calls++;
            return Task.FromResult(Json(Address));
        }
        using var disabled = Create(Handler, new GeocodingOptions { Enabled = false });
        Assert.Equal("0.00000, 0.00000", await disabled.ReverseAsync(0, 0));
        Assert.Empty(await disabled.SearchAsync("Oslo"));
        using var insecure = Create(Handler, new GeocodingOptions { BaseUrl = "http://example.test/" });
        Assert.Empty(await insecure.SearchAsync("Oslo"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExplicitSearchIsBoundedPlainAndUsesOnlyConfiguredHost()
    {
        var calls = 0;
        using var geocoder = Create((request, _) =>
        {
            calls++;
            Assert.Equal("operator.example", request.RequestUri!.Host);
            Assert.Equal("/api/search", request.RequestUri.AbsolutePath);
            Assert.Contains("limit=5", request.RequestUri.Query);
            Assert.Contains("q=https%3A%2F%2Fattacker.example%2F", request.RequestUri.Query);
            var item = """{"lat":"59.91","lon":"10.75","display_name":"<b>Oslo</b>, Norge"}""";
            return Task.FromResult(Json("[" + string.Join(",", Enumerable.Repeat(item, 10)) + "]"));
        }, new GeocodingOptions { BaseUrl = "https://operator.example/api/" });
        var results = await geocoder.SearchAsync("https://attacker.example/");
        Assert.Equal(5, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal("Oslo, Norge", result.Label);
            Assert.Equal(59.91, result.Lat);
            Assert.Equal(10.75, result.Lng);
        });
        Assert.Equal(results, await geocoder.SearchAsync("https://attacker.example/"));
        Assert.Empty(await geocoder.SearchAsync(new string('x', 121)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SearchRejectsInvalidResultCoordinatesAndTruncatesLabels()
    {
        using var geocoder = Create((_, _) => Task.FromResult(Json(
            $$"""[{"lat":"NaN","lon":"10","display_name":"Bad"},{"lat":"91","lon":"0","display_name":"Bad"},{"lat":"1","lon":"2","display_name":"{{new string('x', 500)}}"}]""")));
        var result = Assert.Single(await geocoder.SearchAsync("query"));
        Assert.Equal(200, result.Label.Length);
    }

    [Fact]
    public async Task ConcurrentWorkIsRejectedWithoutQueueing()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var geocoder = Create(async (_, cancellationToken) =>
        {
            calls++;
            started.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Json(Address);
        });
        var first = geocoder.ReverseAsync(58.1467, 7.9956);
        await started.Task;
        var competing = geocoder.ReverseAsync(0, 0);
        Assert.True(competing.IsCompleted);
        Assert.Equal("0.00000, 0.00000", await competing);
        Assert.Empty(await geocoder.SearchAsync("Oslo"));
        release.SetResult();
        Assert.Equal("Markens gate 1, Kristiansand", await first);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReverseAndSearchShareOneRequestPerSecondBudget()
    {
        var starts = new List<long>();
        using var geocoder = Create((_, _) =>
        {
            starts.Add(Stopwatch.GetTimestamp());
            return Task.FromResult(Json(Address));
        });
        await geocoder.ReverseAsync(58.1467, 7.9956);
        await geocoder.SearchAsync("Oslo");
        Assert.Equal(2, starts.Count);
        Assert.True(Stopwatch.GetElapsedTime(starts[0], starts[1]) >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TimeoutAndNetworkErrorsFallBackAndReleaseGate()
    {
        var calls = 0;
        using var geocoder = Create(async (_, cancellationToken) =>
        {
            calls++;
            if (calls == 1) await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new HttpRequestException("unavailable");
        }, new GeocodingOptions { TimeoutSeconds = 1 });
        var stopwatch = Stopwatch.StartNew();
        Assert.Equal("0.00000, 0.00000", await geocoder.ReverseAsync(0, 0));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4));
        Assert.Equal("1.00000, 1.00000", await geocoder.ReverseAsync(1, 1));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NonSuccessResponsesFallBack(HttpStatusCode status)
    {
        using var geocoder = Create((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        Assert.Equal("0.00000, 0.00000", await geocoder.ReverseAsync(0, 0));
    }

    [Fact]
    public async Task OversizedResponseFallsBack()
    {
        using var geocoder = Create((_, _) => Task.FromResult(Json(new string('x', 65537))));
        Assert.Empty(await geocoder.SearchAsync("Oslo"));
    }

    private static PlaceGeocoder Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        GeocodingOptions? options = null) =>
        new(new HttpClient(new Handler(handler)), Options.Create(options ?? new GeocodingOptions()));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
