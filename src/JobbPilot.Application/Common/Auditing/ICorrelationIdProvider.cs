namespace JobbPilot.Application.Common.Auditing;

/// <summary>
/// Producerar correlation-ID per request/scope. Per ADR 0022 stash:as ID:t
/// i HttpContext.Items av Infrastructure-implementationen så samma värde
/// återanvänds över hela request-livscykeln (audit-rad + LoggingBehavior).
/// </summary>
public interface ICorrelationIdProvider
{
    Guid Current { get; }
}
