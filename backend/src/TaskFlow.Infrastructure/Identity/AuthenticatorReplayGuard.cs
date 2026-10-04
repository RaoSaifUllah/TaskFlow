using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

public sealed class AuthenticatorUse
{
    public Guid UserId { get; set; }
    public Guid Generation { get; set; }
    public long Step { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}
public sealed class AuthenticatorReplayGuard(TaskFlowDbContext db, UserManager<TaskFlowUser> users, TimeProvider clock)
{
    // Call only inside a transaction holding FOR UPDATE on this user.
    public async Task<bool> ConsumeAsync(TaskFlowUser user, string? code, CancellationToken ct)
    {
        if (code is null || code.Length!=6 || code.Any(c=>c<'0'||c>'9') ||
            !await users.VerifyTwoFactorTokenAsync(user,TokenOptions.DefaultAuthenticatorProvider,code)) return false;
        var secret=await users.GetAuthenticatorKeyAsync(user);
        if(secret is null) return false;
        var bytes=Base32Encoding.ToBytes(secret);
        try
        {
            // Same SHA-1/30-second/6-digit algorithm as Identity, with a narrower +/-1 step window.
            var totp=new Totp(bytes);
            if(!totp.VerifyTotp(clock.GetUtcNow().UtcDateTime,code,out var step,new VerificationWindow(previous:1,future:1))) return false;
            if(await db.AuthenticatorUses.AnyAsync(x=>x.UserId==user.Id && x.Generation==user.AuthenticatorGeneration && x.Step==step,ct)) return false;
            db.AuthenticatorUses.Add(new AuthenticatorUse {UserId=user.Id,Generation=user.AuthenticatorGeneration,Step=step,AcceptedAt=clock.GetUtcNow()});
            return true;
        }
        finally {CryptographicOperations.ZeroMemory(bytes);}
    }
}
