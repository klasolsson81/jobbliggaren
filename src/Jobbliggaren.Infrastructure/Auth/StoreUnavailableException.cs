namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// A store an auth flow depends on is unreachable. <c>Program.cs</c> renders every subtype as the same
/// uniform 503 body, <see cref="ClientMessage"/>, so a request that cannot reach the store fails the same
/// way whichever store it was and whatever the subtype's own message says (#512; ADR 0142 D2).
/// </summary>
public abstract class StoreUnavailableException : Exception
{
    /// <summary>The 503 body's text for every subtype. The subtype's own message never reaches a client.</summary>
    public const string ClientMessage = "Tjänsten är inte tillgänglig just nu. Försök igen om en stund.";

    protected StoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        InnerType = innerException.GetType().Name;
    }

    /// <summary>
    /// For a store thrown from inside the Mediator pipeline: <c>LoggingBehavior</c> logs the whole
    /// exception, and a StackExchange.Redis message embeds the operated key, which for an address-derived
    /// key is a pseudonymised address fingerprint. Such a subtype carries the degradation class as a type
    /// name and no inner exception at all.
    /// </summary>
    protected StoreUnavailableException(string message, string innerType)
        : base(message)
    {
        InnerType = innerType;
    }

    /// <summary>The type name of the failure underneath (connection, timeout, server) — the alarm's key.</summary>
    public string InnerType { get; }
}
