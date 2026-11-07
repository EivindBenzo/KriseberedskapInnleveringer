using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Services;

/// <summary>Henter det hver rolle skal jobbe med på «Oversikt». Alle spørringer er avgrenset etter rollen.</summary>
public class DashboardService(AppDbContext db)
{
    public async Task<DashboardViewModel> BuildAsync(ClaimsPrincipal user, DateTime nowUtc)
    {
        var actorId = ResourcePermissions.BrukerId(user) ?? throw new InvalidOperationException();
        var provider = user.IsInRole(AppRoles.Ressursleverandor);
        var dispatch = user.IsInRole(AppRoles.DispatchTeam);
        var leader = user.IsInRole(AppRoles.Kriseledelse);
        var publicActor = user.IsInRole(AppRoles.OffentligAktor);

        List<Ressurs> own = [], queue = [], matching = [];
        List<Forespørsel> incoming = [], requests = [];
        List<Tildeling> assignments = [];
        List<Hendelse> incidents = [];
        var needs = new Dictionary<int, Behov>();
        IReadOnlyList<(string, int)> roleCounts = [];

        IQueryable<Ressurs> Ressurser() => ResourceQueries.WithDetails(db.Ressurser.AsNoTracking());
        IQueryable<Forespørsel> Forespørsler() => db.Forespørsler.AsNoTracking()
            .Include(f => f.Ressurs!).ThenInclude(r => r.Ressurstype)
            .Include(f => f.Behov!).ThenInclude(b => b.Behovstype)
            .Include(f => f.Tildeling);
        IQueryable<Behov> BehovMedDetaljer() => db.Behov.AsNoTracking()
            .Include(b => b.Behovstype!).ThenInclude(t => t.PassendeRessurstyper)
            .Include(b => b.Hendelse);

        if (provider)
        {
            own = await Ressurser().Where(r => r.EierID == actorId)
                .OrderByDescending(r => r.Registrert).AsSplitQuery().ToListAsync();
            incoming = await Forespørsler().Where(f => f.Ressurs!.EierID == actorId)
                .OrderByDescending(f => f.Sendt).Take(20).ToListAsync();
        }
        if (dispatch || leader)
        {
            requests = await Forespørsler()
                .Where(f => f.Svar == Statuser.Svar.Venter || (f.Svar == Statuser.Svar.Godtatt && f.Tildeling == null))
                .OrderByDescending(f => f.Sendt).ToListAsync();
            assignments = await db.Tildelinger.AsNoTracking()
                .Include(t => t.Forespørsel!).ThenInclude(f => f.Ressurs!).ThenInclude(r => r.Ressurstype)
                .Include(t => t.Forespørsel!).ThenInclude(f => f.Behov)
                .Where(t => t.Fremdrift != Statuser.Fremdrift.Fullført)
                .OrderByDescending(t => t.Tildelt).ToListAsync();
            foreach (var behov in await BehovMedDetaljer().Where(b => b.Status != Statuser.Behov.Løst).ToListAsync())
                needs[behov.BehovID] = behov;
        }
        if (dispatch)
        {
            queue = await Ressurser().Where(WorkflowEligibility.TrengerVerifisering(nowUtc))
                .OrderBy(r => r.Registrert).AsSplitQuery().ToListAsync();
        }
        if (dispatch)
            matching = await Ressurser().Where(WorkflowEligibility.EligibleExpression(nowUtc))
                .OrderBy(r => r.Navn).AsSplitQuery().ToListAsync();
        if (leader)
        {
            incidents = await db.Hendelser.AsNoTracking().Where(h => h.Status == Statuser.Hendelse.Aktiv)
                .OrderByDescending(h => h.Starttidspunkt).ToListAsync();
            roleCounts = (await (from ur in db.UserRoles
                                 join role in db.Roles on ur.RoleId equals role.Id
                                 group ur by role.Name into g
                                 select new { Role = g.Key, Count = g.Count() }).ToListAsync())
                .Where(x => x.Role != null && AppRoles.All.Contains(x.Role))
                .OrderBy(x => AppRoles.All.ToList().IndexOf(x.Role!)).Select(x => (x.Role!, x.Count)).ToList();
        }
        if (publicActor)
            foreach (var behov in await BehovMedDetaljer().Where(b => b.RegistrertAvID == actorId).ToListAsync())
                needs[behov.BehovID] = behov;

        var resourceIds = own.Concat(queue).Concat(matching).Select(r => r.RessursID).ToHashSet();
        var requestIds = incoming.Concat(requests).Select(f => f.ForespørselID)
            .Concat(assignments.Select(t => t.ForespørselID)).ToHashSet();
        var needIds = needs.Keys.ToHashSet();
        var audits = await (from logg in db.Hendelseslogg.AsNoTracking()
                            where (logg.RessursID != null && resourceIds.Contains(logg.RessursID.Value)) ||
                                  (logg.ForespørselID != null && requestIds.Contains(logg.ForespørselID.Value)) ||
                                  (logg.BehovID != null && needIds.Contains(logg.BehovID.Value))
                            join actor in db.Users.AsNoTracking() on logg.UtførtAvID equals actor.Id into actors
                            from actor in actors.DefaultIfEmpty()
                            orderby logg.Tidspunkt, logg.LoggID
                            select new
                            {
                                logg.RessursID, logg.ForespørselID, logg.Handling, logg.Tidspunkt,
                                Actor = actor == null ? "Ukjent bruker" : (actor.Email ?? actor.UserName ?? "Ukjent bruker")
                            }).ToListAsync();
        TimelineEntry Entry(string action, string actor, DateTime at) => new(WorkflowLabels.Action(action), actor, at);
        var orderedNeeds = needs.Values.OrderBy(b => WorkflowTransitions.PriorityRank(b.Prioritet))
            .ThenBy(b => b.Frist ?? DateTime.MaxValue).ThenBy(b => b.Registrert).ToList();
        var unreadMessageEntities = await db.Meldinger.AsNoTracking()
            .Where(m => m.MottakerID == actorId && m.Lest == null)
            .Include(m => m.SendtAv)
            .OrderByDescending(m => m.Sendt).Take(5)
            .ToListAsync();
        var unreadMessages = unreadMessageEntities.Select(m => new InboxMessageEntry(
            m.MeldingID, m.SendtAv?.FulltNavn ?? "Ukjent bruker", "", m.Emne, m.Innhold, m.Sendt, true)).ToList();

        return new DashboardViewModel
        {
            OwnResources = own,
            IncomingRequests = incoming,
            VerificationQueue = queue,
            Needs = orderedNeeds,
            OpenNeeds = orderedNeeds.Where(b => b.Prioritet != Statuser.Prioritet.Akutt).ToList(),
            UrgentNeeds = orderedNeeds.Where(b => b.Prioritet == Statuser.Prioritet.Akutt).ToList(),
            MatchingResources = matching,
            Requests = requests,
            Assignments = assignments,
            ActiveIncidents = incidents,
            RoleCounts = roleCounts,
            ResourceTimeline = audits.Where(a => a.RessursID != null)
                .GroupBy(a => a.RessursID!.Value).ToDictionary(g => g.Key,
                    g => (IReadOnlyList<TimelineEntry>)g.Select(a => Entry(a.Handling, a.Actor, a.Tidspunkt)).ToList()),
            RequestTimeline = audits.Where(a => a.ForespørselID != null).GroupBy(a => a.ForespørselID!.Value).ToDictionary(g => g.Key,
                g => (IReadOnlyList<TimelineEntry>)g.Select(a => Entry(a.Handling, a.Actor, a.Tidspunkt)).ToList()),
            AssignmentTimeline = assignments.ToDictionary(t => t.TildelingID, t => (IReadOnlyList<TimelineEntry>)audits
                .Where(x => x.ForespørselID == t.ForespørselID).Select(x => Entry(x.Handling, x.Actor, x.Tidspunkt)).ToList()),
            RecentActivity = audits.OrderByDescending(a => a.Tidspunkt).ThenByDescending(a => a.Handling)
                .Take(4).Select(a => Entry(a.Handling, a.Actor, a.Tidspunkt)).ToList(),
            UnreadMessages = unreadMessages
        };
    }
}
