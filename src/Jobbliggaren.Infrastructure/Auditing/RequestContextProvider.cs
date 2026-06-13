using Jobbliggaren.Application.Common.Auditing;
using Microsoft.AspNetCore.Http;

namespace Jobbliggaren.Infrastructure.Auditing;

/// <summary>
/// Producerar IP-adress + User-Agent för audit-rad. Per ADR 0022 + ADR 0024 D7.
///
/// IP-adressen anonymiseras före lagring per GDPR Art. 5(1)(c) data minimization
/// och Breyer-domen (C-582/14). Anonymiseringen är delegerad till
/// <see cref="IIpAnonymizer"/> så att audit-tabellen och app-loggen
/// (<c>AuthAuditLogger</c>) använder identisk maskning.
///
/// User-Agent trunkeras till 256 tecken (matchar audit_log.user_agent-kolumnens
/// längdbegränsning).
/// </summary>
public sealed class RequestContextProvider(
    IHttpContextAccessor httpContextAccessor,
    IIpAnonymizer ipAnonymizer)
    : IRequestContextProvider
{
    private const int MaxUserAgentLength = 256;

    public string? IpAddress
    {
        get
        {
            var raw = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
            return raw is null ? null : ipAnonymizer.Anonymize(raw);
        }
    }

    public string? UserAgent
    {
        get
        {
            var raw = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Length > MaxUserAgentLength ? raw[..MaxUserAgentLength] : raw;
        }
    }
}
