using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// Fail-safe, multi-channel matching for the Art. 17 erasure command (ADR 0106 D8, #842).
/// </summary>
/// <remarks>
/// <b>Raw, parameterised SQL — and it is not laziness.</b> Two of the scanned columns are
/// <c>jsonb</c>, and <c>lower(jsonb)</c> <b>does not exist in PostgreSQL</b> (verified against the
/// dev catalog, PG 18.3). And <c>websearch_to_tsquery</c>'s two-arg overload is
/// <c>(regconfig, text)</c>, so a bound text parameter does <b>not</b> implicitly cast — without
/// <c>::regconfig</c>, channel 1 throws on the first real Art. 17 request. Writing the SQL we
/// actually mean beats discovering at 02:00 which half of the scan silently stopped running.
/// <para>
/// Every value is bound via <c>FormattableString</c> interpolation ⇒ <c>DbParameter</c>s. No
/// concatenation (CLAUDE.md §5). The channel rationale lives on the port.
/// </para>
/// </remarks>
internal sealed class RecruiterErasureMatchQuery(AppDbContext db) : IRecruiterErasureMatchQuery
{
    // MUST be the config search_vector was generated with (JobAdConfiguration:
    // to_tsvector('swedish', …)). A mismatch makes `@@` miss the GIN index and channel 1 returns
    // nothing at all — a vacuous matcher, which is precisely the defect this command replaces.
    private const string TextSearchConfig = JobAdSearchComposition.TextSearchConfig;

    // LIKE metacharacters. `_` is legal and common in email local parts (anna_k@acme.se) and would
    // otherwise be a single-character wildcard; `%` would match the whole corpus. The direction of
    // the error is fail-safe (it over-matches, and the dry run shows it) — but a silent widening of
    // a destructive query is not something to leave to luck. ESCAPE '\' is stated explicitly rather
    // than relying on the backslash default.
    private const char LikeEscape = '\\';

    private static string LikePattern(string identifier)
    {
        var escaped = identifier
            .Trim()
            .ToLowerInvariant()
            .Replace(LikeEscape.ToString(), @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }

    public async Task<IReadOnlyList<ErasureJobAdMatch>> FindJobAdsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var needle = identifier.Trim();
        var pattern = LikePattern(identifier);
        var erased = Domain.JobAds.JobAdStatus.Erased.Value;

        // The matching itself is raw SQL (it has to be — see the class remarks), and it yields IDs.
        // EF's Database.SqlQuery<T> supports SCALAR results only, so the ads themselves are then
        // projected through EF. That split is not a workaround: the SQL does the part only SQL can
        // do, and the aggregate stays the thing the rest of the code talks to.
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM job_ads
                WHERE status <> {erased}
                  AND (
                        search_vector @@ websearch_to_tsquery({TextSearchConfig}::regconfig, {needle})
                     OR lower(title)        LIKE {pattern} ESCAPE '\'
                     OR lower(description)  LIKE {pattern} ESCAPE '\'
                     OR lower(company_name) LIKE {pattern} ESCAPE '\'
                     OR (raw_payload IS NOT NULL AND lower(raw_payload::text) LIKE {pattern} ESCAPE '\')
                  )
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        // The operator reviews ADS, not a number — so he gets the title and an excerpt around the
        // hit, enough to tell a real match from "Anna" inside "Marianna". The excerpt IS the
        // reviewer's evidence, and reviewing is the control.
        // Contains over the strongly-typed VO, not over j.Id.Value — EF cannot translate a member
        // access on the value object inside Contains (it falls back to client evaluation and throws).
        var typedIds = ids.Select(id => new Domain.JobAds.JobAdId(id)).ToList();

        var rows = await db.JobAds
            .AsNoTracking()
            .Where(j => typedIds.Contains(j.Id))
            .Select(j => new
            {
                Id = j.Id.Value,
                ExternalId = j.External != null ? j.External.ExternalId : null,
                j.Title,
                j.Description,
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new ErasureJobAdMatch(
            r.Id, r.ExternalId, r.Title, Excerpt(r.Description, needle)))];
    }

    /// <summary>
    /// A window around the hit, so a human can judge it. Falls back to the head of the body when the
    /// match was not in the description (FTS lexemes, or a hit in company_name / raw_payload).
    /// </summary>
    private static string Excerpt(string? description, string needle)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        const int Window = 200;
        const int Lead = 60;

        var at = description.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        var start = at < 0 ? 0 : Math.Max(0, at - Lead);
        var length = Math.Min(Window, description.Length - start);

        return description.Substring(start, length).Trim();
    }

    public async Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // Deliberately NOT filtered on deleted_at. A soft-deleted saved search still physically
        // holds `criteria` in the row (SoftDelete() hides it; it does not erase it). Reporting only
        // the live ones would under-count what we actually hold — the whole failure mode here.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM saved_searches
            WHERE lower(criteria::text) LIKE {pattern} ESCAPE '\'
            """, cancellationToken);
    }

    public async Task<int> CountApplicationSnapshotsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // snapshot_company is NON-NULLABLE — it is populated on EVERY application, unlike
        // snapshot_description (measured at 0 rows). We search it precisely because we do NOT erase
        // it: a legal ground (Art. 17(3)(e)) asserted over a population we never counted is a ground
        // asserted over a silence.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE lower(coalesce(snapshot_company, ''))     LIKE {pattern} ESCAPE '\'
               OR lower(coalesce(snapshot_description, '')) LIKE {pattern} ESCAPE '\'
            """, cancellationToken);
    }

    public async Task<int> CountUserAuthoredTextAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // A user can absolutely have written "Ringde Magnus Fagerberg" in her own note about her own
        // application. That is the RECRUITER'S personal data, in a place nobody enumerated until the
        // cascade registry was driven from the EF model instead of from memory. Her right reaches it
        // (Art. 6(1)(f) → Art. 21(1)); a HUMAN erases it, because silently rewriting a user's private
        // note about her own job hunt is not something a job may do.
        return await CountAsync($"""
            SELECT (
                (SELECT count(*) FROM applications
                  WHERE lower(coalesce(cover_letter, ''))   LIKE {pattern} ESCAPE '\'
                     OR lower(coalesce(manual_company, '')) LIKE {pattern} ESCAPE '\'
                     OR lower(coalesce(manual_title, ''))   LIKE {pattern} ESCAPE '\')
              + (SELECT count(*) FROM application_notes
                  WHERE lower(coalesce(content, '')) LIKE {pattern} ESCAPE '\')
              + (SELECT count(*) FROM follow_ups
                  WHERE lower(coalesce(note, '')) LIKE {pattern} ESCAPE '\')
            )::int AS "Value"
            """, cancellationToken);
    }

    private async Task<int> CountAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        var counts = await db.Database.SqlQuery<int>(sql).ToListAsync(cancellationToken);
        return counts.Count > 0 ? counts[0] : 0;
    }
}
