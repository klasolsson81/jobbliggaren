namespace Jobbliggaren.Application.Common.Exceptions;

/// <summary>An authentication store could not complete an operation.</summary>
public abstract class AuthStoreUnavailableException : Exception
{
    protected AuthStoreUnavailableException(string message, Exception? innerException)
        : base(message, innerException) { }
}
