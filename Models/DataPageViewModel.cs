using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public class DataPageViewModel
{
    public IReadOnlyList<FormSubmission> FormSubmissions { get; init; } = [];
    public IReadOnlyList<MapSubmission> MapSubmissions { get; init; } = [];
    public IReadOnlyList<ResourceCity> Cities { get; init; } = [];
    public IReadOnlyList<ResourceHistoryEntry> History { get; init; } = [];
}

public class ResourceHistoryEntry
{
    public int? ResourceId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
}
