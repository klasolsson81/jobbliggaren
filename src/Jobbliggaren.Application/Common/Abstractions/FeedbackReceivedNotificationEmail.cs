using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// The notice Klas gets for each saved feedback submission (#1979): which page, the rating if one
/// was given, when, and the id the admin link opens. <b>Never the free text, the screenshot or
/// anything about the reporter</b> — those stay behind the admin sign-in. The recipient is
/// server configuration and travels separately, like every other kind on this port.
/// </summary>
public sealed record FeedbackReceivedNotificationEmail(
    FeedbackPage Page,
    int? Rating,
    DateTimeOffset SubmittedAt,
    Guid FeedbackId);
