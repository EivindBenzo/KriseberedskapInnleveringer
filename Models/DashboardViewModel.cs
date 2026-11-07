using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public class DashboardViewModel
{
    public IReadOnlyList<Ressurs> OwnResources { get; init; } = [];
    public IReadOnlyList<Forespørsel> IncomingRequests { get; init; } = [];
    public IReadOnlyList<Ressurs> VerificationQueue { get; init; } = [];
    public IReadOnlyList<Behov> Needs { get; init; } = [];
    public IReadOnlyList<Behov> OpenNeeds { get; init; } = [];
    public IReadOnlyList<Behov> UrgentNeeds { get; init; } = [];
    public IReadOnlyList<Ressurs> MatchingResources { get; init; } = [];
    public IReadOnlyList<Forespørsel> Requests { get; init; } = [];
    public IReadOnlyList<Tildeling> Assignments { get; init; } = [];
    public IReadOnlyList<Hendelse> ActiveIncidents { get; init; } = [];
    public IReadOnlyList<(string Role, int Count)> RoleCounts { get; init; } = [];
    public IReadOnlyDictionary<int, IReadOnlyList<TimelineEntry>> ResourceTimeline { get; init; } =
        new Dictionary<int, IReadOnlyList<TimelineEntry>>();
    public IReadOnlyDictionary<int, IReadOnlyList<TimelineEntry>> RequestTimeline { get; init; } =
        new Dictionary<int, IReadOnlyList<TimelineEntry>>();
    public IReadOnlyDictionary<int, IReadOnlyList<TimelineEntry>> AssignmentTimeline { get; init; } =
        new Dictionary<int, IReadOnlyList<TimelineEntry>>();
    public IReadOnlyList<TimelineEntry> RecentActivity { get; init; } = [];
}
