namespace TaskFlow.Domain.Workspaces;

public sealed class WorkspaceInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatedBy { get; set; }
    public string NormalizedEmail { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public WorkspaceRole Role { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
