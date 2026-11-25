using System.Reflection;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MariaDbIntegrationTests;

/// <summary>
/// Kriseledelsen kan endre kontaktinformasjon, slette brukere uten data og deaktivere brukere med data.
/// </summary>
public class BrukerAdminTests
{
    private static BrukerAdminService Admin(TestDatabase test) => new(test.Users, test.Db);

    [Fact]
    public async Task BrukerUtenDataSlettesForGodt()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (leder, _) = await test.BrukerAsync(AppRoles.Kriseledelse);
        var (bruker, _) = await test.BrukerAsync(AppRoles.OffentligAktor);

        Assert.Equal(BrukerHandlingResultat.Slettet, await Admin(test).FjernAsync(bruker.Id, leder.Id));
        test.Db.ChangeTracker.Clear();
        Assert.False(await test.Db.Users.AnyAsync(u => u.Id == bruker.Id));
    }

    [Fact]
    public async Task BrukerMedDataDeaktiveresOgHistorikkenBeholdes()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (leder, _) = await test.BrukerAsync(AppRoles.Kriseledelse);
        var (bruker, _) = await test.BrukerAsync(AppRoles.DispatchTeam);
        test.Db.Meldinger.Add(new Melding { SendtAvID = bruker.Id, MottakerID = leder.Id, Emne = "Test", Innhold = "Hei" });
        await test.Db.SaveChangesAsync();

        Assert.Equal(BrukerHandlingResultat.Deaktivert, await Admin(test).FjernAsync(bruker.Id, leder.Id));

        var lagret = await test.Users.FindByIdAsync(bruker.Id.ToString());
        Assert.NotNull(lagret);
        Assert.True(BrukerAdminService.ErDeaktivert(lagret!));
        Assert.True(await test.Users.IsLockedOutAsync(lagret!));
        Assert.Equal(1, await test.Db.Meldinger.CountAsync(m => m.SendtAvID == bruker.Id));
    }

    [Fact]
    public async Task DeaktivertBrukerKanAktiveresIgjen()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (leder, _) = await test.BrukerAsync(AppRoles.Kriseledelse);
        var (bruker, _) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        test.Db.Meldinger.Add(new Melding { SendtAvID = leder.Id, MottakerID = bruker.Id, Emne = "Test", Innhold = "Hei" });
        await test.Db.SaveChangesAsync();
        var admin = Admin(test);
        Assert.Equal(BrukerHandlingResultat.Deaktivert, await admin.FjernAsync(bruker.Id, leder.Id));

        Assert.Equal(BrukerHandlingResultat.Aktivert, await admin.AktiverAsync(bruker.Id, leder.Id));

        var lagret = await test.Users.FindByIdAsync(bruker.Id.ToString());
        Assert.False(BrukerAdminService.ErDeaktivert(lagret!));
        Assert.False(await test.Users.IsLockedOutAsync(lagret!));
    }

    [Fact]
    public async Task KriseledelsenKanIkkeSletteEllerDeaktivereSegSelv()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (leder, _) = await test.BrukerAsync(AppRoles.Kriseledelse);
        var admin = Admin(test);

        Assert.Equal(BrukerHandlingResultat.EgenKonto, await admin.FjernAsync(leder.Id, leder.Id));
        Assert.Equal(BrukerHandlingResultat.EgenKonto, await admin.AktiverAsync(leder.Id, leder.Id));
        Assert.NotNull(await test.Users.FindByIdAsync(leder.Id.ToString()));
    }

    [Fact]
    public async Task KontaktinformasjonOppdateresMenEpostEndresIkke()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (bruker, _) = await test.BrukerAsync(AppRoles.OffentligAktor);
        var epost = bruker.Email;

        var resultat = await Admin(test).OppdaterKontaktAsync(bruker.Id, new ProfileEditModel
            { Fornavn = " Kari ", Etternavn = "Nordmann", Telefon = "91234567", Organisasjon = "  " });

        Assert.Equal(BrukerHandlingResultat.Lagret, resultat);
        var lagret = await test.Users.FindByIdAsync(bruker.Id.ToString());
        Assert.Equal("Kari", lagret!.Fornavn);
        Assert.Equal("Nordmann", lagret.Etternavn);
        Assert.Equal("91234567", lagret.PhoneNumber);
        Assert.Null(lagret.Organisasjon);
        Assert.Equal(epost, lagret.Email);
    }

    [Fact]
    public async Task UkjentBrukerGirIkkeFunnet()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (leder, _) = await test.BrukerAsync(AppRoles.Kriseledelse);
        var admin = Admin(test);

        Assert.Equal(BrukerHandlingResultat.IkkeFunnet, await admin.FjernAsync(999_999, leder.Id));
        Assert.Equal(BrukerHandlingResultat.IkkeFunnet, await admin.OppdaterKontaktAsync(999_999,
            new ProfileEditModel { Fornavn = "A", Etternavn = "B", Telefon = "40000000" }));
    }

    [Fact]
    public async Task EiereAvDemodataRegnesSomBrukereMedData()
    {
        await using var test = await TestDatabase.CreateAsync(demoData: true);
        var (ny, _) = await test.BrukerAsync(AppRoles.OffentligAktor);
        var medData = await Admin(test).BrukereMedDataAsync();

        foreach (var eier in await test.Db.Ressurser.Select(r => r.EierID).Distinct().ToListAsync())
            Assert.Contains(eier, medData);
        Assert.DoesNotContain(ny.Id, medData);
    }

    [Theory]
    [InlineData(nameof(AdminController.EditUser))]
    [InlineData(nameof(AdminController.RemoveUser))]
    [InlineData(nameof(AdminController.ReactivateUser))]
    public void BareKriseledelsenNårHandlingeneOgDeKreverPostMedAntiforgery(string handling)
    {
        var tilgang = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AppRoles.Kriseledelse, tilgang?.Roles);

        var metode = typeof(AdminController).GetMethod(handling)!;
        Assert.NotNull(metode.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(metode.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(metode.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
