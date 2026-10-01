using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ")))
            Skip = "Real PostgreSQL fixture required: TASKFLOW_TEST_WRITE and TASKFLOW_TEST_READ. No SQLite substitution.";
    }
}

public sealed class FoundationTests
{
    [PostgreSqlFact]
    public async Task ConnectivityAndRoleBoundary()
    {
        await using var migration = new NpgsqlConnection(DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE")!));
        await migration.OpenAsync();
        await using var probe = new NpgsqlCommand("SELECT current_setting('server_version_num')::int >= 160000", migration);
        Assert.True((bool)(await probe.ExecuteScalarAsync())!);
        await using var read = new NpgsqlConnection(DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ")!));
        await read.OpenAsync();
        await using var privileges = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolcreatedb OR rolcreaterole))
                OR has_schema_privilege(current_user, 'public', 'CREATE')
                OR EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND
                    (has_table_privilege(current_user, quote_ident(schemaname) || '.' || quote_ident(tablename), 'INSERT')
                    OR has_table_privilege(current_user, quote_ident(schemaname) || '.' || quote_ident(tablename), 'UPDATE')
                    OR has_table_privilege(current_user, quote_ident(schemaname) || '.' || quote_ident(tablename), 'DELETE')))
            """, read);
        Assert.False((bool)(await privileges.ExecuteScalarAsync())!, "Read connection must use a separate SELECT-only role without CREATE or role/database administration privileges.");
    }
    [PostgreSqlFact]
    public async Task MigrationsIsolationConcurrencyAndRestrictedReads()
    {
        var write = DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE")!);
        var read = DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ")!);
        var options = new DbContextOptionsBuilder<TaskFlowDbContext>().UseNpgsql(write).Options;
        await using var db = new TaskFlowDbContext(options);
        await db.Database.MigrateAsync();
        var workspace = new Workspace("Test " + Guid.NewGuid());
        var other = new Workspace("Other " + Guid.NewGuid());
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        db.AddRange(workspace, other,
            new Membership { WorkspaceId = workspace.Id, UserId = owner, Role = WorkspaceRole.Owner },
            new Membership { WorkspaceId = workspace.Id, UserId = member, Role = WorkspaceRole.Member },
            new Membership { WorkspaceId = other.Id, UserId = owner, Role = WorkspaceRole.Owner });
        var project = new Project { WorkspaceId = workspace.Id, CreatorId = owner, Name = "Private" };
        var privateTask = new TaskItem(workspace.Id, owner, "Private", project.Id);
        var inbox = new TaskItem(workspace.Id, member, "Private inbox");
        inbox.SetDate(new DateOnly(2026, 10, 1));
        db.AddRange(project, privateTask, inbox);
        await db.SaveChangesAsync();
        var reads = new TaskReadService(read);
        var memberTasks = await reads.ListAsync(workspace.Id, member, null, 100, default);
        Assert.Single(memberTasks);
        Assert.Equal(inbox.Id, memberTasks[0].Id);
        Assert.Equal(new DateOnly(2026, 10, 1), memberTasks[0].DueDate);
        var ownerTasks = await reads.ListAsync(workspace.Id, owner, null, 100, default);
        Assert.Single(ownerTasks);
        Assert.Equal(privateTask.Id, ownerTasks[0].Id);
        Assert.Empty(await reads.ListAsync(other.Id, member, null, 100, default));
        await using var stale = new TaskFlowDbContext(options);
        var copy = await stale.Tasks.SingleAsync(x => x.Id == inbox.Id);
        inbox.Rename("Fresh", inbox.Version);
        await db.SaveChangesAsync();
        copy.Rename("Stale", copy.Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        db.Tasks.Add(new TaskItem(other.Id, owner, "Cross-tenant", project.Id));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)error.InnerException!).SqlState);
        await using var restricted = new NpgsqlConnection(read);
        await restricted.OpenAsync();
        await using var forbidden = new NpgsqlCommand("DELETE FROM \"Tasks\" WHERE false", restricted);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }
    [Fact]
    public void PostgreSqlUrlIsVerifiedAndParsingErrorsAreRedacted()
    {
        var normalized = DatabaseConnections.Normalize("postgresql://test:synthetic%3Bpassword@example.invalid/db?sslmode=require&channel_binding=require");
        var parsed = new NpgsqlConnectionStringBuilder(normalized);
        Assert.Equal(SslMode.VerifyFull, parsed.SslMode);
        Assert.Equal(ChannelBinding.Require, parsed.ChannelBinding);
        Assert.Equal("synthetic;password", parsed.Password);
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseConnections.Normalize("Secret-that-must-not-appear=credential"));
        Assert.DoesNotContain("credential", error.ToString());
        Assert.DoesNotContain("Secret-that-must-not-appear", error.ToString());
        Assert.Null(error.InnerException);
    }
    [Fact]
    public void ModelHasTenantForeignKeysAndDueConstraints()
    {
        using var db = new DesignTimeFactory().CreateDbContext([]);
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("CK_Task_Container", sql);
        Assert.Contains("CK_Task_Due", sql);
        Assert.Contains("FOREIGN KEY (\"WorkspaceId\", \"ProjectId\")", sql);
        Assert.Contains("REFERENCES \"Projects\" (\"WorkspaceId\", \"Id\")", sql);
    }
    [Fact]
    public void InnerLayersHaveNoInfrastructureDependency()
    {
        var domain = typeof(TaskItem).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        var application = typeof(TaskFlow.Application.Persistence.ITaskRepository).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(domain, x => x!.Contains("EntityFramework") || x.Contains("AspNetCore") || x.Contains("Infrastructure"));
        Assert.DoesNotContain(application, x => x!.Contains("EntityFramework") || x.Contains("Infrastructure"));
    }
}
