using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Feedback;

public interface IFeedbackScreenshotNormalizer
{
    Task<Result<NormalizedFeedbackScreenshot>> NormalizeAsync(
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

public sealed record NormalizedFeedbackScreenshot(ReadOnlyMemory<byte> Content, int Width, int Height)
{
    public override string ToString() => $"NormalizedFeedbackScreenshot({Width}x{Height}, {Content.Length} bytes)";
}
