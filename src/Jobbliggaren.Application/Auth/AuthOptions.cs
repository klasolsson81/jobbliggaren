namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Auth-flow policy the Application layer owns and Infrastructure binds (<c>Auth</c> section). Application
/// declares the contract so the login challenge's handlers can read it via <c>IOptions&lt;AuthOptions&gt;</c>
/// without depending on Infrastructure (Clean Architecture dependency rule; precedent: the backfill/digest job
/// options).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Public-registration kill-switch (Klas-beslut 2026-08-03, ADR 0083 Amendment 2026-08-03). When
    /// <c>false</c> no new account is created: a login challenge for an address without an account mints no
    /// code, and <c>complete</c> refuses as its first statement, before any grant is read.
    /// <para>
    /// Default <c>false</c> = CLOSED, and the polarity is the whole point: the app is publicly reachable before
    /// its legal and security gates are green, so an unset value must fail CLOSED. The mirror-image name
    /// (<c>RegistrationsClosed</c>) would default to open and is therefore wrong.
    /// </para>
    /// <para>
    /// This flag is NOT a waitlist. ADR 0083's teardown of the Waitlist and Invitations bounded contexts
    /// stands in full.
    /// </para>
    /// <para>
    /// Outside Development/Test, <c>AuthOptionsValidator</c> refuses to boot when the registered sender cannot
    /// deliver, whatever this flag says, because login itself is a mailed code or link. The procedure for
    /// opening and closing it is <c>docs/runbooks/registration-gate.md</c>.
    /// </para>
    /// <para>
    /// Read through singleton <c>IOptions</c>, and deployed config arrives as environment variables, which do
    /// not reload: changing it is <i>set env → re-create the container</i>, which also keeps the once-per-process
    /// boot announcement true. Settable (not init-only) so the integration harness can force it via
    /// <c>PostConfigure&lt;AuthOptions&gt;</c>.
    /// </para>
    /// </summary>
    public bool RegistrationsOpen { get; set; }
}
