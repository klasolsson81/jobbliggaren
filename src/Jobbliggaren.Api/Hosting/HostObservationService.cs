using Jobbliggaren.Application.Admin.HostObservations;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Calls the host sampler on <see cref="SamplerLoop"/> for the lifetime of the API (#1982): once at once, so memory
/// and disk are known within the first second of a release, then every <c>SampleIntervalSeconds</c>.
/// </summary>
public sealed partial class HostObservationService(
    IHostObservationSampler sampler,
    IOptions<HostObservationOptions> options,
    ILogger<HostObservationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await SamplerLoop.RunAsync(
            TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds),
            token =>
            {
                Tick(token);
                return Task.CompletedTask;
            },
            stoppingToken);

    internal void Tick(CancellationToken stoppingToken) =>
        SamplerLoop.Guard(sampler.Sample, exception => LogTickFailed(logger, exception), stoppingToken);

    [LoggerMessage(EventId = 6224, Level = LogLevel.Warning,
        Message = "HostObservationService: unexpected tick failure — sample skipped, host continues.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
