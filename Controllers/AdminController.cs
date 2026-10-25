using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize(Roles = IdentitySeeder.AdminRole)]
public class AdminController(UserManager<IdentityUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var accounts = await users.Users.OrderBy(user => user.Email).ToListAsync();
        var model = new List<AdminUserViewModel>();
        foreach (var user in accounts)
            model.Add(new AdminUserViewModel(user.Id, user.Email ?? user.UserName ?? "Ukjent bruker",
                await users.IsInRoleAsync(user, IdentitySeeder.AdminRole)));
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAdmin(string id, bool grant)
    {
        var user = await users.FindByIdAsync(id ?? string.Empty);
        if (user == null || (!grant && users.GetUserId(User) == user.Id))
        {
            TempData["AdminError"] = "Kunne ikke endre rollen. Du kan ikke fjerne din egen administratortilgang.";
            return RedirectToAction(nameof(Index));
        }

        if (await users.IsInRoleAsync(user, IdentitySeeder.AdminRole) != grant)
        {
            var result = grant
                ? await users.AddToRoleAsync(user, IdentitySeeder.AdminRole)
                : await users.RemoveFromRoleAsync(user, IdentitySeeder.AdminRole);
            if (!result.Succeeded || !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            {
                TempData["AdminError"] = "Kunne ikke endre rollen. Prøv igjen.";
                return RedirectToAction(nameof(Index));
            }
        }
        TempData["AdminMessage"] = "Rollen er oppdatert. Brukeren må logge inn på nytt.";
        return RedirectToAction(nameof(Index));
    }
}
