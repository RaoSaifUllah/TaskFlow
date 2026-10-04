using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaskFlow.Application.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

public sealed class AccountChallenge
{
    public string Hash { get; set; } = "";
    public Guid UserId { get; set; }
    public AccountChallengePurpose Purpose { get; set; }
    public string SecurityStamp { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
public sealed class LocalAccountMessageSink(IConfiguration config) : IAccountMessageSink
{
    public async Task DeliverAsync(AccountMessage message, CancellationToken ct)
    {
        // Explicit local provider mock; never invoke SMTP or an external endpoint.
        var directory = config["Auth:MockDeliveryDirectory"] ?? Path.Combine(".local", "account-mail");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(message), ct);
    }
}
public sealed class AccountLifecycle(TaskFlowDbContext db, UserManager<TaskFlowUser> users, IAccountMessageSink messages, TimeProvider clock) : IAccountLifecycle
{
    public async Task RequestResetAsync(string email, CancellationToken ct)
    {
        if (email is null || email.Length > 254) return;
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.Active) return;
        await IssueAsync(user, AccountChallengePurpose.ResetPassword, ct);
    }
    public async Task RequestVerificationAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.Active || user.EmailConfirmed) return;
        await IssueAsync(user, AccountChallengePurpose.VerifyEmail, ct);
    }
    private async Task IssueAsync(TaskFlowUser user, AccountChallengePurpose purpose, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockUserAsync(user, ct);
        if (!user.Active || (purpose == AccountChallengePurpose.VerifyEmail && user.EmailConfirmed)) return;
        var now = clock.GetUtcNow(); var token = AuthService.NewOpaqueToken();
        await db.AccountChallenges.Where(x => x.UserId == user.Id && x.Purpose == purpose && x.ConsumedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.ConsumedAt, now), ct);
        db.AccountChallenges.Add(new AccountChallenge { UserId = user.Id, Purpose = purpose, Hash = AuthService.Hash(token),
            SecurityStamp = user.SecurityStamp!, ExpiresAt = now.Add(purpose == AccountChallengePurpose.ResetPassword ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(24)) });
        db.Audit(purpose==AccountChallengePurpose.ResetPassword ? "reset_requested" : "verification_requested",null,user.Id,now);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await messages.DeliverAsync(new AccountMessage(user.Email!, token, purpose), ct);
    }
    public Task<bool> ResetAsync(string token, string password, CancellationToken ct) =>
        ConsumeAsync(token, AccountChallengePurpose.ResetPassword, password, ct);
    public Task<bool> VerifyAsync(string token, CancellationToken ct) => ConsumeAsync(token, AccountChallengePurpose.VerifyEmail, null, ct);
    private async Task<bool> ConsumeAsync(string token, AccountChallengePurpose purpose, string? password, CancellationToken ct)
    {
        if (token is null || token.Length is < 32 or > 128 || (purpose == AccountChallengePurpose.ResetPassword && (password is null || password.Length is < 12 or > 1024))) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = AuthService.Hash(token);
        var candidate = await db.AccountChallenges.AsNoTracking().SingleOrDefaultAsync(x => x.Hash == hash && x.Purpose == purpose, ct);
        if (candidate is null) return false;
        var user = await users.FindByIdAsync(candidate.UserId.ToString());
        if (user is null) return false;
        // User first, then challenge: same order as issuance and login lockout updates.
        await LockUserAsync(user, ct);
        var rows = await db.AccountChallenges.FromSqlInterpolated($"SELECT * FROM \"AccountChallenges\" WHERE \"Hash\" = {hash} FOR UPDATE").ToListAsync(ct);
        var challenge = rows.Single(); await db.Entry(challenge).ReloadAsync(ct);
        var now = clock.GetUtcNow();
        if (!user.Active || challenge.ConsumedAt is not null || challenge.ExpiresAt <= now || challenge.SecurityStamp != user.SecurityStamp) return false;
        IdentityResult result;
        if (purpose == AccountChallengePurpose.ResetPassword)
        {
            var identityToken = await users.GeneratePasswordResetTokenAsync(user);
            result = await users.ResetPasswordAsync(user, identityToken, password!);
        }
        else result = await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));
        if (!result.Succeeded) return false;
        challenge.ConsumedAt = now;
        db.Audit(purpose==AccountChallengePurpose.ResetPassword ? "password_reset" : "email_verified",user.Id,user.Id,now);
        if (purpose == AccountChallengePurpose.ResetPassword)
            await db.RefreshSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, now), ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
    }
    private async Task LockUserAsync(TaskFlowUser user, CancellationToken ct)
    {
        _ = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE").ToListAsync(ct);
        await db.Entry(user).ReloadAsync(ct);
    }
}
