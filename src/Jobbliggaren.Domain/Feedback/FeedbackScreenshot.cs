using System.Text.Json.Serialization;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Feedback;

public sealed class FeedbackScreenshot : AggregateRoot<FeedbackScreenshotId>
{
    public const int MaxContentBytes = 5 * 1024 * 1024;
    public const long MaxPixels = 16_000_000;
    public const string ContentType = "image/png";

    private readonly byte[] _content = [];

    public FeedbackSubmissionId SubmissionId { get; private set; }
    public JobSeekerId JobSeekerId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    [JsonIgnore]
    public ReadOnlyMemory<byte> Content => _content;

    private FeedbackScreenshot() { }

    private FeedbackScreenshot(FeedbackSubmission submission, ReadOnlyMemory<byte> content, int width, int height)
        : base(FeedbackScreenshotId.New())
    {
        SubmissionId = submission.Id;
        JobSeekerId = submission.JobSeekerId;
        SubmittedAt = submission.SubmittedAt;
        Width = width;
        Height = height;
        _content = content.ToArray();
    }

    public static Result<FeedbackScreenshot> AttachTo(
        FeedbackSubmission submission, ReadOnlyMemory<byte> normalizedPng, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (normalizedPng.IsEmpty || normalizedPng.Length > MaxContentBytes
            || !normalizedPng.Span.StartsWith<byte>([137, 80, 78, 71, 13, 10, 26, 10])
            || width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            return Result.Failure<FeedbackScreenshot>(DomainError.Validation(
                "Feedback.ScreenshotInvalid", "Skärmbilden har fel format eller är för stor."));

        return Result.Success(new FeedbackScreenshot(submission, normalizedPng, width, height));
    }

    public override string ToString() => $"FeedbackScreenshot({Id}, {Width}x{Height})";
}
