namespace Jobbliggaren.Domain.Feedback;

/// <summary>Where the operator is with a submission: Ny, Pågår, Åtgärdad, Avstår.</summary>
public enum FeedbackStatus
{
    New,
    InProgress,
    Resolved,
    Declined,
}

public readonly record struct FeedbackSubmissionId(Guid Value)
{
    public static FeedbackSubmissionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
