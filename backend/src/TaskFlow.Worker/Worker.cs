namespace TaskFlow.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TaskFlow foundation worker started; notification delivery is not implemented");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
