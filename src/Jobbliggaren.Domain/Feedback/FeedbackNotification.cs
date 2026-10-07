using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// The mail that tells the operator a feedback submission was saved (#1979). It is created in the
/// same save as its submission and then lives its own life, written by the Worker and requeued by
/// an administrator (CTO decision 2b, 2026-10-07).
/// <para>
/// The rule the states carry is Klas's (2026-10-07): an outcome that may have been accepted is never
/// sent again unless an administrator acknowledges the risk of a duplicate.
/// </para>
/// </summary>
public sealed class FeedbackNotification : AggregateRoot<FeedbackNotificationId>
{
    public const int MaxAttempts = 5;

    /// <summary>
    /// How long a claim may stay in <see cref="FeedbackNotificationState.Sending"/> before the run
    /// that made it is presumed dead. Well past the provider client's 30-second timeout.
    /// </summary>
    public static readonly TimeSpan StaleSendingAfter = TimeSpan.FromMinutes(10);

    // The wait after the 1st, 2nd, 3rd and 4th refusal; the 5th ends in Failed.
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];

    public FeedbackSubmissionId SubmissionId { get; private set; }
    public JobSeekerId JobSeekerId { get; private set; }
    public FeedbackNotificationState State { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SendingStartedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? StateChangedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core constructor
    private FeedbackNotification() { }

    private FeedbackNotification(
        FeedbackNotificationId id, FeedbackSubmissionId submissionId, JobSeekerId jobSeekerId, DateTimeOffset now)
        : base(id)
    {
        SubmissionId = submissionId;
        JobSeekerId = jobSeekerId;
        State = FeedbackNotificationState.Queued;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    public static FeedbackNotification QueueFor(FeedbackSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new FeedbackNotification(
            FeedbackNotificationId.New(), submission.Id, submission.JobSeekerId, submission.SubmittedAt);
    }

    /// <summary>Takes a due notice for sending. Persist this before calling the provider.</summary>
    public Result Claim(DateTimeOffset now)
    {
        if (State != FeedbackNotificationState.Queued)
            return Refused("Feedback.NotificationNotQueued", "Aviseringen väntar inte på att skickas.");

        if (NextAttemptAt > now)
            return Refused("Feedback.NotificationNotDue", "Aviseringen ska inte skickas än.");

        State = FeedbackNotificationState.Sending;
        Attempts++;
        SendingStartedAt = now;
        StateChangedAt = now;
        return Result.Success();
    }

    public Result RecordAccepted(DateTimeOffset now)
    {
        if (State != FeedbackNotificationState.Sending)
            return NotSending();

        State = FeedbackNotificationState.Accepted;
        AcceptedAt = now;
        StateChangedAt = now;
        return Result.Success();
    }

    /// <summary>The provider demonstrably did not take the message, so another attempt cannot duplicate it.</summary>
    public Result RecordNotAccepted(DateTimeOffset now)
    {
        if (State != FeedbackNotificationState.Sending)
            return NotSending();

        if (Attempts >= MaxAttempts)
        {
            State = FeedbackNotificationState.Failed;
        }
        else
        {
            State = FeedbackNotificationState.Queued;
            NextAttemptAt = now + Backoff[Attempts - 1];
        }

        StateChangedAt = now;
        return Result.Success();
    }

    public Result RecordUnknown(DateTimeOffset now)
    {
        if (State != FeedbackNotificationState.Sending)
            return NotSending();

        State = FeedbackNotificationState.Unknown;
        StateChangedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// A claim that outlived <see cref="StaleSendingAfter"/> belongs to a run that died somewhere
    /// around the provider call, so whether the mail left cannot be known: it becomes Unknown, never Queued.
    /// </summary>
    public bool ExpireIfStale(DateTimeOffset now)
    {
        if (State != FeedbackNotificationState.Sending || SendingStartedAt is not { } started
            || now - started < StaleSendingAfter)
        {
            return false;
        }

        State = FeedbackNotificationState.Unknown;
        StateChangedAt = now;
        return true;
    }

    /// <summary>
    /// An administrator's new round of attempts. From Failed nothing was ever sent, so it needs no
    /// acknowledgement; from Unknown the earlier mail may already have arrived, so it does.
    /// </summary>
    public Result Requeue(bool acknowledgeDuplicateRisk, DateTimeOffset now)
    {
        if (State is not (FeedbackNotificationState.Failed or FeedbackNotificationState.Unknown))
            return Refused("Feedback.NotificationNotRequeueable", "Aviseringen kan inte skickas om nu.");

        if (State == FeedbackNotificationState.Unknown && !acknowledgeDuplicateRisk)
            return Refused("Feedback.DuplicateRiskNotAcknowledged",
                "Bekräfta att aviseringen kan komma fram två gånger.");

        State = FeedbackNotificationState.Queued;
        Attempts = 0;
        NextAttemptAt = now;
        SendingStartedAt = null;
        StateChangedAt = now;
        return Result.Success();
    }

    private static Result NotSending() =>
        Refused("Feedback.NotificationNotSending", "Aviseringen skickas inte just nu.");

    private static Result Refused(string code, string message) =>
        Result.Failure(DomainError.Conflict(code, message));
}
