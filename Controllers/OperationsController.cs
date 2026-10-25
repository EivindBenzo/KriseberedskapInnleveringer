using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class OperationsController(AppDbContext db, WorkflowService workflow) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var admin = User.IsInRole("Admin");
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(actorId)) return Forbid();
        var requests = await db.ResourceRequests.AsNoTracking().Include(r => r.Resource).Include(r => r.Need)
            .Where(r => admin || r.Resource.OwnerId == actorId).OrderByDescending(r => r.CreatedAtUtc).ToListAsync();
        return View(new OperationsViewModel
        {
            Resources = await db.MapSubmissions.AsNoTracking().Where(r => admin || r.OwnerId == actorId)
                .OrderBy(r => r.ResourceName).ToListAsync(),
            Needs = admin
                ? await db.OperationalNeeds.AsNoTracking().OrderByDescending(n => n.CreatedAtUtc).ToListAsync()
                : requests.Select(r => r.Need).DistinctBy(n => n.Id).ToList(),
            Requests = requests,
            Audit = admin ? await db.WorkflowAudits.AsNoTracking().OrderByDescending(a => a.Id).Take(100).ToListAsync() : []
        });
    }

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> Verify(int id, bool approve, string token, string reason) =>
        Run(() => workflow.VerifyAsync(id, approve, token, User, reason));

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateNeed(string description, string place, NeedPriority priority) =>
        Run(async () => { await workflow.CreateNeedAsync(description, place, priority, User); });

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> NeedStatus(int id, Models.NeedStatus status, string token, string reason) =>
        Run(() => workflow.ChangeNeedStatusAsync(id, status, token, User, reason));

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateRequest(int needId, int resourceId, string resourceToken, string needToken, string reason) =>
        Run(async () => { await workflow.CreateRequestAsync(needId, resourceId, resourceToken, needToken, User, reason); });

    [HttpPost]
    public Task<IActionResult> Respond(int id, bool accept, string token, string reason) =>
        Run(() => workflow.RespondAsync(id, accept, token, User, reason));

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> Allocate(int id, string token, string reason) =>
        Run(() => workflow.AllocateAsync(id, token, User, reason));

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<IActionResult> Cancel(int id, string token, string reason) =>
        Run(() => workflow.CancelAsync(id, token, User, reason));

    [HttpPost]
    public Task<IActionResult> Return(int id, string token, string reason) =>
        Run(() => workflow.ReturnAsync(id, token, User, reason));

    [HttpPost]
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
