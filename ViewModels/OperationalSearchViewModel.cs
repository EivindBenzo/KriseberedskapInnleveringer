namespace HeimevernetInnlevering1.ViewModels;

public sealed class OperationalSearchViewModel
{
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<OperationalSearchResult> Results { get; init; } = [];
    public bool HasSearched => !string.IsNullOrWhiteSpace(Query);
}

public sealed record OperationalSearchResult(
    string Category,
    string Title,
    string Details,
    string Status,
    string Url);
