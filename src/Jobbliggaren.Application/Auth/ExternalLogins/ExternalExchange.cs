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
