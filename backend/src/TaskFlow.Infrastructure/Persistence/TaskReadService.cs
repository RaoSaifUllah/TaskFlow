using Dapper;
using System.Data;
using Npgsql;
using TaskFlow.Application.Persistence;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class TaskReadService(string readConnectionString) : ITaskReadService
{
    static TaskReadService() => SqlMapper.AddTypeHandler(new DateOnlyHandler());
    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly date => date,
            DateTime date => DateOnly.FromDateTime(date),
            _ => throw new DataException("Unexpected PostgreSQL date representation.")
        };
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }
    public async Task<IReadOnlyList<TaskSummary>> ListAsync(Guid workspaceId, Guid userId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = new NpgsqlConnection(DatabaseConnections.Normalize(readConnectionString));
        var result = await connection.QueryAsync<TaskSummary>(new CommandDefinition("""
            SELECT t."Id", t."Title", t."Version", t."DueDate"
            FROM "Tasks" t
            JOIN "Memberships" m ON m."WorkspaceId" = t."WorkspaceId" AND m."UserId" = @userId AND m."Active"
            LEFT JOIN "Projects" p ON p."WorkspaceId" = t."WorkspaceId" AND p."Id" = t."ProjectId"
            WHERE t."WorkspaceId" = @workspaceId AND t."DeletedAt" IS NULL
              AND (CAST(@after AS uuid) IS NULL OR t."Id" > CAST(@after AS uuid))
              AND ((t."InboxOwnerId" = @userId AND m."Role" <> 3)
                OR (t."ProjectId" IS NOT NULL AND (
                  (m."Role" <> 3 AND (m."Role" IN (0,1) OR p."CreatorId" = @userId))
                  OR EXISTS (SELECT 1 FROM "ProjectShares" s WHERE s."WorkspaceId" = t."WorkspaceId"
                    AND s."ProjectId" = t."ProjectId" AND s."UserId" = @userId))))
            ORDER BY t."Id" LIMIT @limit
            """, new { workspaceId, userId, after, limit }, cancellationToken: cancellationToken));
        return result.AsList();
    }
}
