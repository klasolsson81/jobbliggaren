using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// TD-94 (perf-ratchet, ADR 0045 Klass (a) 300 ms p95 warm) — coax the planner to the GIN bitmap for a
/// q-COUNT. A bare COUNT over the FTS-hybrid q-predikatet otherwise Seq Scans and de-TOASTs the wide
/// STORED search_vector column per row (~300–2451 ms warm / ~9 s OS-cold; isolerat bevisat: detoast-delta
/// 487 ms, dotnet-architect-rond 2026-06-13). The GIN Bitmap(Or) plan avoids the detoast (&lt;150 ms warm)
/// men planeraren mis-kostar den eftersom TOAST-detoast-kostnaden inte finns i dess kostnadsmodell.
///
/// <para>
/// <c>SET LOCAL enable_seqscan = off</c> är transaktions-scopad: den MÅSTE köras på SAMMA pinnade
/// connection som counten (annars no-op utanför transaktionsblock) och återställs vid commit → läcker
/// aldrig till den poolade connectionen (Npgsql pooling-hygien). Rör inte filter-predikatet → ADR 0039
/// Beslut 1 SPOT på filter-semantik intakt; detta är en exekverings-budget-concern, ett annat ansvar
/// (SoC, senior-cto-advisor-dom 2026-06-13, agentId a0472fa5783cdf9ea).
/// </para>
///
/// <para>
/// #744 — the hygiene is worth its BEGIN/SET LOCAL/COMMIT roundtrips ONLY when the q-FTS predicate is
/// present (<see cref="JobAdSearchComposition.HasFreeTextQuery"/>): that is the sole TOAST-detoast source.
/// <paramref name="useBitmapPlanHygiene"/> gates it — a no-q count runs bare (no transaction), a q-count
/// runs under the GUC. Shared by <see cref="JobAdSearchQuery"/> and <see cref="PerUserJobAdSearchQuery"/>
/// so both count families gate on q identically (DRY — one hygiene implementation, one q-gate).
/// </para>
/// </summary>
internal static class BitmapPlanCount
{
    public static async Task<TResult> CountWithBitmapPlanAsync<TResult>(
        AppDbContext db,
        bool useBitmapPlanHygiene,
        Func<CancellationToken, Task<TResult>> count,
        CancellationToken cancellationToken)
    {
        // No q-FTS predicate → no TOAST-detoast risk → skip the transaction entirely (#744): a no-q browse
        // count no longer pays BEGIN + SET LOCAL + COMMIT for a planner hint it does not need.
        if (!useBitmapPlanHygiene)
            return await count(cancellationToken);

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL enable_seqscan = off", cancellationToken);
        var result = await count(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
