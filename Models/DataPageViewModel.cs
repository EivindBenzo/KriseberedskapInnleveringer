using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public class DataPageViewModel
{
    public IReadOnlyList<Ressurs> Ressurser { get; init; } = [];
    public IReadOnlyList<ResourceCity> Cities { get; init; } = [];
    public IReadOnlyList<ResourceHistoryEntry> History { get; init; } = [];
    public IReadOnlyDictionary<string, int> TypeCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<int, IReadOnlyList<ResourceActivity>> Activity { get; init; } = new Dictionary<int, IReadOnlyList<ResourceActivity>>();
}

public class ResourceHistoryEntry
{
    public int? ResourceId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
}

public record ResourceActivity(string Action, string Description, DateTime OccurredAtUtc);
