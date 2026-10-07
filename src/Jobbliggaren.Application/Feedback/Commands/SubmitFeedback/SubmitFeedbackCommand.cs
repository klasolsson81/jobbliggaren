using System.Globalization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;

namespace Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;

/// <summary>
/// What the browser reported about itself, already narrowed to the closed sets: the endpoint
/// drops an unknown name instead of refusing the feedback (see <see cref="ReportedClientContext"/>).
/// </summary>
public sealed record ReportedClient(
    int? ViewportWidth,
    int? ViewportHeight,
    int? ScreenWidth,
    int? ScreenHeight,
    decimal? PixelRatio,
    ReportedTheme? Theme,
    ReportedDeviceClass? DeviceClass,
    ReportedOsFamily? OsFamily,
    ReportedBrowserFamily? BrowserFamily)
{
    public static ReportedClient None { get; } = new(null, null, null, null, null, null, null, null, null);
}

/// <summary>The saved submission, and whether this call replayed an earlier one with the same key.</summary>
public sealed record FeedbackSubmitted(Guid FeedbackId, bool Replayed);

/// <summary>
/// A signed-in user's feedback on a page (#1979). <see cref="SubmissionKey"/> is the client's
/// idempotency key: the same key always answers with the same submission. Deliberately not audited:
/// an audit row stores the raw user agent, which this feature must not collect.
/// </summary>
public sealed record SubmitFeedbackCommand(
    Guid SubmissionKey,
    string? PageKey,
    int? Rating,
    string? Comment,
    ReportedClient Client,
    string? AppVersion)
    : ICommand<Result<FeedbackSubmitted>>, IAuthenticatedRequest
{
    public override string ToString() =>
        $"SubmitFeedbackCommand({SubmissionKey}, {PageKey}, rating {Rating?.ToString(CultureInfo.InvariantCulture) ?? "none"}, "
        + $"comment {(string.IsNullOrWhiteSpace(Comment) ? "none" : "redacted")})";
}
