using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Identity;

/// <summary>
/// #616 (CTO-bind FORK 2, Variant B) — rejects known-breached passwords at the UserManager
/// chokepoint, so every current AND future set-password path (CreateAsync, ChangePasswordAsync,
/// a future reset flow) is covered by ONE registration. Api-only: chained via
/// <c>.AddPasswordValidator&lt;&gt;()</c> in <c>AddIdentityAndSessions</c>; the Worker's
/// <c>AddIdentityCore</c> composition never sees it (HTTP-free Worker, ADR 0023).
///
/// <para>
/// THE fail-open policy point (CTO-bind FORK 1): <see cref="BreachCheckVerdict.Unavailable"/> is
/// treated as pass — an HIBP outage must never block registration or password change. The check
/// is P2 defense-in-depth on top of the 12-char NIST floor. Flip this single expression if a
/// selective fail-closed posture is ever wanted.
/// </para>
///
/// <para>
/// The error surfaces through the existing <c>DomainError.Validation($"Auth.{error.Code}", ...)</c>
/// mapping in <c>UserAccountService</c> as <c>Auth.PwnedPassword</c> (400). The Description is the
/// same Swedish copy the frontend renders from i18n for the code — kept in lockstep so the wire
/// <c>detail</c> and the UI never drift. No breach source or occurrence count is revealed.
/// </para>
/// </summary>
internal sealed class PwnedPasswordValidator(IBreachedPasswordChecker breachedPasswordChecker)
    : IPasswordValidator<ApplicationUser>
{
    /// <summary>Becomes <c>Auth.PwnedPassword</c> through the UserAccountService error mapping.</summary>
    internal const string ErrorCode = "PwnedPassword";

    internal const string ErrorDescription =
        "Lösenordet har förekommit i kända dataläckor. Välj ett annat lösenord.";

    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        // Null/empty/short passwords are the built-in PasswordOptions validator's concern
        // (RequiredLength = 12) — don't spend an egress call on input that already fails there.
        if (string.IsNullOrEmpty(password))
            return IdentityResult.Success;

        // IPasswordValidator has no CancellationToken; the resilience pipeline's ~2 s attempt
        // budget (BreachCheckOptions.TimeoutSeconds) is the effective cap.
        var verdict = await breachedPasswordChecker.CheckAsync(password, CancellationToken.None);

        return verdict is BreachCheckVerdict.Breached
            ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = ErrorDescription })
            : IdentityResult.Success;
    }
}
