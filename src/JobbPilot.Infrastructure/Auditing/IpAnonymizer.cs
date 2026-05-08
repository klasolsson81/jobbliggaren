using System.Net;
using System.Net.Sockets;
using JobbPilot.Application.Common.Auditing;

namespace JobbPilot.Infrastructure.Auditing;

/// <summary>
/// Implementation av <see cref="IIpAnonymizer"/>. Bevarar geo-region för
/// incident-response men eliminerar unik fingerprint. Logiken låg tidigare
/// inline i <see cref="RequestContextProvider"/>; lyft till port per
/// ADR 0024 delbeslut 7 så att <c>AuthAuditLogger</c> kan dela maskningen.
/// </summary>
public sealed class IpAnonymizer : IIpAnonymizer
{
    public string Anonymize(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            bytes[3] = 0;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            for (var i = 6; i < 16; i++)
            {
                bytes[i] = 0;
            }
        }
        else
        {
            return IIpAnonymizer.UnknownLabel;
        }

        return new IPAddress(bytes).ToString();
    }
}
