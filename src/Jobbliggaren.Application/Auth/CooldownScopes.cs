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
}
