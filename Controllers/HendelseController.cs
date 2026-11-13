using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Kriseledelsen oppretter og avslutter hendelser (f.eks. «Flom i Glomma») som behov kan knyttes til.</summary>
[Authorize(Roles = AppRoles.DispatchTeam + "," + AppRoles.Kriseledelse)]
public class HendelseController(AppDbContext db, WorkflowService workflow, Stedssok stedssok, UserManager<Bruker> users) : Controller
{
    private async Task FyllHendelserAsync()
    {
        var hendelser = await db.Hendelser.AsNoTracking().Include(h => h.Behov)
            .OrderBy(h => h.Status == Statuser.Hendelse.Aktiv ? 0 : h.Status == Statuser.Hendelse.Planlagt ? 1 : 2)
            .ThenByDescending(h => h.Starttidspunkt).ToListAsync();
        var tildelinger = await db.Tildelinger.AsNoTracking()
            .Where(t => t.Forespørsel!.Behov!.HendelseID != null)
            .Select(t => new { Id = t.Forespørsel!.Behov!.HendelseID!.Value, t.Forespørsel.RessursID, Ferdig = t.Fullført != null })
            .ToListAsync();
        var aktører = await db.Hendelseslogg.AsNoTracking().Where(l => l.HendelseID != null)
            .Select(l => new { Id = l.HendelseID!.Value, l.UtførtAvID }).Distinct().ToListAsync();
        var festBruker = await users.GetUserAsync(User);
        var festRå = festBruker is null ? "" : await users.GetAuthenticationTokenAsync(festBruker, "Profil", "FestetHendelser") ?? "";
        var festet = festRå.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)
            .Where(i => hendelser.Any(h => h.HendelseID == i && h.Status == Statuser.Hendelse.Aktiv)).Take(5).ToHashSet();
        ViewBag.Festet = festet;
        hendelser = hendelser.OrderBy(h => festet.Contains(h.HendelseID) ? 0 : 1).ToList();
        ViewBag.Hendelser = hendelser;
        ViewBag.Kort = hendelser.ToDictionary(h => h.HendelseID, h =>
        {
            var t = tildelinger.Where(x => x.Id == h.HendelseID).ToList();
            var ferdig = t.Count(x => x.Ferdig);
            var prosent = h.Status == Statuser.Hendelse.Avsluttet ? 100
                : t.Count > 0 ? (int)Math.Round(100.0 * ferdig / t.Count) : 0;
            return new HendelseKort(t.Count, ferdig, t.Select(x => x.RessursID).Distinct().Count(),
                aktører.Count(a => a.Id == h.HendelseID), prosent, h.Behov.Count);
        });
        var aktive = hendelser.Where(h => h.Status == Statuser.Hendelse.Aktiv).OrderBy(h => h.Starttidspunkt).ToList();
        Hendelse? featured = null;
        if (aktive.Count == 1) featured = aktive[0];
        else if (aktive.Count > 1)
        {
            var bruker = await users.GetUserAsync(User);
            var lagret = bruker is null ? null : await users.GetAuthenticationTokenAsync(bruker, "Profil", "Hovedhendelse");
            featured = aktive.FirstOrDefault(h => h.HendelseID.ToString() == lagret) ?? aktive[0];
        }
        featured ??= hendelser.Where(h => h.Status == Statuser.Hendelse.Planlagt).OrderBy(h => h.Starttidspunkt).FirstOrDefault()
            ?? hendelser.OrderByDescending(h => h.Starttidspunkt).FirstOrDefault();
        ViewBag.HovedId = featured?.HendelseID;
        if (featured is not null)
            ViewBag.Siste = await (from l in db.Hendelseslogg.AsNoTracking()
                                   join b in db.Users.AsNoTracking() on l.UtførtAvID equals b.Id
                                   where l.HendelseID == featured.HendelseID
                                   orderby l.Tidspunkt descending
                                   select new HendelseLoggRad(l.Tidspunkt, l.Handling, l.Begrunnelse, b.Fornavn + " " + b.Etternavn))
                .Take(4).ToListAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Hovedhendelse(int id)
    {
        var hendelse = await db.Hendelser.AsNoTracking().SingleOrDefaultAsync(h => h.HendelseID == id);
        var bruker = await users.GetUserAsync(User);
        if (hendelse is null || hendelse.Status != Statuser.Hendelse.Aktiv || bruker is null)
            TempData["WorkflowError"] = "Bare en pågående hendelse kan være hovedhendelse.";
        else
            await users.SetAuthenticationTokenAsync(bruker, "Profil", "Hovedhendelse", id.ToString());
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Fest(int id)
    {
        var hendelse = await db.Hendelser.AsNoTracking().SingleOrDefaultAsync(h => h.HendelseID == id);
        var bruker = await users.GetUserAsync(User);
        if (hendelse is null || bruker is null || hendelse.Status != Statuser.Hendelse.Aktiv) return RedirectToAction(nameof(Index));
        var rå = await users.GetAuthenticationTokenAsync(bruker, "Profil", "FestetHendelser") ?? "";
        var liste = rå.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        if (!liste.Remove(id))
        {
            if (liste.Count >= 5) TempData["WorkflowError"] = "Du kan feste maks 5 hendelser.";
            else liste.Add(id);
        }
        await users.SetAuthenticationTokenAsync(bruker, "Profil", "FestetHendelser", string.Join(",", liste));
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await FyllHendelserAsync();
        return View(new HendelseSkjemaViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Kriseledelse)]
    public async Task<IActionResult> Status(int id, string status)
    {
        try { await workflow.SetIncidentStatusAsync(id, status, User); }
        catch (WorkflowException exception) { TempData["WorkflowError"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Kriseledelse)]
    public async Task<IActionResult> Prioritet(int id, string prioritet)
    {
        try { await workflow.SetIncidentPriorityAsync(id, prioritet, User); }
        catch (WorkflowException exception) { TempData["WorkflowError"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Kriseledelse)]
    public async Task<IActionResult> Index(HendelseSkjemaViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                if (model.Breddegrad is null || model.Lengdegrad is null)
                {
                    var treff = await stedssok.SøkAsync(model.Område, HttpContext.RequestAborted);
                    if (treff.Count > 0) { model.Breddegrad = (decimal)treff[0].Lat; model.Lengdegrad = (decimal)treff[0].Lng; }
                }
                await workflow.CreateIncidentAsync(model, User);
                TempData["WorkflowSuccess"] = $"Hendelsen «{model.Navn}» er opprettet.";
                return RedirectToAction(nameof(Index));
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        await FyllHendelserAsync();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Kriseledelse)]
    public async Task<IActionResult> Avslutt(int id)
    {
        try
        {
            await workflow.CloseIncidentAsync(id, User);
            TempData["WorkflowSuccess"] = "Hendelsen er avsluttet.";
        }
        catch (WorkflowException exception)
        {
            TempData["WorkflowError"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Arbeidsrom(int id)
    {
        var incident = await db.Hendelser.AsNoTracking()
            .SingleOrDefaultAsync(h => h.HendelseID == id);
        if (incident is null) return NotFound();

        var needs = await db.Behov.AsNoTracking()
            .Include(b => b.Behovstype)
            .Where(b => b.HendelseID == id && Statuser.Behov.Aktive.Contains(b.Status))
            .OrderByDescending(b => b.Prioritet == Statuser.Prioritet.Akutt)
            .ThenBy(b => b.Frist)
            .ToListAsync();
        var assignments = await db.Tildelinger.AsNoTracking()
            .Include(t => t.Forespørsel!).ThenInclude(f => f.Ressurs)
            .Include(t => t.Forespørsel!).ThenInclude(f => f.Behov).ThenInclude(b => b!.Behovstype)
            .Where(t => t.Forespørsel!.Behov!.HendelseID == id && t.Fremdrift != Statuser.Fremdrift.Fullført)
            .OrderByDescending(t => t.SistOppdatert)
            .ToListAsync();
        var activity = await (from l in db.Hendelseslogg.AsNoTracking()
                              join b in db.Users.AsNoTracking() on l.UtførtAvID equals b.Id
                              where l.HendelseID == id
                              orderby l.Tidspunkt descending
                              select new HendelseLoggRad(l.Tidspunkt, l.Handling, l.Begrunnelse, b.Fornavn + " " + b.Etternavn))
            .Take(20).ToListAsync();

        return View(new IncidentWorkspaceViewModel
        {
            Incident = incident,
            OpenNeeds = needs,
            ActiveAssignments = assignments,
            Activity = activity
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Beslutning(int id, string? note)
    {
        var userId = ResourcePermissions.BrukerId(User);
        if (userId is null) return Forbid();
        var text = note?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
        {
            TempData["WorkflowError"] = "Beslutningen må ha mellom 1 og 1000 tegn.";
            return RedirectToAction(nameof(Arbeidsrom), new { id });
        }
        if (!await db.Hendelser.AnyAsync(h => h.HendelseID == id))
            return NotFound();

        db.Hendelseslogg.Add(new Hendelseslogg
        {
            HendelseID = id,
            UtførtAvID = userId.Value,
            Handling = "OperativBeslutning",
            Begrunnelse = text,
            Tidspunkt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        TempData["WorkflowSuccess"] = "Beslutningen er lagt til i hendelsesloggen.";
        return RedirectToAction(nameof(Arbeidsrom), new { id });
    }
}
