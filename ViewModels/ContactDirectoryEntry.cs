namespace HeimevernetInnlevering1.ViewModels;

public sealed record ContactActivity(string Text, DateTime When);

public sealed record ContactDirectoryEntry(int UserId, string Name, string Role, string? Organization, string Email, string? PhoneNumber,
    string? Place = null, IReadOnlyList<string>? Resources = null, IReadOnlyList<ContactActivity>? Activity = null);