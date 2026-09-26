namespace Jobbliggaren.Application.Dev.Abstractions;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Which addresses the Development seed seam
/// (<c>POST /api/v1/dev/accounts</c>, ADR 0142 part 5a) may open an account for: only an address at a domain
/// RFC 2606/6761 reserves, which cannot be anyone's mailbox.
/// <para>
/// It is also the seam's second structural gate. The implementation is registered in DI ONLY in Development
/// (<c>AddDevOnlyTestingSupport</c>), so outside it <c>DevSeedAccountCommandHandler</c> cannot resolve; the
/// first gate is the endpoint map under <c>IsDevelopment()</c>. The account writer the seam calls exists in every
/// environment, so without this port a handler built from production services alone would resolve in
/// Production and leave the map as the only gate.
/// </para>
/// </summary>
public interface IDevSeedableAddressPolicy
{
    bool IsSeedable(string email);
}
