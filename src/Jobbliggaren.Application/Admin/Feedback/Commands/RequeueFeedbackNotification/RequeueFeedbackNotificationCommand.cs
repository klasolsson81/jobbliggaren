using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;

/// <summary>
/// An administrator sends a submission's notice again (#1979). From Failed nothing was ever sent;
/// from Unknown the earlier mail may have arrived, so <see cref="AcknowledgeDuplicateRisk"/> must be
/// set — the domain refuses otherwise. The handler owns the reporter's protected transaction and audit;
/// a concurrent dispatch refuses after rollback and an unknown commit is never replayed.
/// </summary>
public sealed record RequeueFeedbackNotificationCommand(Guid Id, bool AcknowledgeDuplicateRisk)
    : ICommand<Result>, IAdminRequest, IAuditableCommand<Result>, IOwnsAccountTransaction
{
    public string EventType => "Admin.FeedbackNotificationRequeued";
    public string AggregateType => "Feedback";
    public Guid ExtractAggregateId(Result response) => Id;
}
