using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Notifications;
using Jobbliggaren.Application.Matching.Notifications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;

/// <summary>
/// The send phase of <see cref="BackgroundMatchingJob"/> (ADR 0161, #2106): the only sender of a
/// Toppmatch mail. The committed Pending Top rows are its queue. Each run it mails every due recipient
/// once, bundling that recipient's due rows, oldest recipient first, as far as the
/// <see cref="MailPacingRule"/> it is given admits. A refused or unreached row stays Pending for the next
/// run. Nothing here waits.
/// <para>
/// <b>Exactly one claimer of Top rows, under one lock.</b> <c>user_job_ad_matches</c> has no concurrency
/// token, so the claim is an unconditional update: two claimers running at once would both read a row
/// as Pending and both send it. This class runs only inside the job's run, under
/// <c>BackgroundMatchingWorker</c>'s <c>DisableConcurrentExecution</c>, and the digest claims Strong
/// rows only. A second sender of Top rows must share that lock or claim with a conditional update.
/// </para>
/// <para>
/// <b>Due</b> means Top, Pending, an Active ad, and both the ad and the row younger than
/// <see cref="DirectMailWindow"/>. Measuring from the ad's own <c>CreatedAt</c> keeps a fresh row for an
/// old ad from being mailed as new (security-auditor V2). Measuring the row as well keeps this set
/// disjoint from the stranded-match reaper's, which only touches rows older than 48 hours.
/// </para>
/// <para>
/// <b>Per recipient:</b> claim the rows and commit, check consent again on the live row (a withdrawal or
/// an account deletion committed after the claim stops the send: Art. 7(3), Art. 17, #533), read the
/// address, send one mail, then mark the rows Sent and commit. A row whose send did not complete stays
/// Queued and is never re-sent; the reaper fails it later. The first failure ends the phase, so a
/// provider outage costs one recipient's rows per run, not every recipient's.
/// </para>
/// </summary>
public sealed partial class TopMatchMailDispatcher(
    IServiceScopeFactory scopeFactory,
    IEmailSender emailSender,
    IDateTimeProvider clock,
    ILogger<TopMatchMailDispatcher> logger)
{
    /// <summary>
    /// How old an ad and its match may be and still be mailed directly. Older Top matches are shown in
    /// the app only. Kept below the reaper's 48 hours.
    /// </summary>
    public static readonly TimeSpan DirectMailWindow = TimeSpan.FromHours(24);

    /// <summary>The most matches one mail lists. A recipient's later rows go in the next run's mail.</summary>
    public const int MaxItemsPerMail = 20;

    /// <summary>
    /// How long the phase starts new recipients. A send already started may run past it. Together with
    /// the scan's budget it keeps one run shorter than the cron interval.
    /// </summary>
    public static readonly TimeSpan SendBudget = TimeSpan.FromMinutes(1);

    /// <summary>Mails every due recipient the rule admits. Returns the number of mails sent.</summary>
    public async Task<int> DispatchAsync(MailPacingRule rule, CancellationToken ct)
    {
        var started = clock.UtcNow;
        var cutoff = started - DirectMailWindow;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var userAccounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        // The claim set and the display set are one query: every claimed row carries the title and
        // company it is listed with, so the mail lists exactly the rows it claims (#864). Consent is
        // applied here as well, so a row of a user who is not consenting is never claimed.
        var consenting = db.JobSeekers
            .Where(NotificationConsent.BackgroundMatch.Criteria)
            .Select(seeker => seeker.UserId);
        var due = await (
                from match in db.UserJobAdMatches
                join ad in db.JobAds on match.JobAdId equals ad.Id
                where match.Grade == NotifiableMatchGrade.Top
                      && match.NotificationStatus == NotificationStatus.Pending
                      && match.CreatedAt >= cutoff
                      && ad.Status == JobAdStatus.Active
                      && ad.CreatedAt >= cutoff
                      && consenting.Contains(match.UserId)
                orderby match.CreatedAt, match.Id
                select new DueRow(match, ad.Title, ad.Company.Name))
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            LogNothingDue(logger);
            return 0;
        }

        // Oldest waiting recipient first, so the per-run cap never starves the same recipient twice.
        var recipients = due
            .GroupBy(row => row.Match.UserId)
            .Select(rows => rows.Take(MaxItemsPerMail).ToList())
            .ToList();

        var recipientIds = recipients.Select(rows => rows[0].Match.UserId).ToList();
        var lastMailAt = await db.UserJobAdMatches
            .Where(match => match.Grade == NotifiableMatchGrade.Top
                            && match.SentAt != null
                            && recipientIds.Contains(match.UserId))
            .GroupBy(match => match.UserId)
            .Select(sent => new { UserId = sent.Key, Last = sent.Max(match => match.SentAt) })
            .ToDictionaryAsync(sent => sent.UserId, sent => sent.Last, ct);

        var pacing = new MailPacingRun(rule);
        var mailed = 0;
        foreach (var rows in recipients)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.UtcNow - started >= SendBudget)
            {
                LogBudgetSpent(logger, mailed);
                break;
            }

            var userId = rows[0].Match.UserId;
            var decision = pacing.Decide(lastMailAt.GetValueOrDefault(userId), clock.UtcNow);
            if (decision == MailPacingDecision.RunFull)
            {
                LogRunFull(logger, mailed);
                break;
            }

            if (decision == MailPacingDecision.RecipientTooSoon)
                continue;

            try
            {
                if (await SendToRecipientAsync(db, userAccounts, userId, rows, ct))
                    mailed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSendFailed(logger, ex, userId);
                break;
            }
        }

        LogDone(logger, mailed, due.Count);
        return mailed;
    }

    private async Task<bool> SendToRecipientAsync(
        IAppDbContext db,
        IUserAccountService userAccounts,
        Guid userId,
        IReadOnlyList<DueRow> rows,
        CancellationToken ct)
    {
        var claimed = new List<DueRow>();
        foreach (var row in rows)
        {
            if (row.Match.MarkQueued().IsSuccess)
                claimed.Add(row);
        }

        if (claimed.Count == 0)
            return false;
        await db.SaveChangesAsync(ct);

        // The deletion and withdrawal fence: read on the live row through the JobSeeker query filter,
        // after the claim has committed and before the send.
        if (!await db.JobSeekers
                .Where(NotificationConsent.BackgroundMatch.Criteria)
                .AnyAsync(seeker => seeker.UserId == userId, ct))
        {
            LogConsentGone(logger, userId);
            return false;
        }

        var toEmail = await userAccounts.GetEmailAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            LogNoEmail(logger, userId);
            return false;
        }

        var label = NotifiableMatchGrade.Top.ToSwedishLabel();
        var items = claimed
            .Select(row => new MatchNotificationItem(row.Match.JobAdId.Value, row.Title, row.Company, label))
            .ToList();
        // No idempotency key, by design (ADR 0124): the rows are Queued before the send, so no later run
        // sends them again, and the Scaleway arm has no transport retry within a send.
        await emailSender.SendMatchNotificationEmailAsync(
            toEmail, new MatchNotificationEmail(MatchNotificationKind.Direct, null, items, items.Count), ct);

        foreach (var row in claimed)
            row.Match.MarkSent(clock);
        await db.SaveChangesAsync(ct);
        LogSent(logger, userId, items.Count);
        return true;
    }

    private sealed record DueRow(UserJobAdMatch Match, string Title, string Company);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "TopMatchMailDispatcher: no Top match is due")]
    private static partial void LogNothingDue(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TopMatchMailDispatcher: {Mailed} mails sent, {Due} rows were due")]
    private static partial void LogDone(ILogger logger, int mailed, int due);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TopMatchMailDispatcher: Top mail sent to user {UserId} with {Count} matches")]
    private static partial void LogSent(ILogger logger, Guid userId, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TopMatchMailDispatcher: the run's mail cap is reached after {Mailed} mails; the rest wait for the next run")]
    private static partial void LogRunFull(ILogger logger, int mailed);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "TopMatchMailDispatcher: the run budget is spent after {Mailed} mails; the rest wait for the next run")]
    private static partial void LogBudgetSpent(ILogger logger, int mailed);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TopMatchMailDispatcher: user {UserId} no longer consents after the claim; rows left Queued")]
    private static partial void LogConsentGone(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TopMatchMailDispatcher: user {UserId} has no account email; rows left Queued")]
    private static partial void LogNoEmail(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "TopMatchMailDispatcher: the send for user {UserId} failed; its rows stay Queued and the phase stops")]
    private static partial void LogSendFailed(ILogger logger, Exception ex, Guid userId);
}
