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

    /// <summary>
    /// Public-registration kill-switch (Klas-beslut 2026-08-03, ADR 0083 Amendment 2026-08-03).
    /// When <c>false</c> the register command is refused BEFORE any account is created; when
    /// <c>true</c> registration behaves exactly as it did before the flag existed.
    /// <para>
    /// Default <c>false</c> = CLOSED, and the polarity is the whole point: the app becomes publicly
    /// reachable before its legal and security gates are green, so an unset value must fail CLOSED.
    /// The mirror-image name (<c>RegistrationsClosed</c>) would default to open and is therefore
    /// wrong. Measured 2026-08-03: without this flag, Production takes
    /// <see cref="Commands.Register.RegisterCommandHandler"/>'s legacy instant-login branch —
    /// <c>Auth:RequireEmailConfirmation</c> is absent from every non-Development appsettings file —
    /// so a stranger obtains a logged-in account in one request and no email is sent at all.
    /// <c>NullEmailSender</c> gates nothing on that path; it is never reached.
    /// </para>
    /// <para>
    /// This flag is NOT a waitlist. ADR 0083's teardown of the Waitlist and Invitations bounded
    /// contexts stands in full.
    /// </para>
    /// <para>
    /// <b>Opening it is not a single switch.</b> Outside Development/Test,
    /// <c>AuthOptionsValidator</c> refuses to boot on <c>RegistrationsOpen</c> without
    /// <c>RequireEmailConfirmation</c>: that combination is legacy instant-login, which mints an
    /// account bound to an address the registrant may not own and puts the acknowledged-deferred
    /// duplicate-enumeration oracle on a public IP. Going live therefore needs BOTH flags plus a real
    /// <c>Email:Provider</c> — the prerequisites are owned by <b>#734</b>. <b>All three are enforced,
    /// not merely documented:</b> since 2026-08-09 the same validator also refuses to boot when both
    /// flags are on and the registered sender cannot deliver, because that configuration creates
    /// accounts whose activation link reaches nobody.
    /// </para>
    /// <para>
    /// Both flags are read through singleton <c>IOptions</c>, and deployed config arrives as
    /// environment variables, which do not reload. Changing either is <i>set env → restart</i>, and
    /// the restart is deliberate: it is what keeps the once-per-process boot announcement true, and
    /// it is the audit trail. To add a user while closed, once email is live: open both flags,
    /// restart, register, confirm via the real link, close them, restart. Never hand-write an account
    /// into the database (Identity hash + security stamp + the JobSeeker aggregate).
    /// </para>
    /// <para>
    /// Settable (not init-only) for the same reason as <see cref="RequireEmailConfirmation"/>: the
    /// integration harness forces it via <c>PostConfigure&lt;AuthOptions&gt;</c>.
    /// </para>
    /// </summary>
    public bool RegistrationsOpen { get; set; }
}
