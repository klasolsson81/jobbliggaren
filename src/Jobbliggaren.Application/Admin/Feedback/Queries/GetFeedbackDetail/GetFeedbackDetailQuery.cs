using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;

/// <summary>
/// One feedback submission for the admin (#1979): the full text, what the browser reported, the
/// notice's delivery state, and the reporter's address read on the server. Accounts have no name
/// (ADR 0150 D3), so the address is the identity.
/// </summary>
public sealed record GetFeedbackDetailQuery(Guid Id) : IQuery<FeedbackDetailDto?>, IAdminRequest;

public sealed record ReportedClientDto(
    int? ViewportWidth,
    int? ViewportHeight,
    int? ScreenWidth,
    int? ScreenHeight,
    decimal? PixelRatio,
    ReportedTheme? Theme,
    ReportedDeviceClass? DeviceClass,
    ReportedOsFamily? OsFamily,
    ReportedBrowserFamily? BrowserFamily);

public sealed record FeedbackNotificationDto(
    FeedbackNotificationState State,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? StateChangedAt);

public sealed record FeedbackDetailDto(
    Guid Id,
    string PageKey,
    int? Rating,
    string? Comment,
    FeedbackStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StatusChangedAt,
    string? ReporterEmail,
    ReportedClientDto Client,
    string? AppVersion,
    FeedbackNotificationDto? Notification)
{
    public override string ToString() => $"FeedbackDetailDto({Id}, {Status})";
}

public sealed class GetFeedbackDetailQueryHandler(IAppDbContext db, IUserAccountService userAccounts)
    : IQueryHandler<GetFeedbackDetailQuery, FeedbackDetailDto?>
{
    public async ValueTask<FeedbackDetailDto?> Handle(GetFeedbackDetailQuery query, CancellationToken cancellationToken)
    {
        var id = new FeedbackSubmissionId(query.Id);
        var submission = await db.FeedbackSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (submission is null)
            return null;

        var notice = await db.FeedbackNotifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.SubmissionId == id, cancellationToken);

        // An account inside its 30-day restore window is soft-deleted; its feedback still shows.
        var userId = await db.JobSeekers
            .IgnoreQueryFilters()
            .Where(js => js.Id == submission.JobSeekerId)
            .Select(js => (Guid?)js.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        var email = userId is { } owner ? await userAccounts.GetEmailAsync(owner, cancellationToken) : null;

        var context = submission.Context;
        return new FeedbackDetailDto(
            submission.Id.Value,
            submission.Page.Name,
            submission.Rating?.Value,
            submission.Comment?.Value,
            submission.Status,
            submission.SubmittedAt,
            submission.StatusChangedAt,
            email,
            new ReportedClientDto(
                context.ViewportWidth, context.ViewportHeight, context.ScreenWidth, context.ScreenHeight,
                context.PixelRatio, context.Theme, context.DeviceClass, context.OsFamily, context.BrowserFamily),
            submission.AppVersion,
            notice is null
                ? null
                : new FeedbackNotificationDto(
                    notice.State, notice.Attempts, notice.NextAttemptAt, notice.AcceptedAt, notice.StateChangedAt));
    }
}
