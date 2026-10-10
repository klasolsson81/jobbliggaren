using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// What an administrator writes back to the person who sent a feedback submission (#2069). Stored
/// plain beside the feedback (ADR 0156, Klas's grant 3) and never logged.
/// </summary>
public sealed record FeedbackReplyText
{
    public const int MaxLength = 4000;

    public string Value { get; }

    private FeedbackReplyText(string value) => Value = value;

    public static Result<FeedbackReplyText> Create(string? text)
    {
        var normalized = (text ?? string.Empty).ReplaceLineEndings("\n")
            .Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
            return Result.Failure<FeedbackReplyText>(DomainError.Validation(
                "Feedback.ReplyEmpty", "Skriv ett svar."));

        return normalized.Length > MaxLength
            ? Result.Failure<FeedbackReplyText>(DomainError.Validation(
                "Feedback.ReplyTooLong", $"Svaret får vara högst {MaxLength} tecken."))
            : Result.Success(new FeedbackReplyText(normalized));
    }

    public override string ToString() => $"FeedbackReplyText({Value.Length} chars)";
}
