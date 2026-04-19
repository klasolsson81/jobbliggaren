namespace JobbPilot.Worker;

public partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            LogHeartbeat(DateTimeOffset.Now);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Worker heartbeat at {Time}")]
    private partial void LogHeartbeat(DateTimeOffset time);
}
