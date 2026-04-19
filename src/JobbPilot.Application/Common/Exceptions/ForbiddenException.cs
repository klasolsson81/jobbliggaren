namespace JobbPilot.Application.Common.Exceptions;

public sealed class ForbiddenException(string message = "Åtkomst nekad.") : Exception(message);
