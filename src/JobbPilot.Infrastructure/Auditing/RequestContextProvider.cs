using System.Net;
using System.Net.Sockets;
using JobbPilot.Application.Common.Auditing;
using Microsoft.AspNetCore.Http;

namespace JobbPilot.Infrastructure.Auditing;

/// <summary>
/// Producerar IP-adress + User-Agent för audit-rad. Per ADR 0022.
///
/// IP-adressen anonymiseras före lagring per GDPR Art. 5(1)(c) data minimization
/// och Breyer-domen (C-582/14) som klassificerar IP som personuppgift:
/// - IPv4: sista oktetten nollas (/24-mask) — bevarar geo-region för
///   incident-response, eliminerar unique fingerprint.
/// - IPv6: sista 80 bitarna nollas (/48-mask) — motsvarande granularitet.
///
/// User-Agent trunkeras till 256 tecken (matchar AuthAuditLogger-konventionen
/// och audit_log.user_agent-kolumnens längdbegränsning).
/// </summary>
public sealed class RequestContextProvider(IHttpContextAccessor httpContextAccessor)
    : IRequestContextProvider
{
    private const int MaxUserAgentLength = 256;

    public string? IpAddress
    {
        get
        {
            var raw = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
            return raw is null ? null : Anonymize(raw);
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

    /// <summary>
    /// Maskar IP-adressen så att den inte är unikt identifierande men
    /// fortfarande användbar för geo-region-spårning vid incident-response.
    /// </summary>
    private static string Anonymize(IPAddress address)
    {
        // Mappa IPv4-mapped IPv6 (::ffff:1.2.3.4) tillbaka till IPv4-form
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            // IPv4 /24 — nolla sista oktetten
            bytes[3] = 0;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            // IPv6 /48 — nolla sista 80 bitarna (10 byte)
            for (var i = 6; i < 16; i++) bytes[i] = 0;
        }
        else
        {
            // Okänd familj — returnera oanvändbar placeholder snarare än rå adress
            return "unknown";
        }

        return new IPAddress(bytes).ToString();
    }
}
