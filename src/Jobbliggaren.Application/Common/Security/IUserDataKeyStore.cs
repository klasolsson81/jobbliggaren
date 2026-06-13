using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1) — port mot per-användare-DEK-lagret. Returnerar
/// aldrig <c>UserDataKey</c>-typen (Infrastructure-intern, FRÅGA 2) — bara
/// <see cref="JobSeekerId"/> in och plaintext-DEK (<c>byte[]</c>) ut.
///
/// Fail-closed: KMS-fel propageras — aldrig en default/klartext-fallback-DEK
/// (CTO-domen 2026-05-18). Anroparen äger den returnerade bufferten och bör
/// nolla den efter bruk (cachen nollar sin egen kopia vid scope-dispose).
/// </summary>
public interface IUserDataKeyStore
{
    /// <summary>
    /// Hämtar (eller skapar vid första behov) användarens DEK och returnerar
    /// den unwrappad. Skapar + persisterar wrapped-DEK (KMS GenerateDataKey)
    /// om ingen rad finns; annars unwrappas befintlig (KMS Decrypt). Memoiseras
    /// per scope via <see cref="IUserDataKeyCache"/> (en KMS-op per användare
    /// per scope).
    /// </summary>
    Task<byte[]> GetOrCreateDataKeyAsync(JobSeekerId owner, CancellationToken ct);

    /// <summary>
    /// Crypto-erasure (ADR 0049 Beslut 2): raderar alla DEK-rader (alla
    /// versioner) för <paramref name="owner"/>. Idempotent — 0 rader = no-op,
    /// kastar ej. Anropas av C6 inom hard-delete-transaktionen
    /// (<c>AccountHardDeleter</c>) så raderingen är atomär med aggregat-delete.
    /// </summary>
    Task DeleteDataKeysAsync(JobSeekerId owner, CancellationToken ct);
}
