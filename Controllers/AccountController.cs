using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

public class AccountController(UserManager<Bruker> users, SignInManager<Bruker> signIn) : Controller
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
            Roles = roles
        };

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
        return View(model);
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
