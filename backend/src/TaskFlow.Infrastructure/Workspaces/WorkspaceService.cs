using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Persistence;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Workspaces;

public sealed class WorkspaceService(TaskFlowDbContext db, IWorkspaceRepository repository, IUnitOfWork unit, TimeProvider clock) : IWorkspaceService
{
    public async Task<Guid> CreateAsync(RequestActor actor, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new WorkspaceRejectedException("invalid_name", 400);
        await RequireMfaAsync(actor, true, ct);
        var allowed = await db.Users.AnyAsync(x => x.Id == actor.UserId && x.InstanceAdmin && x.Active, ct) ||
            await db.Memberships.AnyAsync(x => x.UserId == actor.UserId && x.Active && x.Role != WorkspaceRole.Guest, ct);
        if (!allowed) throw new WorkspaceRejectedException("workspace_creation_denied");
        var workspace = new Workspace(name);
        await unit.ExecuteAsync(_ => { repository.Add(workspace, new Membership { WorkspaceId = workspace.Id, UserId = actor.UserId, Role = WorkspaceRole.Owner }); db.Audit("workspace_created",actor.UserId,workspace.Id,clock.GetUtcNow(),workspace.Id); return Task.CompletedTask; }, ct);
        return workspace.Id;
    }
    public async Task AuthorizeReadAsync(RequestActor actor, Guid workspaceId, bool directory, CancellationToken ct)
    {
        var member = await repository.MemberAsync(workspaceId, actor.UserId, ct);
        if (member is null || !member.Active) throw new WorkspaceRejectedException("workspace_not_found", 404);
        if (directory && member.Role == WorkspaceRole.Guest) throw new WorkspaceRejectedException("directory_denied");
        if (member.Role is WorkspaceRole.Owner or WorkspaceRole.Admin) await RequireMfaAsync(actor, false, ct);
    }
    public async Task ChangeMemberAsync(RequestActor actor, Guid workspaceId, Guid userId, WorkspaceRole role, bool active, CancellationToken ct)
    {
        if (!Enum.IsDefined(role)) throw new WorkspaceRejectedException("invalid_role", 400);
        await unit.ExecuteAsync(async token =>
        {
            if (await repository.LockAsync(workspaceId, token) is null) throw new WorkspaceRejectedException("workspace_not_found", 404);
            var caller = await repository.MemberAsync(workspaceId, actor.UserId, token);
            if (caller is null || !caller.Active) throw new WorkspaceRejectedException("workspace_not_found", 404);
            if (caller.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin)) throw new WorkspaceRejectedException("member_change_denied");
            await RequireMfaAsync(actor, true, token);
            var target = await repository.MemberAsync(workspaceId, userId, token) ?? throw new WorkspaceRejectedException("member_not_found", 404);
            if (caller.Role == WorkspaceRole.Admin && (target.Role == WorkspaceRole.Owner || role == WorkspaceRole.Owner)) throw new WorkspaceRejectedException("ownership_denied");
            if (target.Active && target.Role == WorkspaceRole.Owner && (!active || role != WorkspaceRole.Owner) && await repository.OwnersAsync(workspaceId, token) <= 1)
                throw new WorkspaceRejectedException("last_owner", 409);
            target.Role = role; target.Active = active;
            db.Audit("membership_changed",actor.UserId,userId,clock.GetUtcNow(),workspaceId);
        }, ct);
    }
    public async Task AuthorizeManagementAsync(RequestActor actor, Guid workspaceId, WorkspaceRole grantingRole, CancellationToken ct)
    {
        var caller = await repository.MemberAsync(workspaceId, actor.UserId, ct);
        if (caller is null || !caller.Active) throw new WorkspaceRejectedException("workspace_not_found", 404);
        if (caller.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin)) throw new WorkspaceRejectedException("member_change_denied");
        if (caller.Role == WorkspaceRole.Admin && grantingRole == WorkspaceRole.Owner) throw new WorkspaceRejectedException("ownership_denied");
        await RequireMfaAsync(actor, true, ct);
    }
    public async Task RenameAsync(RequestActor actor, Guid workspaceId, string name, long? version, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new WorkspaceRejectedException("invalid_name",400);
        await unit.ExecuteAsync(async cancellation =>
        {
            var workspace=await repository.LockAsync(workspaceId,cancellation) ?? throw new WorkspaceRejectedException("workspace_not_found",404);
            await AuthorizeManagementAsync(actor,workspaceId,WorkspaceRole.Member,cancellation);
            CheckVersion(workspace,version); workspace.Rename(name);
            db.Audit("workspace_renamed",actor.UserId,workspaceId,clock.GetUtcNow(),workspaceId);
        },ct);
    }
    public async Task DeleteAsync(RequestActor actor, Guid workspaceId, long? version, CancellationToken ct)
    {
        await unit.ExecuteAsync(async cancellation =>
        {
            var workspace=await repository.LockAsync(workspaceId,cancellation) ?? throw new WorkspaceRejectedException("workspace_not_found",404);
            await AuthorizeManagementAsync(actor,workspaceId,WorkspaceRole.Owner,cancellation);
            CheckVersion(workspace,version); workspace.Delete(clock.GetUtcNow());
            db.Audit("workspace_deleted",actor.UserId,workspaceId,clock.GetUtcNow(),workspaceId);
            // Keep referential history while withdrawing every membership and outstanding invite.
            await db.Memberships.Where(x=>x.WorkspaceId==workspaceId && x.Active).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.Active,false),cancellation);
        },ct);
    }
    private static void CheckVersion(Workspace workspace,long? version)
    {
        if(version is null) throw new WorkspaceRejectedException("version_required",428);
        if(version != workspace.Version) throw new WorkspaceRejectedException("version_conflict",409);
    }
    private async Task RequireMfaAsync(RequestActor actor, bool recent, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var valid = await (from session in db.RefreshSessions join user in db.Users on session.UserId equals user.Id
            where session.Id == actor.SessionId && user.Id == actor.UserId && user.Active && user.TwoFactorEnabled &&
                session.SecurityStamp == user.SecurityStamp && session.RevokedAt == null && session.IdleExpiresAt > now && session.AbsoluteExpiresAt > now &&
                session.MfaVerifiedAt != null && (!recent || session.MfaVerifiedAt >= now.AddMinutes(-5))
            select session.Id).AnyAsync(ct);
        if (!valid) throw new WorkspaceRejectedException(recent ? "mfa_step_up_required" : "mfa_required");
    }
}
