using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1) — per-användare data-encryption-key via envelope
/// encryption. En DEK per <see cref="JobSeekerId"/>; DEK:en wrappas av providern
/// (lokal AES-256-GCM master-nyckel efter #802/ADR 0066 — tidigare en AWS-KMS
/// CMK). Plaintext-DEK existerar bara i minnet under en krypto-operation och
/// nollas efter bruk (Infrastructure-impl-ansvar).
///
/// Fail-closed: provider-fel propageras som exception — returnerar aldrig en
/// default/tom/klartext-DEK (CTO-domen 2026-05-18, ADR 0049 Beslut 4).
/// </summary>
public interface IDataKeyProvider
{
    /// <summary>
    /// Genererar en ny DEK för <paramref name="owner"/> och wrappar den via
    /// providern. Returnerar plaintext- + wrapped-DEK + key-id. Kastar (ingen
    /// halvfärdig <see cref="GeneratedDataKey"/>) vid provider-fel.
    /// </summary>
    Task<GeneratedDataKey> CreateDataKeyAsync(JobSeekerId owner, CancellationToken ct);

    /// <summary>
    /// Unwrappar en lagrad wrapped-DEK för <paramref name="owner"/> via providern
    /// (owner-AAD/encryption-context binder DEK:en till ägaren). Kastar vid
    /// provider-fel — returnerar aldrig en fallback-DEK.
    /// </summary>
    Task<byte[]> UnwrapDataKeyAsync(JobSeekerId owner, byte[] wrappedDek, CancellationToken ct);
}

/// <summary>
/// Resultat av <see cref="IDataKeyProvider.CreateDataKeyAsync"/>.
/// <see cref="PlaintextDek"/> ska nollas av anroparen efter bruk.
/// </summary>
public readonly record struct GeneratedDataKey(
    byte[] PlaintextDek,
    byte[] WrappedDek,
    string CmkKeyId);
