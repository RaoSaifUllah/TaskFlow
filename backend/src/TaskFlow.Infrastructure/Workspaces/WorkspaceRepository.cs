using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Workspaces;

public sealed class WorkspaceRepository(TaskFlowDbContext db) : IWorkspaceRepository
{
    public async Task<Workspace?> LockAsync(Guid workspaceId, CancellationToken ct)
    {
        // Every ownership mutation takes this row lock before reading memberships.
        var rows = await db.Workspaces.FromSqlInterpolated($"SELECT * FROM \"Workspaces\" WHERE \"Id\" = {workspaceId} FOR UPDATE").ToListAsync(ct);
        var workspace = rows.SingleOrDefault();
        if (workspace is not null) await db.Entry(workspace).ReloadAsync(ct);
        return workspace is { DeletedAt: null } ? workspace : null;
    }
    public async Task<Membership?> MemberAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var member = await db.Memberships.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct);
        // Do not authorize using a stale entity previously read in this scope.
        if (member is not null) await db.Entry(member).ReloadAsync(ct);
        return member;
    }
    public Task<int> OwnersAsync(Guid workspaceId, CancellationToken ct) =>
        db.Memberships.CountAsync(x => x.WorkspaceId == workspaceId && x.Active && x.Role == WorkspaceRole.Owner, ct);
    public void Add(Workspace workspace, Membership owner) { db.Workspaces.Add(workspace); db.Memberships.Add(owner); }
}
