using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TaskFlowDbContext>
{
    public TaskFlowDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TaskFlowDbContext>()
        .UseNpgsql(DatabaseConnections.Normalize(Environment.GetEnvironmentVariable("TASKFLOW_MIGRATION_CONNECTION") ??
            "Host=localhost;Database=taskflow;Username=taskflow_migrator")).Options);
}
