using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize(Roles = AppRoles.Kriseledelse)]
public class AdminController(UserManager<Bruker> users, HeimevernetInnlevering1.Data.AppDbContext db, BrukerAdminService admin) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var accounts = await users.Users.OrderBy(user => user.Email).ToListAsync();
        var siste = await db.Hendelseslogg.GroupBy(l => l.UtførtAvID).Select(g => new { g.Key, T = g.Max(l => l.Tidspunkt) }).ToDictionaryAsync(x => x.Key, x => x.T);
        var medData = await admin.BrukereMedDataAsync();
        var model = new List<AdminUserViewModel>();
        foreach (var user in accounts)
        {
            var assigned = await users.GetRolesAsync(user);
            var rolle = AppRoles.All.FirstOrDefault(assigned.Contains);
            DateTime? sist = DateTime.TryParse(await users.GetAuthenticationTokenAsync(user, "Profil", "SistAktiv"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var s) ? s.ToUniversalTime() : null;
            var harVakt = rolle is AppRoles.Kriseledelse or AppRoles.DispatchTeam;
            var paVakt = harVakt && (await users.GetAuthenticationTokenAsync(user, "Profil", "Vaktstatus") ?? "pa") == "pa";
            var nettsideApen = sist is { } x && (DateTime.UtcNow - x).TotalMinutes < 5;
            var ikkeLast = !(user.LockoutEnd is { } slutt && slutt > DateTimeOffset.UtcNow);
            model.Add(new AdminUserViewModel(user.Id, user.FulltNavn, user.Email ?? user.UserName ?? "Ukjent bruker",
                user.Organisasjon, rolle, ikkeLast && (paVakt || nettsideApen), sist,
                user.Fornavn, user.Etternavn, user.PhoneNumber, BrukerAdminService.ErDeaktivert(user), medData.Contains(user.Id)));
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(int id, string role)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user == null || !AppRoles.All.Contains(role) || users.GetUserId(User) == user.Id.ToString())
        {
            TempData["AdminError"] = "Kunne ikke endre rollen. Du kan ikke endre din egen rolle.";
            return RedirectToAction(nameof(Index));
        }

        var current = (await users.GetRolesAsync(user)).Where(AppRoles.All.Contains).ToList();
        if (current.Count != 1 || current[0] != role)
        {
            var stale = current.Where(existing => existing != role).ToList();
            var result = stale.Count == 0 ? IdentityResult.Success : await users.RemoveFromRolesAsync(user, stale);
            if (result.Succeeded && !current.Contains(role)) result = await users.AddToRoleAsync(user, role);
            if (!result.Succeeded || !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            {
                TempData["AdminError"] = "Kunne ikke endre rollen. Prøv igjen.";
                return RedirectToAction(nameof(Index));
            }
        }
        TempData["AdminMessage"] = "Rollen er oppdatert. Brukeren må logge inn på nytt.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Kriseledelsen endrer navn, telefon og organisasjon for en bruker. E-post endres ikke her.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(int id, ProfileEditModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["AdminError"] = "Kontroller opplysningene: navn og et gyldig telefonnummer må oppgis.";
            return RedirectToAction(nameof(Index));
        }
        var resultat = await admin.OppdaterKontaktAsync(id, model);
        Melding(resultat, "Kontaktinformasjonen er oppdatert.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Sletter brukeren hvis den ikke eier data. Ellers deaktiveres den, slik at historikken blir værende.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUser(int id)
    {
        var resultat = await admin.FjernAsync(id, InnloggetId());
        Melding(resultat, resultat == BrukerHandlingResultat.Slettet
            ? "Brukeren er slettet."
            : "Brukeren er deaktivert. Den har data i systemet og kan derfor ikke slettes, men kan ikke lenger logge inn.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateUser(int id)
    {
        var resultat = await admin.AktiverAsync(id, InnloggetId());
        Melding(resultat, "Brukeren er aktivert igjen og kan logge inn.");
        return RedirectToAction(nameof(Index));
    }

    private int InnloggetId() => int.TryParse(users.GetUserId(User), out var id) ? id : 0;

    private void Melding(BrukerHandlingResultat resultat, string ok)
    {
        switch (resultat)
        {
            case BrukerHandlingResultat.IkkeFunnet: TempData["AdminError"] = "Fant ikke brukeren."; break;
            case BrukerHandlingResultat.EgenKonto: TempData["AdminError"] = "Du kan ikke slette, deaktivere eller aktivere din egen konto."; break;
            case BrukerHandlingResultat.Feil: TempData["AdminError"] = "Kunne ikke lagre endringen. Prøv igjen."; break;
            default: TempData["AdminMessage"] = ok; break;
        }
    }
}
