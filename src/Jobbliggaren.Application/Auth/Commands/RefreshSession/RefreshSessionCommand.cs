using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.RefreshSession;

// Slides the current session (via the auth pipeline's GetAsync) and rotates its id if
// the profile's rotation interval has elapsed (#481 persistent-login, security C3). The
// caller (the Next.js /auth/refresh seam) writes any new id into the __Host- cookie.
public sealed record RefreshSessionCommand : ICommand<Result<RefreshSessionResult>>, IAuthenticatedRequest;

// Rotated=false → the session was only slid; the cookie is unchanged. Rotated=true → the
// caller must replace the cookie value with SessionId (the freshly minted id).
public sealed record RefreshSessionResult(bool Rotated, string? SessionId, DateTimeOffset? ExpiresAt);
