using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests;

public sealed class AuthHttpTests
{
    [PostgreSqlFact]
    public async Task BootstrapIsSingleUseCookieFlagsCsrfAndRevokedJwtAreEnforcedOverHttp()
    {
        var original = DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE")!);
        var builder = new NpgsqlConnectionStringBuilder(original);
        Assert.StartsWith("taskflow_test", builder.Database);
        var isolatedDatabase = "taskflow_test_http_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(original);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{isolatedDatabase}\"", admin)) await create.ExecuteNonQueryAsync();
        builder.Database = isolatedDatabase;
        var path = Path.Combine(AppContext.BaseDirectory, ".local", "http-test-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        var key = Path.Combine(path, "signing.pem");
        using var rsa = RSA.Create(3072);
        await File.WriteAllTextAsync(key, rsa.ExportRSAPrivateKeyPem());
        try
        {
            await using (var db = new TaskFlowDbContext(new DbContextOptionsBuilder<TaskFlowDbContext>().UseNpgsql(builder.ConnectionString).Options))
                await db.Database.MigrateAsync();
            var bootstrap = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
                host.UseEnvironment("Testing").ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Write"] = builder.ConnectionString,
                    ["ConnectionStrings:Read"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ"),
                    ["Auth:SigningKeyPath"] = key,
                    ["Auth:DataProtectionKeyDirectory"] = Path.Combine(path, "protection"),
                    ["Auth:BootstrapToken"] = bootstrap, ["Auth:Origin"] = "https://localhost:5173",
                    ["Logging:LogLevel:Default"] = "Warning"
                })));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            Assert.True(factory.Services.GetRequiredService<IConfiguration>()["Auth:BootstrapToken"] == bootstrap, "Test configuration must contain the synthetic bootstrap token.");
            Assert.True(factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Write") == builder.ConnectionString, "Test write configuration must contain its isolated database.");
            using (var diagnostic = factory.Services.CreateScope())
            {
                var diagnosticDb = diagnostic.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
                Assert.Equal(isolatedDatabase, new NpgsqlConnectionStringBuilder(diagnosticDb.Database.GetConnectionString()).Database);
                Assert.Equal(isolatedDatabase, await diagnosticDb.Database.SqlQueryRaw<string>("SELECT current_database() AS \"Value\"").SingleAsync());
                Assert.False(await diagnosticDb.Users.AnyAsync(), "Isolated bootstrap fixture must start without users.");
            }
            var email = Guid.NewGuid() + "@example.invalid";
            var credentials = new { email, password = "synthetic test passphrase", code = (string?)null, device = "HTTP test" };
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/login", credentials)).StatusCode);
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf.GetProperty("csrfToken").GetString());
            client.DefaultRequestHeaders.Add("Origin", "https://localhost:5173");
            var setup = new { email, password = credentials.password, token = bootstrap, workspaceName = "HTTP Test", timeZone = "UTC" };
            var firstBootstrap = await client.PostAsJsonAsync("/api/v1/auth/bootstrap", setup);
            Assert.True(firstBootstrap.StatusCode == HttpStatusCode.NoContent, "First bootstrap failed: " + await firstBootstrap.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/bootstrap", setup)).StatusCode);
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", credentials);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookies = string.Join("\n", login.Headers.GetValues("Set-Cookie"));
            Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/api/v1/auth", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookies, StringComparison.OrdinalIgnoreCase);
            var grant = (await login.Content.ReadFromJsonAsync<JsonElement>());
            client.DefaultRequestHeaders.Authorization = new("Bearer", grant.GetProperty("accessToken").GetString());
            client.DefaultRequestHeaders.Remove("X-CSRF-Token");
            client.DefaultRequestHeaders.Add("X-CSRF-Token", grant.GetProperty("csrfToken").GetString());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/sessions")).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", "https://localhost:5173");
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/sessions")).StatusCode);
        }
        finally
        {
            Assert.StartsWith("taskflow_test_http_", isolatedDatabase);
            Assert.NotEqual(new NpgsqlConnectionStringBuilder(original).Database, isolatedDatabase);
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{isolatedDatabase}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            File.Delete(key);
            var protectionPath = Path.GetFullPath(Path.Combine(path, "protection"));
            Assert.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, protectionPath);
            if (Directory.Exists(protectionPath)) Directory.Delete(protectionPath, true);
            Directory.Delete(path);
        }
    }
}
