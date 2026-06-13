using Jobbliggaren.Application.Common.Auditing;

namespace Jobbliggaren.Worker.Auditing;

/// <summary>
/// Stub-implementation av <see cref="IRequestContextProvider"/> för Worker-context.
/// Worker-jobb har inget HTTP-request — IP-adress och User-Agent är inte tillämpliga.
/// Audit-rader för Worker-dispatched commands får <c>ip_address = NULL</c>,
/// <c>user_agent = NULL</c> per ADR 0022.
/// </summary>
public sealed class WorkerRequestContextProvider : IRequestContextProvider
{
    public string? IpAddress => null;
    public string? UserAgent => null;
}
