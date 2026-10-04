using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Application.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

public sealed class AuthService(TaskFlowDbContext db, UserManager<TaskFlowUser> users, JwtIssuer jwt, TimeProvider clock, AuthenticatorReplayGuard otp) : IAuthService
{
    public static string NewOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool EqualHash(string expected, string raw) => expected.Length == 64 && raw.Length <= 128 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(Hash(raw)));

    public async Task<AuthGrant?> LoginAsync(string email, string password, string? totp, string device, CancellationToken cancellationToken)
    {
        if (email.Length > 254 || password.Length is < 1 or > 1024 || device.Length > 120) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await users.FindByEmailAsync(email);
        if (user is null) return null;
        _ = await db.Users.FromSql($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE").SingleAsync(cancellationToken);
        await db.Entry(user).ReloadAsync(cancellationToken);
        if (!user.Active || await users.IsLockedOutAsync(user)) return null;
        if (!await users.CheckPasswordAsync(user, password))
        { await users.AccessFailedAsync(user); await transaction.CommitAsync(cancellationToken); return null; }
        if (user.TwoFactorEnabled && !await otp.ConsumeAsync(user,totp,cancellationToken))
        {
            if (totp is null || totp.Length != 32 || await db.RecoveryCodes.Where(x => x.UserId == user.Id && x.Hash == Hash(totp) && x.UsedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, clock.GetUtcNow()), cancellationToken) != 1)
            { await users.AccessFailedAsync(user); await transaction.CommitAsync(cancellationToken); return null; }
            await users.SetTwoFactorEnabledAsync(user, false);
            await users.UpdateSecurityStampAsync(user);
        }
        await users.ResetAccessFailedCountAsync(user);
        var now = clock.GetUtcNow();
        var csrf = NewOpaqueToken();
        var refresh = NewOpaqueToken();
        var session = new RefreshSession
        {
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp!,
            DeviceName = device,
            CsrfHash = Hash(csrf),
            CreatedAt = now,
            LastSeenAt = now,
            IdleExpiresAt = now.AddDays(7),
            AbsoluteExpiresAt = now.AddDays(30),
            MfaVerifiedAt = user.TwoFactorEnabled ? now : null
        };
        db.RefreshSessions.Add(session);
        db.RefreshTokens.Add(new RefreshToken { Hash = Hash(refresh), SessionId = session.Id, CreatedAt = now });
        db.Audit("session_created",user.Id,session.Id,now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Grant(user, session, refresh, csrf, now);
    }
    public async Task<AuthGrant?> RotateAsync(string tokenValue, string csrf, CancellationToken cancellationToken)
    {
        if (tokenValue.Length is < 32 or > 128 || csrf.Length is < 32 or > 128) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hash = Hash(tokenValue);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.Hash == hash, cancellationToken);
        if (token is null) return null;
        var session = await db.RefreshSessions.FromSql($"SELECT * FROM \"RefreshSessions\" WHERE \"Id\" = {token.SessionId} FOR UPDATE").SingleAsync(cancellationToken);
        await db.Entry(token).ReloadAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (!EqualHash(session.CsrfHash, csrf) || session.RevokedAt is not null) return null;
        if (token.ConsumedAt is not null)
        {
            session.RevokedAt = now;
            db.Audit("refresh_replay_revoked",session.UserId,session.Id,now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var user = await db.Users.SingleAsync(x => x.Id == session.UserId, cancellationToken);
        if (!user.Active || user.SecurityStamp != session.SecurityStamp || session.IdleExpiresAt <= now || session.AbsoluteExpiresAt <= now) return null;
        token.ConsumedAt = now;
        session.LastSeenAt = now;
        session.IdleExpiresAt = Min(now.AddDays(7), session.AbsoluteExpiresAt);
        var next = NewOpaqueToken();
        db.RefreshTokens.Add(new RefreshToken { Hash = Hash(next), SessionId = session.Id, CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Grant(user, session, next, csrf, now);
    }
    public async Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
        var now=clock.GetUtcNow();
        var count=await db.RefreshSessions.Where(x=>x.UserId==userId && x.Id==sessionId && x.RevokedAt==null)
            .ExecuteUpdateAsync(update=>update.SetProperty(x=>x.RevokedAt,now),cancellationToken);
        if(count==1) {db.Audit("session_revoked",userId,sessionId,now);await db.SaveChangesAsync(cancellationToken);}
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return db.RefreshSessions.AsNoTracking().Join(db.Users, s => s.UserId, u => u.Id, (s, u) => new { s, u })
            .AnyAsync(x => x.s.Id == sessionId && x.u.Id == userId && x.u.Active && x.s.RevokedAt == null &&
                x.s.SecurityStamp == x.u.SecurityStamp && x.s.IdleExpiresAt > now && x.s.AbsoluteExpiresAt > now, cancellationToken);
    }
    public async Task<IReadOnlyList<SessionSummary>> SessionsAsync(Guid userId, Guid currentSession, CancellationToken cancellationToken) =>
        await db.RefreshSessions.AsNoTracking().Where(x => x.UserId == userId && x.RevokedAt == null)
            .OrderByDescending(x => x.LastSeenAt).Take(100)
            .Select(x => new SessionSummary(x.Id, x.DeviceName, x.CreatedAt, x.LastSeenAt, x.Id == currentSession)).ToListAsync(cancellationToken);
    private AuthGrant Grant(TaskFlowUser user, RefreshSession session, string refresh, string csrf, DateTimeOffset now) =>
        new(jwt.Issue(user, session, now), refresh, csrf, now.AddMinutes(5),
            new AuthUser(user.Id, user.Email!, user.TimeZone, user.InstanceAdmin, user.TwoFactorEnabled));
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
