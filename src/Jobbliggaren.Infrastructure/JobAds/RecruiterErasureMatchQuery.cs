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
    // otherwise be a single-character wildcard; `%` would match the whole corpus.
    private const char LikeEscape = '\\';

    // The ESCAPE clause every LIKE in this class carries — DERIVED from LikeEscape and bound as a
    // parameter (Postgres accepts any text expression after ESCAPE), so the pattern builder and
    // the clause are one definition. Round 4 hand-typed the literal on 18 lines and lost the
    // backslash on two of them: `ESCAPE ''` disables escaping silently, `\_` then matches a
    // literal backslash, and the identifier `anna_k@acme.se` stops matching its own row — the
    // regression the metacharacter integration test now holds red.
    private static readonly string LikeEscapeSql = LikeEscape.ToString();

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

        // Runes, not chars (round-3 security m1, discharged round 6): enumerating UTF-16 code
        // units puts a backslash BETWEEN the halves of a surrogate pair — the broken half-pair
        // round-trips to U+FFFD and the destructive query silently matches nothing. Enumerating
        // scalar values keeps a non-BMP character (an emoji in a company name) intact. Rune's
        // IsLetterOrDigit is the same Unicode classification char used for the BMP, so å/ä/ö
        // behave exactly as before.
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune))
                sb.Append('\\');

            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }

    /// <summary>
    /// The identifier as a normalised org.nr, when it IS one — the Domain VO owns the written
    /// forms (<c>556012-5790</c> → <c>5560125790</c>). Null means "not org.nr-shaped": the caller
    /// falls back to the free-text channels, never to a guess.
    /// </summary>
    /// <remarks>
    /// #842 CTO ruling (2026-07-14): org.nr/personnummer is a first-class Art. 17 identifier — an
    /// enskild firma's org.nr IS her personnummer, and it is a STRUCTURED key with a dedicated
    /// column. Round 5 bolted it into the free-text regex arm, which is exactly what produced the
    /// vacuous matcher: a name never matches a ten-digit string, and the hyphenated written form
    /// never matched the stored one. Structured keys get exact matching against their columns.
    /// </remarks>
    private static string? NormalizedOrgNr(string identifier) =>
        Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(identifier)?.Value;

    public async Task<IReadOnlyList<ErasureJobAdMatch>> FindJobAdsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var needle = identifier.Trim();
        var pattern = LikePattern(identifier);
        var erased = Domain.JobAds.JobAdStatus.Erased.Value;
        var orgNr = NormalizedOrgNr(identifier);

        // The matching itself is raw SQL (it has to be — see the class remarks), and it yields IDs.
        // EF's Database.SqlQuery<T> supports SCALAR results only, so the ads themselves are then
        // projected through EF.
        //
        // The organization_number arm is exact-match on the NORMALISED org.nr. `{orgNr}` is NULL
        // for a non-org.nr identifier, and `column = NULL` is never true — the arm switches itself
        // off. It exists because raw_payload is NULLed at 30 days (PurgeStaleRawPayloadsJob), after
        // which the materialised organization_number column (#841) is the ONLY place a sole
        // trader's org.nr survives in the row — the same 30-day logic that forced the company_name
        // channel (see the port).
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM job_ads
                WHERE status <> {erased}
                  AND (
                        search_vector @@ websearch_to_tsquery({TextSearchConfig}::regconfig, {needle})
                     OR lower(title)        LIKE {pattern} ESCAPE {LikeEscapeSql}
                     OR lower(description)  LIKE {pattern} ESCAPE {LikeEscapeSql}
                     OR lower(company_name) LIKE {pattern} ESCAPE {LikeEscapeSql}
                     OR (raw_payload IS NOT NULL AND lower(raw_payload::text) LIKE {pattern} ESCAPE {LikeEscapeSql})
                     OR organization_number = {orgNr}
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
                j.OrganizationNumber,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r =>
            {
                var (channel, excerpt) = orgNr is not null && r.OrganizationNumber == orgNr
                    ? (ErasureMatchChannel.OrganizationNumber, OrgNrEvidence(orgNr))
                    : Evidence(r.Title, r.Description, r.Company, needle);
                return new ErasureJobAdMatch(r.Id, r.ExternalId, r.Title, r.Company, channel, excerpt);
            }),
        ];
    }

    /// <summary>
    /// The reviewable evidence for an org.nr hit: the subject's own supplied identifier, in the
    /// normalised form that matched — flagged when it is personnummer-shaped (ADR 0087 D8(c): a
    /// personnummer is never surfaced un-flagged, even to the admin operator, even when the
    /// subject herself supplied it). Review payload only; never logged.
    /// </summary>
    private static string OrgNrEvidence(string orgNr) =>
        Domain.CompanyWatches.OrganizationNumber.FromTrusted(orgNr).IsPersonnummerShaped()
            ? $"{orgNr} (personnummer-format)"
            : orgNr;

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
        var orgNr = NormalizedOrgNr(identifier);

        // `~*` = case-insensitive ARE match. `q` is a plain varchar(100), so no cast is needed.
        //
        // employer_list holds 10-DIGIT ORG.NR (write path: ValidateEmployerList →
        // OrganizationNumber.Create) — a sole trader's org.nr IS her personnummer, so an org.nr
        // Art. 17 request must reach the rows that filter on her. The arm is EXACT match on the
        // normalised identifier: `{orgNr}` is NULL for a non-org.nr identifier and `NULL = ANY`
        // is never true, so the arm switches itself off. (Round 5 ran the word-boundary REGEX
        // over this column on the ground that it held employer NAMES — a name never matches a
        // ten-digit string, and the zero was certified as a search result.)
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM recent_job_searches
                WHERE (q IS NOT NULL AND q ~* {pattern})
                   OR {orgNr} = ANY(coalesce(employer_list, ARRAY[]::text[]))
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        // Which of the matched rows matched on the EMPLOYER channel — per-row evidence for the
        // operator ("a count cannot be reviewed", least of all on a hard-deleted row). A row can
        // match on both channels; the q evidence then rides along too.
        var employerMatched = orgNr is null
            ? []
            : await db.Database
                .SqlQuery<Guid>($"""
                    SELECT id AS "Value"
                    FROM recent_job_searches
                    WHERE {orgNr} = ANY(coalesce(employer_list, ARRAY[]::text[]))
                    """)
                .ToListAsync(cancellationToken);

        var employerSet = employerMatched.ToHashSet();
        var typedIds = ids.Select(id => new RecentJobSearchId(id)).ToList();

        var rows = await db.RecentJobSearches
            .AsNoTracking()
            .Where(r => typedIds.Contains(r.Id))
            .Select(r => new { Id = r.Id.Value, r.Q })
            .ToListAsync(cancellationToken);

        // EVERY SQL-matched row is returned — the deletion runs on these ids. Round 5 filtered
        // `.Where(r => r.Q is not null)` here, which threw away the employer-only match (q = NULL
        // is the domain's canonical employer-only form) AFTER the SQL had found it: never deleted,
        // never counted, certified erased.
        return
        [
            .. rows.Select(r => new ErasureRecentSearchMatch(
                r.Id,
                r.Q,
                employerSet.Contains(r.Id) ? orgNr : null)),
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
            WHERE lower(criteria::text)          LIKE {pattern} ESCAPE {LikeEscapeSql}
               OR lower(coalesce(name, ''))      LIKE {pattern} ESCAPE {LikeEscapeSql}
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
        //
        // snapshot_url is the frozen ad URL, and a URL path carries names routinely — the
        // identical argument that put manual_url in scope. It was classified MatchedRetained
        // ("searched and reported") for one whole round while this query never touched it
        // (round-5 B5-2); the registry's channel list now claims it, and the single-column
        // integration test holds this line here.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE lower(coalesce(snapshot_company, ''))     LIKE {pattern} ESCAPE {LikeEscapeSql}
               OR lower(coalesce(snapshot_title, ''))       LIKE {pattern} ESCAPE {LikeEscapeSql}
               OR lower(coalesce(snapshot_description, '')) LIKE {pattern} ESCAPE {LikeEscapeSql}
               OR lower(coalesce(snapshot_url, ''))         LIKE {pattern} ESCAPE {LikeEscapeSql}
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
            WHERE lower(coalesce(manual_company, '')) LIKE {pattern} ESCAPE {LikeEscapeSql}
               OR lower(coalesce(manual_title, ''))   LIKE {pattern} ESCAPE {LikeEscapeSql}
               OR lower(coalesce(manual_url, ''))     LIKE {pattern} ESCAPE {LikeEscapeSql}
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
            WHERE lower(coalesce(label, '')) LIKE {pattern} ESCAPE {LikeEscapeSql}
            """, cancellationToken);
    }

    public async Task<int> CountResumeMetadataAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var pattern = LikePattern(identifier);

        // The PLAINTEXT metadata around her CV: the two file names (same uploaded file, two tables),
        // the CV's own name, its headline role, and its skill list.
        //
        // The CV BODY is NOT scanned here — raw_text, parsed_content_enc, content_enc and the sealed
        // file bytes are all encrypted (HeldButNotSearchable) and DISCLOSED, never quietly reported
        // as clean.
        //
        // top_skills is a text[], so it needs `unnest` — a LIKE against the array itself compares
        // against its literal text form and would match on the punctuation between elements.
        return await CountAsync($"""
            SELECT (
                (SELECT count(*) FROM parsed_resumes
                  WHERE lower(coalesce(source_file_name, '')) LIKE {pattern} ESCAPE {LikeEscapeSql})
              + (SELECT count(*) FROM resume_files
                  WHERE lower(coalesce(file_name, '')) LIKE {pattern} ESCAPE {LikeEscapeSql})
              + (SELECT count(*) FROM resumes
                  WHERE lower(coalesce(name, ''))        LIKE {pattern} ESCAPE {LikeEscapeSql}
                     OR lower(coalesce(latest_role, '')) LIKE {pattern} ESCAPE {LikeEscapeSql}
                     OR EXISTS (
                          SELECT 1 FROM unnest(coalesce(top_skills, ARRAY[]::text[])) AS skill
                          WHERE lower(skill) LIKE {pattern} ESCAPE {LikeEscapeSql}))
            )::int AS "Value"
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
