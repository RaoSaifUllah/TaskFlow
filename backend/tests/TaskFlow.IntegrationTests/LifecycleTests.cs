using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskFlow.Application.Identity;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests;

public sealed class LifecycleTests
{
    [PostgreSqlFact]
    public async Task ResetRaceRevokesSessionsAndVerificationExpiryPurposeAndReplayAreEnforced()
    {
        var path = Path.Combine(AppContext.BaseDirectory,".local","lifecycle-"+Guid.NewGuid()); Directory.CreateDirectory(path);
        var key = Path.Combine(path,"signing.pem"); using var rsa = RSA.Create(3072); await File.WriteAllTextAsync(key,rsa.ExportRSAPrivateKeyPem());
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["ConnectionStrings:Write"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE"),
                ["ConnectionStrings:Read"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ"),
                ["Auth:SigningKeyPath"] = key, ["Auth:DataProtectionKeyDirectory"] = Path.Combine(path,"protection")
            }).Build();
            var sink = new MockMessages(); var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(config); services.AddTaskFlow(config);
            services.RemoveAll<IAccountMessageSink>(); services.AddSingleton<IAccountMessageSink>(sink);
            await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>(); await db.Database.MigrateAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TaskFlowUser>>();
            var user = new TaskFlowUser {Id=Guid.NewGuid(),Email=Guid.NewGuid()+"@example.invalid",UserName=Guid.NewGuid().ToString(),TwoFactorEnabled=true};
            Assert.True((await users.CreateAsync(user,"original synthetic passphrase")).Succeeded);
            var session = new RefreshSession {UserId=user.Id,SecurityStamp=user.SecurityStamp!,IdleExpiresAt=DateTimeOffset.UtcNow.AddDays(7),AbsoluteExpiresAt=DateTimeOffset.UtcNow.AddDays(30)};
            db.RefreshSessions.Add(session); await db.SaveChangesAsync();
            var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
            await lifecycle.RequestResetAsync(Guid.NewGuid()+"@example.invalid",default); Assert.Empty(sink.Items);
            await lifecycle.RequestResetAsync(user.Email,default); var original=sink.Items.Last().Token;
            Assert.Equal(64,(await db.AccountChallenges.SingleAsync(x=>x.Hash==AuthService.Hash(original))).Hash.Length);
            Assert.False(await lifecycle.VerifyAsync(original,default));
            await lifecycle.RequestResetAsync(user.Email,default); var token=sink.Items.Last().Token;
            Assert.False(await lifecycle.ResetAsync(original,"changed synthetic passphrase",default));
            async Task<bool> Reset() { await using var concurrent=provider.CreateAsyncScope(); return await concurrent.ServiceProvider.GetRequiredService<IAccountLifecycle>().ResetAsync(token,"changed synthetic passphrase",default); }
            var outcomes=await Task.WhenAll(Reset(),Reset()); Assert.Single(outcomes,x=>x); Assert.Single(outcomes,x=>!x);
            Assert.False(await scope.ServiceProvider.GetRequiredService<IAuthService>().ValidateSessionAsync(user.Id,session.Id,default));
            await db.Entry(user).ReloadAsync(); Assert.True(user.TwoFactorEnabled);
            Assert.False(await users.CheckPasswordAsync(user,"original synthetic passphrase")); Assert.True(await users.CheckPasswordAsync(user,"changed synthetic passphrase"));
            await lifecycle.RequestVerificationAsync(user.Id,default); var verify=sink.Items.Last().Token;
            Assert.True(await lifecycle.VerifyAsync(verify,default)); Assert.False(await lifecycle.VerifyAsync(verify,default));
            await db.Entry(user).ReloadAsync(); Assert.True(user.EmailConfirmed);
            await lifecycle.RequestResetAsync(user.Email,default); var expired=sink.Items.Last().Token;
            await db.AccountChallenges.Where(x=>x.Hash==AuthService.Hash(expired)).ExecuteUpdateAsync(x=>x.SetProperty(c=>c.ExpiresAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.False(await lifecycle.ResetAsync(expired,"another synthetic passphrase",default));
        }
        finally
        {
            var resolved=Path.GetFullPath(path); Assert.StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".local"))+Path.DirectorySeparatorChar,resolved);
            Directory.Delete(resolved,true);
        }
    }
    private sealed class MockMessages : IAccountMessageSink
    {
        public ConcurrentQueue<AccountMessage> Items { get; } = new();
        public Task DeliverAsync(AccountMessage message,CancellationToken ct) { Items.Enqueue(message); return Task.CompletedTask; }
    }
}
