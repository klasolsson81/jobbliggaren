using System.Net;

namespace JobbPilot.Application.Common.Auditing;

/// <summary>
/// Anonymiserar IP-adress per GDPR Art. 5(1)(c) data minimisation och
/// Breyer-domen (C-582/14). Används både av audit-pipelinen
/// (<see cref="IRequestContextProvider"/>) och app-loggen
/// (<c>AuthAuditLogger</c>) så samma maskning gäller överallt.
///
/// Per ADR 0024 delbeslut 7:
/// - IPv4: sista oktetten nollas (/24-mask).
/// - IPv6: sista 80 bitarna nollas (/48-mask).
/// - IPv4-mapped-IPv6 normaliseras till IPv4 före maskning.
/// </summary>
public interface IIpAnonymizer
{
    /// <summary>
    /// Etikett som loggas/sparas när IP-adress inte kan tolkas (okänd
    /// adressfamilj eller null-input via OrUnknown-overloaden).
    /// Delad konstant så audit-pipelinen och app-loggen aldrig drift:ar
    /// isär på fallback-strängen.
    /// </summary>
    public const string UnknownLabel = "unknown";

    /// <summary>
    /// Returnerar den maskade strängrepresentationen. För adressfamiljer
    /// utanför IPv4/IPv6 returneras <see cref="UnknownLabel"/> hellre än
    /// rå adress.
    /// </summary>
    string Anonymize(IPAddress address);
}
