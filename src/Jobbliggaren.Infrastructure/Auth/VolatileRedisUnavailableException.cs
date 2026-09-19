namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// The volatile Redis instance is unreachable (ADR 0142 D1). Named for the INSTANCE rather than for a flow:
/// a rate budget is not a login-challenge store, and the instance is the axis an operator needs — which
/// container to look at. <c>Program.cs</c> renders it as the uniform 503, the same way it renders an
/// unreachable session store (ADR 0142 D2). It is thrown from inside the Mediator pipeline, so it carries the
/// Redis failure's type name and never the Redis exception itself.
/// </summary>
public sealed class VolatileRedisUnavailableException(string innerType)
    : StoreUnavailableException("Volatile Redis (login challenge store and rate budgets) unavailable.", innerType);
