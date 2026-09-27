namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// What a provider's code exchange ends in (ADR 0142 Amendment (16)): a closed set, so each refusal has one
/// representation and one answer. The adapter logs the cause of the two refusals; the caller never sees it.
/// </summary>
public abstract record ExternalExchange
{
    private ExternalExchange() { }

    /// <summary>The provider named the person and an address its rule admits.</summary>
    public sealed record Identified(ExternalIdentity Identity) : ExternalExchange;

    /// <summary>The provider's address rule refused the address (EventId 1023). Answered 400.</summary>
    public sealed record AddressRefused : ExternalExchange;

    /// <summary>The exchange itself failed: a refused code, a transport error, a body that cannot be read (1022).</summary>
    public sealed record Failed : ExternalExchange;
}

/// <summary>
/// The address a provider vouches for, in one of the two strengths the product tells apart (ADR 0142 D8, Amendment
/// (16)). A closed set: every reader switches on it, so no reader can treat an asserted address as a proof.
/// </summary>
public abstract record ExternalAddress
{
    private ExternalAddress() { }

    /// <summary>The provider is the mailbox, or its domain's administrator controls it.</summary>
    public sealed record Authoritative(VerifiedEmail Email) : ExternalAddress;

    /// <summary>The provider verified the address once and is not the mailbox; a code must prove it now.</summary>
    public sealed record Asserted(AssertedEmail Email) : ExternalAddress;
}
