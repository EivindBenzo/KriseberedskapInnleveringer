using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Kriseledelsen oppretter og avslutter hendelser (f.eks. «Flom i Glomma») som behov kan knyttes til.</summary>
[Authorize(Roles = AppRoles.DispatchTeam + "," + AppRoles.Kriseledelse)]
public class HendelseController(AppDbContext db, WorkflowService workflow) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.Hendelser = await db.Hendelser.AsNoTracking().Include(h => h.Behov)
            .OrderBy(h => h.Status).ThenByDescending(h => h.Starttidspunkt).ToListAsync();
        return View(new HendelseSkjemaViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Kriseledelse)]
    public async Task<IActionResult> Index(HendelseSkjemaViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await workflow.CreateIncidentAsync(model, User);
                TempData["WorkflowSuccess"] = $"Hendelsen «{model.Navn}» er opprettet.";
                return RedirectToAction(nameof(Index));
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        ViewBag.Hendelser = await db.Hendelser.AsNoTracking().Include(h => h.Behov)
            .OrderBy(h => h.Status).ThenByDescending(h => h.Starttidspunkt).ToListAsync();
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
        var activity = await db.Hendelseslogg.AsNoTracking()
            .Where(l => l.HendelseID == id)
            .OrderByDescending(l => l.Tidspunkt)
            .Take(20)
            .ToListAsync();

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
