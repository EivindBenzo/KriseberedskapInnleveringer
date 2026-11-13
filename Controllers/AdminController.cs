using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize(Roles = AppRoles.Kriseledelse)]
public class AdminController(UserManager<Bruker> users, HeimevernetInnlevering1.Data.AppDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var accounts = await users.Users.OrderBy(user => user.Email).ToListAsync();
        var siste = await db.Hendelseslogg.GroupBy(l => l.UtførtAvID).Select(g => new { g.Key, T = g.Max(l => l.Tidspunkt) }).ToDictionaryAsync(x => x.Key, x => x.T);
        var model = new List<AdminUserViewModel>();
        foreach (var user in accounts)
        {
            var assigned = await users.GetRolesAsync(user);
            model.Add(new AdminUserViewModel(user.Id, user.FulltNavn, user.Email ?? user.UserName ?? "Ukjent bruker",
                user.Organisasjon, AppRoles.All.FirstOrDefault(assigned.Contains),
                !(user.LockoutEnd is { } slutt && slutt > DateTimeOffset.UtcNow), siste.TryGetValue(user.Id, out var t) ? t : null));
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
}
