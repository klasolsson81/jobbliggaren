namespace Jobbliggaren.Domain.Feedback;

public readonly record struct FeedbackScreenshotId(Guid Value)
{
    public static FeedbackScreenshotId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
