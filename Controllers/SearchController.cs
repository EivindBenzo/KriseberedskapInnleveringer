using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Role-scoped lookup for operational records without exposing another actor's data.</summary>
[Authorize(Roles = AppRoles.Any)]
public sealed class SearchController(AppDbContext db) : Controller
{
    private const int ResultLimit = 12;

    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length is > 0 and < 2)
        {
            ModelState.AddModelError(nameof(q), "Skriv minst to tegn for å søke.");
            return View(new OperationalSearchViewModel { Query = query });
        }

        if (query.Length > 100)
        {
            ModelState.AddModelError(nameof(q), "Søket kan ikke være lengre enn 100 tegn.");
            return View(new OperationalSearchViewModel { Query = query[..100] });
        }

        if (query.Length == 0)
            return View(new OperationalSearchViewModel());

        var userId = ResourcePermissions.BrukerId(User);
        var dispatchOrLeadership = User.IsInRole(AppRoles.DispatchTeam) || User.IsInRole(AppRoles.Kriseledelse);
        var provider = User.IsInRole(AppRoles.Ressursleverandor);
        var publicActor = User.IsInRole(AppRoles.OffentligAktor);
        var term = $"%{query}%";
        var results = new List<OperationalSearchResult>();

        if (dispatchOrLeadership || provider)
        {
            var resources = await db.Ressurser.AsNoTracking()
                .Include(r => r.Ressurstype)
                .Where(r => (dispatchOrLeadership || r.EierID == userId)
                    && (EF.Functions.Like(r.Navn, term)
                        || EF.Functions.Like(r.Stedsnavn, term)
                        || (r.Beskrivelse != null && EF.Functions.Like(r.Beskrivelse, term))
                        || EF.Functions.Like(r.Ressurstype!.Navn, term)))
                .OrderBy(r => r.Status == Statuser.Ressurs.Tilgjengelig ? 0 : 1)
                .ThenBy(r => r.Navn)
                .Take(ResultLimit)
                .ToListAsync();

            results.AddRange(resources.Select(r => new OperationalSearchResult(
                "Ressurs", r.Navn, $"{r.Ressurstype!.Navn} · {r.Stedsnavn}", Statuser.Visning(r.Status),
                Url.Action("Map", "Home", new { focus = r.RessursID })!)));
        }

        if (dispatchOrLeadership || publicActor)
        {
            var needs = await db.Behov.AsNoTracking()
                .Include(b => b.Behovstype)
                .Where(b => (dispatchOrLeadership || b.RegistrertAvID == userId)
                    && (EF.Functions.Like(b.Stedsnavn, term)
                        || EF.Functions.Like(b.Beskrivelse, term)
                        || EF.Functions.Like(b.Behovstype!.Navn, term)))
                .OrderByDescending(b => b.Prioritet == Statuser.Prioritet.Akutt)
                .ThenByDescending(b => b.Registrert)
                .Take(ResultLimit)
                .ToListAsync();

            results.AddRange(needs.Select(b => new OperationalSearchResult(
                "Behov", b.Behovstype!.Navn, $"{b.Stedsnavn} · {b.Beskrivelse}", Statuser.Visning(b.Status),
                Url.Action("Dashboard", "Home") + "#behov-" + b.BehovID)));
        }

        return View(new OperationalSearchViewModel
        {
            Query = query,
            Results = results.Take(ResultLimit).ToList()
        });
    }
}
