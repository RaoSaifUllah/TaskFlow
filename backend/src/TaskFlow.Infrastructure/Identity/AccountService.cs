using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaskFlow.Application.Identity;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

public sealed class RecoveryCode
{
    public Guid UserId { get; set; }
    public string Hash { get; set; } = "";
    public DateTimeOffset? UsedAt { get; set; }
}
public sealed class AccountService(TaskFlowDbContext db, UserManager<TaskFlowUser> users, IConfiguration configuration, TimeProvider clock, AuthenticatorReplayGuard otp) : IAccountService
{
    public async Task<bool> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken)
    {
        var expected = configuration["Auth:BootstrapToken"];
        if (expected is null || request.Token.Length > 128 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)))) return false;
        if (request.Email.Length > 254 || request.Password.Length is < 12 or > 1024 || string.IsNullOrWhiteSpace(request.WorkspaceName) || request.WorkspaceName.Trim().Length > 120) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone); } catch (TimeZoneNotFoundException) { return false; }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(521123314)", cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken)) return false;
        var user = new TaskFlowUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            UserName = request.Email,
            InstanceAdmin = true,
            EmailConfirmed = true,
            TimeZone = request.TimeZone,
            CreatedAt = clock.GetUtcNow()
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return false;
        var workspace = new Workspace(request.WorkspaceName);
        db.Workspaces.Add(workspace);
        db.Memberships.Add(new Membership { UserId = user.Id, WorkspaceId = workspace.Id, Role = WorkspaceRole.Owner });
        db.Audit("instance_bootstrapped",user.Id,user.Id,clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task<string?> BeginMfaAsync(Guid userId, Guid sessionId, string password, CancellationToken cancellationToken)
    {
        if (password.Length > 1024) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        await LockUserAsync(user, cancellationToken);
        if (!user.Active || user.TwoFactorEnabled || await users.IsLockedOutAsync(user)) return null;
        if (!await users.CheckPasswordAsync(user,password)) {await users.AccessFailedAsync(user);await transaction.CommitAsync(cancellationToken);return null;}
        user.AuthenticatorGeneration=Guid.NewGuid();
        if (!(await users.ResetAuthenticatorKeyAsync(user)).Succeeded) return null;
        await db.RefreshSessions.Where(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.SecurityStamp, user.SecurityStamp!), cancellationToken);
        db.Audit("mfa_setup_started",userId,userId,clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await users.GetAuthenticatorKeyAsync(user);
    }
    public async Task<IReadOnlyList<string>?> ConfirmMfaAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        await LockUserAsync(user, cancellationToken);
        if (!user.Active || user.TwoFactorEnabled || code.Length > 12 || await users.IsLockedOutAsync(user)) return null;
        if(!await otp.ConsumeAsync(user,code,cancellationToken)) {await users.AccessFailedAsync(user);await transaction.CommitAsync(cancellationToken);return null;}
        await users.ResetAccessFailedCountAsync(user);
        var result = await users.SetTwoFactorEnabledAsync(user, true);
        if (!result.Succeeded) return null;
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        await db.RecoveryCodes.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var recovery in codes) db.RecoveryCodes.Add(new RecoveryCode { UserId = user.Id, Hash = AuthService.Hash(recovery) });
        // MFA enrollment changes the security stamp: existing access/refresh sessions fail immediately.
        await users.UpdateSecurityStampAsync(user);
        db.Audit("mfa_enabled",userId,userId,clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return codes;
    }
    public async Task<bool> StepUpAsync(Guid userId, Guid sessionId, string code, CancellationToken cancellationToken)
    {
        if (code.Length > 12) return false;
        await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        if(user is null) return false;
        await LockUserAsync(user,cancellationToken);
        var now=clock.GetUtcNow();
        if(!user.Active || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user)) return false;
        if(!await otp.ConsumeAsync(user,code,cancellationToken)) {await users.AccessFailedAsync(user);db.Audit("mfa_step_up_rejected",userId,sessionId,now);await db.SaveChangesAsync(cancellationToken);await transaction.CommitAsync(cancellationToken);return false;}
        await users.ResetAccessFailedCountAsync(user);
        var updated=await db.RefreshSessions.Where(x=>x.Id==sessionId && x.UserId==userId && x.RevokedAt==null && x.SecurityStamp==user.SecurityStamp && x.IdleExpiresAt>now && x.AbsoluteExpiresAt>now)
            .ExecuteUpdateAsync(update=>update.SetProperty(x=>x.MfaVerifiedAt,now),cancellationToken);
        if(updated!=1) return false;
        db.Audit("mfa_step_up",userId,sessionId,now); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }
    private async Task LockUserAsync(TaskFlowUser user, CancellationToken ct)
    {
        _ = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE").ToListAsync(ct);
        await db.Entry(user).ReloadAsync(ct);
    }
}
