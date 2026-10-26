using HeimevernetInnlevering1.Models;

namespace HeimevernetInnlevering1.ViewModels;

public class DataPageViewModel
{
    public IReadOnlyList<FormSubmission> FormSubmissions { get; init; } = [];
    public IReadOnlyList<MapSubmission> MapSubmissions { get; init; } = [];
    public IReadOnlyList<ResourceCity> Cities { get; init; } = [];
}
