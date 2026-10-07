namespace Jobbliggaren.Application.Common.Exceptions;

public sealed class AccountAccessCommitUncertainException(Exception innerException)
    : Exception("The account-access commit could not be confirmed.", innerException);
