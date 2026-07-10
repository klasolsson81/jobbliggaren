using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1, CTO-triage FRÅGA 1) — scoped DEK-cache.
/// Registreras <c>Scoped</c>; lever och dör med SaveChanges-/request-scopet.
/// Cachen äger sina plaintext-buffrar och <c>ZeroMemory</c>:ar dem vid
/// <see cref="Dispose"/> (C1-gate security Minor 2). Anroparen får alltid en
/// oberoende kopia — caller-bufferten påverkas inte av scope-dispose.
///
/// Den interna ytan (<see cref="UnwrapCountFor"/>, <see cref="TryPeekCachedDek"/>,
/// <see cref="LastDisposedBuffersAllZeroed"/>) är <c>internal</c> (Seam 3 — ej
/// på prod-porten; §5.4: plaintext-DEK-peek får aldrig finnas på publik yta).
/// <see cref="TryPeekCachedDek"/> är även den synkrona in-assembly-läsvägen för
/// prod-konsumenterna <c>FieldDecryptionMaterializationInterceptor</c> (read,
/// EF:s InitializedInstance är synkron) och <c>BinaryFieldSealer</c> (Form C
/// write-seal, Fas 4b PR-9a) — §3.5 förbjuder sync-over-async, så båda peekar
/// den DEK prefetch-behaviorn redan värmt i stället för att gå async-porten.
/// Synlig för Worker-integ via <c>[InternalsVisibleTo]</c>.
/// </summary>
public sealed class ScopedUserDataKeyCache : IUserDataKeyCache
{
    private readonly Dictionary<JobSeekerId, byte[]> _cache = [];
    private readonly Dictionary<JobSeekerId, int> _unwrapCounts = [];
    private bool _disposed;

    /// <summary>Test-observerbarhet (Seam 3): true om alla cachade buffrar
    /// verifierat nollställdes vid senaste <see cref="Dispose"/>.</summary>
    internal bool LastDisposedBuffersAllZeroed { get; private set; }

    public async Task<byte[]> GetOrUnwrapAsync(
        JobSeekerId owner,
        Func<Task<byte[]>> unwrapFactory,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(unwrapFactory);

        if (_cache.TryGetValue(owner, out var cached))
        {
            return (byte[])cached.Clone();
        }

        // Fail-closed: om factory:n kastar (KMS-fel) cachas INGENTING —
        // ingen klartext/default-DEK (ADR 0049 Beslut 4, CTO-domen).
        var dek = await unwrapFactory().ConfigureAwait(false);

        try
        {
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            // Low1 (epic #480): på cancellation EFTER unwrap men FÖRE cachning
            // memoiseras den färsk-unwrappade plaintext-DEK:en aldrig → Dispose
            // fångar den inte. Nolla den här så nyckelmaterial inte läcker
            // o-zeroat. Success-vägen memoiserar oförändrat (dispose nollar då).
            CryptographicOperations.ZeroMemory(dek);
            throw;
        }

        _cache[owner] = dek; // cache-ägd buffert (nollas vid dispose)
        _unwrapCounts[owner] = _unwrapCounts.GetValueOrDefault(owner) + 1;

        return (byte[])dek.Clone();
    }

    // ── internal test-observerbarhet (Seam 3) ───────────────────────────────

    internal int UnwrapCountFor(JobSeekerId owner) =>
        _unwrapCounts.GetValueOrDefault(owner);

    internal bool TryPeekCachedDek(JobSeekerId owner, out byte[] live) =>
        _cache.TryGetValue(owner, out live!);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var allZeroed = true;
        foreach (var buffer in _cache.Values)
        {
            CryptographicOperations.ZeroMemory(buffer);
            if (Array.Exists(buffer, b => b != 0))
            {
                allZeroed = false;
            }
        }

        LastDisposedBuffersAllZeroed = _cache.Count == 0 || allZeroed;
        _cache.Clear();
        _disposed = true;
    }
}
