using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;

/// <summary>
/// One feedback submission for the admin (#1979): the full text, what the browser reported, the
/// notice's delivery state, and the reporter's address read on the server. Accounts have no name
/// (ADR 0150 D3), so the address is the identity.
/// </summary>
public sealed record GetFeedbackDetailQuery(Guid Id) : IQuery<FeedbackDetailDto?>, IAdminRequest;
