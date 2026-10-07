namespace Jobbliggaren.Domain.Feedback;

public readonly record struct FeedbackNotificationId(Guid Value)
{
    public static FeedbackNotificationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
