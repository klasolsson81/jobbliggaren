using System.Globalization;
using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>The windows the statistics memo holds one read for: the three periods and the canary.</summary>
internal enum StatisticsWindow
{
    Last24Hours,
    Last3Days,
    Last7Days,
    Sentinel,
}

internal static class StatisticsWindows
{
    internal static StatisticsWindow Of(EmailDeliveryPeriod period) => period switch
    {
        EmailDeliveryPeriod.Last24Hours => StatisticsWindow.Last24Hours,
        EmailDeliveryPeriod.Last3Days => StatisticsWindow.Last3Days,
        EmailDeliveryPeriod.Last7Days => StatisticsWindow.Last7Days,
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Not a delivery period."),
    };

    internal static TimeSpan LengthOf(StatisticsWindow window) => window switch
    {
        StatisticsWindow.Last24Hours => EmailDeliveryPeriods.LengthOf(EmailDeliveryPeriod.Last24Hours),
        StatisticsWindow.Last3Days => EmailDeliveryPeriods.LengthOf(EmailDeliveryPeriod.Last3Days),
        StatisticsWindow.Last7Days => EmailDeliveryPeriods.LengthOf(EmailDeliveryPeriod.Last7Days),
        StatisticsWindow.Sentinel => EmailDeliveryLimits.SentinelWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Not a statistics window."),
    };
}

/// <summary>
/// The <c>since</c> both delivery reads send: the window's start, computed here from the clock and a closed period,
/// never from anything the browser sent, in whole UTC seconds so the instant sent is the instant checked against.
/// </summary>
internal static class ScalewaySince
{
    internal const string Parameter = "since";

    internal static DateTimeOffset Of(DateTimeOffset now, TimeSpan length)
    {
        var start = (now - length).ToUniversalTime();
        return start.AddTicks(-(start.Ticks % TimeSpan.TicksPerSecond));
    }

    internal static string Format(DateTimeOffset since) =>
        since.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>The statistics adapter behind its memo. Infrastructure-internal: Application sees only <see cref="IEmailDeliveryStatisticsSource"/>.</summary>
internal interface IDeliveryStatisticsAttemptSource
{
    /// <summary>Never throws for a refusal, a timeout or an unreadable body; those are reasons on the result.</summary>
    ValueTask<ProviderAttempt<EmailDeliveryStatisticsRead>> AttemptAsync(StatisticsWindow window, CancellationToken cancellationToken);
}

/// <summary>The recent-failures adapter behind its memo. Infrastructure-internal: Application sees only <see cref="IEmailRecentFailuresSource"/>.</summary>
internal interface IRecentFailuresAttemptSource
{
    /// <inheritdoc cref="IDeliveryStatisticsAttemptSource.AttemptAsync"/>
    ValueTask<ProviderAttempt<EmailRecentFailuresRead>> AttemptAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken);
}
