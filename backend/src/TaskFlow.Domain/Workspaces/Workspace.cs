namespace TaskFlow.Domain.Workspaces;

public enum WorkspaceRole { Owner, Admin, Member, Guest }
public sealed class Workspace
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
    private Workspace() { }
    public Workspace(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Workspace name must contain 1–120 characters.", nameof(name));
        Name = name.Trim();
    }
}
public sealed class Membership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public bool Active { get; set; } = true;
}
