namespace HeimevernetInnlevering1.ViewModels;

public sealed record InboxMessageEntry(long Id, int SenderId, string SenderName, string SenderRole, string Subject, string Body, DateTime SentAtUtc, bool IsUnread);

public sealed class InboxViewModel
{
    public IReadOnlyList<InboxMessageEntry> Messages { get; init; } = [];
}
