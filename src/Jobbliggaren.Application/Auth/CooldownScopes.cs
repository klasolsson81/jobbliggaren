namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Stable, namespaced <see cref="Common.Abstractions.ICooldownGate"/> scope keys for the auth
/// anti-email-bomb throttles (#733/#703). Each constant becomes part of the Redis key
/// (<c>cd/{scope}/v1/{hash}</c>), so a value MUST NOT change once shipped (in-flight windows would reset)
/// and distinct actions MUST NOT share a scope (their windows would collide). Constants, not literals
/// (§5 — no magic strings).
/// </summary>
public static class CooldownScopes
{
    /// <summary>Per-target throttle on the confirmation-link resend endpoint (#733; silent no-op).</summary>
    public const string ResendConfirm = "resend-confirm";

    /// <summary>Per-target throttle on the registration account-exists notice (#703; silent no-op).</summary>
    public const string AccountExists = "account-exists";

    /// <summary>Per-TARGET (new-address) throttle on the change-email request (#703; visible 409).</summary>
    public const string ChangeEmailTarget = "change-email-target";

    /// <summary>Per-USER (actor) throttle on the change-email request (#703; visible 409).</summary>
    public const string ChangeEmailUser = "change-email-user";

    /// <summary>
    /// Per-TARGET throttle on the forgot-password request (#1171). <b>SILENT no-op, never a visible
    /// 409 — and that is not a copy of the resend scope's choice but the same requirement.</b> The
    /// surface is unauthenticated and answers a uniform 202 for every well-formed address, so a
    /// visible throttle would answer differently for an address someone had recently requested than
    /// for one they had not, which is an enumeration oracle built out of the anti-abuse control. The
    /// two visible <c>ChangeEmail*</c> scopes above are visible only because that surface is
    /// authenticated and already discloses existence through <c>Auth.EmailTaken</c>.
    /// </summary>
    public const string PasswordReset = "password-reset";
}
