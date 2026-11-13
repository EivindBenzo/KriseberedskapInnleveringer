using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>
/// Forespørsler, tildelinger, verifisering og behovsstatus. Hver POST-handling har [Authorize] med riktig rolle,
/// og WorkflowService sjekker rolle og eierskap en gang til på serveren.
/// </summary>
[Authorize(Roles = AppRoles.Any)]
[AutoValidateAntiforgeryToken]
public class OperationsController(AppDbContext db, WorkflowService workflow) : Controller
{
    // Den gamle forespørselssiden er fjernet; alt arbeid skjer i Oversikt.
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Dashboard", "Home");

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Verify(int id, bool approve, string? reason) =>
        Run(() => workflow.VerifyAsync(id, approve, User, reason), approve ? "Ressursen er godkjent og kan nå forespørres." : "Ressursen er avvist.");

    [HttpPost, Authorize(Roles = AppRoles.Kriseledelse)]
    public Task<IActionResult> NeedStatus(int id, string status, string? reason) =>
        Run(() => workflow.ChangeNeedStatusAsync(id, status, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.Kriseledelse)]
    public Task<IActionResult> Prioritize(int id, string priority, string? reason) =>
        Run(() => workflow.ChangeNeedPriorityAsync(id, priority, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> CreateRequest(int needId, int resourceId, decimal quantity, string? reason) =>
        Run(async () => { await workflow.CreateRequestAsync(needId, resourceId, quantity, User, reason); },
            "Forespørselen er sendt til ressurseieren. Behovet venter på svar.");

    [HttpPost, Authorize(Roles = AppRoles.Ressursleverandor)]
    public Task<IActionResult> Respond(int id, bool accept, string? reason) =>
        Run(() => workflow.RespondAsync(id, accept, User, reason), accept
            ? "Du har godtatt forespørselen. Dispatch må nå opprette tildelingen før ressursen sendes ut."
            : "Du har avslått forespørselen. Ressursen er frigjort igjen.");

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Allocate(int id, string? reason) =>
        Run(() => workflow.AllocateAsync(id, User, reason),
            "Tildelingen er opprettet. Behovet er nå tildelt og kan følges under Aktive tildelinger.");

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Cancel(int id, string? reason) =>
        Run(() => workflow.CancelAsync(id, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Assignment(int id, string status, string? reason, DateTime? expectedArrival,
        string? receivingContact, decimal? deliveredQuantity, string? deviation) =>
        Run(() => workflow.UpdateAssignmentAsync(id, status, User, reason, expectedArrival, receivingContact,
            deliveredQuantity, deviation), status == Statuser.Fremdrift.Fullført
                ? "Overleveringen er bekreftet og oppdraget er fullført."
                : $"Tildelingen er oppdatert til {Statuser.Visning(status)}.");

    [HttpPost, Authorize(Roles = AppRoles.Ressursleverandor)]
    public Task<IActionResult> Availability(int id, bool available, string? reason) =>
        Run(() => workflow.UpdateResourceAvailabilityAsync(id, available, User, reason));

    private IActionResult Back()
    {
        var returnUrl = Request.Form["returnUrl"].ToString();
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Run(Func<Task> action, string? successMessage = null)
    {
        if (!ModelState.IsValid)
        {
            TempData["WorkflowError"] = "Ugyldige opplysninger. Kontroller feltene og prøv igjen.";
            return Back();
        }
        try
        {
            await action();
            TempData["WorkflowSuccess"] = successMessage ?? "Endringen er lagret.";
        }
        catch (WorkflowException exception)
        {
            TempData["WorkflowError"] = exception.Message;
        }
        return Back();
    }
}
