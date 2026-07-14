using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// Fail-safe, multi-channel matching for the Art. 17 erasure command (ADR 0106 D8, #842).
/// </summary>
/// <remarks>
/// <b>Raw, parameterised SQL — and it is not laziness.</b> Two of the scanned columns are
/// <c>jsonb</c>, and <c>lower(jsonb)</c> <b>does not exist in PostgreSQL</b> (verified against the
/// dev catalog, PG 18.3). <c>websearch_to_tsquery</c>'s two-arg overload is <c>(regconfig, text)</c>,
/// so a bound text parameter does <b>not</b> implicitly cast — without <c>::regconfig</c> the FTS
/// channel throws on the first real Art. 17 request. And the word-boundary match needs Postgres's
/// ARE regex, which has no LINQ equivalent that survives a provider swap.
/// <para>
/// Every value is bound via <c>FormattableString</c> interpolation ⇒ <c>DbParameter</c>s. No
/// concatenation (CLAUDE.md §5). The channel rationale lives on the port.
/// </para>
/// </remarks>
internal sealed class RecruiterErasureMatchQuery(AppDbContext db) : IRecruiterErasureMatchQuery
{
    // MUST be the config search_vector was generated with (JobAdConfiguration:
    // to_tsvector('swedish', …)). A mismatch makes `@@` miss the GIN index and the FTS channel
    // returns nothing at all — a vacuous matcher, which is precisely the defect this command
    // replaces.
    private const string TextSearchConfig = JobAdSearchComposition.TextSearchConfig;

    // LIKE metacharacters. `_` is legal and common in email local parts (anna_k@acme.se) and would
    // otherwise be a single-character wildcard; `%` would match the whole corpus. ESCAPE '\' is
    // stated explicitly rather than relying on the backslash default.
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

    /// <summary>
    /// A case-insensitive ARE pattern that matches <paramref name="identifier"/> only as a WHOLE
    /// WORD.
    /// </summary>
    /// <remarks>
    /// <b>Why lookaround and not <c>\m…\M</c>, which is what the ruling named.</b> Postgres defines
    /// a word character as a letter, digit or underscore, and <c>\m</c> can only match at a position
    /// immediately BEFORE one. An identifier that starts with a non-word character — a phone number
    /// such as <c>+46701234567</c> — would put <c>\m</c> in front of <c>+</c>, where it can never
    /// match. The regex would then return zero rows, silently, on every request, and the reply would
    /// tell a named person we hold nothing of hers. <b>That is the exact defect class this command
    /// exists to end, and it would have been reintroduced by the fix for it.</b>
    /// <para>
    /// <c>(?&lt;![[:alnum:]_])x(?![[:alnum:]_])</c> says "not preceded and not followed by a word
    /// character", which is satisfiable whatever the identifier starts with, and reduces to
    /// <c>\m…\M</c> when it starts and ends with one. Where the two differ (a locale whose
    /// <c>[:alnum:]</c> excludes <c>å</c>), it errs toward OVER-matching — and the operator sees
    /// every matched string in the dry run before anything is deleted.
    /// </para>
    /// </remarks>
    private static string WordBoundaryPattern(string identifier) =>
        $"(?<![[:alnum:]_]){EscapeAre(identifier.Trim())}(?![[:alnum:]_])";

    /// <summary>
    /// Quote a literal for a Postgres ARE. <b>Not <c>Regex.Escape</c></b> — that is built for the
    /// .NET flavor, and it leaves <c>]</c> and <c>}</c> unescaped. A near-miss on a destructive
    /// query is the defect class this issue is about.
    /// </summary>
    /// <remarks>
    /// The rule comes straight from the PG docs and is total in both directions: <i>"\k (where k is
    /// a non-alphanumeric character) matches that character taken as an ordinary character"</i>
    /// (Table 9.17), and <i>"a \ followed by an alphanumeric character but not constituting a valid
    /// escape is illegal in AREs"</i> (§9.7.3.3). So: <b>escape every non-alphanumeric, never an
    /// alphanumeric.</b> A blocklist of metacharacters would miss ARE's own escapes (<c>\d</c>,
    /// <c>\m</c>, <c>\y</c>, <c>(?…)</c>), which is the detector-is-not-the-matcher trap.
    /// <para>
    /// <c>char.IsLetterOrDigit</c> is Unicode-aware, so <c>å</c>/<c>ä</c>/<c>ö</c> are correctly left
    /// alone (escaping them would be illegal per the quote above). <c>magnus@skill.se</c> becomes
    /// <c>magnus\@skill\.se</c> — the <c>.</c> is neutralised, which is the whole point.
    /// </para>
    /// </remarks>
    private static string EscapeAre(string value)
    {
        var sb = new StringBuilder(value.Length * 2);

        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
                sb.Append('\\');

            sb.Append(c);
        }

        return sb.ToString();
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
        // projected through EF.
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
                Company = j.Company.Name,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r =>
            {
                var (channel, excerpt) = Evidence(r.Title, r.Description, r.Company, needle);
                return new ErasureJobAdMatch(r.Id, r.ExternalId, r.Title, r.Company, channel, excerpt);
            }),
        ];
    }

    /// <summary>
    /// The reviewer's evidence: WHICH channel hit, and the text around it.
    /// </summary>
    /// <remarks>
    /// <b>When there is no literal hit, we say so — we do not substitute an unrelated window.</b> An
    /// earlier version returned the first 200 characters of the body whenever the needle was not in
    /// it, which is precisely what happens on the FTS channel (<i>"Fagerberg, Magnus"</i>) and on the
    /// <c>company_name</c> channel — the two that were ADDED because they were missing. The operator
    /// would have been shown a window with no trace of her in it and no way to tell that from a false
    /// positive. This is the one gate between him and irreversible corpus-wide destruction, and a
    /// window with no hit in it is evidence of nothing.
    /// </remarks>
    private static (ErasureMatchChannel Channel, string Excerpt) Evidence(
        string title, string? description, string company, string needle)
    {
        const int Window = 200;
        const int Lead = 60;

        if (!string.IsNullOrWhiteSpace(description))
        {
            var at = description.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var start = Math.Max(0, at - Lead);
                var length = Math.Min(Window, description.Length - start);
                return (ErasureMatchChannel.Description, description.Substring(start, length).Trim());
            }
        }

        if (title.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return (ErasureMatchChannel.Title, title);

        if (company.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return (ErasureMatchChannel.CompanyName, company);

        // The hit came from the FTS lexemes or from raw_payload. There is no literal substring to
        // window, and pretending otherwise is the failure above.
        return (ErasureMatchChannel.FullTextOrRawPayload, string.Empty);
    }

    public async Task<IReadOnlyList<ErasureRecentSearchMatch>> FindRecentJobSearchesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = WordBoundaryPattern(identifier);

        // `~*` = case-insensitive ARE match. `q` is a plain varchar(100), so no cast is needed.
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM recent_job_searches
                WHERE q IS NOT NULL AND q ~* {pattern}
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        var typedIds = ids.Select(id => new RecentJobSearchId(id)).ToList();

        var rows = await db.RecentJobSearches
            .AsNoTracking()
            .Where(r => typedIds.Contains(r.Id))
            .Select(r => new { Id = r.Id.Value, r.Q })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .Where(r => r.Q is not null)
                .Select(r => new ErasureRecentSearchMatch(r.Id, r.Q!)),
        ];
    }

    public async Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // Deliberately NOT filtered on deleted_at. A soft-deleted saved search still physically
        // holds `criteria` in the row (SoftDelete() hides it; it does not erase it). Reporting only
        // the live ones would under-count what we actually hold — the whole failure mode here.
        //
        // `name` is a separate plaintext column, and a user who names a saved search "Anna Karlssons
        // annonser" holds the recruiter's name in it. It was classified as searched and was not.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM saved_searches
            WHERE lower(criteria::text)          LIKE {pattern} ESCAPE '\'
               OR lower(coalesce(name, ''))      LIKE {pattern} ESCAPE '\'
            """, cancellationToken);
    }

    public async Task<int> CountApplicationSnapshotsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // snapshot_company is NON-NULLABLE — it is populated on EVERY application, unlike
        // snapshot_description (measured at 0 rows), which is the one the original scope reasoned
        // about. We search it precisely because we do NOT erase it (Art. 17(3)(e) — see the
        // registry's written ground).
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE lower(coalesce(snapshot_company, ''))     LIKE {pattern} ESCAPE '\'
               OR lower(coalesce(snapshot_title, ''))       LIKE {pattern} ESCAPE '\'
               OR lower(coalesce(snapshot_description, '')) LIKE {pattern} ESCAPE '\'
            """, cancellationToken);
    }

    public async Task<int> CountManualAdEntriesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // ONLY the plaintext columns. cover_letter sits in this same table and is Form-A encrypted —
        // it is NOT scanned here (registry: HeldButNotSearchable; disclosed via UnsearchableSurfaces).
        // A LIKE against it would compare her name to base64 and return 0, forever.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE lower(coalesce(manual_company, '')) LIKE {pattern} ESCAPE '\'
               OR lower(coalesce(manual_title, ''))   LIKE {pattern} ESCAPE '\'
               OR lower(coalesce(manual_url, ''))     LIKE {pattern} ESCAPE '\'
            """, cancellationToken);
    }

    public async Task<int> CountCompanyWatchCriteriaAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // Deliberately NOT filtered on deleted_at — same reasoning as CountSavedSearchesAsync: a
        // soft-deleted row still physically HOLDS the label, and reporting only the live ones would
        // under-count what we actually hold.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM company_watch_criteria
            WHERE lower(coalesce(label, '')) LIKE {pattern} ESCAPE '\'
            """, cancellationToken);
    }

    public async Task<int> CountApplicationsReferencingAsync(
        IReadOnlyCollection<Guid> matchedJobAdIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(matchedJobAdIds);

        if (matchedJobAdIds.Count == 0)
            return 0;

        // Raw SQL with `= ANY(uuid[])`: applications.job_ad_id is a nullable strongly-typed VO
        // (JobAdId?), and EF cannot parameterise a List<JobAdId?> inside Contains — it falls back to
        // client evaluation and throws. The array parameter sidesteps the VO entirely.
        var ids = matchedJobAdIds.ToArray();

        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE job_ad_id = ANY({ids})
            """, cancellationToken);
    }

    private async Task<int> CountAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        var counts = await db.Database.SqlQuery<int>(sql).ToListAsync(cancellationToken);
        return counts.Count > 0 ? counts[0] : 0;
    }
}
