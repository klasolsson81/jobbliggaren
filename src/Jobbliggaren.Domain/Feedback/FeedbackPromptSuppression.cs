using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// That a user has given feedback on a page, so the inline prompt stays hidden there on every
/// device (#1979). It outlives the feedback's 90-day retention on purpose: v1 never asks again.
/// One row per user and page, enforced by a unique index.
/// </summary>
public sealed class FeedbackPromptSuppression : AggregateRoot<FeedbackPromptSuppressionId>
{
    public JobSeekerId JobSeekerId { get; private set; }
    public FeedbackPage Page { get; private set; } = null!;

    // EF Core constructor
    private FeedbackPromptSuppression() { }

    private FeedbackPromptSuppression(FeedbackPromptSuppressionId id, JobSeekerId jobSeekerId, FeedbackPage page)
        : base(id)
    {
        JobSeekerId = jobSeekerId;
        Page = page;
    }

    public static FeedbackPromptSuppression Record(JobSeekerId jobSeekerId, FeedbackPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (jobSeekerId == default)
            throw new ArgumentException("A suppression belongs to a job seeker.", nameof(jobSeekerId));

        return new FeedbackPromptSuppression(FeedbackPromptSuppressionId.New(), jobSeekerId, page);
    }
}
