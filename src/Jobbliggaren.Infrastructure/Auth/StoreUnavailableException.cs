namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// A store an auth flow depends on is unreachable. <c>Program.cs</c> renders every subtype as a 503 (#512;
/// ADR 0142 D2).
/// </summary>
public abstract class StoreUnavailableException : Exception
{
    protected StoreUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
