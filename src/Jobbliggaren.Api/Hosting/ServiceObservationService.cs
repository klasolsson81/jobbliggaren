using Jobbliggaren.Application.Admin.ServiceObservations;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Calls the services sampler on <see cref="SamplerLoop"/> for the lifetime of the API (#1982, ADR 0159): once at
/// once, then every <c>SampleIntervalSeconds</c>. It reads the Worker's own heartbeat, so it cannot run in the Worker.
/// </summary>
public sealed partial class ServiceObservationService(
    IServiceObservationSampler sampler,
    IOptions<ServiceObservationOptions> options,
    ILogger<ServiceObservationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await SamplerLoop.RunAsync(
            TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds), TickAsync, stoppingToken);

    internal Task TickAsync(CancellationToken stoppingToken) =>
        SamplerLoop.GuardAsync(
            sampler.SampleAsync, exception => LogTickFailed(logger, exception.GetType().Name), stoppingToken);

    [LoggerMessage(EventId = 6232, Level = LogLevel.Warning,
        Message = "ServiceObservationService: unexpected tick failure ({ExceptionType}) — sample skipped, host continues.")]
    private static partial void LogTickFailed(ILogger logger, string exceptionType);
}
