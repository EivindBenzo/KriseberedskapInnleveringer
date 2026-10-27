using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HeimevernetInnlevering1.Controllers;

public class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : Controller
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
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Home");
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
        var user = new IdentityUser { UserName = email, Email = email };
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

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();
}
