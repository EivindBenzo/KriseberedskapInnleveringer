using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MariaDbIntegrationTests;

/// <summary>
/// Tester arbeidsflyten fra rapporten mot ekte MariaDB:
/// ressurs → verifisering → forespørsel → svar fra eier → tildeling → fremdrift → returnert.
/// </summary>
public class WorkflowTests
{
    private static RessursSkjemaViewModel Ressurs(string navn = "Minibuss", string type = "Kjøretøy") => new()
    {
        Navn = navn, Ressurstype = type, Beskrivelse = "9 seter", TilgjengeligFra = DateOnly.FromDateTime(DateTime.UtcNow),
        Kontaktperson = "Rita", Kontakttelefon = "40000000", Stedsnavn = "Hamar", Breddegrad = 60.79, Lengdegrad = 11.07
    };

    private static BehovSkjemaViewModel Behov(int behovstype = 1) => new()
    {
        BehovstypeID = behovstype, Beskrivelse = "Transport av seks personer", Prioritet = Statuser.Prioritet.Akutt,
        Antall = 6, Kontaktperson = "Olav", Kontakttelefon = "40000000", Stedsnavn = "Hamar", Breddegrad = 60.8, Lengdegrad = 11.1
    };

    private sealed record Aktører(
        System.Security.Claims.ClaimsPrincipal Eier, System.Security.Claims.ClaimsPrincipal Verifiserer,
        System.Security.Claims.ClaimsPrincipal Dispatch, System.Security.Claims.ClaimsPrincipal Offentlig,
        System.Security.Claims.ClaimsPrincipal Leder);

    private static async Task<Aktører> AktørerAsync(TestDatabase test) => new(
        (await test.BrukerAsync(AppRoles.Ressursleverandor)).Principal,
        (await test.BrukerAsync(AppRoles.Verifiseringsansvarlig)).Principal,
        (await test.BrukerAsync(AppRoles.DispatchTeam)).Principal,
        (await test.BrukerAsync(AppRoles.OffentligAktor)).Principal,
        (await test.BrukerAsync(AppRoles.Kriseledelse)).Principal);

    private static async Task<string> RessursStatus(TestDatabase test, int id)
    {
        test.Db.ChangeTracker.Clear();
        return (await test.Db.Ressurser.SingleAsync(r => r.RessursID == id)).Status;
    }

    [Fact]
    public async Task HeleFlytenFraRegistreringTilReturnert()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);

        var ressursId = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        Assert.Equal(Statuser.Ressurs.VenterPåVerifisering, await RessursStatus(test, ressursId));

        await test.Workflow.VerifyAsync(ressursId, true, a.Verifiserer, null);
        Assert.Equal(Statuser.Ressurs.Tilgjengelig, await RessursStatus(test, ressursId));
        Assert.Equal(1, await test.Db.Verifiseringer.CountAsync(v => v.RessursID == ressursId && v.Resultat == Statuser.Resultat.Godkjent));

        var behovId = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var forespørselId = await test.Workflow.CreateRequestAsync(behovId, ressursId, a.Dispatch);
        Assert.Equal(Statuser.Ressurs.Forespurt, await RessursStatus(test, ressursId));
        Assert.Equal(Statuser.Behov.Vurderes, (await test.Db.Behov.SingleAsync(b => b.BehovID == behovId)).Status);

        await test.Workflow.RespondAsync(forespørselId, true, a.Eier, null);
        await test.Workflow.AllocateAsync(forespørselId, a.Dispatch);
        Assert.Equal(Statuser.Ressurs.Tildelt, await RessursStatus(test, ressursId));
        var tildeling = await test.Db.Tildelinger.SingleAsync(t => t.ForespørselID == forespørselId);
        Assert.Equal(Statuser.Fremdrift.Bekreftet, tildeling.Fremdrift);
        Assert.Equal(Statuser.Behov.Tildelt, (await test.Db.Behov.SingleAsync(b => b.BehovID == behovId)).Status);

        foreach (var steg in new[] { Statuser.Fremdrift.PåVei, Statuser.Fremdrift.Fremme, Statuser.Fremdrift.Fullført })
            await test.Workflow.UpdateAssignmentAsync(tildeling.TildelingID, steg, a.Dispatch, null);
        Assert.Equal(Statuser.Ressurs.Returnert, await RessursStatus(test, ressursId));
        Assert.NotNull((await test.Db.Tildelinger.SingleAsync(t => t.TildelingID == tildeling.TildelingID)).Fullført);

        // Etter retur må ressursen verifiseres på nytt før den kan forespørres igjen.
        var nyttBehov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.CreateRequestAsync(nyttBehov, ressursId, a.Dispatch));
        await test.Workflow.ChangeNeedStatusAsync(behovId, Statuser.Behov.Løst, a.Leder, "Ferdig");
        Assert.True(await test.Db.Hendelseslogg.CountAsync(l => l.RessursID == ressursId) >= 7);
    }

    [Fact]
    public async Task VerifiseringsansvarligKanIkkeGodkjenneEgenRessursOgAvvisningKreverBegrunnelse()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (bruker, _) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        var begge = TestDatabase.Principal(bruker.Id, AppRoles.Ressursleverandor, AppRoles.Verifiseringsansvarlig);
        var id = await test.Workflow.RegisterResourceAsync(Ressurs(), begge);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.VerifyAsync(id, true, begge, null));

        var verifiserer = (await test.BrukerAsync(AppRoles.Verifiseringsansvarlig)).Principal;
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.VerifyAsync(id, false, verifiserer, " "));
        await test.Workflow.VerifyAsync(id, false, verifiserer, "Mangler dokumentasjon");
        Assert.Equal(Statuser.Ressurs.Avvist, await RessursStatus(test, id));
        Assert.Equal("Mangler dokumentasjon", (await test.Db.Verifiseringer.SingleAsync()).Begrunnelse);
    }

    [Fact]
    public async Task UverifisertRessursKanIkkeForespørresOgForespurtRessursKanIkkeForespørresIgjen()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        var behov1 = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var behov2 = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.CreateRequestAsync(behov1, ressurs, a.Dispatch));
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);
        await test.Workflow.CreateRequestAsync(behov1, ressurs, a.Dispatch);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.CreateRequestAsync(behov2, ressurs, a.Dispatch));
    }

    [Fact]
    public async Task BareEierenKanSvareOgTildelingKreverGodtattForespørsel()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var annenLeverandør = (await test.BrukerAsync(AppRoles.Ressursleverandor)).Principal;
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);
        var behov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var forespørsel = await test.Workflow.CreateRequestAsync(behov, ressurs, a.Dispatch);

        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.RespondAsync(forespørsel, true, annenLeverandør, null));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.AllocateAsync(forespørsel, a.Dispatch));

        await test.Workflow.RespondAsync(forespørsel, false, a.Eier, "Ikke ledig likevel");
        Assert.Equal(Statuser.Ressurs.Tilgjengelig, await RessursStatus(test, ressurs));
        Assert.Equal(Statuser.Svar.Avslått, (await test.Db.Forespørsler.SingleAsync()).Svar);
    }

    [Fact]
    public async Task DispatchKanAvbryteForespørselOgRessursenBlirTilgjengeligIgjen()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);
        var behov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var forespørsel = await test.Workflow.CreateRequestAsync(behov, ressurs, a.Dispatch);
        await test.Workflow.RespondAsync(forespørsel, true, a.Eier, null);
        await test.Workflow.CancelAsync(forespørsel, a.Dispatch, "Behovet ble dekket på annen måte");
        Assert.Equal(Statuser.Ressurs.Tilgjengelig, await RessursStatus(test, ressurs));
        Assert.Equal(Statuser.Svar.Avbrutt, (await test.Db.Forespørsler.SingleAsync()).Svar);
    }

    [Fact]
    public async Task FremdriftGårBareEttStegOmGangen()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);
        var behov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var forespørsel = await test.Workflow.CreateRequestAsync(behov, ressurs, a.Dispatch);
        await test.Workflow.RespondAsync(forespørsel, true, a.Eier, null);
        await test.Workflow.AllocateAsync(forespørsel, a.Dispatch);
        var tildeling = await test.Db.Tildelinger.SingleAsync();
        await Assert.ThrowsAsync<WorkflowException>(() =>
            test.Workflow.UpdateAssignmentAsync(tildeling.TildelingID, Statuser.Fremdrift.Fullført, a.Dispatch, null));
        await test.Workflow.UpdateAssignmentAsync(tildeling.TildelingID, Statuser.Fremdrift.PåVei, a.Dispatch, null);
        await test.Workflow.UpdateAssignmentAsync(tildeling.TildelingID, Statuser.Fremdrift.Fremme, a.Dispatch, null);
        Assert.Equal(Statuser.Ressurs.IBruk, await RessursStatus(test, ressurs));
    }

    [Fact]
    public async Task ToSamtidigeForespørslerOmSammeRessursGirBareEnVinner()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);
        var behov1 = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var behov2 = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);

        // To dispatchere i hver sin databasekobling forsøker samtidig.
        await using var scope1 = test.Services.CreateAsyncScope();
        await using var scope2 = test.Services.CreateAsyncScope();
        var w1 = scope1.ServiceProvider.GetRequiredService<WorkflowService>();
        var w2 = scope2.ServiceProvider.GetRequiredService<WorkflowService>();
        var results = await Task.WhenAll(
            Try(() => w1.CreateRequestAsync(behov1, ressurs, a.Dispatch)),
            Try(() => w2.CreateRequestAsync(behov2, ressurs, a.Dispatch)));
        Assert.Equal(1, results.Count(ok => ok));
        Assert.Equal(1, await test.Db.Forespørsler.CountAsync(f => f.RessursID == ressurs));

        static async Task<bool> Try(Func<Task> action)
        {
            try { await action(); return true; } catch (WorkflowException) { return false; }
        }
    }

    [Fact]
    public async Task EndringAvNavnEllerPlasseringKreverNyVerifisering()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);

        var bareKontakt = Ressurs(); bareKontakt.Kontaktperson = "Ny kontakt";
        Assert.False(await test.Workflow.UpdateResourceAsync(ressurs, bareKontakt, a.Eier));
        Assert.Equal(Statuser.Ressurs.Tilgjengelig, await RessursStatus(test, ressurs));

        var flyttet = Ressurs(); flyttet.Breddegrad = 61.0;
        Assert.True(await test.Workflow.UpdateResourceAsync(ressurs, flyttet, a.Eier));
        Assert.Equal(Statuser.Ressurs.VenterPåVerifisering, await RessursStatus(test, ressurs));
    }

    [Fact]
    public async Task RessursMedForespørselshistorikkKanIkkeSlettes()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ny = await test.Workflow.RegisterResourceAsync(Ressurs("Ny"), a.Eier);
        await test.Workflow.DeleteResourceAsync(ny, a.Eier, null);
        Assert.False(await test.Db.Ressurser.AnyAsync(r => r.RessursID == ny));

        var brukt = await test.Workflow.RegisterResourceAsync(Ressurs("Brukt"), a.Eier);
        await test.Workflow.VerifyAsync(brukt, true, a.Verifiserer, null);
        var behov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var f = await test.Workflow.CreateRequestAsync(behov, brukt, a.Dispatch);
        await test.Workflow.RespondAsync(f, false, a.Eier, null);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.DeleteResourceAsync(brukt, a.Eier, null));
    }

    [Fact]
    public async Task RessursleverandørKanLeggeTilEnNyRessurstype()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var antallFør = await test.Db.Ressurstyper.CountAsync();
        var id = await test.Workflow.RegisterResourceAsync(Ressurs("Snøscooter 1", "snøscooter"), a.Eier);

        var ressurs = await test.Db.Ressurser.Include(r => r.Ressurstype).SingleAsync(r => r.RessursID == id);
        Assert.Equal("Snøscooter", ressurs.Ressurstype!.Navn);
        Assert.Equal(antallFør + 1, await test.Db.Ressurstyper.CountAsync());

        // Neste gang brukes den samme raden, det lages ikke en kopi.
        await test.Workflow.RegisterResourceAsync(Ressurs("Snøscooter 2", "Snøscooter"), a.Eier);
        Assert.Equal(antallFør + 1, await test.Db.Ressurstyper.CountAsync());
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.RegisterResourceAsync(Ressurs("Ukjent ting", "1"), a.Eier));
    }

    [Fact]
    public async Task OffentligAktørKanMeldeBehovMedEnNyBehovstype()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var antallFør = await test.Db.Behovstyper.CountAsync();
        var behov = Behov(BehovSkjemaViewModel.AnnenType); behov.NyBehovstype = "vannforsyning";
        var id = await test.Workflow.CreateNeedAsync(behov, a.Offentlig);

        var lagret = await test.Db.Behov.Include(b => b.Behovstype).SingleAsync(b => b.BehovID == id);
        Assert.Equal("Vannforsyning", lagret.Behovstype!.Navn);
        Assert.Equal(antallFør + 1, await test.Db.Behovstyper.CountAsync());

        // Samme navn igjen gjenbruker raden, og ugyldige navn avvises.
        var igjen = Behov(BehovSkjemaViewModel.AnnenType); igjen.NyBehovstype = "Vannforsyning";
        await test.Workflow.CreateNeedAsync(igjen, a.Offentlig);
        Assert.Equal(antallFør + 1, await test.Db.Behovstyper.CountAsync());
        var ugyldig = Behov(BehovSkjemaViewModel.AnnenType); ugyldig.NyBehovstype = "7";
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.CreateNeedAsync(ugyldig, a.Offentlig));
    }

    [Fact]
    public async Task FeilRolleAvvisesPåServeren()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.RegisterResourceAsync(Ressurs(), a.Offentlig));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.CreateNeedAsync(Behov(), a.Eier));
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.VerifyAsync(ressurs, true, a.Dispatch, null));
        await Assert.ThrowsAsync<WorkflowException>(() =>
            test.Workflow.CreateIncidentAsync(new HendelseSkjemaViewModel { Navn = "Flom", Område = "Agder" }, a.Dispatch));
    }

    [Fact]
    public async Task BehovKanKnyttesTilAktivHendelseMenIkkeAvsluttet()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var hendelse = await test.Workflow.CreateIncidentAsync(
            new HendelseSkjemaViewModel { Navn = "Flom i Otra", Område = "Agder", Starttidspunkt = DateTime.UtcNow }, a.Leder);
        var behov = Behov(); behov.HendelseID = hendelse;
        var id = await test.Workflow.CreateNeedAsync(behov, a.Offentlig);
        Assert.Equal(hendelse, (await test.Db.Behov.SingleAsync(b => b.BehovID == id)).HendelseID);

        await test.Workflow.CloseIncidentAsync(hendelse, a.Leder);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Workflow.CreateNeedAsync(behov, a.Offentlig));
    }

    [Fact]
    public async Task BehovKanIkkeLøsesMensForespørselVenter()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var ressurs = await test.Workflow.RegisterResourceAsync(Ressurs(), a.Eier);
        await test.Workflow.VerifyAsync(ressurs, true, a.Verifiserer, null);
        var behov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        await test.Workflow.CreateRequestAsync(behov, ressurs, a.Dispatch);
        await Assert.ThrowsAsync<WorkflowException>(() =>
            test.Workflow.ChangeNeedStatusAsync(behov, Statuser.Behov.Løst, a.Leder, "Ferdig"));
    }

    [Fact]
    public async Task BeholdningReserveresFrigisOgForbruksvarerTrekkesVedFullføring()
    {
        await using var test = await TestDatabase.CreateAsync();
        var a = await AktørerAsync(test);
        var vann = Ressurs("Drikkevann");
        vann.Antall = 2;
        vann.Enhet = "L";
        vann.ErForbruksvare = true;
        var ressursId = await test.Workflow.RegisterResourceAsync(vann, a.Eier);
        await test.Workflow.VerifyAsync(ressursId, true, a.Verifiserer, null);

        var behov = await test.Workflow.CreateNeedAsync(Behov(), a.Offentlig);
        var forespørselId = await test.Workflow.CreateRequestAsync(behov, ressursId, 1, a.Dispatch);
        var ressurs = await test.Db.Ressurser.SingleAsync(r => r.RessursID == ressursId);
        Assert.Equal(1, ressurs.TilgjengeligAntall);
        Assert.Equal(1, ressurs.ReservertAntall);
        Assert.Equal(Statuser.Ressurs.Tilgjengelig, ressurs.Status);

        await test.Workflow.RespondAsync(forespørselId, true, a.Eier, null);
        await test.Workflow.AllocateAsync(forespørselId, a.Dispatch);
        var tildeling = await test.Db.Tildelinger.SingleAsync(t => t.ForespørselID == forespørselId);
        foreach (var steg in new[] { Statuser.Fremdrift.PåVei, Statuser.Fremdrift.Fremme, Statuser.Fremdrift.Fullført })
            await test.Workflow.UpdateAssignmentAsync(tildeling.TildelingID, steg, a.Dispatch, null);

        test.Db.ChangeTracker.Clear();
        ressurs = await test.Db.Ressurser.SingleAsync(r => r.RessursID == ressursId);
        Assert.Equal(1, ressurs.Antall);
        Assert.Equal(0, ressurs.ReservertAntall);
        Assert.Equal(1, ressurs.TilgjengeligAntall);
        Assert.Equal(Statuser.Ressurs.Tilgjengelig, ressurs.Status);
    }
}
