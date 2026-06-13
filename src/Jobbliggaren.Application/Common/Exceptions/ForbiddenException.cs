namespace Jobbliggaren.Application.Common.Exceptions;

public sealed class ForbiddenException(string message = "Åtkomst nekad.") : Exception(message);
