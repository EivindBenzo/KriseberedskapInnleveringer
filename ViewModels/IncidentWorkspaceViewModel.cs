using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public sealed class IncidentWorkspaceViewModel
{
    public required Hendelse Incident { get; init; }
    public IReadOnlyList<Behov> OpenNeeds { get; init; } = [];
    public IReadOnlyList<Tildeling> ActiveAssignments { get; init; } = [];
    public IReadOnlyList<HendelseLoggRad> Activity { get; init; } = [];
}

public sealed record HendelseLoggRad(DateTime Tidspunkt, string Handling, string? Tekst, string Aktør);

public sealed record HendelseKort(int Tiltak, int TiltakFerdig, int Ressurser, int Aktører, int Prosent, int Behov);
