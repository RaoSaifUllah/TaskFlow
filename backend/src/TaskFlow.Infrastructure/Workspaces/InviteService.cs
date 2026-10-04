using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Persistence;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Workspaces;

public sealed class InviteService(TaskFlowDbContext db, IWorkspaceRepository repository, IWorkspaceService permissions,
    IUnitOfWork unit, UserManager<TaskFlowUser> users, TimeProvider clock) : IInviteService
{
    public async Task<InviteGrant> CreateAsync(RequestActor actor, Guid workspaceId, string email, WorkspaceRole role, CancellationToken ct)
    {
        if (!ValidEmail(email) || !Enum.IsDefined(role)) throw new WorkspaceRejectedException("invalid_invitation", 400);
        var token = AuthService.NewOpaqueToken(); var expiry = clock.GetUtcNow().AddDays(7);
        await unit.ExecuteAsync(async cancellation =>
        {
            if (await repository.LockAsync(workspaceId, cancellation) is null) throw new WorkspaceRejectedException("workspace_not_found", 404);
            await permissions.AuthorizeManagementAsync(actor, workspaceId, role, cancellation);
            var normalized = users.NormalizeEmail(email);
            var alreadyMember = await (from member in db.Memberships join user in db.Users on member.UserId equals user.Id
                where member.WorkspaceId == workspaceId && member.Active && user.NormalizedEmail == normalized select member.UserId).AnyAsync(cancellation);
            if (alreadyMember) throw new WorkspaceRejectedException("already_member", 409);
            db.WorkspaceInvites.Add(new WorkspaceInvite { WorkspaceId = workspaceId, CreatedBy = actor.UserId, NormalizedEmail = normalized!,
                TokenHash = AuthService.Hash(token), Role = role, ExpiresAt = expiry });
            db.Audit("invitation_created",actor.UserId,workspaceId,clock.GetUtcNow(),workspaceId);
        }, ct);
        // Returned once to the authorized inviter for manual sharing; no live provider is invoked.
        return new InviteGrant(token, expiry);
    }
    public async Task<bool> RegisterAsync(InviteRegistration request, CancellationToken ct)
    {
        if (!ValidEmail(request.Email) || request.Password is null || request.Password.Length is < 12 or > 1024 || !ValidZone(request.TimeZone)) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var invite = await LockInviteAsync(request.Token, ct);
        if (invite is null || invite.NormalizedEmail != users.NormalizeEmail(request.Email) || await users.FindByEmailAsync(request.Email) is not null) return false;
        var user = new TaskFlowUser { Id = Guid.NewGuid(), UserName = request.Email.Trim(), Email = request.Email.Trim(), EmailConfirmed = true,
            TimeZone = request.TimeZone, CreatedAt = clock.GetUtcNow() };
        if (!(await users.CreateAsync(user, request.Password)).Succeeded) return false;
        db.Memberships.Add(new Membership { WorkspaceId = invite.WorkspaceId, UserId = user.Id, Role = invite.Role });
        invite.ConsumedAt = clock.GetUtcNow();
        db.Audit("invitation_registered",user.Id,invite.Id,clock.GetUtcNow(),invite.WorkspaceId);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
    }
    public async Task<bool> AcceptAsync(RequestActor actor, string token, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var invite = await LockInviteAsync(token, ct);
        if (invite is null) return false;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == actor.UserId && x.Active, ct);
        if (user is null || user.NormalizedEmail != invite.NormalizedEmail) return false;
        var member = await repository.MemberAsync(invite.WorkspaceId, user.Id, ct);
        if (member?.Active == true) return false;
        if (member is null) db.Memberships.Add(new Membership { WorkspaceId = invite.WorkspaceId, UserId = user.Id, Role = invite.Role });
        else { member.Active = true; member.Role = invite.Role; }
        invite.ConsumedAt = clock.GetUtcNow();
        db.Audit("invitation_accepted",actor.UserId,invite.Id,clock.GetUtcNow(),invite.WorkspaceId);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
    }
    private async Task<WorkspaceInvite?> LockInviteAsync(string token, CancellationToken ct)
    {
        if (token is null || token.Length is < 32 or > 128) return null;
        var hash = AuthService.Hash(token);
        var candidate = await db.WorkspaceInvites.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (candidate is null || await repository.LockAsync(candidate.WorkspaceId, ct) is null) return null;
        var rows = await db.WorkspaceInvites.FromSqlInterpolated($"SELECT * FROM \"WorkspaceInvites\" WHERE \"Id\" = {candidate.Id} FOR UPDATE").ToListAsync(ct);
        var invite = rows.Single(); await db.Entry(invite).ReloadAsync(ct);
        if (invite.ConsumedAt is not null || invite.ExpiresAt <= clock.GetUtcNow()) return null;
        // Revocation/demotion of the inviter withdraws outstanding grants.
        var creator = await repository.MemberAsync(invite.WorkspaceId, invite.CreatedBy, ct);
        if (creator is null || !creator.Active || creator.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin) ||
            (invite.Role == WorkspaceRole.Owner && creator.Role != WorkspaceRole.Owner)) return null;
        return invite;
    }
    private static bool ValidEmail(string email) => email is not null && email.Length <= 254 && MailAddress.TryCreate(email.Trim(), out var parsed) && parsed.Address == email.Trim();
    private static bool ValidZone(string zone)
    {
        if (zone is null || zone.Length > 100 || (zone != "UTC" && !zone.Contains('/'))) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); return true; } catch (TimeZoneNotFoundException) { return false; }
    }
}
