using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #1981 — the provider reads every host <see cref="ApiFactory"/> builds resolves, in place of the configured sources, so
/// no test reaches the provider whatever a gitignored <c>appsettings.Local.json</c> says. Every read answers "not
/// configured" unless a test scripts it: a scope on this singleton rather than a derived host, for the reasons
/// <see cref="RecordingEmailSender.Incapable"/> gives. The allowance, the delivery statistics and the recent failures
/// are scripted apart, and each scope restores only its own reads.
/// </summary>
internal sealed class ScriptedEmailProviderReads
    : IEmailProviderUsageSource, IEmailDeliveryStatisticsSource, IEmailRecentFailuresSource
{
    private static readonly Allowance NoAllowance =
        new(new EmailProviderUsageRead.NotConfigured(), new EmailProviderPlanRead.NotConfigured());

    private static readonly Statistics NoStatistics =
        new(new EmailDeliveryStatisticsRead.NotConfigured(), new EmailDeliveryStatisticsRead.NotConfigured());

    private static readonly EmailRecentFailuresRead NoFailures = new EmailRecentFailuresRead.NotConfigured();

    private Allowance _allowance = NoAllowance;
    private Statistics _statistics = NoStatistics;
    private EmailRecentFailuresRead _failures = NoFailures;

    public ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _allowance).Usage);

    public ValueTask<EmailProviderPlanRead> ReadPlanAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _allowance).Plan);

    public ValueTask<EmailDeliveryStatisticsRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _statistics).Period);

    public ValueTask<EmailDeliveryStatisticsRead> ReadSentinelAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _statistics).Sentinel);

    ValueTask<EmailRecentFailuresRead> IEmailRecentFailuresSource.ReadAsync(
        EmailDeliveryPeriod period, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _failures));

    /// <summary>Both allowance reads answer as scripted until the returned scope is disposed, which restores the answers before it.</summary>
    internal IDisposable Script(EmailProviderUsageRead usage, EmailProviderPlanRead plan)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(plan);

        var previous = Interlocked.Exchange(ref _allowance, new Allowance(usage, plan));
        return new Scope(() => Volatile.Write(ref _allowance, previous));
    }

    /// <summary>Every period's statistics and the sentinel answer as scripted until the returned scope is disposed.</summary>
    internal IDisposable ScriptStatistics(EmailDeliveryStatisticsRead period, EmailDeliveryStatisticsRead sentinel)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(sentinel);

        var previous = Interlocked.Exchange(ref _statistics, new Statistics(period, sentinel));
        return new Scope(() => Volatile.Write(ref _statistics, previous));
    }

    /// <summary>Every period's failures answer as scripted until the returned scope is disposed.</summary>
    internal IDisposable ScriptFailures(EmailRecentFailuresRead failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var previous = Interlocked.Exchange(ref _failures, failures);
        return new Scope(() => Volatile.Write(ref _failures, previous));
    }

    private sealed record Allowance(EmailProviderUsageRead Usage, EmailProviderPlanRead Plan);

    private sealed record Statistics(EmailDeliveryStatisticsRead Period, EmailDeliveryStatisticsRead Sentinel);

    private sealed class Scope(Action restore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                restore();
            }
        }
    }
}
