using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Admin.Email.Jobs.EmailSendCountRetention;

/// <summary>
/// #1981 PR 3 (ADR 0180) — deletes our own send counts the longest period can no longer read. The first date kept is
/// <see cref="EmailSendCountDates.FirstDateOf"/> of the seven-day period, the same rule the read uses, so the deletion
/// never takes a date a read still needs. At the scheduled run that keeps today and the seven dates before it, so a
/// count lives at most eight Swedish dates plus the time to the next successful scheduled run. A failed run leaves the
/// rows for the next one. Logs a count only.
/// </summary>
public sealed partial class EmailSendCountRetentionJob(
    IEmailSendCountRetention retention,
    IDateTimeProvider clock,
    ISwedishCalendar calendar,
    ILogger<EmailSendCountRetentionJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var firstKept = EmailSendCountDates.FirstDateOf(EmailDeliveryPeriod.Last7Days, clock.UtcNow, calendar);
        var deleted = await retention.DeleteBeforeAsync(firstKept, cancellationToken);
        LogDeleted(deleted, firstKept);
    }

    [LoggerMessage(3110, LogLevel.Information,
        "[EmailSendCountRetention] Deleted {Deleted} send counts dated before {FirstKept}")]
    private partial void LogDeleted(int deleted, DateOnly firstKept);
}
