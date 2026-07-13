using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// Two-channel, fail-safe matching for the Art. 17 erasure command (ADR 0106 D8, #842).
/// </summary>
/// <remarks>
/// <b>Raw, parameterised SQL — and it is not laziness.</b> Two of the three columns that must be
/// scanned are <c>jsonb</c>, and <c>lower(jsonb)</c> <b>does not exist in PostgreSQL</b> (verified
/// against the dev catalog, PG 18.3: <c>ERROR: function lower(jsonb) does not exist</c>). An
/// EF-LINQ <c>.ToLower().Contains(...)</c> over <c>raw_payload</c> or <c>criteria</c> does not
/// translate — so the channel that reaches <c>employer.name</c> would have failed at runtime, or
/// worse, been quietly dropped. An explicit <c>::text</c> cast is the only correct form, and
/// writing the SQL we actually mean beats discovering at 02:00 which half of the scan silently
/// stopped running.
/// <para>
/// Every value is passed as a parameter via <c>FormattableString</c> interpolation — EF builds
/// <c>DbParameter</c>s, no concatenation (CLAUDE.md §5).
/// </para>
/// </remarks>
internal sealed class RecruiterErasureMatchQuery(AppDbContext db) : IRecruiterErasureMatchQuery
{
    // MUST be the config search_vector was generated with (JobAdConfiguration:
    // to_tsvector('swedish', …)). A mismatch makes `@@` miss the GIN index and channel 1 returns
    // nothing at all — a vacuous matcher, which is precisely the defect this command replaces.
    private const string TextSearchConfig = JobAdSearchComposition.TextSearchConfig;

    public async Task<IReadOnlyList<Guid>> FindJobAdIdsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var needle = identifier.Trim();
        var pattern = $"%{needle.ToLowerInvariant()}%";
        var erased = Domain.JobAds.JobAdStatus.Erased.Value;

        // Channel 1: FTS (the reverse-lookup exposure being closed).
        // Channel 2: substring over title + description + raw_payload::text (obfuscation, and
        //            employer.name — see the interface docs).
        // The ::regconfig cast is REQUIRED, not decoration: websearch_to_tsquery's two-arg overload
        // is (regconfig, text), and a bound text parameter does not implicitly cast to regconfig.
        // Verified against the dev catalog (PG 18.3): without it,
        // `ERROR: function websearch_to_tsquery(text, text) does not exist` — i.e. channel 1 would
        // have thrown on the first real Art. 17 request.
        //
        // The `AS "Value"` alias is EF's contract for Database.SqlQuery<T> over a scalar.
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM job_ads
                WHERE status <> {erased}
                  AND (
                        search_vector @@ websearch_to_tsquery({TextSearchConfig}::regconfig, {needle})
                     OR lower(title) LIKE {pattern}
                     OR lower(description) LIKE {pattern}
                     OR (raw_payload IS NOT NULL AND lower(raw_payload::text) LIKE {pattern})
                  )
                """)
            .ToListAsync(cancellationToken);

        return ids;
    }

    public async Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = $"%{identifier.Trim().ToLowerInvariant()}%";

        // NOTE — this deliberately does NOT filter on deleted_at. A soft-deleted saved search
        // still physically holds `criteria` in the row (SavedSearch.SoftDelete() hides it; it does
        // not erase it). Reporting only the live ones would under-count what we actually hold,
        // which is the whole failure mode of this issue. Counted, never erased —
        // ErasureCascadeRegistry.MatchedButNotErased carries the ground.
        var counts = await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM saved_searches
                WHERE lower(criteria::text) LIKE {pattern}
                """)
            .ToListAsync(cancellationToken);

        return counts.Count > 0 ? counts[0] : 0;
    }
}
