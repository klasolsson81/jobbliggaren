namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class SessionStoreUnavailableException : StoreUnavailableException
{
    public SessionStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
