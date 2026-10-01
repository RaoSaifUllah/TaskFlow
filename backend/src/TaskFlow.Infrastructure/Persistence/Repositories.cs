using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Persistence;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class TaskRepository(TaskFlowDbContext db) : ITaskRepository
{
    public Task<TaskItem?> FindAsync(Guid workspaceId, Guid taskId, CancellationToken cancellationToken) =>
        db.Tasks.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == taskId && x.DeletedAt == null, cancellationToken);
    public void Add(TaskItem task) => db.Tasks.Add(task);
}
public sealed class UnitOfWork(TaskFlowDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await action(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
