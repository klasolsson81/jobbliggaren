namespace JobbPilot.Application.Common.Exceptions;

/// <summary>
/// Kastas av kill-switch-gate när <c>IFeatureFlags.RegistrationsOpen == false</c>.
/// Mappas till HTTP 503 Service Unavailable i Program.cs-middleware per ADR 0005
/// amendment 2026-05-12.
/// </summary>
public sealed class RegistrationsClosedException()
    : Exception("Registreringar är just nu stängda. Försök igen senare.");
