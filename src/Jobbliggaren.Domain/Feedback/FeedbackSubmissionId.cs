namespace Jobbliggaren.Domain.Feedback;

public readonly record struct FeedbackSubmissionId(Guid Value)
{
    public static FeedbackSubmissionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
