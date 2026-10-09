using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback.Events;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// One piece of feedback a user sent about a page (#1979): a rating, a text, or both. The
/// <see cref="SubmissionKey"/> is the client's idempotency key, unique per owner, so a double click
/// or a lost response replays this record instead of creating a second one.
/// </summary>
public sealed partial class FeedbackSubmission : AggregateRoot<FeedbackSubmissionId>
{
    public const int AppVersionMaxLength = 40;

    public JobSeekerId JobSeekerId { get; private set; }
    public Guid SubmissionKey { get; private set; }
    public FeedbackPage Page { get; private set; } = null!;
    public FeedbackRating? Rating { get; private set; }
    public FeedbackComment? Comment { get; private set; }
    public ReportedClientContext Context { get; private set; } = ReportedClientContext.Empty;
    public string? AppVersion { get; private set; }
    public FeedbackStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? StatusChangedAt { get; private set; }

    // EF Core constructor
    private FeedbackSubmission() { }

    private FeedbackSubmission(
        FeedbackSubmissionId id,
        JobSeekerId jobSeekerId,
        Guid submissionKey,
        FeedbackPage page,
        FeedbackRating? rating,
        FeedbackComment? comment,
        ReportedClientContext context,
        string? appVersion,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        SubmissionKey = submissionKey;
        Page = page;
        Rating = rating;
        Comment = comment;
        Context = context;
        AppVersion = appVersion;
        Status = FeedbackStatus.New;
        SubmittedAt = now;
    }

    public static Result<FeedbackSubmission> Submit(
        JobSeekerId jobSeekerId,
        Guid submissionKey,
        FeedbackPage page,
        FeedbackRating? rating,
        FeedbackComment? comment,
        ReportedClientContext context,
        string? appVersion,
        DateTimeOffset now)
    {
        if (jobSeekerId == default)
            return Result.Failure<FeedbackSubmission>(DomainError.Validation(
                "Feedback.OwnerRequired", "Feedback måste höra till ett konto."));

        if (submissionKey == Guid.Empty)
            return Result.Failure<FeedbackSubmission>(DomainError.Validation(
                "Feedback.SubmissionKeyRequired", "Inskicket saknar nyckel."));

        if (rating is null && comment is null)
            return Result.Failure<FeedbackSubmission>(DomainError.Validation(
                "Feedback.Empty", "Ge ett betyg eller skriv en text."));

        if (appVersion is not null && !AppVersionShape().IsMatch(appVersion))
            return Result.Failure<FeedbackSubmission>(DomainError.Validation(
                "Feedback.AppVersionInvalid", "Appversionen har fel format."));

        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(context);

        var stored = appVersion is null ? ReportedClientContext.Empty : context;
        var submission = new FeedbackSubmission(
            FeedbackSubmissionId.New(), jobSeekerId, submissionKey, page, rating, comment, stored, appVersion, now);
        submission.RaiseDomainEvent(new FeedbackSubmittedDomainEvent(submission.Id, page, now));
        return Result.Success(submission);
    }

    /// <summary>
    /// Moves the submission to another status. Any status may follow any other, so a closed item can
    /// be reopened; a change to the current status is refused rather than recorded as a change.
    /// </summary>
    public Result ChangeStatus(FeedbackStatus to, DateTimeOffset now)
    {
        if (!Enum.IsDefined(to))
            return Result.Failure(DomainError.Validation("Feedback.StatusInvalid", "Okänd status."));

        if (to == Status)
            return Result.Failure(DomainError.Validation(
                "Feedback.StatusUnchanged", "Feedbacken har redan den statusen."));

        var from = Status;
        Status = to;
        StatusChangedAt = now;
        RaiseDomainEvent(new FeedbackStatusChangedDomainEvent(Id, from, to, now));
        return Result.Success();
    }

    // The web server stamps the commit its build came from. Lowercase hex only, so the column is a
    // closed domain no person's name can be typed into (ErasureCascadeRegistry's ground relies on it).
    [GeneratedRegex(@"^[0-9a-f]{7,40}\z")]
    private static partial Regex AppVersionShape();
}
