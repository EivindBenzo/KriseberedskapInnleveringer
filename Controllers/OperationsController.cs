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
    /// <param name="vis">Seksjonen som skal være åpen når siden lastes: «resources» eller «needs».</param>
    [HttpGet]
    public async Task<IActionResult> Index(string? vis = null)
    {
        ViewBag.Vis = vis is "resources" or "needs" ? vis : null;
        if (ResourcePermissions.BrukerId(User) is not int actorId) return Forbid();
        var provider = User.IsInRole(AppRoles.Ressursleverandor);
        var dispatch = User.IsInRole(AppRoles.DispatchTeam);
        var leader = User.IsInRole(AppRoles.Kriseledelse);
        var publicActor = User.IsInRole(AppRoles.OffentligAktor);
        var now = DateTime.UtcNow;

        var resources = new Dictionary<int, Ressurs>();
        void Add(IEnumerable<Ressurs> found) { foreach (var r in found) resources[r.RessursID] = r; }
        IQueryable<Ressurs> All() => ResourceQueries.WithDetails(db.Ressurser.AsNoTracking()).AsSplitQuery();
        if (leader) Add(await All().ToListAsync());
        if (provider) Add(await All().Where(r => r.EierID == actorId).ToListAsync());
        if (dispatch)
        {
            Add(await All().Where(WorkflowEligibility.EligibleExpression(now)).ToListAsync());
            Add(await All().Where(WorkflowEligibility.TrengerVerifisering(now)).ToListAsync());
        }

        var requestQuery = db.Forespørsler.AsNoTracking()
            .Include(f => f.Ressurs!).ThenInclude(r => r.Ressurstype)
            .Include(f => f.Behov).Include(f => f.Tildeling).AsQueryable();
        var assignmentQuery = db.Tildelinger.AsNoTracking()
            .Include(t => t.Forespørsel!).ThenInclude(f => f.Ressurs)
            .Include(t => t.Forespørsel!).ThenInclude(f => f.Behov).AsQueryable();
        var seesAll = dispatch || leader;
        if (!seesAll)
        {
            requestQuery = requestQuery.Where(f => provider && f.Ressurs!.EierID == actorId);
            assignmentQuery = assignmentQuery.Where(t => provider && t.Forespørsel!.Ressurs!.EierID == actorId);
        }
        List<Forespørsel> requests = await requestQuery.OrderByDescending(f => f.Sendt).ToListAsync();
        List<Tildeling> assignments = await assignmentQuery.OrderByDescending(t => t.Tildelt).ToListAsync();

        var needs = new Dictionary<int, Behov>();
        var needQuery = db.Behov.AsNoTracking().Include(b => b.Behovstype!)
            .ThenInclude(type => type.PassendeRessurstyper).Include(b => b.Hendelse);
        if (seesAll) foreach (var b in await needQuery.ToListAsync()) needs[b.BehovID] = b;
        else
        {
            foreach (var b in requests.Select(f => f.Behov!)) needs[b.BehovID] = b;
            if (publicActor)
                foreach (var b in await needQuery.Where(b => b.RegistrertAvID == actorId).ToListAsync())
                    needs[b.BehovID] = b;
        }

        var audit = leader
            ? await db.Hendelseslogg.AsNoTracking().OrderByDescending(a => a.LoggID).Take(100).ToListAsync()
            : [];
        var actorIds = audit.Select(a => a.UtførtAvID).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? "Ukjent bruker");

        return View(new OperationsViewModel
        {
            Resources = resources.Values.OrderBy(r => r.Navn).ToList(),
            Needs = needs.Values.OrderByDescending(b => b.Registrert).ToList(),
            Requests = requests,
            Assignments = assignments,
            Audit = audit,
            ActorNames = names
        });
    }

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
    public Task<IActionResult> Assignment(int id, string status, string? reason) =>
        Run(() => workflow.UpdateAssignmentAsync(id, status, User, reason));

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
