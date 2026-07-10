namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Auth-flow policy toggles the Application layer owns and Infrastructure binds
/// (<c>Auth</c> section). Application declares the contract so
/// <see cref="Commands.Register.RegisterCommandHandler"/> can read it via
/// <c>IOptions&lt;AuthOptions&gt;</c> without depending on Infrastructure (Clean
/// Architecture dependency rule; precedent: the backfill/digest job options).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Email-confirmation-first registration (#714). Closes the 200-vs-400
    /// account-enumeration status oracle: when ON, registration always returns 202
    /// (no instant login), a confirmation link is emailed to a fresh address and a
    /// login-nudge notice to a taken one, and login is gated on
    /// <c>ApplicationUser.EmailConfirmed</c>. When OFF, registration keeps the legacy
    /// instant-login behaviour (200 + session).
    /// <para>
    /// Default <c>false</c> is deliberate and prod-safe. This flag is ORTHOGONAL to the
    /// email transport (<c>Email:Provider</c>): with the transport set to a no-op
    /// (<c>NullEmailSender</c> in non-dev) a confirmation link would go nowhere, so
    /// flipping this ON in production must wait for a live email provider AND a one-time
    /// <c>EmailConfirmed=true</c> backfill of pre-existing accounts (they were created
    /// under instant-login and must not be locked out). Dev/Test set it <c>true</c>; the
    /// default integration-test host keeps it <c>false</c> so the instant-login test
    /// bootstrap (RegisterAndGetSessionIdAsync) is unaffected.
    /// </para>
    /// <para>
    /// A settable (not init-only) property so the integration harness can force the value via
    /// <c>PostConfigure&lt;AuthOptions&gt;</c> — the base host pins it OFF (protecting the 142
    /// instant-login bootstrap sites) and the flag-ON test classes flip it ON per class.
    /// </para>
    /// </summary>
    public bool RequireEmailConfirmation { get; set; }
}
