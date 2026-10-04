using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskFlow.Application.Identity;

namespace TaskFlow.IntegrationTests;

public sealed class AuthBoundaryTests
{
    [Fact]
    public async Task AuthBodyAndConcurrencyAreBoundedAndDatabaseFailuresAreRedacted()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ".local", "boundary-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        var key = Path.Combine(path, "signing.pem");
        using var rsa = RSA.Create(3072);
        await File.WriteAllTextAsync(key, rsa.ExportRSAPrivateKeyPem());
        var auth = new BlockingAuth();
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.UseEnvironment("Testing")
                .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:SigningKeyPath"] = key, ["Auth:Origin"] = "https://localhost:5173", ["Logging:LogLevel:Default"] = "Warning"
                })).ConfigureServices(services => { services.RemoveAll<IAuthService>(); services.AddSingleton<IAuthService>(auth); }));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            const string csrf = "synthetic-csrf-for-local-boundary-tests";
            client.DefaultRequestHeaders.Add("Cookie", "__Host-TaskFlow.Csrf=" + csrf);
            client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            client.DefaultRequestHeaders.Add("Origin", "https://localhost:5173");
            using var oversized = await client.PostAsJsonAsync("/api/v1/auth/login", new {email="test@example.invalid",password=new string('x',9000),device="test"});
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
            Assert.Equal(0, auth.Calls);
            var pending = Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/v1/auth/login", new {email="test@example.invalid",password="test",device="test"})).ToArray();
            await auth.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var busy = await client.PostAsJsonAsync("/api/v1/auth/login", new {email="test@example.invalid",password="test",device="test"});
            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
            Assert.True(busy.Headers.Contains("Retry-After"));
            auth.Release.TrySetResult();
            foreach (var response in await Task.WhenAll(pending)) { Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); response.Dispose(); }
            auth.FailDatabase = true;
            using var unavailable = await client.PostAsJsonAsync("/api/v1/auth/login", new {email="test@example.invalid",password="test",device="test"});
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            var body = await unavailable.Content.ReadAsStringAsync();
            Assert.Contains("database_unavailable", body); Assert.DoesNotContain("synthetic-private-detail", body);
            Assert.Contains("correlationId", body);
        }
        finally { auth.Release.TrySetResult(); File.Delete(key); Directory.Delete(path); }
    }

    private sealed class SyntheticDbFailure() : DbException("synthetic-private-detail");
    private sealed class BlockingAuth : IAuthService
    {
        public int Calls;
        public bool FailDatabase;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AuthGrant?> LoginAsync(string email, string password, string? code, string device, CancellationToken ct)
        {
            if (FailDatabase) throw new SyntheticDbFailure();
            if (Interlocked.Increment(ref Calls) == 4) Entered.TrySetResult();
            await Release.Task.WaitAsync(ct); return null;
        }
        public Task<AuthGrant?> RotateAsync(string token, string csrf, CancellationToken ct) => Task.FromResult<AuthGrant?>(null);
        public Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<SessionSummary>> SessionsAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionSummary>>([]);
    }
}
