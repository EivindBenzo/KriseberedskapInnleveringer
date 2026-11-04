using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Kriseledelsen oppretter og avslutter hendelser (f.eks. «Flom i Glomma») som behov kan knyttes til.</summary>
[Authorize(Roles = AppRoles.Kriseledelse)]
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
}
