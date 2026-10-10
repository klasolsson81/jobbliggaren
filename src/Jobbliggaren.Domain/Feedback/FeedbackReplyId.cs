namespace Jobbliggaren.Domain.Feedback;

public readonly record struct FeedbackReplyId(Guid Value)
{
    public static FeedbackReplyId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
