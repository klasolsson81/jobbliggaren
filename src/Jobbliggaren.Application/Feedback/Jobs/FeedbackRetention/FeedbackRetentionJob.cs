using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;

/// <summary>
/// Deletes feedback 90 days after it was sent, with its screenshot and operator notice (Klas, 2026-10-07). A
/// constant rather than an option, because the privacy policy states the period. The per-page
/// prompt suppressions are kept: they live as long as the account, so the prompt never returns
/// on its own. Logs counts only, like <c>ParsedResumeRetentionJob</c>.
/// </summary>
public sealed partial class FeedbackRetentionJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILogger<FeedbackRetentionJob> logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow - Retention;

        var screenshots = await db.FeedbackScreenshots
            .Where(s => s.SubmittedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var notices = await db.FeedbackNotifications
            .Where(n => n.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var submissions = await db.FeedbackSubmissions
            .Where(s => s.SubmittedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        LogPurged(submissions, notices, screenshots);
    }

    [LoggerMessage(3105, LogLevel.Information,
        "[FeedbackRetention] Deleted {Submissions} feedback submissions, {Notices} notices and {Screenshots} screenshots older than 90 days")]
    private partial void LogPurged(int submissions, int notices, int screenshots);
}
