using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MariaDbIntegrationTests;

public class SeedingOgRegistreringTests
{
    [Fact]
    public async Task DemodataGirEnKontoPerRolleOgKanKjøresFlereGanger()
    {
        await using var test = await TestDatabase.CreateAsync(demoData: true);
        foreach (var (email, role, fornavn, _, _) in IdentitySeeder.DemoAccounts)
        {
            var user = await test.Users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(fornavn, user!.Fornavn);
            Assert.Equal([role], await test.Users.GetRolesAsync(user));
        }
        var ressurser = await test.Db.Ressurser.CountAsync();
        Assert.True(ressurser >= 4);
        Assert.True(await test.Db.Tildelinger.AnyAsync());
        Assert.True(await test.Db.Hendelser.AnyAsync());

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["SEED_DEMO_ACCOUNTS"] = "true" }).Build();
        await IdentitySeeder.SeedAsync(test.Scope.ServiceProvider, config);
        Assert.Equal(ressurser, await test.Db.Ressurser.CountAsync());
    }

    private static AccountController Account(TestDatabase test)
    {
        var services = test.Scope.ServiceProvider;
        var http = new DefaultHttpContext { RequestServices = services };
        return new AccountController(services.GetRequiredService<UserManager<Bruker>>(),
            services.GetRequiredService<SignInManager<Bruker>>(), services.GetRequiredService<HeimevernetInnlevering1.Services.ShellService>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempData()),
            Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(new ActionContext(http,
                new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()))
        };
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static RegisterViewModel Skjema(string rolle) => new()
    {
        Fornavn = "Nina", Etternavn = "Nordmann", Telefon = "91234567", Organisasjon = "Test AS",
        Email = $"nina-{Guid.NewGuid():N}@test.no", Role = rolle, Password = TestDatabase.Passord, ConfirmPassword = TestDatabase.Passord
    };

    [Fact]
    public async Task RegistreringLagrerNavnOgTelefonITabellenBruker()
    {
        await using var test = await TestDatabase.CreateAsync();
        var model = Skjema(AppRoles.OffentligAktor);
        var result = await Account(test).Register(model);
        Assert.IsType<RedirectToActionResult>(result);
        var bruker = await test.Users.FindByEmailAsync(model.Email);
        Assert.Equal("Nina", bruker!.Fornavn);
        Assert.Equal("91234567", bruker.PhoneNumber);
        Assert.Equal("Test AS", bruker.Organisasjon);
        Assert.True(await test.Users.IsInRoleAsync(bruker, AppRoles.OffentligAktor));
        Assert.NotEqual(TestDatabase.Passord, bruker.PasswordHash);
    }

    [Fact]
    public async Task PassordUtenSpesialtegnGodtas()
    {
        await using var test = await TestDatabase.CreateAsync();
        var model = Skjema(AppRoles.Ressursleverandor);
        model.Password = model.ConfirmPassword = "Heimevernet2026";
        Assert.IsType<RedirectToActionResult>(await Account(test).Register(model));
        Assert.NotNull(await test.Users.FindByEmailAsync(model.Email));
    }

    [Theory]
    [InlineData(AppRoles.Kriseledelse)]
    [InlineData(AppRoles.DispatchTeam)]
    public async Task ManKanIkkeGiSegSelvEnBeskyttetRolleVedRegistrering(string rolle)
    {
        await using var test = await TestDatabase.CreateAsync();
        var model = Skjema(rolle);
        var result = await Account(test).Register(model);
        Assert.IsType<ViewResult>(result);
        Assert.Null(await test.Users.FindByEmailAsync(model.Email));
    }

    [Fact]
    public async Task BootstrapAdminGirKriseledelseOgKreverRiktigPassordForEksisterendeKonto()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (bruker, _) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        var feil = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ADMIN_EMAIL"] = bruker.Email, ["ADMIN_PASSWORD"] = "feil-passord-123!" }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => IdentitySeeder.SeedAsync(test.Scope.ServiceProvider, feil));
        Assert.False(await test.Users.IsInRoleAsync(bruker, AppRoles.Kriseledelse));

        var riktig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ADMIN_EMAIL"] = bruker.Email, ["ADMIN_PASSWORD"] = TestDatabase.Passord }).Build();
        await IdentitySeeder.SeedAsync(test.Scope.ServiceProvider, riktig);
        Assert.True(await test.Users.IsInRoleAsync(bruker, AppRoles.Kriseledelse));
    }
}
