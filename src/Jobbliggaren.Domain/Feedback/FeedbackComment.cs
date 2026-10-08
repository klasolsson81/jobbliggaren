using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>
/// The optional free text of a feedback submission. Stored as written (trimmed), never logged.
/// Klas 2026-10-07: feedback is not secret data, so the column is plain rather than DEK-sealed.
/// </summary>
public sealed record FeedbackComment
{
    public const int MaxLength = 2000;

    public string Value { get; }

    private FeedbackComment(string value) => Value = value;

    /// <summary>Whitespace or nothing is "no comment" (<see langword="null"/>), not an error.</summary>
    public static Result<FeedbackComment?> Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result.Success<FeedbackComment?>(null);

        var trimmed = text.Trim();
        return trimmed.Length > MaxLength
            ? Result.Failure<FeedbackComment?>(DomainError.Validation(
                "Feedback.CommentTooLong", $"Texten får vara högst {MaxLength} tecken."))
            : Result.Success<FeedbackComment?>(new FeedbackComment(trimmed));
    }

    public override string ToString() => $"FeedbackComment({Value.Length} chars)";
}
