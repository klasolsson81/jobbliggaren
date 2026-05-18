using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1, CTO-triage FRÅGA 1) — scoped memoisering av
/// unwrappad plaintext-DEK per <see cref="JobSeekerId"/> inom ETT
/// SaveChanges-/request-scope. Undviker ett KMS <c>Decrypt</c>-anrop per rad
/// (latens/kostnad/throttle). <b>Ingen process-wide static / <c>AsyncLocal</c></b>
/// — nyckelmaterialets livslängd = scopets livslängd.
///
/// <see cref="IDisposable"/>: <c>Dispose()</c> <c>CryptographicOperations
/// .ZeroMemory</c>:ar varje cachad plaintext-buffert (C1-gate security
/// Minor 2). Test-observerbarhet (unwrap-count, peek, zeroed-flagga) ligger
/// EJ på denna prod-port — den finns som <c>internal</c> på den konkreta
/// <c>ScopedUserDataKeyCache</c> (Seam 3, ISP/§5.4 — läck ej internals/
/// nyckelmaterial i prod-API).
/// </summary>
public interface IUserDataKeyCache : IDisposable
{
    /// <summary>
    /// Returnerar en kopia av den cachade plaintext-DEK:en för
    /// <paramref name="owner"/>; kör annars <paramref name="unwrapFactory"/>
    /// (KMS unwrap/create) exakt en gång, cachar resultatet och returnerar en
    /// kopia. Cachen äger sin buffert (nollas vid dispose); anroparen får en
    /// oberoende kopia.
    /// </summary>
    Task<byte[]> GetOrUnwrapAsync(
        JobSeekerId owner,
        Func<Task<byte[]>> unwrapFactory,
        CancellationToken ct);
}
