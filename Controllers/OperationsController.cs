using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize(Roles = AppRoles.Any)]
[AutoValidateAntiforgeryToken]
public class OperationsController(AppDbContext db, WorkflowService workflow) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(actorId)) return Forbid();
        var provider = User.IsInRole(AppRoles.Ressursleverandor);
        var dispatch = User.IsInRole(AppRoles.DispatchTeam);
        var leader = User.IsInRole(AppRoles.Kriseledelse);
        var verifier = User.IsInRole(AppRoles.Verifiseringsansvarlig);
        var publicActor = User.IsInRole(AppRoles.OffentligAktor);
        var now = DateTime.UtcNow;

        var resources = new Dictionary<int, MapSubmission>();
        void Add(IEnumerable<MapSubmission> found) { foreach (var r in found) resources[r.Id] = r; }
        var all = db.MapSubmissions.AsNoTracking();
        if (leader) Add(await all.ToListAsync());
        if (provider) Add(await all.Where(r => r.OwnerId == actorId).ToListAsync());
        if (dispatch) Add(await all.Where(ResourceQueries.EligibleNow(now)).ToListAsync());
        if (verifier)
            Add(await all.Where(r => r.Availability != ResourceAvailability.Reserved &&
                r.Availability != ResourceAvailability.Allocated && r.Availability != ResourceAvailability.InUse &&
                (r.VerificationStatus == ResourceVerificationStatus.Pending ||
                 (r.VerificationStatus == ResourceVerificationStatus.Approved && !(r.VerificationExpiresAtUtc > now))))
                .ToListAsync());

        var requestQuery = db.ResourceRequests.AsNoTracking().Include(r => r.Resource).Include(r => r.Need).AsQueryable();
        var assignmentQuery = db.ResourceAssignments.AsNoTracking().Include(a => a.Resource).Include(a => a.Need).AsQueryable();
        var seesAll = dispatch || leader;
        if (!seesAll)
        {
            requestQuery = requestQuery.Where(r => provider && r.Resource.OwnerId == actorId);
            assignmentQuery = assignmentQuery.Where(a => provider && a.Resource.OwnerId == actorId);
        }
        var noRequests = verifier && !seesAll && !provider;
        List<ResourceRequest> requests = noRequests ? [] : await requestQuery.OrderByDescending(r => r.CreatedAtUtc).ToListAsync();
        List<ResourceAssignment> assignments = noRequests ? [] : await assignmentQuery.OrderByDescending(a => a.CreatedAtUtc).ToListAsync();

        var needs = new Dictionary<int, OperationalNeed>();
        if (seesAll) foreach (var n in await db.OperationalNeeds.AsNoTracking().ToListAsync()) needs[n.Id] = n;
        else
        {
            foreach (var n in requests.Select(r => r.Need)) needs[n.Id] = n;
            if (publicActor)
                foreach (var n in await db.OperationalNeeds.AsNoTracking().Where(n => n.CreatedById == actorId).ToListAsync())
                    needs[n.Id] = n;
        }
        return View(new OperationsViewModel
        {
            Resources = resources.Values.OrderBy(r => r.ResourceName).ToList(),
            Needs = needs.Values.OrderByDescending(n => n.CreatedAtUtc).ToList(),
            Requests = requests,
            Assignments = assignments,
            Audit = leader ? await db.WorkflowAudits.AsNoTracking().OrderByDescending(a => a.Id).Take(100).ToListAsync() : []
        });
    }

    [HttpPost, Authorize(Roles = AppRoles.Verifiseringsansvarlig)]
    public Task<IActionResult> Verify(int id, bool approve, string token, string? reason) =>
        Run(() => workflow.VerifyAsync(id, approve, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.OffentligAktor)]
    public Task<IActionResult> CreateNeed(string description, string place, NeedPriority priority) =>
        Run(async () => { await workflow.CreateNeedAsync(description, place, priority, User); });

    [HttpPost, Authorize(Roles = AppRoles.Kriseledelse)]
    public Task<IActionResult> NeedStatus(int id, Models.NeedStatus status, string token, string reason) =>
        Run(() => workflow.ChangeNeedStatusAsync(id, status, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.Kriseledelse)]
    public Task<IActionResult> Prioritize(int id, NeedPriority priority, string token, string reason) =>
        Run(() => workflow.ChangeNeedPriorityAsync(id, priority, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> CreateRequest(int needId, int resourceId, string resourceToken, string needToken, string reason) =>
        Run(async () => { await workflow.CreateRequestAsync(needId, resourceId, resourceToken, needToken, User, reason); });

    [HttpPost, Authorize(Roles = AppRoles.Ressursleverandor)]
    public Task<IActionResult> Respond(int id, bool accept, string token, string reason) =>
        Run(() => workflow.RespondAsync(id, accept, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Allocate(int id, string token, string reason) =>
        Run(() => workflow.AllocateAsync(id, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Cancel(int id, string token, string reason) =>
        Run(() => workflow.CancelAsync(id, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.DispatchTeam)]
    public Task<IActionResult> Assignment(int id, AssignmentStatus status, string token, string reason) =>
        Run(() => workflow.UpdateAssignmentAsync(id, status, token, User, reason));

    [HttpPost, Authorize(Roles = AppRoles.Ressursleverandor)]
    public Task<IActionResult> Availability(int id, ResourceAvailability availability, string token, string reason) =>
        Run(() => workflow.UpdateResourceAvailabilityAsync(id, availability, token, User, reason));

    private async Task<IActionResult> Run(Func<Task> action)
    {
        if (!ModelState.IsValid)
        {
            TempData["WorkflowError"] = "Ugyldige opplysninger. Kontroller feltene og prøv igjen.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            await action();
            TempData["WorkflowSuccess"] = "Endringen er lagret.";
        }
        catch (WorkflowException exception)
        {
            TempData["WorkflowError"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
