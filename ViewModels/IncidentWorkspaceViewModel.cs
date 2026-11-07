using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public sealed class IncidentWorkspaceViewModel
{
    public required Hendelse Incident { get; init; }
    public IReadOnlyList<Behov> OpenNeeds { get; init; } = [];
    public IReadOnlyList<Tildeling> ActiveAssignments { get; init; } = [];
    public IReadOnlyList<Hendelseslogg> Activity { get; init; } = [];
}
