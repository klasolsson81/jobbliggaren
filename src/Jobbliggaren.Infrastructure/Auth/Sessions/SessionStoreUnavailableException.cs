namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class SessionStoreUnavailableException : Exception
{
    public SessionStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
