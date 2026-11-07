using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Forside, oversikt, ressurskart og ressurslisten.</summary>
[Authorize(Roles = AppRoles.Any)]
public class HomeController(AppDbContext db, IPlaceGeocoder geocoder, WorkflowService workflow, Stedssok stedssok) : Controller
{
    private ResourceFilter? _mapFilter;

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Dashboard([FromServices] DashboardService dashboard) =>
        View(await dashboard.BuildAsync(User, DateTime.UtcNow));

    [HttpGet]
    public async Task<IActionResult> Contacts()
    {
        var contacts = await (from user in db.Users.AsNoTracking()
                              join userRole in db.UserRoles on user.Id equals userRole.UserId
                              join role in db.Roles on userRole.RoleId equals role.Id
                              where role.Name != null && role.Name != AppRoles.OffentligAktor && AppRoles.All.Contains(role.Name)
                              orderby role.Name, user.Fornavn, user.Etternavn
                              select new ContactDirectoryEntry(
                                  user.Id, user.FulltNavn, role.Name!, user.Organisasjon, user.Email ?? user.UserName ?? "Ikke oppgitt",
                                  user.PhoneNumber)).ToListAsync();
        return View(contacts);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }

    // ---------- Kart og registrering av ressurs ----------

    /// <summary>
    /// Kartsiden. Alt som vises lages her på serveren:
    /// <list type="bullet">
    /// <item><c>type</c> filtrerer på ressurstype («Annet» = typer brukerne har lagt til selv).</item>
    /// <item><c>sted</c> er «Finn sted»: kartet flyttes til stedet.</item>
    /// <item><c>focus</c> er en ressurs som skal vises: kartet zoomer inn og kortet i listen åpnes.</item>
    /// </list>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Map(string? type = null, string? query = null, string? city = null, bool mine = false,
        bool eligible = false, string? sted = null, int? focus = null)
    {
        _mapFilter = ResourceFilter.For(User, type, query, city, mine, eligible);
        ViewBag.MapFilter = _mapFilter;
        await LoadResourcesAsync();

        var senter = KartVisning.Standard;
        if (!string.IsNullOrWhiteSpace(sted))
        {
            var treff = await stedssok.SøkAsync(sted, HttpContext?.RequestAborted ?? default);
            senter = treff.Count == 0
                ? senter with { Melding = $"Fant ikke «{sted.Trim()}». Prøv et annet stedsnavn." }
                : new KartVisning(treff[0].Lat, treff[0].Lng, 12)
                {
                    Melding = $"Viser {treff[0].Label}.",
                    Stedsforslag = treff.Skip(1).ToList()
                };
        }
        var model = await NewResourceFormAsync();
        await ByggKart(model, senter, focus);
        return View(model);
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
                return RedirectToAction(nameof(Map), null, new { focus = id }, "ressurs-" + id);
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        await LoadResourcesAsync();
        await ByggKart(model);
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
        var model = new RessursSkjemaViewModel
        {
            Navn = resource.Navn,
            Ressurstype = resource.Ressurstype!.Navn,
            Beskrivelse = resource.Beskrivelse,
            Antall = resource.Antall,
            Enhet = resource.Enhet,
            ErForbruksvare = resource.ErForbruksvare,
            TilgjengeligFra = resource.TilgjengeligFra,
            TilgjengeligTil = resource.TilgjengeligTil,
            Kontaktperson = resource.Kontaktperson,
            Kontakttelefon = resource.Kontakttelefon,
            Stedsnavn = resource.Stedsnavn,
            Breddegrad = (double)resource.Breddegrad,
            Lengdegrad = (double)resource.Lengdegrad
        };
        await ByggKart(model);
        return View("Map", model);
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
                return RedirectToAction(nameof(Map), null, new { focus = id }, "ressurs-" + id);
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        ViewBag.EditId = id;
        await LoadResourcesAsync();
        await ByggKart(model);
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
        var resource = await db.Ressurser.AsNoTracking().SingleOrDefaultAsync(r => r.RessursID == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.EierID)) return Forbid();
        try
        {
            await workflow.DeleteResourceAsync(id, User, "Bekreftet sletting fra ressursoversikten.");
        }
        catch (WorkflowException exception)
        {
            ModelState.AddModelError("", exception.Message);
            var current = await db.Ressurser.AsNoTracking().Include(r => r.Ressurstype)
                .SingleOrDefaultAsync(r => r.RessursID == id);
            return current is null ? NotFound() : View("Delete", current);
        }
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
        string? city = null, bool history = false)
    {
        if (history && !User.IsInRole(AppRoles.Kriseledelse)) return Forbid();
        ViewBag.SelectedType = type;
        ViewBag.Query = query;
        ViewBag.Mine = mine;
        ViewBag.Eligible = eligible;
        ViewBag.City = ResourceCity.Normalize(city);
        ViewBag.ResourceHistory = history;
        var model = history
            ? new DataPageViewModel { History = await LoadResourceHistoryAsync() }
            : await BuildDataModelAsync(type, query, mine, eligible, city);
        return View("Verify", model);
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

    /// <summary>
    /// Lager innholdet i kartet: hvor det starter, én markør per ressurs med riktig symbol og
    /// popup-tekst, og hvilke skjemafelt et klikk i kartet skal fylle ut når skjemaet er åpent.
    /// </summary>
    private async Task ByggKart(RessursSkjemaViewModel model, KartVisning? senter = null, int? focus = null)
    {
        var ressurser = (ViewBag.Resources as List<Ressurs>) ?? [];
        var redigerer = ViewBag.EditId as int?;
        var harPunkt = PlaceGeocoder.ValidPoint(model.Breddegrad, model.Lengdegrad);
        var nå = DateTime.UtcNow;
        var html = HtmlEncoder.Default;
        senter ??= KartVisning.Standard;

        var fokusert = ressurser.FirstOrDefault(r => r.RessursID == focus);
        if (fokusert is not null) senter = new KartVisning((double)fokusert.Breddegrad, (double)fokusert.Lengdegrad, 15);
        else if (harPunkt) senter = new KartVisning(model.Breddegrad!.Value, model.Lengdegrad!.Value, 13);

        var markører = ressurser
            .Where(r => r.RessursID != redigerer) // ressursen som redigeres vises som den flyttbare markøren
            .Select(r =>
            {
                var type = r.Ressurstype?.Navn ?? "Ukjent";
                var status = ResourceOperationalStatus.From(r, nå);
                var lenke = Url?.Action(nameof(Map), new { focus = r.RessursID }) + "#ressurs-" + r.RessursID;
                var popup = $"<strong>{html.Encode(r.Navn)}</strong><br>Type: {html.Encode(type)}<br>" +
                            $"Sted: {html.Encode(r.Stedsnavn)}<br>Status: {html.Encode(status.Availability)} · " +
                            $"Godkjenning: {html.Encode(status.Verification)}<br><a href=\"{html.Encode(lenke)}\">Vis detaljer i listen</a>";
                return new KartMarkor((double)r.Breddegrad, (double)r.Lengdegrad, BildeUrl(RessursIkon.Sti(type, status)),
                    $"{r.Navn} ({type}) – {status.Availability}", popup, r.RessursID == focus);
            })
            .ToList();

        if (User.Identity?.IsAuthenticated == true)
        {
            var akutteBehov = await db.Behov.AsNoTracking().Include(b => b.Behovstype!)
                .ThenInclude(t => t.PassendeRessurstyper).ThenInclude(k => k.Ressurstype)
                .Where(b => b.Prioritet == Statuser.Prioritet.Akutt && b.Status != Statuser.Behov.Løst)
                .OrderBy(b => b.Frist ?? DateTime.MaxValue).ToListAsync();
            foreach (var behov in akutteBehov)
            {
                var ressurstype = behov.Behovstype?.PassendeRessurstyper.FirstOrDefault()?.Ressurstype?.Navn;
                var lenke = Url?.Action(nameof(Dashboard)) + "#behov-" + behov.BehovID;
                var popup = $"<strong>AKUTT: {html.Encode(behov.Behovstype?.Navn ?? "Behov")}</strong><br>" +
                            $"Sted: {html.Encode(behov.Stedsnavn)}<br>{html.Encode(behov.Beskrivelse)}<br>" +
                            $"<a href=\"{html.Encode(lenke)}\">Åpne behovet</a>";
                markører.Add(new KartMarkor((double)behov.Breddegrad, (double)behov.Lengdegrad,
                    BildeUrl(RessursIkon.Sti(ressurstype)),
                    $"Akutt behov: {behov.Stedsnavn}", popup, false, true));
            }
        }

        var registrerer = User.IsInRole(AppRoles.Ressursleverandor) && (redigerer is not null || !ModelState.IsValid
            || HttpContext?.Request.Query["register"].ToString() == "true");
        ViewBag.Focus = focus;
        ViewBag.Kart = senter with
        {
            Markorer = markører,
            Plassering = registrerer
                ? new KartPlassering("latitude", "longitude", "selectedPlace",
                    harPunkt ? model.Breddegrad : null, harPunkt ? model.Lengdegrad : null)
                : null
        };
    }

    private string BildeUrl(string sti) => Url?.Content(sti) ?? sti.TrimStart('~');

    /// <summary>Alle ressurstyper fra tabellen Ressurstype, også de brukerne har lagt til selv.</summary>
    private async Task<List<string>> RessurstypeNavnAsync() =>
        await db.Ressurstyper.AsNoTracking().OrderBy(t => t.RessurstypeID).Select(t => t.Navn).ToListAsync();

    private async Task<RessursSkjemaViewModel> NewResourceFormAsync()
    {
        var actorId = ResourcePermissions.BrukerId(User);
        var owner = actorId is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actorId);
        return new RessursSkjemaViewModel
        {
            Kontaktperson = owner?.FulltNavn ?? string.Empty,
            Kontakttelefon = owner?.PhoneNumber ?? string.Empty
        };
    }

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
