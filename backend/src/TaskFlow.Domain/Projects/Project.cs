namespace TaskFlow.Domain.Projects;

public enum ProjectAccess { Viewer, Editor }
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatorId { get; set; }
    public string Name { get; set; } = "";
    public bool Archived { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class ProjectShare
{
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public ProjectAccess Access { get; set; }
}
