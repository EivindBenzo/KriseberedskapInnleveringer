using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Services;

public enum BrukerHandlingResultat { Lagret, Slettet, Deaktivert, Aktivert, IkkeFunnet, EgenKonto, Feil }

/// <summary>
/// Kriseledelsens administrasjon av andre brukere: endre kontaktinformasjon, slette og deaktivere.
/// En bruker som eier data (ressurser, behov, logg, meldinger osv.) kan ikke slettes, fordi
/// fremmednøklene er ON DELETE RESTRICT. Slike brukere deaktiveres i stedet: de låses ute,
/// men historikken og sporbarheten blir værende.
/// </summary>
public class BrukerAdminService(UserManager<Bruker> users, AppDbContext db)
{
    /// <summary>Lockout til år 9999 betyr «deaktivert av kriseledelsen», ikke en vanlig midlertidig lockout.</summary>
    public static readonly DateTimeOffset DeaktivertTil = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public static bool ErDeaktivert(Bruker bruker) => bruker.LockoutEnd is { } slutt && slutt.Year >= 9999;

    /// <summary>Id-ene til alle brukere som er knyttet til data og derfor ikke kan slettes.</summary>
    public async Task<HashSet<int>> BrukereMedDataAsync()
    {
        var ider = new HashSet<int>();
        ider.UnionWith(await db.Hendelser.Select(x => x.OpprettetAvID).Distinct().ToListAsync());
        ider.UnionWith(await db.Behov.Select(x => x.RegistrertAvID).Distinct().ToListAsync());
        ider.UnionWith(await db.Ressurser.Select(x => x.EierID).Distinct().ToListAsync());
        ider.UnionWith(await db.Verifiseringer.Select(x => x.VerifisertAvID).Distinct().ToListAsync());
        ider.UnionWith(await db.Forespørsler.Select(x => x.SendtAvID).Distinct().ToListAsync());
        ider.UnionWith(await db.Hendelseslogg.Select(x => x.UtførtAvID).Distinct().ToListAsync());
        ider.UnionWith(await db.Meldinger.Select(x => x.SendtAvID).Distinct().ToListAsync());
        ider.UnionWith(await db.Meldinger.Select(x => x.MottakerID).Distinct().ToListAsync());
        return ider;
    }

    public async Task<bool> EierDataAsync(int id) =>
        await db.Hendelser.AnyAsync(x => x.OpprettetAvID == id)
        || await db.Behov.AnyAsync(x => x.RegistrertAvID == id)
        || await db.Ressurser.AnyAsync(x => x.EierID == id)
        || await db.Verifiseringer.AnyAsync(x => x.VerifisertAvID == id)
        || await db.Forespørsler.AnyAsync(x => x.SendtAvID == id)
        || await db.Hendelseslogg.AnyAsync(x => x.UtførtAvID == id)
        || await db.Meldinger.AnyAsync(x => x.SendtAvID == id || x.MottakerID == id);

    /// <summary>Endrer navn, telefon og organisasjon. E-post (brukernavnet) endres ikke her.</summary>
    public async Task<BrukerHandlingResultat> OppdaterKontaktAsync(int id, ProfileEditModel model)
    {
        var bruker = await users.FindByIdAsync(id.ToString());
        if (bruker is null) return BrukerHandlingResultat.IkkeFunnet;

        bruker.Fornavn = model.Fornavn.Trim();
        bruker.Etternavn = model.Etternavn.Trim();
        bruker.PhoneNumber = model.Telefon.Trim();
        bruker.Organisasjon = string.IsNullOrWhiteSpace(model.Organisasjon) ? null : model.Organisasjon.Trim();
        return (await users.UpdateAsync(bruker)).Succeeded ? BrukerHandlingResultat.Lagret : BrukerHandlingResultat.Feil;
    }

    /// <summary>Sletter brukeren hvis den ikke eier data, ellers deaktiveres den. Ingen kan fjerne sin egen konto.</summary>
    public async Task<BrukerHandlingResultat> FjernAsync(int id, int utførtAvId)
    {
        if (id == utførtAvId) return BrukerHandlingResultat.EgenKonto;
        var bruker = await users.FindByIdAsync(id.ToString());
        if (bruker is null) return BrukerHandlingResultat.IkkeFunnet;

        if (!await EierDataAsync(id))
        {
            try
            {
                if ((await users.DeleteAsync(bruker)).Succeeded) return BrukerHandlingResultat.Slettet;
            }
            catch (DbUpdateException)
            {
                // Databasen fant en kobling vi ikke kjente til (RESTRICT). Deaktiver i stedet.
                db.ChangeTracker.Clear();
                bruker = await users.FindByIdAsync(id.ToString());
                if (bruker is null) return BrukerHandlingResultat.IkkeFunnet;
            }
        }
        return await DeaktiverAsync(bruker);
    }

    public async Task<BrukerHandlingResultat> AktiverAsync(int id, int utførtAvId)
    {
        if (id == utførtAvId) return BrukerHandlingResultat.EgenKonto;
        var bruker = await users.FindByIdAsync(id.ToString());
        if (bruker is null) return BrukerHandlingResultat.IkkeFunnet;

        var ok = (await users.SetLockoutEndDateAsync(bruker, null)).Succeeded
                 && (await users.ResetAccessFailedCountAsync(bruker)).Succeeded;
        return ok ? BrukerHandlingResultat.Aktivert : BrukerHandlingResultat.Feil;
    }

    private async Task<BrukerHandlingResultat> DeaktiverAsync(Bruker bruker)
    {
        // Ny sikkerhetsstempel logger brukeren ut med en gang (ValidationInterval = 0 i Program.cs).
        var ok = (await users.SetLockoutEnabledAsync(bruker, true)).Succeeded
                 && (await users.SetLockoutEndDateAsync(bruker, DeaktivertTil)).Succeeded
                 && (await users.UpdateSecurityStampAsync(bruker)).Succeeded;
        return ok ? BrukerHandlingResultat.Deaktivert : BrukerHandlingResultat.Feil;
    }
}
