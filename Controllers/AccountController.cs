using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HeimevernetInnlevering1.Services;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

public class AccountController(UserManager<Bruker> users, SignInManager<Bruker> signIn, ShellService shell) : Controller
{
    private const string LoginError = "Kunne ikke logge inn. Kontroller opplysningene eller prøv igjen senere.";

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, LoginError);
            return View(model);
        }

        var result = await signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, LoginError);
            return View(model);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Dashboard", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (ModelState.IsValid && !AppRoles.SelfService.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Velg en gyldig kontotype.");
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim();
        var user = new Bruker
        {
            UserName = email, Email = email, Fornavn = model.Fornavn.Trim(), Etternavn = model.Etternavn.Trim(),
            PhoneNumber = model.Telefon.Trim(),
            Organisasjon = string.IsNullOrWhiteSpace(model.Organisasjon) ? null : model.Organisasjon.Trim()
        };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, model.Role);
            if (result.Succeeded)
            {
                TempData["AccountMessage"] = "Kontoen er opprettet. Du kan nå logge inn.";
                return RedirectToAction(nameof(Login));
            }
            await users.DeleteAsync(user);
        }
        ModelState.AddModelError(string.Empty, "Kunne ikke opprette kontoen. Kontroller opplysningene og passordkravene, eller prøv å logge inn.");
        return View(model);
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Profile([FromServices] AppDbContext db)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var roles = (await users.GetRolesAsync(user)).Where(AppRoles.All.Contains).ToList();
        var model = new ProfileViewModel
        {
            Name = user.FulltNavn,
            Email = user.Email ?? user.UserName ?? "Ikke oppgitt",
            Phone = user.PhoneNumber,
            Organisation = user.Organisasjon,
            FirstName = user.Fornavn,
            LastName = user.Etternavn,
            HasAvatar = await users.GetAuthenticationTokenAsync(user, AvatarProvider, AvatarToken) is not null,
            Roles = roles,
            Ansvar = Ansvarsområde(roles),
            Vakt = await users.GetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VaktToken) is { } v && ShellService.Vaktstatuser.Any(s => s.Verdi == v) ? v : "pa",
            Varselvalg = ShellService.LesValg(await users.GetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VarselToken))
        };
        AddShortcuts(model, roles);

        if (roles.Contains(AppRoles.Ressursleverandor))
        {
            var resources = db.Ressurser.AsNoTracking().Where(r => r.EierID == user.Id);
            model.Stats.Add(("Mine ressurser", await resources.CountAsync()));
            model.Stats.Add(("Venter på verifisering", await resources.CountAsync(r => r.Status == Statuser.Ressurs.VenterPåVerifisering)));
        }
        if (roles.Contains(AppRoles.OffentligAktor))
        {
            var needs = db.Behov.AsNoTracking().Where(b => b.RegistrertAvID == user.Id);
            model.Stats.Add(("Behov meldt inn", await needs.CountAsync()));
            model.Stats.Add(("Akutte behov", await needs.CountAsync(b => b.Prioritet == Statuser.Prioritet.Akutt && b.Status != Statuser.Behov.Løst)));
        }
        if (roles.Contains(AppRoles.DispatchTeam) || roles.Contains(AppRoles.Kriseledelse))
        {
            model.Stats.Add(("Åpne behov", await db.Behov.AsNoTracking().CountAsync(b => b.Status != Statuser.Behov.Løst)));
            model.Stats.Add(("Akutte behov", await db.Behov.AsNoTracking().CountAsync(b => b.Prioritet == Statuser.Prioritet.Akutt && b.Status != Statuser.Behov.Løst)));
        }
        var logg = await db.Hendelseslogg.AsNoTracking().Where(l => l.UtførtAvID == user.Id)
            .OrderByDescending(l => l.Tidspunkt).Take(5).Select(l => new { l.Handling, l.Begrunnelse, l.Tidspunkt }).ToListAsync();
        model.Aktivitet.AddRange(logg.Select(l => (l.Handling, l.Begrunnelse, l.Tidspunkt)));
        return View(model);
    }

    private static string Ansvarsområde(IReadOnlyCollection<string> roles) =>
        roles.Contains(AppRoles.Kriseledelse) ? "Overordnet ledelse og brukeradministrasjon"
        : roles.Contains(AppRoles.DispatchTeam) ? "Koordinering av behov, forespørsler og leveranser"
        : roles.Contains(AppRoles.Ressursleverandor) ? "Ressurser og beholdning"
        : roles.Contains(AppRoles.OffentligAktor) ? "Meldte behov og oppfølging" : "Ikke angitt";

    private static void AddShortcuts(ProfileViewModel model, IReadOnlyCollection<string> roles)
    {
        model.Shortcuts.Add(("Oversikt", "Se status og det som krever handling nå.", "Home", "Dashboard"));
        model.Shortcuts.Add(("Kart", "Ressurser og akutte behov på kartet.", "Home", "Map"));
        if (roles.Contains(AppRoles.OffentligAktor))
            model.Shortcuts.Add(("Meld behov", "Send inn et nytt behov med sted.", "Behov", "Registrer"));
        if (roles.Contains(AppRoles.Ressursleverandor))
            model.Shortcuts.Add(("Mine ressurser", "Se, rediger og følg opp ressursene dine.", "Home", "Mine"));
        if (roles.Contains(AppRoles.DispatchTeam) || roles.Contains(AppRoles.Kriseledelse))
            model.Shortcuts.Add(("Hendelser", "Koordiner hendelser og tildelinger.", "Hendelse", "Index"));
        if (roles.Contains(AppRoles.Kriseledelse))
            model.Shortcuts.Add(("Brukere", "Administrer kontoer og roller.", "Admin", "Index"));
    }

    // «Lagre endringer» på profilsiden: varselvalgene styrer hva bjellen viser (ShellService), og vaktstatus lagres på kontoen.
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences(string[]? varsel, string? vakt)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VarselToken, ShellService.SkrivValg(varsel ?? []));
        if (vakt is not null && ShellService.Vaktstatuser.Any(s => s.Verdi == vakt))
            await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VaktToken, vakt);
        TempData["ProfileMessage"] = "Endringene er lagret.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(ProfileEditModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid)
        {
            TempData["ProfileError"] = "Kontroller opplysningene: navn og et gyldig telefonnummer må oppgis.";
            return RedirectToAction(nameof(Profile));
        }
        user.Fornavn = model.Fornavn.Trim();
        user.Etternavn = model.Etternavn.Trim();
        user.PhoneNumber = model.Telefon.Trim();
        user.Organisasjon = string.IsNullOrWhiteSpace(model.Organisasjon) ? null : model.Organisasjon.Trim();
        var result = await users.UpdateAsync(user);
        if (result.Succeeded) TempData["ProfileMessage"] = "Opplysningene dine er oppdatert.";
        else TempData["ProfileError"] = "Kunne ikke lagre endringene. Prøv igjen.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid)
        {
            TempData["ProfileError"] = "Passordet må være minst 12 tegn, og de to nye passordene må være like.";
            return RedirectToAction(nameof(Profile));
        }
        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await signIn.RefreshSignInAsync(user);
            TempData["ProfileMessage"] = "Passordet er byttet.";
        }
        else TempData["ProfileError"] = "Kunne ikke bytte passord. Kontroller det nåværende passordet og passordkravene.";
        return RedirectToAction(nameof(Profile));
    }

    // Profilbildet lagres som et lite JPEG-bilde (nettleseren skalerer det ned) i Identity sin tokentabell.
    private const string AvatarProvider = "Profil";
    private const string AvatarToken = "Profilbilde";
    private const string JpegPrefix = "data:image/jpeg;base64,";

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAvatar(string? bilde)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (bilde is not null && bilde.StartsWith(JpegPrefix, StringComparison.Ordinal) && bilde.Length < 200_000
            && IsJpeg(bilde[JpegPrefix.Length..]))
        {
            await users.SetAuthenticationTokenAsync(user, AvatarProvider, AvatarToken, bilde);
            TempData["ProfileMessage"] = "Profilbildet er oppdatert.";
        }
        else TempData["ProfileError"] = "Bildet kunne ikke brukes. Velg et vanlig bilde (JPEG, PNG eller WebP).";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAvatar()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        await users.RemoveAuthenticationTokenAsync(user, AvatarProvider, AvatarToken);
        TempData["ProfileMessage"] = "Profilbildet er fjernet.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Avatar()
    {
        var user = await users.GetUserAsync(User);
        var data = user is null ? null : await users.GetAuthenticationTokenAsync(user, AvatarProvider, AvatarToken);
        if (data is null || !data.StartsWith(JpegPrefix, StringComparison.Ordinal)) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(Convert.FromBase64String(data[JpegPrefix.Length..]), "image/jpeg");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationsSeen()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var modell = await shell.HentAsync(User);
        await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.SettToken, string.Join('\n', modell.Nøkler));
        return NoContent();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationSeen(string key)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var modell = await shell.HentAsync(User);
        if (!modell.Nøkler.Contains(key)) return NoContent();
        var sett = (await users.GetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.SettToken) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        sett.Add(key);
        await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.SettToken, string.Join('\n', sett.Where(modell.Nøkler.Contains)));
        return NoContent();
    }

    private static bool IsJpeg(string base64)
    {
        var buffer = new byte[base64.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written > 3
            && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF;
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();
}
