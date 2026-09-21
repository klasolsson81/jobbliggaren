using Jobbliggaren.Application.Auth.Grants;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>What a proven login challenge leads to. A closed set: only the variants nested here exist.</summary>
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    public sealed record SignedIn(string SessionId) : LoginOutcome
    {
        public const string WireName = "signedIn";
    }

    public sealed record PendingDeletion(DateOnly PermanentDeletionEarliest) : LoginOutcome
    {
        public const string WireName = "pendingDeletion";
    }

    public sealed record RegistrationClosed : LoginOutcome
    {
        public const string WireName = "registrationClosed";
    }

    /// <summary>
    /// The address is proven and has no account, and registration is open: the terms come next. The grant
    /// is what <c>complete</c> redeems; it is a bearer token, held as its own type so a printed outcome shows
    /// a prefix and never the whole of it.
    /// </summary>
    public sealed record ConsentRequired(GrantToken Grant) : LoginOutcome
    {
        public const string WireName = "consentRequired";
    }

    /// <summary>
    /// Registration is open and this address can neither log in nor register right now: it has an Identity
    /// row without a profile (#1349), a LINK was proven for an address whose account went away inside the
    /// challenge's lifetime, or the proven spelling resolves to an account stored under another spelling.
    /// Never a grant and never a session (senior-cto-advisor, 2026-09-20).
    /// </summary>
    public sealed record AccountUnavailable : LoginOutcome
    {
        public const string WireName = "accountUnavailable";
    }
}
