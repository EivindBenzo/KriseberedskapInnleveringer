using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>Offentlige aktører melder behov. Skjemaet har et kart der brukeren klikker inn hvor behovet er.</summary>
[Authorize(Roles = AppRoles.OffentligAktor)]
public class BehovController(AppDbContext db, WorkflowService workflow, IPlaceGeocoder geocoder) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Registrer()
    {
        // Kontaktfeltene starter tomme og viser bare eksempeltekst.
        await FyllValgAsync();
        var model = new BehovSkjemaViewModel();
        ByggKart(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrer(BehovSkjemaViewModel model)
    {
        ModelState.Remove(nameof(model.Stedsnavn));
        if (model.BehovstypeID == 0)
            ModelState.AddModelError(nameof(model.BehovstypeID), "Velg en behovstype");
        else if (model.BehovstypeID == BehovSkjemaViewModel.AnnenType)
        {
            // Brukeren har valgt «Annen type …» og skrevet inn en ny behovstype selv.
            var ny = model.NyBehovstype?.Trim() ?? string.Empty;
            if (ny.Length is < 2 or > 100 || !ny.Any(char.IsLetter))
                ModelState.AddModelError(nameof(model.NyBehovstype), "Skriv inn behovstypen (2–100 tegn).");
        }
        if (!Statuser.Prioritet.Alle.Contains(model.Prioritet))
            ModelState.AddModelError(nameof(model.Prioritet), "Velg prioritet.");
        if (model.Frist is DateTime frist && frist < DateTime.Now.AddMinutes(-5))
            ModelState.AddModelError(nameof(model.Frist), "Fristen kan ikke være tilbake i tid.");
        if (PlaceGeocoder.ValidPoint(model.Breddegrad, model.Lengdegrad))
            model.Stedsnavn = await geocoder.ReverseAsync(model.Breddegrad!.Value, model.Lengdegrad!.Value,
                HttpContext?.RequestAborted ?? default);

        if (ModelState.IsValid)
        {
            try
            {
                await workflow.CreateNeedAsync(model, User);
                TempData["WorkflowSuccess"] = $"Behovet ved {model.Stedsnavn} er meldt inn.";
                return RedirectToAction("Dashboard", "Home");
            }
            catch (WorkflowException exception)
            {
                ModelState.AddModelError("", exception.Message);
            }
        }
        await FyllValgAsync();
        ByggKart(model);
        return View(model);
    }

    /// <summary>
    /// Kartet i skjemaet: starter over Innlandet (eller over punktet som allerede er valgt),
    /// og et klikk i kartet fyller ut feltene needLat og needLng.
    /// </summary>
    private void ByggKart(BehovSkjemaViewModel model)
    {
        var harPunkt = PlaceGeocoder.ValidPoint(model.Breddegrad, model.Lengdegrad);
        var senter = harPunkt
            ? new KartVisning(model.Breddegrad!.Value, model.Lengdegrad!.Value, 13)
            : new KartVisning(60.7945, 11.0680, 8);
        ViewBag.Kart = senter with
        {
            Plassering = new KartPlassering("needLat", "needLng", "needPlace",
                harPunkt ? model.Breddegrad : null, harPunkt ? model.Lengdegrad : null)
        };
    }

    private async Task FyllValgAsync()
    {
        ViewBag.Behovstyper = await db.Behovstyper.AsNoTracking().OrderBy(t => t.BehovstypeID).ToListAsync();
        ViewBag.Hendelser = new SelectList(await db.Hendelser.AsNoTracking()
                .Where(h => h.Status == Statuser.Hendelse.Aktiv).OrderByDescending(h => h.Starttidspunkt).ToListAsync(),
            nameof(Hendelse.HendelseID), nameof(Hendelse.Navn));
    }
}
