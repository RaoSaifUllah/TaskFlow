using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Identity;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests;

public sealed class IdentityTests
{
    [Fact]
    public void Argon2idSaltsVerifiesRejectsMalformedAndBoundsWork()
    {
        var hasher = new Argon2PasswordHasher();
        var user = new TaskFlowUser();
        var watch = Stopwatch.StartNew();
        var hash = hasher.HashPassword(user, "synthetic test passphrase");
        watch.Stop();
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=2$", hash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, "synthetic test passphrase"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "wrong passphrase"));
        Assert.NotEqual(hash, hasher.HashPassword(user, "synthetic test passphrase"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash.Replace("m=65536", "m=2147483647"), "test"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, "malformed", "test"));
        Console.WriteLine($"Argon2id test-host single-hash elapsed: {watch.ElapsedMilliseconds}ms; 64 MiB, t=3, p=2. Not a production benchmark.");
    }

    [PostgreSqlFact]
    public async Task ConcurrentRotationDetectsReplayAndRevocationImmediatelyDisablesAccess()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-signing-" + Guid.NewGuid() + ".pem");
        using var rsa = RSA.Create(3072);
        await File.WriteAllTextAsync(path, rsa.ExportRSAPrivateKeyPem());
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Write"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE"),
                ["ConnectionStrings:Read"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ"),
                ["Auth:SigningKeyPath"] = path,
                ["Auth:DataProtectionKeyDirectory"] = Path.Combine(AppContext.BaseDirectory, ".local", "test-protection")
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(config);
            services.AddTaskFlow(config);
            await using var provider = services.BuildServiceProvider();
            Guid userId;
            AuthGrant grant;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
                await db.Database.MigrateAsync();
                var user = new TaskFlowUser { Id = Guid.NewGuid(), Email = Guid.NewGuid() + "@example.invalid", UserName = Guid.NewGuid().ToString(), CreatedAt = DateTimeOffset.UtcNow, EmailConfirmed = true };
                userId = user.Id;
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<TaskFlowUser>>();
                Assert.True((await manager.CreateAsync(user, "synthetic test passphrase")).Succeeded);
                grant = (await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync(user.Email, "synthetic test passphrase", null, "Test", default))!;
                Assert.NotNull(grant);
                Assert.DoesNotContain("synthetic", grant.AccessToken);
            }
            async Task<AuthGrant?> Rotate()
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IAuthService>().RotateAsync(grant.RefreshToken, grant.CsrfToken, default);
            }
            var outcomes = await Task.WhenAll(Rotate(), Rotate());
            Assert.Single(outcomes, x => x is not null);
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
                var session = await db.RefreshSessions.SingleAsync(x => x.UserId == userId);
                Assert.NotNull(session.RevokedAt);
                var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
                Assert.False(await auth.ValidateSessionAsync(userId, session.Id, default));
                Assert.Null(await auth.RotateAsync(outcomes.Single(x => x is not null)!.RefreshToken, grant.CsrfToken, default));
                var second = await auth.LoginAsync(grant.User.Email, "synthetic test passphrase", null, "Second", default);
                Assert.NotNull(second);
                var newSession = await db.RefreshSessions.SingleAsync(x => x.UserId == userId && x.RevokedAt == null);
                Assert.Null(await auth.RotateAsync(second.RefreshToken, "invalid-csrf-token-which-is-long-enough", default));
                Assert.True(await auth.ValidateSessionAsync(userId, newSession.Id, default));
                await auth.RevokeAsync(userId, newSession.Id, default);
                Assert.False(await auth.ValidateSessionAsync(userId, newSession.Id, default));
            }
        }
        finally { File.Delete(path); }
    }
}
