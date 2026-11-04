using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Forside, oversikt, ressurskart og ressurslisten.</summary>
[Authorize(Roles = AppRoles.Any)]
public class HomeController(AppDbContext db, IPlaceGeocoder geocoder, WorkflowService workflow) : Controller
{
    private ResourceFilter? _mapFilter;

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Dashboard([FromServices] DashboardService dashboard) =>
        View(await dashboard.BuildAsync(User, DateTime.UtcNow));

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }

    // ---------- Kart og registrering av ressurs ----------

    [HttpGet]
    public async Task<IActionResult> Map(string? type = null, string? query = null, string? city = null, bool mine = false,
        bool eligible = false)
    {
        _mapFilter = ResourceFilter.For(User, type, query, city, mine, eligible);
        ViewBag.MapFilter = _mapFilter;
        await LoadResourcesAsync();
        // Kontaktfeltene starter tomme: ressursen kan ha en annen kontaktperson enn den som registrerer.
        return View(new RessursSkjemaViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> Place(double? latitude, double? longitude)
    {
        if (!ModelState.IsValid || !PlaceGeocoder.ValidPoint(latitude, longitude))
            return BadRequest();
        return Json(new { label = await geocoder.ReverseAsync(latitude!.Value, longitude!.Value,
            HttpContext?.RequestAborted ?? default) });
    }

    [HttpGet]
    public async Task<IActionResult> SearchPlaces(string? query)
    {
        if (!ModelState.IsValid || !PlaceGeocoder.ValidQuery(query)) return BadRequest();
        return Json(await geocoder.SearchAsync(query!, HttpContext?.RequestAborted ?? default));
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Ressursleverandor)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Map(RessursSkjemaViewModel model)
    {
        await ValidateResourceAsync(model);
        if (ModelState.IsValid)
        {
            try
            {
                var id = await workflow.RegisterResourceAsync(model, User);
                TempData["MapSuccess"] = $"{model.Ressurstype} «{model.Navn}» ble registrert ved {model.Stedsnavn} og venter på verifisering.";
                TempData["MapPlacedResourceId"] = id;
                return RedirectToAction(nameof(Map));
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        await LoadResourcesAsync();
        return View(model);
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.Ressursleverandor)]
    public async Task<IActionResult> Edit(int id)
    {
        var resource = await db.Ressurser.AsNoTracking().Include(r => r.Ressurstype)
            .SingleOrDefaultAsync(r => r.RessursID == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.EierID)) return Forbid();
        ViewBag.EditId = id;
        await LoadResourcesAsync();
        return View("Map", new RessursSkjemaViewModel
        {
            Navn = resource.Navn,
            Ressurstype = resource.Ressurstype!.Navn,
            Beskrivelse = resource.Beskrivelse,
            TilgjengeligFra = resource.TilgjengeligFra,
            TilgjengeligTil = resource.TilgjengeligTil,
            Kontaktperson = resource.Kontaktperson,
            Kontakttelefon = resource.Kontakttelefon,
            Stedsnavn = resource.Stedsnavn,
            Breddegrad = (double)resource.Breddegrad,
            Lengdegrad = (double)resource.Lengdegrad
        });
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Ressursleverandor)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RessursSkjemaViewModel model)
    {
        var resource = await db.Ressurser.AsNoTracking().SingleOrDefaultAsync(r => r.RessursID == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.EierID)) return Forbid();
        await ValidateResourceAsync(model);
        if (ModelState.IsValid)
        {
            try
            {
                var reverify = await workflow.UpdateResourceAsync(id, model, User);
                TempData["MapSuccess"] = reverify
                    ? "Ressursen er oppdatert og venter på ny verifisering."
                    : "Ressursen er oppdatert.";
                TempData["MapPlacedResourceId"] = id;
                return RedirectToAction(nameof(Map));
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        ViewBag.EditId = id;
        await LoadResourcesAsync();
        return View("Map", model);
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.Ressursleverandor)]
    public async Task<IActionResult> Delete(int id)
    {
        var resource = await db.Ressurser.AsNoTracking().Include(r => r.Ressurstype)
            .SingleOrDefaultAsync(r => r.RessursID == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.EierID)) return Forbid();
        return View(resource);
    }

    [HttpPost, ActionName("Delete")]
    [Authorize(Roles = AppRoles.Ressursleverandor)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var json = Request.GetTypedHeaders().Accept?.Any(a =>
            string.Equals(a.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase)) is true;
        var resource = await db.Ressurser.AsNoTracking().SingleOrDefaultAsync(r => r.RessursID == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.EierID)) return Forbid();
        try
        {
            await workflow.DeleteResourceAsync(id, User, "Bekreftet sletting fra ressursoversikten.");
        }
        catch (WorkflowException exception)
        {
            if (json) return Conflict(new { error = exception.Message });
            ModelState.AddModelError("", exception.Message);
            var current = await db.Ressurser.AsNoTracking().Include(r => r.Ressurstype)
                .SingleOrDefaultAsync(r => r.RessursID == id);
            return current is null ? NotFound() : View("Delete", current);
        }
        if (json) return Json(new { deletedId = id });
        TempData["MapSuccess"] = "Ressursen er slettet.";
        return RedirectToAction(nameof(Map));
    }

    // ---------- Ressursoversikt ----------

    [HttpGet]
    public IActionResult Data(string? type) => RedirectToAction(nameof(Resources), new { type });

    [HttpGet]
    public IActionResult Verify(string? type) => RedirectToAction(nameof(Resources), new { type });

    [HttpGet]
    public Task<IActionResult> Mine(string? type, string? query, bool eligible = false) =>
        Resources(type, query, true, eligible);

    [HttpGet]
    public async Task<IActionResult> Resources(string? type, string? query, bool mine = false, bool eligible = false,
        bool overlay = false, string? city = null, bool fragment = false, bool history = false)
    {
        if (history && !User.IsInRole(AppRoles.Kriseledelse)) return Forbid();
        ViewBag.SelectedType = type;
        ViewBag.Query = query;
        ViewBag.Mine = mine;
        ViewBag.Eligible = eligible;
        ViewBag.ResourcesOverlay = overlay;
        ViewBag.ResourcesPartial = overlay || fragment;
        ViewBag.City = ResourceCity.Normalize(city);
        ViewBag.ResourceHistory = history;
        var model = history
            ? new DataPageViewModel { History = await LoadResourceHistoryAsync() }
            : await BuildDataModelAsync(type, query, mine, eligible, city);
        return overlay || fragment ? PartialView("Verify", model) : View("Verify", model);
    }

    private async Task<IReadOnlyList<ResourceHistoryEntry>> LoadResourceHistoryAsync() =>
        await (from logg in db.Hendelseslogg.AsNoTracking()
               where logg.Handling == "RessursRegistrert" || logg.Handling == "RessursSlettet"
               join actor in db.Users.AsNoTracking() on logg.UtførtAvID equals actor.Id into actors
               from actor in actors.DefaultIfEmpty()
               orderby logg.Tidspunkt descending, logg.LoggID descending
               select new ResourceHistoryEntry
               {
                   ResourceId = logg.RessursID,
                   Action = logg.Handling,
                   Description = logg.Begrunnelse,
                   Actor = actor == null ? "Ukjent bruker" : (actor.Email ?? actor.UserName ?? "Ukjent bruker"),
                   OccurredAtUtc = logg.Tidspunkt
               }).Take(500).ToListAsync();

    protected virtual async Task LoadResourcesAsync()
    {
        ViewBag.Resources = await ResourceQueries.LoadAsync(db,
            _mapFilter ?? ResourceFilter.For(User, null, null, null, false, false), DateTime.UtcNow);
        ViewBag.Ressurstyper = await RessurstypeNavnAsync();
    }

    /// <summary>Alle ressurstyper fra tabellen Ressurstype, også de brukerne har lagt til selv.</summary>
    private async Task<List<string>> RessurstypeNavnAsync() =>
        await db.Ressurstyper.AsNoTracking().OrderBy(t => t.RessurstypeID).Select(t => t.Navn).ToListAsync();

    /// <summary>Kontrollerer skjemaet og finner stedsnavnet for punktet på serveren.</summary>
    private async Task ValidateResourceAsync(RessursSkjemaViewModel model)
    {
        ModelState.Remove(nameof(model.Stedsnavn));
        model.Stedsnavn = string.Empty;
        if (model.Ressurstype == RessursSkjemaViewModel.AnnenType)
        {
            // Brukeren har valgt «Annen type …» og skrevet inn en ny type selv.
            var ny = model.NyRessurstype?.Trim() ?? string.Empty;
            if (ny.Length is < 2 or > 100 || !ny.Any(char.IsLetter))
                ModelState.AddModelError(nameof(model.NyRessurstype), "Skriv inn ressurstypen (2–100 tegn).");
            else
                model.Ressurstype = ny;
        }
        else if (!await db.Ressurstyper.AnyAsync(t => t.Navn == model.Ressurstype))
        {
            ModelState.AddModelError(nameof(model.Ressurstype), "Velg en gyldig ressurstype");
        }
        if (model.TilgjengeligTil is not null && model.TilgjengeligFra is not null && model.TilgjengeligTil < model.TilgjengeligFra)
            ModelState.AddModelError(nameof(model.TilgjengeligTil), "«Tilgjengelig til» kan ikke være før «tilgjengelig fra».");
        if (model.Breddegrad is not double lat || !double.IsFinite(lat) || lat is < -90 or > 90)
            ModelState.AddModelError(nameof(model.Breddegrad), "Klikk i kartet for å velge plassering.");
        if (model.Lengdegrad is not double lng || !double.IsFinite(lng) || lng is < -180 or > 180)
            ModelState.AddModelError(nameof(model.Lengdegrad), "Klikk i kartet for å velge plassering.");
        if (PlaceGeocoder.ValidPoint(model.Breddegrad, model.Lengdegrad))
            model.Stedsnavn = await geocoder.ReverseAsync(model.Breddegrad!.Value, model.Lengdegrad!.Value,
                HttpContext?.RequestAborted ?? default);
    }

    private async Task<DataPageViewModel> BuildDataModelAsync(string? type, string? query, bool mine, bool eligible, string? city)
    {
        var now = DateTime.UtcNow;
        var filter = ResourceFilter.For(User, type, query, city, mine, eligible);
        var cityScope = filter with { Type = null, Query = null, City = null, Eligible = AppRoles.DispatchOnly(User) };
        ViewBag.Ressurstyper = await RessurstypeNavnAsync();
        return new DataPageViewModel
        {
            Cities = ResourceCity.Aggregate(await ResourceQueries.LoadAsync(db, cityScope, now)),
            Ressurser = await ResourceQueries.LoadAsync(db, filter, now)
        };
    }
}
