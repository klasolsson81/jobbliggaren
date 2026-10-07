namespace Jobbliggaren.Domain.Feedback;

public readonly record struct FeedbackPromptSuppressionId(Guid Value)
{
    public static FeedbackPromptSuppressionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
