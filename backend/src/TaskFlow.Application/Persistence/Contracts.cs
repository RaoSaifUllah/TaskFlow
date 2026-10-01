using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Persistence;

public interface ITaskRepository
{
    Task<TaskItem?> FindAsync(Guid workspaceId, Guid taskId, CancellationToken cancellationToken);
    void Add(TaskItem task);
}
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
public sealed record TaskSummary(Guid Id, string Title, long Version, DateOnly? DueDate);
public interface ITaskReadService
{
    Task<IReadOnlyList<TaskSummary>> ListAsync(Guid workspaceId, Guid userId, Guid? after, int limit, CancellationToken cancellationToken);
}
