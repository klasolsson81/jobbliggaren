using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>A star rating of a page, 1–5.</summary>
public readonly record struct FeedbackRating
{
    public const int Min = 1;
    public const int Max = 5;

    public int Value { get; }

    private FeedbackRating(int value) => Value = value;

    public static Result<FeedbackRating> Create(int value) =>
        value is >= Min and <= Max
            ? Result.Success(new FeedbackRating(value))
            : Result.Failure<FeedbackRating>(DomainError.Validation(
                "Feedback.RatingOutOfRange", $"Betyget måste vara mellan {Min} och {Max}."));
}
