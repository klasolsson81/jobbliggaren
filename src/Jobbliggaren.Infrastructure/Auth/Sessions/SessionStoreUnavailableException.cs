namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class SessionStoreUnavailableException : StoreUnavailableException
{
    public const string StoreName = "session";

    public SessionStoreUnavailableException(string message, Exception innerException)
        : base(StoreName, message, innerException) { }
}
