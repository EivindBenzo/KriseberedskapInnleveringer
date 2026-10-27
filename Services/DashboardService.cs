using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Services;

/// <summary>Loads what each role should act on next. Every query is scoped by the caller's roles.</summary>
public class DashboardService(AppDbContext db)
{
    public async Task<DashboardViewModel> BuildAsync(ClaimsPrincipal user, DateTime nowUtc)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException();
        var provider = user.IsInRole(AppRoles.Ressursleverandor);
        var verifier = user.IsInRole(AppRoles.Verifiseringsansvarlig);
        var dispatch = user.IsInRole(AppRoles.DispatchTeam);
        var leader = user.IsInRole(AppRoles.Kriseledelse);
        var publicActor = user.IsInRole(AppRoles.OffentligAktor);

        List<MapSubmission> own = [], queue = [], matching = [];
        List<ResourceRequest> incoming = [], requests = [];
        List<ResourceAssignment> assignments = [];
        var needs = new Dictionary<int, OperationalNeed>();
        IReadOnlyList<(string, int)> roleCounts = [];

        if (provider)
        {
            own = await db.MapSubmissions.AsNoTracking().Where(r => r.OwnerId == actorId)
                .OrderByDescending(r => r.CreatedAtUtc).ToListAsync();
            incoming = await db.ResourceRequests.AsNoTracking().Include(r => r.Resource).Include(r => r.Need)
                .Where(r => r.Resource.OwnerId == actorId).OrderByDescending(r => r.CreatedAtUtc).Take(20).ToListAsync();
        }
        if (verifier)
        {
            queue = await db.MapSubmissions.AsNoTracking()
                .Where(r => r.Availability != ResourceAvailability.Reserved &&
                    r.Availability != ResourceAvailability.Allocated && r.Availability != ResourceAvailability.InUse &&
                    (r.VerificationStatus == ResourceVerificationStatus.Pending ||
                     (r.VerificationStatus == ResourceVerificationStatus.Approved && !(r.VerificationExpiresAtUtc > nowUtc))))
                .OrderBy(r => r.CreatedAtUtc).ToListAsync();
        }
        if (dispatch || leader)
        {
            requests = await db.ResourceRequests.AsNoTracking().Include(r => r.Resource).Include(r => r.Need)
                .Where(r => r.Status == ResourceRequestStatus.Pending || r.Status == ResourceRequestStatus.Accepted)
                .OrderByDescending(r => r.CreatedAtUtc).ToListAsync();
            assignments = await db.ResourceAssignments.AsNoTracking().Include(a => a.Resource).Include(a => a.Need)
                .Where(a => a.Status != AssignmentStatus.Completed).OrderByDescending(a => a.CreatedAtUtc).ToListAsync();
            foreach (var need in await db.OperationalNeeds.AsNoTracking()
                         .Where(n => n.Status != NeedStatus.Completed && n.Status != NeedStatus.Cancelled).ToListAsync())
                needs[need.Id] = need;
        }
        if (dispatch)
            matching = await db.MapSubmissions.AsNoTracking().Where(ResourceQueries.EligibleNow(nowUtc))
                .OrderBy(r => r.ResourceName).ToListAsync();
        if (leader)
            roleCounts = (await (from ur in db.UserRoles
                                 join role in db.Roles on ur.RoleId equals role.Id
                                 group ur by role.Name into g
                                 select new { Role = g.Key, Count = g.Count() }).ToListAsync())
                .Where(x => x.Role != null && AppRoles.All.Contains(x.Role))
                .OrderBy(x => AppRoles.All.ToList().IndexOf(x.Role!)).Select(x => (x.Role!, x.Count)).ToList();
        if (publicActor)
            foreach (var need in await db.OperationalNeeds.AsNoTracking().Where(n => n.CreatedById == actorId).ToListAsync())
                needs[need.Id] = need;

        var resourceIds = own.Concat(queue).Concat(matching).Select(r => r.Id).ToHashSet();
        var requestIds = incoming.Concat(requests).Select(r => r.Id)
            .Concat(assignments.Select(a => a.RequestId)).ToHashSet();
        var audits = await (from audit in db.WorkflowAudits.AsNoTracking()
                            where (audit.ResourceId != null && resourceIds.Contains(audit.ResourceId.Value)) ||
                                  (audit.RequestId != null && requestIds.Contains(audit.RequestId.Value))
                            join actor in db.Users.AsNoTracking() on audit.ActorId equals actor.Id into actors
                            from actor in actors.DefaultIfEmpty()
                            orderby audit.OccurredAtUtc, audit.Id
                            select new { audit.ResourceId, audit.RequestId, audit.Action, audit.OccurredAtUtc,
                                Actor = actor == null ? "Ukjent bruker" : (actor.Email ?? actor.UserName ?? "Ukjent bruker") })
            .ToListAsync();
        TimelineEntry Entry(string action, string actor, DateTime at) => new(WorkflowLabels.Action(action), actor, at);

        return new DashboardViewModel
        {
            OwnResources = own,
            IncomingRequests = incoming,
            VerificationQueue = queue,
            Needs = needs.Values.OrderByDescending(n => n.Priority).ThenBy(n => n.CreatedAtUtc).ToList(),
            MatchingResources = matching,
            Requests = requests,
            Assignments = assignments,
            RoleCounts = roleCounts,
            ResourceTimeline = audits.Where(a => a.ResourceId != null)
                .GroupBy(a => a.ResourceId!.Value).ToDictionary(g => g.Key,
                    g => (IReadOnlyList<TimelineEntry>)g.Select(a => Entry(a.Action, a.Actor, a.OccurredAtUtc)).ToList()),
            RequestTimeline = audits.Where(a => a.RequestId != null).GroupBy(a => a.RequestId!.Value).ToDictionary(g => g.Key,
                g => (IReadOnlyList<TimelineEntry>)g.Select(a => Entry(a.Action, a.Actor, a.OccurredAtUtc)).ToList()),
            AssignmentTimeline = assignments.ToDictionary(a => a.Id, a => (IReadOnlyList<TimelineEntry>)audits
                .Where(x => x.RequestId == a.RequestId).Select(x => Entry(x.Action, x.Actor, x.OccurredAtUtc)).ToList())
        };
    }
}
