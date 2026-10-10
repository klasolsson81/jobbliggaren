using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.SendFeedbackReply;

/// <summary>
/// The recorded outcome of one reply. It is data about a saved and audited row, not a command
/// failure, so a refused or unknown send still answers with it.
/// </summary>
public sealed record FeedbackReplyOutcome(Guid ReplyId, FeedbackReplyState State, bool Replayed);
