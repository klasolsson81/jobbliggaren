namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// A store an auth flow depends on is unreachable. <c>Program.cs</c> renders every subtype as the uniform
/// 503, so a request that cannot reach the store fails the same way whichever store it was (#512; ADR 0142
/// D2). Each store throws its own subtype, which keeps its message and its inner exception.
/// </summary>
public abstract class StoreUnavailableException : Exception
{
    protected StoreUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
