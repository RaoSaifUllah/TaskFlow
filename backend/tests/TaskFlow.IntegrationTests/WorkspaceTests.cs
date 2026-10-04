using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Workspaces;

namespace TaskFlow.IntegrationTests;

public sealed class WorkspaceTests
{
    private static TaskFlowDbContext Database() => new(new DbContextOptionsBuilder<TaskFlowDbContext>()
        .UseNpgsql(DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE")!)).Options);
    private static WorkspaceService Service(TaskFlowDbContext db) => new(db, new WorkspaceRepository(db), new UnitOfWork(db), TimeProvider.System);

    [PostgreSqlFact]
    public async Task ScopedDirectoriesMfaAndAdminOwnershipRestrictions()
    {
        await using var db = Database();
        var workspace = new Workspace("Permissions fixture");
        var other = new Workspace("Other fixture");
        var owner = AddActor(db, true); var admin = AddActor(db, true); var guest = AddActor(db, false);
        var outsider = AddActor(db, true);
        db.AddRange(workspace, other,
            new Membership { WorkspaceId = workspace.Id, UserId = owner.UserId, Role = WorkspaceRole.Owner },
            new Membership { WorkspaceId = workspace.Id, UserId = admin.UserId, Role = WorkspaceRole.Admin },
            new Membership { WorkspaceId = workspace.Id, UserId = guest.UserId, Role = WorkspaceRole.Guest },
            new Membership { WorkspaceId = other.Id, UserId = outsider.UserId, Role = WorkspaceRole.Owner });
        await db.SaveChangesAsync();
        var service = Service(db);
        var reads = new WorkspaceReadService(Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ")!);
        Assert.Single(await reads.ListAsync(guest.UserId, default));
        Assert.Empty(await reads.MembersAsync(workspace.Id, guest.UserId, default));
        Assert.Empty(await reads.MembersAsync(workspace.Id, outsider.UserId, default));
        Assert.Equal(3, (await reads.MembersAsync(workspace.Id, owner.UserId, default)).Count);
        Assert.Equal("directory_denied", (await Assert.ThrowsAsync<WorkspaceRejectedException>(() => service.AuthorizeReadAsync(guest, workspace.Id, true, default))).Code);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkspaceRejectedException>(() => service.AuthorizeReadAsync(outsider, workspace.Id, true, default))).Status);
        Assert.Equal("ownership_denied", (await Assert.ThrowsAsync<WorkspaceRejectedException>(() => service.ChangeMemberAsync(admin, workspace.Id, owner.UserId, WorkspaceRole.Member, true, default))).Code);
        await db.RefreshSessions.Where(x => x.Id == admin.SessionId).ExecuteUpdateAsync(x => x.SetProperty(s => s.MfaVerifiedAt, DateTimeOffset.UtcNow.AddMinutes(-10)));
        Assert.Equal("mfa_step_up_required", (await Assert.ThrowsAsync<WorkspaceRejectedException>(() => service.ChangeMemberAsync(admin, workspace.Id, guest.UserId, WorkspaceRole.Member, true, default))).Code);
        await service.AuthorizeReadAsync(admin, workspace.Id, true, default);
        await db.RefreshSessions.Where(x => x.Id == owner.SessionId).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow));
        Assert.Equal("mfa_required", (await Assert.ThrowsAsync<WorkspaceRejectedException>(() => service.AuthorizeReadAsync(owner, workspace.Id, true, default))).Code);
    }

    [PostgreSqlFact]
    public async Task ConcurrentOwnerDemotionsPreserveOneOwner()
    {
        await using var db = Database();
        var workspace = new Workspace("Ownership race");
        var first = AddActor(db, true); var second = AddActor(db, true);
        db.AddRange(workspace,
            new Membership { WorkspaceId = workspace.Id, UserId = first.UserId, Role = WorkspaceRole.Owner },
            new Membership { WorkspaceId = workspace.Id, UserId = second.UserId, Role = WorkspaceRole.Owner });
        await db.SaveChangesAsync();
        async Task<string> Demote(RequestActor actor)
        {
            await using var context = Database();
            try { await Service(context).ChangeMemberAsync(actor, workspace.Id, actor.UserId, WorkspaceRole.Member, true, default); return "changed"; }
            catch (WorkspaceRejectedException error) { return error.Code; }
        }
        var results = await Task.WhenAll(Demote(first), Demote(second));
        Assert.Single(results, x => x == "changed");
        Assert.Single(results, x => x == "last_owner");
        Assert.Equal(1, await db.Memberships.CountAsync(x => x.WorkspaceId == workspace.Id && x.Active && x.Role == WorkspaceRole.Owner));
        Assert.Equal(1,await db.AuditEvents.CountAsync(x=>x.WorkspaceId==workspace.Id && x.Action=="membership_changed"));
    }

    private static RequestActor AddActor(TaskFlowDbContext db, bool mfa)
    {
        var user = new TaskFlowUser { Id = Guid.NewGuid(), Email = Guid.NewGuid() + "@example.invalid", SecurityStamp = Guid.NewGuid().ToString(), TwoFactorEnabled = mfa };
        var now = DateTimeOffset.UtcNow;
        var session = new RefreshSession { UserId = user.Id, SecurityStamp = user.SecurityStamp, CreatedAt = now, LastSeenAt = now,
            IdleExpiresAt = now.AddDays(7), AbsoluteExpiresAt = now.AddDays(30), MfaVerifiedAt = mfa ? now : null };
        db.AddRange(user, session);
        return new RequestActor(user.Id, session.Id);
    }

    [PostgreSqlFact]
    public async Task WorkspaceLifecycleRequiresVersionAndOwnerDeletionWithdrawsAccess()
    {
        await using var db=Database(); var workspace=new Workspace("Lifecycle fixture");
        var owner=AddActor(db,true); var admin=AddActor(db,true);
        db.AddRange(workspace,new Membership {WorkspaceId=workspace.Id,UserId=owner.UserId,Role=WorkspaceRole.Owner},
            new Membership {WorkspaceId=workspace.Id,UserId=admin.UserId,Role=WorkspaceRole.Admin}); await db.SaveChangesAsync();
        var service=Service(db);
        Assert.Equal(428,(await Assert.ThrowsAsync<WorkspaceRejectedException>(()=>service.RenameAsync(owner,workspace.Id,"Changed",null,default))).Status);
        await service.RenameAsync(admin,workspace.Id,"Changed",1,default);
        Assert.Equal(409,(await Assert.ThrowsAsync<WorkspaceRejectedException>(()=>service.RenameAsync(owner,workspace.Id,"Stale",1,default))).Status);
        Assert.Equal("ownership_denied",(await Assert.ThrowsAsync<WorkspaceRejectedException>(()=>service.DeleteAsync(admin,workspace.Id,2,default))).Code);
        await service.DeleteAsync(owner,workspace.Id,2,default);
        var reads=new WorkspaceReadService(Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ")!);
        Assert.Empty(await reads.ListAsync(owner.UserId,default)); Assert.Empty(await reads.MembersAsync(workspace.Id,owner.UserId,default));
        Assert.Equal(404,(await Assert.ThrowsAsync<WorkspaceRejectedException>(()=>service.AuthorizeReadAsync(owner,workspace.Id,true,default))).Status);
        Assert.False(await db.Memberships.AnyAsync(x=>x.WorkspaceId==workspace.Id && x.Active));
    }
}
