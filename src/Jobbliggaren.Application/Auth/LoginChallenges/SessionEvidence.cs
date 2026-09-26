namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// What a session is opened on (ADR 0142 Amendment (16), #1745): whether this login proved the account's inbox, or
/// only presented a provider login a code bound earlier. Only a proof of the inbox may confirm an address.
/// </summary>
public enum SessionEvidence
{
    /// <summary>A verified code, a consumed link, or an authoritative provider's address that is the account's own.</summary>
    InboxProven = 1,

    /// <summary>A provider login found linked to the account; the code that bound it proved the inbox then, not now.</summary>
    BoundLink = 2,
}
