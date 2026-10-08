using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;

/// <summary>
/// Whether feedback is open, and the pages where this user's inline prompt stays hidden because
/// they already gave feedback there (#1979). Shared by every device, since it lives on the server.
/// </summary>
public sealed record GetFeedbackPromptStateQuery : IQuery<FeedbackPromptStateDto>, IAuthenticatedRequest;
