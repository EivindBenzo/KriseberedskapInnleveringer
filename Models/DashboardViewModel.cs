using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public class DashboardViewModel
{
    public IReadOnlyList<MapSubmission> OwnResources { get; init; } = [];
    public IReadOnlyList<ResourceRequest> IncomingRequests { get; init; } = [];
    public IReadOnlyList<MapSubmission> VerificationQueue { get; init; } = [];
    public IReadOnlyList<OperationalNeed> Needs { get; init; } = [];
    public IReadOnlyList<MapSubmission> MatchingResources { get; init; } = [];
    public IReadOnlyList<ResourceRequest> Requests { get; init; } = [];
    public IReadOnlyList<ResourceAssignment> Assignments { get; init; } = [];
    public IReadOnlyList<(string Role, int Count)> RoleCounts { get; init; } = [];
    public IReadOnlyDictionary<int, IReadOnlyList<TimelineEntry>> ResourceTimeline { get; init; } =
        new Dictionary<int, IReadOnlyList<TimelineEntry>>();
    public IReadOnlyDictionary<int, IReadOnlyList<TimelineEntry>> RequestTimeline { get; init; } =
        new Dictionary<int, IReadOnlyList<TimelineEntry>>();
    public IReadOnlyDictionary<int, IReadOnlyList<TimelineEntry>> AssignmentTimeline { get; init; } =
        new Dictionary<int, IReadOnlyList<TimelineEntry>>();
}
