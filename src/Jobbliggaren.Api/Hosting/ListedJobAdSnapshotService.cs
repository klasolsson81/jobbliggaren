using Jobbliggaren.Application.JobAds.Listing;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Reads the listed set on <see cref="SamplerLoop"/> for the lifetime of the API (ADR 0174): once at once, so a release
/// can answer the sitemap as soon as the first read completes, then every
/// <see cref="ListedJobAdSnapshotLimits.SampleInterval"/>.
/// </summary>
public sealed partial class ListedJobAdSnapshotService(
    IListedJobAdSnapshotSampler sampler,
    ILogger<ListedJobAdSnapshotService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await SamplerLoop.RunAsync(ListedJobAdSnapshotLimits.SampleInterval, TickAsync, stoppingToken);

    internal Task TickAsync(CancellationToken stoppingToken) =>
        SamplerLoop.GuardAsync(
            sampler.SampleAsync, exception => LogTickFailed(logger, exception.GetType().Name), stoppingToken);

    [LoggerMessage(EventId = 6233, Level = LogLevel.Warning,
        Message = "ListedJobAdSnapshotService: a read of the listed set failed ({ExceptionType}); the previous snapshot is kept as it was, host continues.")]
    private static partial void LogTickFailed(ILogger logger, string exceptionType);
}
