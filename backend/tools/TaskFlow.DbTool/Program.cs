using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TaskFlow.Infrastructure.Persistence;

var settingsPath = args.Length > 0 ? args[0] : "backend/src/TaskFlow.Api/appsettings.Development.json";
var mode = args.Length > 1 ? args[1] : "probe";
try
{
    var settings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!;
    var strings = settings["ConnectionStrings"]!;
    var configured = strings["Migration"]?.GetValue<string>() ?? strings["Write"]!.GetValue<string>();
    var owner = new NpgsqlConnectionStringBuilder(DatabaseConnections.Normalize(configured));
    if (owner.Host?.EndsWith(".neon.tech", StringComparison.OrdinalIgnoreCase) == true)
        owner.Host = owner.Host.Replace("-pooler.", ".", StringComparison.Ordinal);
    await using var connection = new NpgsqlConnection(owner.ConnectionString);
    await connection.OpenAsync();
    if (mode == "probe")
    {
        await using var query = new NpgsqlCommand("SELECT count(*) FROM pg_tables WHERE schemaname = 'public'", connection);
        Console.WriteLine($"Connected. Public table count: {await query.ExecuteScalarAsync()}. No credentials printed.");
        return;
    }
    if (mode == "refresh-grants")
    {
        await MigrateAndGrantAsync(owner.ConnectionString, settings["Provisioning"]!["ReadRole"]!.GetValue<string>(), settings["Provisioning"]!["WriteRole"]!.GetValue<string>());
        Console.WriteLine("Reviewed runtime grants refreshed.");
        return;
    }
    if (mode == "verify-test-migrations")
    {
        var testName = settings["Provisioning"]?["TestDatabase"]?.GetValue<string>();
        var test = new NpgsqlConnectionStringBuilder(DatabaseConnections.Normalize(strings["TestMigration"]!.GetValue<string>()));
        if (testName is null || !System.Text.RegularExpressions.Regex.IsMatch(testName, "^taskflow_test_[a-f0-9]{12}$") ||
            test.Database != testName || test.Host != owner.Host || test.Database == owner.Database)
            throw new InvalidOperationException("Refusing rollback outside the provisioned isolated test database.");
        await using var testContext = new TaskFlowDbContext(new DbContextOptionsBuilder<TaskFlowDbContext>().UseNpgsql(test.ConnectionString).Options);
        await testContext.Database.MigrateAsync();
        await testContext.GetService<IMigrator>().MigrateAsync("0");
        await MigrateAndGrantAsync(test.ConnectionString, settings["Provisioning"]!["TestReadRole"]!.GetValue<string>(), null);
        Console.WriteLine("Migration up/down/up passed on the provisioned isolated test database; read grants restored.");
        return;
    }
    if (mode != "initialize") throw new InvalidOperationException("Supported modes: probe, initialize, verify-test-migrations.");
    if (settings["Provisioning"] is not null) throw new InvalidOperationException("Provisioning record already exists; refusing duplicate resources.");
    var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
    var readRole = "taskflow_read_" + suffix;
    var writeRole = "taskflow_write_" + suffix;
    var testReadRole = "taskflow_testread_" + suffix;
    var testDatabase = "taskflow_test_" + suffix;
    var readPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var writePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var testPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    strings["Migration"] = owner.ConnectionString;
    settings["Provisioning"] = new JsonObject { ["TestDatabase"] = testDatabase, ["ReadRole"] = readRole, ["WriteRole"] = writeRole, ["TestReadRole"] = testReadRole, ["State"] = "planned" };
    strings["Read"] = ForRole(owner, readRole, readPassword);
    strings["Write"] = ForRole(owner, writeRole, writePassword);
    var testOwner = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Database = testDatabase };
    strings["TestMigration"] = testOwner.ConnectionString;
    strings["TestRead"] = ForRole(testOwner, testReadRole, testPassword);
    await SaveAsync(settingsPath, settings);
    await CreateRoleAsync(connection, readRole, readPassword);
    await CreateRoleAsync(connection, writeRole, writePassword);
    await CreateRoleAsync(connection, testReadRole, testPassword);
    await ExecuteAsync(connection, $"CREATE DATABASE {Quote(testDatabase)}");
    await MigrateAndGrantAsync(owner.ConnectionString, readRole, writeRole);
    await MigrateAndGrantAsync(testOwner.ConnectionString, testReadRole, null);
    settings["Provisioning"]!["State"] = "initialized";
    await SaveAsync(settingsPath, settings);
    Console.WriteLine("Runtime migration and restricted roles initialized; isolated test database ready. Credentials saved only in ignored settings.");
}
catch (Exception error)
{
    Console.Error.WriteLine(error is PostgresException postgres
        ? $"Database operation failed (SQLSTATE {postgres.SqlState}). No connection or SQL text logged."
        : $"Database operation failed ({error.GetType().Name}). No credentials logged.");
    Environment.ExitCode = 1;
}

static string Quote(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
static string ForRole(NpgsqlConnectionStringBuilder original, string role, string password) =>
    new NpgsqlConnectionStringBuilder(original.ConnectionString) { Username = role, Password = password }.ConnectionString;
static async Task SaveAsync(string path, JsonNode node) => await File.WriteAllTextAsync(path,
    node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}
static Task CreateRoleAsync(NpgsqlConnection connection, string role, string password) =>
    ExecuteAsync(connection, $"CREATE ROLE {Quote(role)} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT");
static async Task MigrateAndGrantAsync(string connectionString, string readRole, string? writeRole)
{
    await using var db = new TaskFlowDbContext(new DbContextOptionsBuilder<TaskFlowDbContext>().UseNpgsql(connectionString).Options);
    await db.Database.MigrateAsync();
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
    await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {Quote(database)} TO {Quote(readRole)}");
    await ExecuteAsync(connection, "REVOKE ALL ON SCHEMA public FROM PUBLIC");
    await ExecuteAsync(connection, $"GRANT USAGE ON SCHEMA public TO {Quote(readRole)}");
    var tables = string.Join(", ", new[] { "Workspaces", "Memberships", "Projects", "ProjectShares", "Tasks" }.Select(Quote));
    await ExecuteAsync(connection, $"GRANT SELECT ON {tables} TO {Quote(readRole)}");
    if (writeRole is not null)
    {
        await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {Quote(database)} TO {Quote(writeRole)}");
        await ExecuteAsync(connection, $"GRANT USAGE ON SCHEMA public TO {Quote(writeRole)}");
        var writeTables = tables + ", " + string.Join(", ", new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims", "RefreshSessions", "RefreshTokens", "RecoveryCodes" }.Select(Quote));
        await ExecuteAsync(connection, $"GRANT SELECT, INSERT, UPDATE, DELETE ON {writeTables} TO {Quote(writeRole)}");
    }
}
