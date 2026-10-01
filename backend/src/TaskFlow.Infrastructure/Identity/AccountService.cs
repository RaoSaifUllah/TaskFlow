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
public sealed class AccountService(TaskFlowDbContext db, UserManager<TaskFlowUser> users, IConfiguration configuration, TimeProvider clock) : IAccountService
{
    public async Task<bool> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken)
    {
        var expected = configuration["Auth:BootstrapToken"];
        if (expected is null || request.Token.Length > 128 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)))) return false;
        if (request.Email.Length > 254 || request.Password.Length is < 12 or > 1024 || request.WorkspaceName.Length is < 1 or > 120) return false;
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
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task<string?> BeginMfaAsync(Guid userId, Guid sessionId, string password, CancellationToken cancellationToken)
    {
        if (password.Length > 1024) return null;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.TwoFactorEnabled || !await users.CheckPasswordAsync(user, password)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!(await users.ResetAuthenticatorKeyAsync(user)).Succeeded) return null;
        await db.RefreshSessions.Where(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.SecurityStamp, user.SecurityStamp!), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await users.GetAuthenticatorKeyAsync(user);
    }
    public async Task<IReadOnlyList<string>?> ConfirmMfaAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.TwoFactorEnabled || code.Length > 12 ||
            !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await users.SetTwoFactorEnabledAsync(user, true);
        if (!result.Succeeded) return null;
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        await db.RecoveryCodes.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var recovery in codes) db.RecoveryCodes.Add(new RecoveryCode { UserId = user.Id, Hash = AuthService.Hash(recovery) });
        // MFA enrollment changes the security stamp: existing access/refresh sessions fail immediately.
        await users.UpdateSecurityStampAsync(user);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return codes;
    }
    public async Task<bool> StepUpAsync(Guid userId, Guid sessionId, string code, CancellationToken cancellationToken)
    {
        if (code.Length > 12) return false;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.TwoFactorEnabled || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)) return false;
        return await db.RefreshSessions.Where(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.MfaVerifiedAt, clock.GetUtcNow()), cancellationToken) == 1;
    }
}
