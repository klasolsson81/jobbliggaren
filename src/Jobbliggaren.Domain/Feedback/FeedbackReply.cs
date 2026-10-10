using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// One mail an administrator sends to the person behind a feedback submission (#2069). The row is
/// saved as <see cref="FeedbackReplyState.Sending"/> before the provider call and then records exactly
/// one outcome. There is no retry: another attempt is a new reply the administrator writes, so an
/// outcome that may have been accepted is never resent on its own (Klas, 2026-10-07).
/// </summary>
public sealed class FeedbackReply : AggregateRoot<FeedbackReplyId>
{
    public const int MaxPerSubmission = 10;

    /// <summary>
    /// How long a reply may stay in <see cref="FeedbackReplyState.Sending"/> before the request that
    /// saved it is presumed dead. Well past the provider client's 30-second timeout.
    /// </summary>
    public static readonly TimeSpan StaleSendingAfter = TimeSpan.FromMinutes(10);

    public FeedbackSubmissionId SubmissionId { get; private set; }
    public JobSeekerId JobSeekerId { get; private set; }
    public Guid ReplyKey { get; private set; }
    public FeedbackReplyText Text { get; private set; } = null!;
    public FeedbackReplyState State { get; private set; }

    /// <summary>The submission's own time, so retention removes the reply with it.</summary>
    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // EF Core constructor
    private FeedbackReply() { }

    private FeedbackReply(FeedbackSubmission submission, Guid replyKey, FeedbackReplyText text, DateTimeOffset now)
        : base(FeedbackReplyId.New())
    {
        SubmissionId = submission.Id;
        JobSeekerId = submission.JobSeekerId;
        SubmittedAt = submission.SubmittedAt;
        ReplyKey = replyKey;
        Text = text;
        State = FeedbackReplyState.Sending;
        CreatedAt = now;
    }

    /// <summary>
    /// Starts a reply. <paramref name="existingReplies"/> is the submission's reply count, read by the
    /// caller under the lock that serialises replies to one reporter.
    /// </summary>
    public static Result<FeedbackReply> Start(
        FeedbackSubmission submission, int existingReplies, Guid replyKey, FeedbackReplyText text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(text);

        if (replyKey == Guid.Empty)
            return Result.Failure<FeedbackReply>(DomainError.Validation(
                "Feedback.ReplyKeyRequired", "Svaret saknar nyckel."));

        if (existingReplies >= MaxPerSubmission)
            return Result.Failure<FeedbackReply>(DomainError.Conflict(
                "Feedback.ReplyLimitReached", $"Ett inskick kan få högst {MaxPerSubmission} svar."));

        return Result.Success(new FeedbackReply(submission, replyKey, text, now));
    }

    public Result RecordAccepted(DateTimeOffset now) => Complete(FeedbackReplyState.Accepted, now);

    /// <summary>The provider demonstrably did not take the message.</summary>
    public Result RecordNotAccepted(DateTimeOffset now) => Complete(FeedbackReplyState.Failed, now);

    public Result RecordUnknown(DateTimeOffset now) => Complete(FeedbackReplyState.Unknown, now);

    /// <summary>
    /// A reply still <see cref="FeedbackReplyState.Sending"/> after <see cref="StaleSendingAfter"/> was
    /// saved by a request that died near the provider call, so whether the mail left cannot be known.
    /// Nothing writes that row again; this is how every reader states it.
    /// </summary>
    public static FeedbackReplyState Effective(FeedbackReplyState stored, DateTimeOffset createdAt, DateTimeOffset now) =>
        stored == FeedbackReplyState.Sending && now - createdAt >= StaleSendingAfter
            ? FeedbackReplyState.Unknown
            : stored;

    public override string ToString() => $"FeedbackReply({Id}, {State})";

    private Result Complete(FeedbackReplyState outcome, DateTimeOffset now)
    {
        if (State != FeedbackReplyState.Sending)
            return Result.Failure(DomainError.Conflict("Feedback.ReplyNotSending", "Svaret skickas inte just nu."));

        State = outcome;
        CompletedAt = now;
        return Result.Success();
    }
}
