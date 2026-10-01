using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using TaskFlow.Application.Identity;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests;

public sealed class MfaTests
{
    [PostgreSqlFact]
    public async Task TotpSecretIsProtectedEnrollmentRevokesSessionsAndRecoveryCodesAreSingleUse()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, ".local", "mfa-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "signing.pem");
        using var rsa = RSA.Create(3072);
        await File.WriteAllTextAsync(path, rsa.ExportRSAPrivateKeyPem());
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Write"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE"),
                ["ConnectionStrings:Read"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ"),
                ["Auth:SigningKeyPath"] = path,
                ["Auth:DataProtectionKeyDirectory"] = Path.Combine(directory, "protection")
            }).Build();
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(config); services.AddTaskFlow(config);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
            await db.Database.MigrateAsync();
            var user = new TaskFlowUser { Id = Guid.NewGuid(), Email = Guid.NewGuid() + "@example.invalid", UserName = Guid.NewGuid().ToString(), CreatedAt = DateTimeOffset.UtcNow };
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TaskFlowUser>>();
            Assert.True((await users.CreateAsync(user, "synthetic MFA passphrase")).Succeeded);
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var account = scope.ServiceProvider.GetRequiredService<IAccountService>();
            var initial = (await auth.LoginAsync(user.Email, "synthetic MFA passphrase", null, "MFA fixture", default))!;
            var sessionId = (await db.RefreshTokens.SingleAsync(x => x.Hash == AuthService.Hash(initial.RefreshToken))).SessionId;
            var secret = await account.BeginMfaAsync(user.Id, sessionId, "synthetic MFA passphrase", default);
            Assert.NotNull(secret);
            var stored = await db.UserTokens.AsNoTracking().SingleAsync(x => x.UserId == user.Id && x.Name == "AuthenticatorKey");
            Assert.NotEqual(secret, stored.Value);
            Assert.True(await auth.ValidateSessionAsync(user.Id, sessionId, default));
            var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow);
            var recovery = await account.ConfirmMfaAsync(user.Id, code, default);
            Assert.NotNull(recovery); Assert.Equal(10, recovery.Count);
            Assert.False(await auth.ValidateSessionAsync(user.Id, sessionId, default));
            Assert.Null(await auth.LoginAsync(user.Email, "synthetic MFA passphrase", null, "MFA fixture", default));
            var totpGrant = await auth.LoginAsync(user.Email, "synthetic MFA passphrase", code, "MFA fixture", default);
            Assert.NotNull(totpGrant);
            Assert.True(totpGrant.User.MfaEnabled);
            Assert.DoesNotContain(await db.RecoveryCodes.Where(x => x.UserId == user.Id).Select(x => x.Hash).ToArrayAsync(), x => recovery.Contains(x));
            var recovered = await auth.LoginAsync(user.Email, "synthetic MFA passphrase", recovery[0], "Recovery fixture", default);
            Assert.NotNull(recovered); Assert.False(recovered.User.MfaEnabled);
            var recoveredSession = (await db.RefreshTokens.SingleAsync(x => x.Hash == AuthService.Hash(recovered.RefreshToken))).SessionId;
            Assert.Null((await db.RefreshSessions.SingleAsync(x => x.Id == recoveredSession)).MfaVerifiedAt);
            var nextSecret = await account.BeginMfaAsync(user.Id, recoveredSession, "synthetic MFA passphrase", default);
            var nextCode = new Totp(Base32Encoding.ToBytes(nextSecret!)).ComputeTotp(DateTime.UtcNow);
            Assert.NotNull(await account.ConfirmMfaAsync(user.Id, nextCode, default));
            Assert.Null(await auth.LoginAsync(user.Email, "synthetic MFA passphrase", recovery[0], "Recovery replay", default));
        }
        finally
        {
            var protectedKeys = Path.GetFullPath(Path.Combine(directory, "protection"));
            Assert.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, protectedKeys);
            if (Directory.Exists(protectedKeys)) Directory.Delete(protectedKeys, true);
            File.Delete(path); Directory.Delete(directory);
        }
    }
}
