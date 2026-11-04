using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace MariaDbIntegrationTests;

/// <summary>
/// Tester det som tidligere ble gjort med JavaScript i nettleseren, men som nå gjøres i C# på serveren:
/// symbol per ressurstype, «Annet»-filteret, «Finn sted» og bytte av tema.
/// </summary>
public class KartOgTemaTests
{
    [Fact]
    public void FasteTyperHarEgetSymbolOgEgneTyperDelerAnnetSymbolet()
    {
        Assert.Equal("car", RessursIkon.Fil("Kjøretøy"));
        Assert.Equal("toolbox", RessursIkon.Fil("Utstyr"));
        Assert.Equal("other", RessursIkon.Fil("Snøscooter"));
        Assert.Equal("unknown", RessursIkon.Fil(""));
        Assert.Equal(RessursIkon.Annet, RessursIkon.Gruppe("Snøscooter"));
        Assert.Equal("Drone", RessursIkon.Gruppe("Drone"));
        Assert.Equal(7, RessursIkon.Grupper.Count);

        var tilgjengelig = new ResourceOperationalStatus(true, "Tilgjengelig", "Godkjent", null, null, "");
        var opptatt = new ResourceOperationalStatus(false, "Tildelt", "Godkjent", null, null, "");
        Assert.Equal("~/images/resources/car-available.svg", RessursIkon.Sti("Kjøretøy", tilgjengelig));
        Assert.Equal("~/images/resources/other-unavailable.svg", RessursIkon.Sti("Båt", opptatt));
        Assert.Equal("~/images/resources/drone.svg", RessursIkon.Sti("Drone"));
    }

    [Fact]
    public async Task AnnetFilteretViserBareTyperBrukerneHarLagtTilSelv()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (_, eier) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        await test.Workflow.RegisterResourceAsync(Skjema("Lastebil", "Kjøretøy"), eier);
        await test.Workflow.RegisterResourceAsync(Skjema("Snøscooter 1", "Snøscooter"), eier);

        var annet = await ResourceQueries.LoadAsync(test.Db, new ResourceFilter(Type: RessursIkon.Annet), DateTime.UtcNow);
        Assert.Equal(["Snøscooter 1"], annet.Select(r => r.Navn));
        var kjøretøy = await ResourceQueries.LoadAsync(test.Db, new ResourceFilter(Type: "Kjøretøy"), DateTime.UtcNow);
        Assert.Equal(["Lastebil"], kjøretøy.Select(r => r.Navn));
    }

    [Fact]
    public async Task FinnStedSøkerLokaltUtenAksenterOgSpørOpenStreetMapBareNårDetIkkeFinnes()
    {
        var geocoder = new FalskGeocoder();
        var søk = new Stedssok(new Miljø(), geocoder);

        var tromsø = await søk.SøkAsync("tromso");
        Assert.StartsWith("Tromsø", tromsø[0].Label);
        Assert.Equal(0, geocoder.Søk);

        var kristiansand = await søk.SøkAsync("Kristiansand");
        Assert.StartsWith("Kristiansand", kristiansand[0].Label);

        Assert.Empty(await søk.SøkAsync("x"));
        Assert.Equal(0, geocoder.Søk);
        await søk.SøkAsync("Et sted som ikke finnes");
        Assert.Equal(1, geocoder.Søk);
    }

    [Fact]
    public void TemaKnappenLagrerValgetICookieOgSenderBareTilLokaleAdresser()
    {
        var controller = TemaController();
        var svar = Assert.IsType<LocalRedirectResult>(controller.Bytt("/Home/Map?type=Drone"));
        Assert.Equal("/Home/Map?type=Drone", svar.Url);
        Assert.Contains("tema=dark", controller.Response.Headers.SetCookie.ToString());

        var tilbakeTilLys = TemaController(cookie: "tema=dark");
        tilbakeTilLys.Bytt("/");
        Assert.Contains("tema=light", tilbakeTilLys.Response.Headers.SetCookie.ToString());

        var annenSide = TemaController();
        Assert.IsType<RedirectToActionResult>(annenSide.Bytt("https://example.com/"));
    }

    private static TemaController TemaController(string? cookie = null)
    {
        var http = new DefaultHttpContext();
        if (cookie is not null) http.Request.Headers.Cookie = cookie;
        var controller = new TemaController { ControllerContext = new ControllerContext { HttpContext = http } };
        controller.Url = new UrlHelper(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        return controller;
    }

    private static RessursSkjemaViewModel Skjema(string navn, string type) => new()
    {
        Navn = navn, Ressurstype = type, TilgjengeligFra = DateOnly.FromDateTime(DateTime.UtcNow),
        Kontaktperson = "Rita", Kontakttelefon = "40000000", Stedsnavn = "Hamar", Breddegrad = 60.79, Lengdegrad = 11.07
    };

    /// <summary>Peker på wwwroot i prosjektet, slik at stedslisten kan leses.</summary>
    private sealed class Miljø : IWebHostEnvironment
    {
        public Miljø()
        {
            var mappe = new DirectoryInfo(AppContext.BaseDirectory);
            while (mappe is not null && !File.Exists(Path.Combine(mappe.FullName, "HeimevernetInnlevering1.csproj")))
                mappe = mappe.Parent;
            WebRootPath = Path.Combine(mappe?.FullName ?? AppContext.BaseDirectory, "wwwroot");
        }

        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class FalskGeocoder : IPlaceGeocoder
    {
        public int Søk { get; private set; }
        public Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaceGeocoder.CoordinateLabel(latitude, longitude));
        public Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Søk++;
            return Task.FromResult<IReadOnlyList<PlaceResult>>([]);
        }
    }
}
