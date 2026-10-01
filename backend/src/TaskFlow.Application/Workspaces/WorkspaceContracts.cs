using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Workspaces;

public sealed record RequestActor(Guid UserId, Guid SessionId);
public sealed record WorkspaceSummary(Guid Id, string Name, WorkspaceRole Role);
public sealed record MemberSummary(Guid UserId, WorkspaceRole Role, bool Active);
public sealed class WorkspaceRejectedException(string code, int status = 403) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
public interface IWorkspaceRepository
{
    Task<Workspace?> LockAsync(Guid workspaceId, CancellationToken ct);
    Task<Membership?> MemberAsync(Guid workspaceId, Guid userId, CancellationToken ct);
    Task<int> OwnersAsync(Guid workspaceId, CancellationToken ct);
    void Add(Workspace workspace, Membership owner);
}
public interface IWorkspaceService
{
    Task<Guid> CreateAsync(RequestActor actor, string name, CancellationToken ct);
    Task ChangeMemberAsync(RequestActor actor, Guid workspaceId, Guid userId, WorkspaceRole role, bool active, CancellationToken ct);
    Task AuthorizeReadAsync(RequestActor actor, Guid workspaceId, bool directory, CancellationToken ct);
}
public interface IWorkspaceReadService
{
    Task<IReadOnlyList<WorkspaceSummary>> ListAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<MemberSummary>> MembersAsync(Guid workspaceId, Guid userId, CancellationToken ct);
}
