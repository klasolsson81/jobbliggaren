using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.VerifyCredentials;

/// <summary>
/// Backs <c>POST /auth/verify</c> — the re-authentication credential check (TD-28 / OWASP ASVS
/// V6.2.5). Delegates to the shared <see cref="IReauthenticationService"/> — the SAME check
/// <c>ReauthenticationBehavior</c> runs for <c>IReauthenticatingRequest</c> commands — so the
/// re-auth policy (password validation, lockout-awareness, TOCTOU guard, soft-delete gate) lives in
/// exactly one place. The endpoint maps a failure to the byte-identical <c>Auth.InvalidCredentials</c>
/// 401 (AuthProblem) and success to 204; no session mutation.
/// </summary>
public sealed class VerifyCredentialsQueryHandler(IReauthenticationService reauthentication)
    : IQueryHandler<VerifyCredentialsQuery, Result>
{
    public ValueTask<Result> Handle(VerifyCredentialsQuery query, CancellationToken cancellationToken) =>
        reauthentication.VerifyCurrentUserPasswordAsync(query.Password, cancellationToken);
}
