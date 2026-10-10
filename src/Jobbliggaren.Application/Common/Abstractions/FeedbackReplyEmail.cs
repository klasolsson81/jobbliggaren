using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// An administrator's reply to the person behind a feedback submission (#2069): the text he wrote,
/// and the page and time that tell the reporter which feedback it answers. <b>Never what the
/// reporter wrote</b> (ADR 0156 D5). The recipient travels separately, like every other kind on this port.
/// </summary>
public sealed record FeedbackReplyEmail(
    FeedbackPage Page,
    DateTimeOffset SubmittedAt,
    FeedbackReplyText Text)
{
    public override string ToString() => $"FeedbackReplyEmail({Page.Name}, text redacted)";
}
