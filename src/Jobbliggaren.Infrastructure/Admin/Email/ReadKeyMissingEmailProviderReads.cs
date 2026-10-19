using Jobbliggaren.Application.Admin.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Every provider read when the deployment sends through Scaleway but holds no read key
/// (<c>Email:ScalewayRead:SecretKey</c> absent): an operator step owed, which registration alone decides, so no adapter
/// or memo ever produces this state. Nothing is read, and the sending key is never used in its place. One instance
/// answers every read port.
/// </summary>
internal sealed class ReadKeyMissingEmailProviderReads
    : IEmailProviderUsageSource, IEmailDeliveryStatisticsSource, IEmailRecentFailuresSource
{
    private static readonly EmailProviderUsageRead NoUsage = new EmailProviderUsageRead.ReadKeyMissing();
    private static readonly EmailProviderPlanRead NoPlan = new EmailProviderPlanRead.ReadKeyMissing();
    private static readonly EmailDeliveryStatisticsRead NoStatistics = new EmailDeliveryStatisticsRead.ReadKeyMissing();
    private static readonly EmailRecentFailuresRead NoFailures = new EmailRecentFailuresRead.ReadKeyMissing();

    public ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoUsage);

    public ValueTask<EmailProviderPlanRead> ReadPlanAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoPlan);

    public ValueTask<EmailDeliveryStatisticsRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoStatistics);

    public ValueTask<EmailDeliveryStatisticsRead> ReadSentinelAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoStatistics);

    ValueTask<EmailRecentFailuresRead> IEmailRecentFailuresSource.ReadAsync(
        EmailDeliveryPeriod period, CancellationToken cancellationToken) =>
        ValueTask.FromResult(NoFailures);
}

/// <summary>
/// Writes one line at startup when the read key is missing, so a dropped <c>.env</c> pointer shows in the API's journal
/// and not only on the page. It names the configuration key, never a value.
/// </summary>
internal sealed partial class ReadKeyMissingNotice(ILogger<ReadKeyMissingNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogReadKeyMissing(ScalewayReadSelection.ReadKeyName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(3021, LogLevel.Information,
        "[ReadKeyMissingNotice] Email:Provider is Scaleway and {ReadKey} is not set, so the provider reads answer ReadKeyMissing and nothing is read")]
    private partial void LogReadKeyMissing(string readKey);
}
