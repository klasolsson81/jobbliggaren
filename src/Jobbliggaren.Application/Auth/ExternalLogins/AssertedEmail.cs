namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// An address a provider reports as its account's verified primary one, where the provider is NOT the mailbox (ADR
/// 0142 Amendment (16), #1745): the flag records that someone once proved the inbox, not that anyone holds it now.
/// It chooses where a login code is sent, and it is compared with an address an account holds or a code proved; no
/// account, link or inbox proof is made from it. Only a provider adapter decides that an address qualifies.
/// <see cref="ToString"/> prints no part of it.
/// </summary>
public sealed record AssertedEmail
{
    private AssertedEmail(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => "AssertedEmail(redacted)";

    /// <summary>Null for anything that is not one address of a local part and a domain within the length bound.</summary>
    public static AssertedEmail? TryCreate(string? address) =>
        ExternalAddressBounds.IsWithinBounds(address) ? new AssertedEmail(address) : null;
}
