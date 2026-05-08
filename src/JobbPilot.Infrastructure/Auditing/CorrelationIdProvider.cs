using JobbPilot.Application.Common.Auditing;
using Microsoft.AspNetCore.Http;

namespace JobbPilot.Infrastructure.Auditing;

/// <summary>
/// Producerar correlation-ID per request — alltid server-genererat per
/// ADR 0022 + OWASP ASVS V7.1.4 (server-side correlation-ID som default).
///
/// Tidigare versioner accepterade <c>X-Correlation-Id</c>-header från klient,
/// vilket öppnade för audit-spoofing (angripare kunde injicera valfri Guid och
/// korrelera sin angreppsaktivitet med en legitim användares trail). Headern
/// läses inte längre — om klient-correlation behövs i framtiden ska det vara
/// ett separat fält (<c>client_request_id</c>), aldrig blandat med audit-trail-ID.
///
/// ID:t stash:as i <see cref="HttpContext.Items"/> så samma värde återanvänds
/// över hela request-livscykeln.
/// </summary>
public sealed class CorrelationIdProvider(IHttpContextAccessor httpContextAccessor)
    : ICorrelationIdProvider
{
    private const string ItemsKey = "JobbPilot.CorrelationId";

    public Guid Current
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext;

            // Worker eller annat icke-HTTP-context — ny Guid per anrop.
            // När Worker-pipelinen aktiveras (Fas 2) registreras en
            // scope-cachad implementation per ADR 0022.
            if (ctx is null) return Guid.NewGuid();

            if (ctx.Items.TryGetValue(ItemsKey, out var existing) && existing is Guid cached)
                return cached;

            var id = Guid.NewGuid();
            ctx.Items[ItemsKey] = id;
            return id;
        }
    }
}
