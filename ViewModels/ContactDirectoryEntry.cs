namespace HeimevernetInnlevering1.ViewModels;

public sealed record ContactDirectoryEntry(int UserId, string Name, string Role, string? Organization, string Email, string? PhoneNumber);
