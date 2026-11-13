namespace HeimevernetInnlevering1.ViewModels;

public sealed record InboxMessageEntry(long Id, int SenderId, string SenderName, string SenderRole, string Subject, string Body, DateTime SentAtUtc, bool IsUnread);

public sealed record ThreadMessage(int SenderId, string SenderName, bool Mine, string Subject, string Body, DateTime SentAtUtc);

public sealed record MessageThread(string Key, bool IsGroup, string Title, string Subtitle, string RoleClass, string Initials,
    IReadOnlyList<int> OtherIds, IReadOnlyList<string> Names, string ReplySubject, int Unread, DateTime LastAtUtc, string Preview,
    IReadOnlyList<ThreadMessage> Messages);

public sealed class InboxViewModel
{
    public IReadOnlyList<InboxMessageEntry> Messages { get; init; } = [];
    public IReadOnlyList<MessageThread> Threads { get; init; } = [];
}
