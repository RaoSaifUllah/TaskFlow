using Dapper;
using Npgsql;
using TaskFlow.Application.Workspaces;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Workspaces;

public sealed class WorkspaceReadService(string connectionString) : IWorkspaceReadService
{
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(Guid userId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnections.Normalize(connectionString));
        return (await connection.QueryAsync<WorkspaceSummary>(new CommandDefinition("""
            SELECT w."Id", w."Name", m."Role", w."Version" FROM "Workspaces" w
            JOIN "Memberships" m ON m."WorkspaceId" = w."Id"
            WHERE m."UserId" = @userId AND m."Active" AND w."DeletedAt" IS NULL ORDER BY w."Id" LIMIT 100
            """, new { userId }, cancellationToken: ct))).AsList();
    }
    public async Task<IReadOnlyList<MemberSummary>> MembersAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnections.Normalize(connectionString));
        return (await connection.QueryAsync<MemberSummary>(new CommandDefinition("""
            SELECT m."UserId", d."Email", m."Role", m."Active" FROM "Memberships" m
            JOIN "AccountDirectory" d ON d."Id" = m."UserId"
            WHERE m."WorkspaceId" = @workspaceId AND EXISTS (
                SELECT 1 FROM "Memberships" caller WHERE caller."WorkspaceId" = @workspaceId
                    AND caller."UserId" = @userId AND caller."Active" AND caller."Role" <> 3)
            ORDER BY m."UserId" LIMIT 100
            """, new { workspaceId, userId }, cancellationToken: ct))).AsList();
    }
}
