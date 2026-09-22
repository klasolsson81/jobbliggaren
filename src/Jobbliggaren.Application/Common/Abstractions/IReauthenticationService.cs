using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Single source of the re-authentication check for sensitive operations (C5, epik #481; a grant since
/// #1739, ADR 0142 D5). Resolves the acting user from <see cref="ICurrentUser"/>, redeems the supplied
/// grant against <c>GrantSubject.Reauthentication(userId)</c> — the store asserts the purpose and the user,
/// and a redemption is single-use whichever way it ends — and gates a soft-deleted account (Layer 1 —
/// reject + best-effort session self-heal). Returns a bare <see cref="Result"/>: success, or
/// <c>Auth.InvalidCredentials</c> for ANY failure (no grant, a grant that cannot be redeemed, or
/// soft-deleted — indistinguishable, oracle-avoidance). No session mutation on the success path.
///
/// Consumed by <c>ReauthenticationBehavior</c> (throws on failure) — one enforcement path for the whole
/// app.
/// </summary>
public interface IReauthenticationService
{
    ValueTask<Result> VerifyCurrentUserGrantAsync(string? grant, CancellationToken ct);
}
