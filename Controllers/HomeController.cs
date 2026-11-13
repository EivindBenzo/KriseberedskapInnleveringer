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

    private static readonly Dictionary<string, string> DemoPlaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dispatch@gmail.com"] = "Lillehammer", ["kriseledelse@gmail.com"] = "Hamar", ["ressurs@gmail.com"] = "Gjøvik",
        ["logistikk@gmail.com"] = "Elverum", ["politi@gmail.com"] = "Hamar", ["energi@gmail.com"] = "Kongsvinger",
        ["transport@gmail.com"] = "Lillehammer", ["kommune@gmail.com"] = "Lillehammer", ["samband@gmail.com"] = "Hamar"
    };
    private static readonly string[] DemoActivity =
    [
        "Melding sendt: Oppdatert status for ressurser", "Ressurs tildelt: Lastebil (LB-02)", "Hendelse oppdatert: Status endret til På vei"
    ];

    [HttpGet]
    public async Task<IActionResult> Contacts()
    {
        var contacts = await (from user in db.Users.AsNoTracking()
                              join userRole in db.UserRoles on user.Id equals userRole.UserId
                              join role in db.Roles on userRole.RoleId equals role.Id
                              where role.Name != null && role.Name != AppRoles.OffentligAktor &&
                                    user.Email != "verifisering@gmail.com" && AppRoles.All.Contains(role.Name)
                              orderby role.Name, user.Fornavn, user.Etternavn
                              select new ContactDirectoryEntry(
                                  user.Id, user.FulltNavn, role.Name!, user.Organisasjon, user.Email ?? user.UserName ?? "Ikke oppgitt",
                                  user.PhoneNumber)).ToListAsync();
        var ids = contacts.Select(c => c.UserId).ToList();
        var ressurser = await db.Ressurser.AsNoTracking().Where(r => ids.Contains(r.EierID))
            .Select(r => new { r.EierID, r.Navn, r.Stedsnavn, r.Breddegrad, r.Lengdegrad }).ToListAsync();
        var logg = await db.Hendelseslogg.AsNoTracking().Where(l => ids.Contains(l.UtførtAvID))
            .OrderByDescending(l => l.Tidspunkt).Select(l => new { l.UtførtAvID, l.Handling, l.Tidspunkt }).Take(400).ToListAsync();
        contacts = contacts.Select(c =>
        {
            var egne = ressurser.Where(r => r.EierID == c.UserId).ToList();
            var sted = DemoPlaces.GetValueOrDefault(c.Email) ?? egne.Select(r => NorwegianPlaces.Display(r.Stedsnavn, r.Breddegrad, r.Lengdegrad)).FirstOrDefault();
            var aktivitet = logg.Where(l => l.UtførtAvID == c.UserId).Take(3)
                .Select(l => new ContactActivity(Services.ShellService.Menneskelig(l.Handling), l.Tidspunkt)).ToList();
            if (aktivitet.Count < 3) aktivitet.AddRange(DemoActivity.Skip(aktivitet.Count).Select((a, i) => new ContactActivity(a, DateTime.UtcNow.AddHours(-3 * (i + 1) - c.UserId % 5))));
            return c with { Place = sted, Resources = egne.Select(r => r.Navn).Take(8).ToList(), Activity = aktivitet };
        }).ToList();
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
        string? city = null, bool history = false, string? sort = null)
    {
        ViewBag.Sort = sort;
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
                var godkjentTil = WorkflowEligibility.GodkjentTil(r);
                var popup = PanelHtml(html, $"Ressurs · {type}", r.Navn, BildeUrl(RessursIkon.Sti(type, status)), false,
                    NorwegianPlaces.Display(r.Stedsnavn, r.Breddegrad, r.Lengdegrad), r.Kontaktperson, r.Kontakttelefon,
                    [(status.Availability, status.IsEligible ? "ok" : "warn"), ($"Godkjenning: {status.Verification}", "info")],
                    [("Beholdning", $"{r.TilgjengeligAntall:0.##} av {r.Antall:0.##} {r.Enhet} tilgjengelig"),
                     ("Tilgjengelig", $"fra {r.TilgjengeligFra:dd.MM.yyyy}" + (r.TilgjengeligTil is DateOnly til ? $" til {til:dd.MM.yyyy}" : "")),
                     ("Type", type)],
                    r.Beskrivelse,
                    [("Tilgjengelighet", status.Availability), ("Godkjenning", status.Verification),
                     ("Godkjent til", godkjentTil?.ToString("dd.MM.yyyy HH:mm") + " UTC"), ("Registrert", r.Registrert.ToString("dd.MM.yyyy HH:mm") + " UTC")],
                    (double)r.Breddegrad, (double)r.Lengdegrad, lenke, "Se detaljer",
                    User.IsInRole(AppRoles.DispatchTeam) ? Url?.Action("Index", "Operations", new { vis = "needs" }) + "#needs" : null,
                    status.IsEligible ? "Klar til å forespørres." : "Sjekk status før tildeling.");
                return new KartMarkor((double)r.Breddegrad, (double)r.Lengdegrad, BildeUrl(RessursIkon.Sti(type, status)),
                    $"{r.Navn} ({type}) – {status.Availability}", popup, r.RessursID == focus, false, null, r.RessursID, status.IsEligible);
            })
            .ToList();
        var resourceIds = ressurser.Select(resource => resource.RessursID).ToList();
        var deliveryStates = await db.Tildelinger.AsNoTracking().Include(assignment => assignment.Forespørsel)
            .Where(assignment => resourceIds.Contains(assignment.Forespørsel!.RessursID))
            .OrderByDescending(assignment => assignment.SistOppdatert)
            .Select(assignment => new { assignment.Forespørsel!.RessursID, assignment.Fremdrift, assignment.Fullført })
            .ToListAsync();
        var latestDelivery = deliveryStates.GroupBy(assignment => assignment.RessursID)
            .ToDictionary(group => group.Key, group => group.First());
        markører = markører.Select(marker =>
        {
            if (marker.RessursId is not int resourceId || !latestDelivery.TryGetValue(resourceId, out var delivery)) return marker;
            var label = delivery.Fullført is not null ? "Fullført" : Statuser.Visning(delivery.Fremdrift);
            return marker with
            {
                LeveranseStatus = label,
                Popup = marker.Popup.Replace("<!--chips-->", $"<span class=\"map-popup-chip is-info\">Leveranse: {html.Encode(label)}</span>", StringComparison.Ordinal)
            };
        }).ToList();

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
                var popup = PanelHtml(html, "Akutt behov", behov.Behovstype?.Navn ?? "Behov", BildeUrl(RessursIkon.Sti(ressurstype)), true,
                    NorwegianPlaces.Display(behov.Stedsnavn, behov.Breddegrad, behov.Lengdegrad), behov.Kontaktperson, behov.Kontakttelefon,
                    [("Akutt", "urgent"), (Statuser.Visning(behov.Status), "info")],
                    [("Frist", behov.Frist?.ToString("dd.MM.yyyy HH:mm") + " UTC"), ("Status", Statuser.Visning(behov.Status))],
                    behov.Beskrivelse,
                    [("Prioritet", "Akutt"), ("Status", Statuser.Visning(behov.Status)), ("Frist", behov.Frist?.ToString("dd.MM.yyyy HH:mm") + " UTC")],
                    (double)behov.Breddegrad, (double)behov.Lengdegrad, lenke, "Åpne behovet", null,
                    "Akutt behov – kontakt stedet så raskt som mulig.");
                markører.Add(new KartMarkor((double)behov.Breddegrad, (double)behov.Lengdegrad,
                    BildeUrl(RessursIkon.Sti(ressurstype)),
                    $"Akutt behov: {behov.Stedsnavn}", popup, false, true));
            }
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            var hendelser = await db.Hendelser.AsNoTracking()
                .Where(h => h.Status != Statuser.Hendelse.Avsluttet && h.Breddegrad != null && h.Lengdegrad != null).ToListAsync();
            foreach (var h in hendelser)
            {
                var prio = h.Prioritet == Statuser.HendelsePrioritet.Hoy ? "Høy prioritet" : h.Prioritet == Statuser.HendelsePrioritet.Medium ? "Medium prioritet" : "Lav prioritet";
                var etikett = Statuser.Hendelse.Etikett(h.Status);
                var lenke = User.IsInRole(AppRoles.DispatchTeam) || User.IsInRole(AppRoles.Kriseledelse)
                    ? Url?.Action("Arbeidsrom", "Hendelse", new { id = h.HendelseID }) : Url?.Action("Index", "Behov");
                var popup = PanelHtml(html, "Hendelse", h.Navn, BildeUrl("~/images/hendelse-varsel.svg"), true,
                    h.Område, null, null,
                    [(etikett, "urgent"), (prio, "info")],
                    [("Status", etikett), ("Prioritet", prio), ("Startet", h.Starttidspunkt.ToString("dd.MM.yyyy HH:mm") + " UTC")],
                    h.Beskrivelse,
                    [("Status", etikett), ("Prioritet", prio)],
                    (double)h.Breddegrad!.Value, (double)h.Lengdegrad!.Value, lenke ?? "#", "Åpne hendelsen", null,
                    "Hendelse – følg opp i arbeidsrommet.");
                markører.Add(new KartMarkor((double)h.Breddegrad!.Value, (double)h.Lengdegrad!.Value,
                    BildeUrl("~/images/hendelse-varsel.svg"), $"Hendelse: {h.Navn}", popup, false, false,
                    null, null, false, true));
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

    /// <summary>
    /// Innholdet i detaljpanelet som åpnes når en markør velges: overskrift, kort sammendrag og tre faner
    /// (Oversikt, Handlinger, Status). All tekst kodes (encoded) her på serveren; kart.js bytter fane og legger til avstand.
    /// </summary>
    private static string PanelHtml(HtmlEncoder html, string kicker, string tittel, string ikon, bool akutt,
        string sted, string? kontakt, string? telefon, IEnumerable<(string Tekst, string Type)> merker,
        IEnumerable<(string Etikett, string? Verdi)> oversikt, string? beskrivelse,
        IEnumerable<(string Etikett, string? Verdi)> status, double lat, double lng, string lenke, string lenkeTekst,
        string? foresporselLenke, string hint)
    {
        string E(string? s) => html.Encode(s ?? "");
        string Rader(IEnumerable<(string Etikett, string? Verdi)> rader) =>
            string.Concat(rader.Where(r => !string.IsNullOrWhiteSpace(r.Verdi?.Trim())).Select(r => $"<div><dt>{E(r.Etikett)}</dt><dd>{E(r.Verdi)}</dd></div>"));
        var chips = string.Concat(merker.Select(m => $"<span class=\"mp-chip is-{m.Type}\">{E(m.Tekst)}</span>"));
        var tel = new string((telefon ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var rute = $"https://www.google.com/maps/dir/?api=1&destination={lat.ToString(inv)},{lng.ToString(inv)}&travelmode=driving";
        string Handling(string href, string svg, string tekst, string attr = "") =>
            $"<a class=\"mp-action\" href=\"{E(href)}\" {attr}><svg viewBox=\"0 0 24 24\" width=\"22\" height=\"22\" aria-hidden=\"true\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{svg}</svg><span>{E(tekst)}</span><i aria-hidden=\"true\">›</i></a>";
        var handlinger = Handling(rute, "<path d=\"M5 20v-6a4 4 0 0 1 4-4h10\"/><path d=\"m15 5 4 5-4 5\"/>", "Veibeskrivelse", "target=\"_blank\" rel=\"noopener noreferrer\" data-primary")
            + (tel.Length > 0 ? Handling("tel:" + tel, "<path d=\"M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2z\"/>", akutt ? "Ring kontakt" : "Ring eier")
                                 + Handling("sms:" + tel, "<path d=\"M4 5h16v11H9l-5 4z\"/>", "Send melding") : "")
            + Handling(lenke, "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 11v5M12 8h.01\"/>", lenkeTekst)
            + (foresporselLenke is null ? "" : Handling(foresporselLenke, "<rect x=\"4\" y=\"4\" width=\"16\" height=\"16\" rx=\"2\"/><path d=\"M12 8v8M8 12h8\"/>", "Opprett forespørsel"))
            + Handling(lenke, "<circle cx=\"6\" cy=\"12\" r=\"2.5\"/><circle cx=\"18\" cy=\"6\" r=\"2.5\"/><circle cx=\"18\" cy=\"18\" r=\"2.5\"/><path d=\"m8.3 10.9 7.4-3.8M8.3 13.1l7.4 3.8\"/>", "Del lenke", "data-share");
        return $"<div class=\"mp-head{(akutt ? " is-urgent" : "")}\"><span class=\"mp-icon\"><img src=\"{E(ikon)}\" alt=\"\" width=\"30\" height=\"30\" /></span>" +
               $"<div><span class=\"mp-kicker\">{E(kicker)}</span><strong class=\"mp-title\">{E(tittel)}</strong></div></div>" +
               "<div class=\"mp-body\">" +
               "<div class=\"mp-summary\">" +
               $"<p class=\"mp-line mp-place\"><svg viewBox=\"0 0 24 24\" width=\"20\" height=\"20\" aria-hidden=\"true\"><path fill=\"currentColor\" d=\"M12 2a7 7 0 0 0-7 7c0 5 7 13 7 13s7-8 7-13a7 7 0 0 0-7-7zm0 9.5A2.5 2.5 0 1 1 12 6.5a2.5 2.5 0 0 1 0 5z\"/></svg><span>{E(sted)}</span></p>" +
               (string.IsNullOrWhiteSpace(kontakt) && tel.Length == 0 ? "" :
                   $"<p class=\"mp-line mp-contact\"><svg viewBox=\"0 0 24 24\" width=\"20\" height=\"20\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"8\" r=\"4\" fill=\"currentColor\"/><path d=\"M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7z\" fill=\"currentColor\"/></svg><span>{E(string.Join(" · ", new[] { kontakt, telefon }.Where(x => !string.IsNullOrWhiteSpace(x))))}</span></p>") +
               $"<div class=\"mp-chips\">{chips}<!--chips--></div></div>" +
               "<div class=\"mp-tabs\" role=\"tablist\"><button type=\"button\" role=\"tab\" data-tab=\"oversikt\" aria-selected=\"true\">Oversikt</button>" +
               "<button type=\"button\" role=\"tab\" data-tab=\"handlinger\" aria-selected=\"false\">Handlinger</button>" +
               "<button type=\"button\" role=\"tab\" data-tab=\"status\" aria-selected=\"false\">Status</button></div>" +
               $"<div class=\"mp-pane\" data-pane=\"oversikt\" role=\"tabpanel\"><dl class=\"mp-facts\">{Rader(oversikt)}</dl>" +
               (string.IsNullOrWhiteSpace(beskrivelse) ? "" : $"<p class=\"mp-desc\">{E(beskrivelse)}</p>") +
               "<p class=\"mp-distance\" hidden></p></div>" +
               $"<div class=\"mp-pane\" data-pane=\"handlinger\" role=\"tabpanel\" hidden><p class=\"mp-hint\"><svg viewBox=\"0 0 24 24\" width=\"22\" height=\"22\" aria-hidden=\"true\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 11v5M12 8h.01\"/></svg>{E(hint)}</p><div class=\"mp-actions\">{handlinger}</div></div>" +
               $"<div class=\"mp-pane\" data-pane=\"status\" role=\"tabpanel\" hidden><dl class=\"mp-facts\">{Rader(status)}</dl></div>" +
               "</div>";
    }    private string BildeUrl(string sti) => Url?.Content(sti) ?? sti.TrimStart('~');

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
        var ressurser = await ResourceQueries.LoadAsync(db, filter, now);
        var typeScope = await ResourceQueries.LoadAsync(db, filter with { Type = null }, now);
        var ids = ressurser.Select(r => r.RessursID).ToList();
        var logg = await db.Hendelseslogg.AsNoTracking()
            .Where(l => l.RessursID != null && ids.Contains(l.RessursID.Value))
            .OrderByDescending(l => l.Tidspunkt).Take(600)
            .Select(l => new { l.RessursID, l.Handling, l.Begrunnelse, l.Tidspunkt }).ToListAsync();
        return new DataPageViewModel
        {
            Cities = ResourceCity.Aggregate(await ResourceQueries.LoadAsync(db, cityScope, now)),
            Ressurser = ressurser,
            TypeCounts = typeScope.GroupBy(r => r.Ressurstype?.Navn ?? "Ukjent").ToDictionary(g => g.Key, g => g.Count()),
            Activity = logg.GroupBy(l => l.RessursID!.Value).ToDictionary(g => g.Key,
                g => (IReadOnlyList<ResourceActivity>)g.Take(3).Select(l => new ResourceActivity(l.Handling, l.Begrunnelse, l.Tidspunkt)).ToList())
        };
    }
}
