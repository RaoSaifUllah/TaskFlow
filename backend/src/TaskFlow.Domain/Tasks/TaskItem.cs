namespace TaskFlow.Domain.Tasks;

public enum DueKind { None, DateOnly, DateTime }
public sealed class TaskItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid? InboxOwnerId { get; private set; }
    public Guid CreatorId { get; private set; }
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public DueKind DueKind { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public TimeOnly? DueLocalTime { get; private set; }
    public string? DueZone { get; private set; }
    public DateTimeOffset? DueAtUtc { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public long Version { get; private set; } = 1;
    private TaskItem() { }
    public TaskItem(Guid workspaceId, Guid creatorId, string title, Guid? projectId = null)
    {
        if (workspaceId == Guid.Empty || creatorId == Guid.Empty) throw new ArgumentException("Scope is required.");
        WorkspaceId = workspaceId;
        CreatorId = creatorId;
        ProjectId = projectId;
        InboxOwnerId = projectId is null ? creatorId : null;
        SetTitle(title);
    }
    public void Rename(string title, long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("Stale task version.");
        SetTitle(title);
        Version++;
    }
    public void SetDate(DateOnly date) { DueKind = DueKind.DateOnly; DueDate = date; DueLocalTime = null; DueZone = null; DueAtUtc = null; Version++; }
    private void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 500) throw new ArgumentException("Title must contain 1–500 characters.", nameof(title));
        Title = title.Trim();
    }
}
