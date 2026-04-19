namespace JobbPilot.Application.Common.Exceptions;

public sealed class UnauthorizedException(string message = "Autentisering krävs.")
    : Exception(message);
