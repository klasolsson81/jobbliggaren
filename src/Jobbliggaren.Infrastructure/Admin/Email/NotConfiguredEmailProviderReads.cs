using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Every provider read when <c>Email:Provider</c> is not Scaleway: dev, CI and the test hosts. There is no provider to
/// ask, so each answer is a state and never a failure, and nothing is read. One instance answers every read port.
/// </summary>
internal sealed class NotConfiguredEmailProviderReads
    : IEmailProviderUsageSource, IEmailDeliveryStatisticsSource, IEmailRecentFailuresSource
{
    private static readonly EmailProviderUsageRead NoUsage = new EmailProviderUsageRead.NotConfigured();
    private static readonly EmailProviderPlanRead NoPlan = new EmailProviderPlanRead.NotConfigured();
    private static readonly EmailDeliveryStatisticsRead NoStatistics = new EmailDeliveryStatisticsRead.NotConfigured();
    private static readonly EmailRecentFailuresRead NoFailures = new EmailRecentFailuresRead.NotConfigured();

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
