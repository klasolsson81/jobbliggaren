using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.SendFeedbackReply;

/// <summary>
/// An administrator mails a reply to the person behind a feedback submission (#2069).
/// <see cref="ReplyKey"/> is the form's idempotency key: a repeated request with the same key replays
/// the recorded outcome instead of sending again. The handler owns its transactions and its audit.
/// </summary>
public sealed record SendFeedbackReplyCommand(Guid Id, Guid ReplyKey, string? Text)
    : ICommand<Result<FeedbackReplyOutcome>>, IAdminRequest, IAuditableCommand<Result<FeedbackReplyOutcome>>,
        IOwnsAccountTransaction
{
    public string EventType => "Admin.FeedbackReplied";
    public string AggregateType => "Feedback";
    public Guid ExtractAggregateId(Result<FeedbackReplyOutcome> response) => Id;

    public override string ToString() => $"SendFeedbackReplyCommand({Id}, {ReplyKey}, text redacted)";
}
