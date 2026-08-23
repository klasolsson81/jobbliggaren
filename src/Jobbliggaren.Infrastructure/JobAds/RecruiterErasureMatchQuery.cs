using System.Diagnostics;
using System.Text;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
internal sealed partial class RecruiterErasureMatchQuery : IRecruiterErasureMatchQuery
{
    private readonly AppDbContext _db;
    private readonly IProtectedIdentityTokenizer _tokenizer;
    private readonly ILogger<RecruiterErasureMatchQuery> _logger;

    public RecruiterErasureMatchQuery(
        AppDbContext db,
        IProtectedIdentityTokenizer tokenizer,
        ILogger<RecruiterErasureMatchQuery> logger)
    {
        _db = db;
        _tokenizer = tokenizer;
        _logger = logger;

        // Applied at the PORT rather than at the one slow method. SetCommandTimeout lives on the
        // DatabaseFacade for as long as the scoped AppDbContext does, so a per-method call would
        // raise the ceiling for everything the request touches afterwards anyway — a wider and
        // UNDECLARED radius, not a narrower one. The port is AddScoped and injected only by
        // EraseRecruiterAdsCommandHandler, so the radius is exactly one Art. 17 request.
        //
        // The four neighbouring constants sit on raw NpgsqlCommands, which never pick this up;
        // CompanyWatchBrowseQuery.cs explains why and is not restated here (#1173).
        _db.Database.SetCommandTimeout(CommandTimeoutSeconds);
    }

    /// <summary>
    /// The command ceiling for every query this port issues. Explicit and reviewed — never
    /// inherited, and never 0 (Npgsql reads 0 as INFINITE; a genuinely hung command must still
    /// fail loud).
    /// </summary>
    /// <remarks>
    /// <b>Npgsql's client default is 30 s</b> (npgsql.org/doc/connection-string-parameters.html,
    /// read 2026-08-23), no connection string in the repo overrides it, and the dev server's
    /// <c>statement_timeout</c> was measured at 0 the same day — so 30 s was the binding limit, and
    /// the Art. 17 dry run THREW instead of answering when it passed (#1463).
    /// <para>
    /// <b>What 180 is margin over</b> (measurements dated 2026-08-23; they are provenance for the
    /// choice, not a live claim about today). Worst COMPLETING run: <b>63,9 s</b> cold on the dev
    /// corpus. 180 is 2,8× that, so it survives a tripling of the cold cost without a spurious
    /// failure. The Netcup box ran the same predicate in 6 076 ms then 5 449 ms and does not breach
    /// 30 s; dev is nonetheless the figure calibrated against, because it is the pessimistic
    /// environment on both axes that drive cold cost (2 493 MB against the box's 834 MB, on a fifth
    /// of the buffer pool). <b>The box's COLD case is UNMEASURED</b> — dropping a live host's page
    /// cache is not a read-only act — and stays written as unknown rather than assumed benign.
    /// </para>
    /// <para>
    /// The neighbouring sites' *"a ceiling on a bug"* rationale does NOT transfer: a browse that
    /// takes 30 s is a bug, but this path runs a handful of times per year on the only human gate
    /// before an irreversible erase, and a spurious failure here makes Art. 17(1) and Art. 12(3)
    /// unsatisfiable through the product's own mechanism. Art. 12(2) is absolute.
    /// </para>
    /// <para>
    /// <b>THE CONDITION THAT MAKES THIS NUMBER DECORATIVE AGAIN, measured 2026-08-23.</b> Nothing
    /// outside ASP.NET caps this request today: Kestrel sets no execution timeout, and the deployed
    /// edge has no <c>/api</c> matcher (ADR 0050 Option B), so the operator's
    /// <c>docker exec … curl http://api:8080/…</c> reaches the API over the internal network with no
    /// proxy in the path. Put Caddy in front of it (its <c>write</c> timeout is 30 s) or introduce
    /// <c>AddRequestTimeouts</c>, and the failure moves back up the stack and this ceiling must be
    /// re-measured against whatever binds first. <c>deploy/caddy/Caddyfile</c> and
    /// <c>Jobbliggaren.Api/Program.cs</c> carry a pointer here, because that is where the two
    /// actors who could trigger it are reading.
    /// </para>
    /// </remarks>
    internal const int CommandTimeoutSeconds = 180;

    /// <summary>
    /// When the matching command has eaten this much of <see cref="CommandTimeoutSeconds"/>, the
    /// margin is being consumed and someone should know before the ceiling is reached.
    /// </summary>
    /// <remarks>
    /// <b>Derived from the ceiling, never written as a second number.</b> An absolute constant would
    /// have to be recomputed by hand every time the ceiling moved, and both drift directions are
    /// silent: raise the ceiling and a fixed threshold becomes noise, lower it and the threshold
    /// never fires before the ceiling does.
    /// <para>
    /// <b>Why half.</b> Not noise: the warm figures above are ~15× inside it, and even the 63,9 s
    /// cold run is HEALTHY and must not warn. Not silence: what this detects is monotone corpus
    /// growth, not a spike — at half the ceiling a run must DOUBLE its cost between the first
    /// warning and the first failure, which on a path that runs a handful of times per year is the
    /// runway that matters, counted in RUNS. At three quarters, 33 % growth would close the gap and
    /// you might get exactly one warned run before the failing one.
    /// </para>
    /// <para>
    /// <b>It shrinks the silent window; it does not close it</b> (security-auditor, 2026-08-23).
    /// The runway is counted in runs, and the corpus can more than double between two runs a year
    /// apart — so the first warning may never precede the first failure. That is tolerable only
    /// because the failure is loud, lands on the dry run before anything is destroyed, and delays
    /// an Art. 17 answer rather than corrupting one.
    /// </para>
    /// <para>
    /// <b>Not throttled, deliberately.</b> <c>SessionStoreUnavailableLog</c> throttles because a
    /// Redis outage makes EVERY request take its path; this one runs a handful of times per year, so
    /// there is nothing to flood and a throttle could swallow the only run that ever warns.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan MarginWarningThreshold =
        TimeSpan.FromSeconds(CommandTimeoutSeconds / 2.0);

    // MUST be the config search_vector was generated with (JobAdConfiguration:
    // to_tsvector('swedish', …)). A mismatch makes `@@` miss the GIN index and the FTS channel
    // returns nothing at all — a vacuous matcher, which is precisely the defect this command
    // replaces.
    private const string TextSearchConfig = JobAdSearchComposition.TextSearchConfig;

    // LIKE metacharacters. `_` is legal and common in email local parts (anna_k@acme.se) and would
    // otherwise be a single-character wildcard; `%` would match the whole corpus.
    //
    // ⚠ This MUST be Postgres's own default escape. Every arm matches with `LIKE ANY({patterns})`,
    // which admits NO ESCAPE clause — the server rejects one as a syntax error — so the
    // escape is whatever Postgres defaults to, and the pattern builder has to agree with it by
    // construction rather than by coincidence. `LikeEscape_is_the_postgres_default_backslash` holds
    // the server's half; LikePattern below DERIVES every emitted prefix from this constant, so the
    // metacharacter integration test holds ours. Both halves are needed: the constant was once read
    // only as a search argument while the emitted prefixes were hardcoded, which left it pinned by
    // nothing and made this very comment's causal claim false.
    //
    // The array form is not a style choice. Cross-joining the patterns instead —
    // `FROM jsonb_path_query(...) v, unnest({patterns}) p` — multiplies every walked value by the
    // pattern count and took FindJobAdsAsync past Npgsql's 30 s command timeout on the dev corpus,
    // i.e. the Art. 17 dry run threw instead of answering.
    private const char LikeEscape = '\\';

    private static string LikePattern(string identifier)
    {
        // Backslash FIRST: escaping it after `%`/`_` would re-escape the prefixes just written.
        var e = LikeEscape.ToString();

        var escaped = identifier
            .Trim()
            .ToLowerInvariant()
            .Replace(e, e + e, StringComparison.Ordinal)
            .Replace("%", e + "%", StringComparison.Ordinal)
            .Replace("_", e + "_", StringComparison.Ordinal);

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
        var patterns = WrittenFormPatterns(identifier);
        var erased = Domain.JobAds.JobAdStatus.Erased.Value;
        var writtenForms = WrittenForms(identifier);

        // The matching itself is raw SQL (it has to be — see the class remarks), and it yields IDs.
        // EF's Database.SqlQuery<T> supports SCALAR results only, so the ads themselves are then
        // projected through EF.
        //
        // The organization_number arm binds every WRITTEN form, not the normalised one. This column
        // LOOKS like a normalising one and is not: the ingest ACL hands the wire value to
        // `JobAdFacets.Normalize`, which trims and nothing else, so `OrganizationNumber.Create`
        // never runs on this path. That the corpus holds only ten-digit values is a fact about the
        // SOURCE's current format, not about our write path, and it is the arm's own premise —
        // `= ANY(writtenForms)` stops depending on it at no cost, since the ten-digit form is the
        // first element. `= ANY('{}')` is never true, so the arm still switches itself off for a
        // non-org.nr identifier, exactly as `= NULL` did.
        //
        // The arm exists because raw_payload is eventually NULLed (PurgeStaleRawPayloadsJob; rule in
        // ADR 0032 Amendment 2026-07-26 §C2), after
        // which the materialised organization_number column (#841) is the ONLY place a sole
        // trader's org.nr survives in the row — the same payload-retention logic that forced the
        // company_name channel (see the port; rule in ADR 0032 Amendment 2026-07-26 §C2).
        //
        // Both jsonb arms walk VALUES. `raw_payload::text` matched the SANITIZER'S ALLOWLISTED KEY
        // NAMES: measured on the dev corpus 2026-08-23, `headline`, `line`, `work`, `move` and
        // `employer` each reached 5 000 of 5 000 sampled ads — i.e. an ordinary surname proposed the
        // whole corpus for erasure, every one of them with an empty excerpt, since there is no
        // literal to window. The Origin exclusion is NOT applied to raw_payload: `declared` is an
        // ordinary word there, not a closed vocabulary.
        // Clamped around the MATCHING command alone, and not around this method, because the
        // ceiling it is measured against is a PER-COMMAND CommandTimeout while this method can
        // issue a second one (the EF projection below, on a non-empty match). Timing both would
        // compare a wall clock over two commands against a one-command ceiling — two different
        // quantities — and it would go wrong in the REASSURING direction as the projection grows.
        var matchingStartedAt = Stopwatch.GetTimestamp();

        var ids = await _db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM job_ads
                WHERE status <> {erased}
                  AND (
                        search_vector @@ websearch_to_tsquery({TextSearchConfig}::regconfig, {needle})
                     OR organization_number = ANY({writtenForms})
                     OR lower(title)        LIKE ANY({patterns})
                     OR lower(description)  LIKE ANY({patterns})
                     OR lower(company_name) LIKE ANY({patterns})
                     OR EXISTS (
                          SELECT 1
                          FROM jsonb_path_query(contacts, '$.**') AS v
                          WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                            AND lower(v #>> {WholeJsonbValue}) <> ALL({AdContactOriginLiterals})
                            AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
                     OR EXISTS (
                          SELECT 1
                          FROM jsonb_path_query(raw_payload, '$.**') AS v
                          WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                            AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
                  )
                """)
            .ToListAsync(cancellationToken);

        WarnIfMarginConsumed(Stopwatch.GetElapsedTime(matchingStartedAt));

        if (ids.Count == 0)
            return [];

        // Contains over the strongly-typed VO, not over j.Id.Value — EF cannot translate a member
        // access on the value object inside Contains (it falls back to client evaluation and throws).
        var typedIds = ids.Select(id => new Domain.JobAds.JobAdId(id)).ToList();

        var rows = await _db.JobAds
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
                j.Contacts,
            })
            .ToListAsync(cancellationToken);

        // The terms the evidence picker looks for, built ONCE: the identifier as supplied, then every
        // written form. The needle is first so a literal hit on what she actually typed wins the
        // window. Both are constant over the whole result set.
        string[] terms = [needle, .. writtenForms];

        // ADR 0087 D8(c) — "flagged/masked/excluded in ANY display projection", and the free-text
        // channels are display projections too. Decided from the matched terms with the predicate
        // that already exists: same rule, no second detector over free prose. It is a property of
        // the REQUEST, so it is decided once and not per row.
        var termsArePersonnummerShaped = terms.Any(t =>
            Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(t)
                ?.IsPersonnummerShaped() == true);

        return
        [
            .. rows.Select(r =>
            {
                // Membership in the written forms, not equality with the normalised one. The SQL arm
                // above matches every form, so a row storing `551218-1234` would fail an equality
                // test against `5512181234`, fall through to Evidence(), find no literal there
                // either, and reach the operator as FullTextOrRawPayload with an EMPTY excerpt — a
                // window with no hit in it, on the one human gate before irreversible destruction.
                // And it shows the STORED form that matched, for the reason FirstMatchedAxisValue
                // gives: what he is authorising is the deletion of THAT string.
                var matchedForm = r.OrganizationNumber is not null
                    && writtenForms.Contains(r.OrganizationNumber, StringComparer.Ordinal)
                        ? r.OrganizationNumber
                        : null;

                var (channel, excerpt) = matchedForm is not null
                    ? (ErasureMatchChannel.OrganizationNumber, matchedForm)
                    : Evidence(r.Title, r.Description, r.Company, r.Contacts, terms);

                // One flag, every channel that has an excerpt. Previously only the org.nr channel
                // carried it, so a personnummer-shaped value reaching the operator through
                // `description` arrived un-flagged on the one screen ADR 0087 D8(c) names explicitly
                // ("even to the admin operator, even when the subject herself supplied it"). An empty
                // excerpt is left empty; a flag on nothing flags nothing.
                var flagged = termsArePersonnummerShaped && excerpt.Length > 0
                    ? $"{excerpt} (personnummer-format)"
                    : excerpt;

                return new ErasureJobAdMatch(
                    r.Id, r.ExternalId, r.Title, r.Company, channel, flagged);
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
    /// <para>
    /// <paramref name="terms"/> carries the identifier as supplied AND every written form, because
    /// the SQL matches every written form: an ad whose description holds <c>551218-1234</c>, matched
    /// by a request for <c>5512181234</c>, would otherwise reach the operator with no excerpt at all.
    /// </para>
    /// </remarks>
    private static (ErasureMatchChannel Channel, string Excerpt) Evidence(
        string title, string? description, string company, Domain.JobAds.AdContacts? contacts,
        IReadOnlyList<string> terms)
    {
        const int Window = 200;
        const int Lead = 60;

        if (!string.IsNullOrWhiteSpace(description))
        {
            foreach (var term in terms)
            {
                var at = description.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    continue;

                var start = Math.Max(0, at - Lead);
                var length = Math.Min(Window, description.Length - start);
                return (ErasureMatchChannel.Description, description.Substring(start, length).Trim());
            }
        }

        if (terms.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return (ErasureMatchChannel.Title, title);

        if (terms.Any(t => company.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return (ErasureMatchChannel.CompanyName, company);

        // #842 Tier A (T8 CTO 2026-07-16) — the structured contacts hit gets its OWN channel with
        // REAL evidence: post-scrub this is the only carrier of a detected email/phone, i.e. the
        // load-bearing Tier-A channel, and an empty FullTextOrRawPayload excerpt would hide a
        // reviewable hit from the one human gate before irreversible destruction. The excerpt is
        // the matched contact's own fields — exactly the data under review, admin-only, never
        // logged.
        var matchedContact = contacts?.Contacts.FirstOrDefault(c => terms.Any(t =>
            (c.Name is not null && c.Name.Contains(t, StringComparison.OrdinalIgnoreCase))
            || (c.Role is not null && c.Role.Contains(t, StringComparison.OrdinalIgnoreCase))
            || (c.Email is not null && c.Email.Contains(t, StringComparison.OrdinalIgnoreCase))
            || (c.Phone is not null && c.Phone.Contains(t, StringComparison.OrdinalIgnoreCase))));
        if (matchedContact is not null)
        {
            var fields = new[]
                {
                    matchedContact.Name, matchedContact.Role, matchedContact.Email,
                    matchedContact.Phone,
                }
                .Where(f => f is not null);
            return (ErasureMatchChannel.ContactsMatch, string.Join(" · ", fields));
        }

        // The hit came from the FTS lexemes or from raw_payload. There is no literal substring to
        // window, and pretending otherwise is the failure above.
        return (ErasureMatchChannel.FullTextOrRawPayload, string.Empty);
    }

    public async Task<IReadOnlyList<ErasureRecentSearchMatch>> FindRecentJobSearchesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var needle = identifier.Trim();
        var pattern = WordBoundaryPattern(identifier);
        var orgNr = NormalizedOrgNr(identifier);

        // The exact arm must meet the STORE's form, not the request's. employer_list normalises
        // on write (ValidateEmployerList -> OrganizationNumber.Create), so the 10-digit form is
        // the only one there. The five axes validate on SHAPE ONLY and store whatever was typed,
        // so `550928-1234`, `195509281234` and `19550928-1234` all sit there unreached by a
        // normalised comparison. OrganizationNumber owns the rendering as the inverse of
        // TryFromWrittenForm, so the pair cannot drift (#844). Empty for a non-org.nr
        // identifier, and `= ANY('{}')` is never true, so the arm switches itself off.
        var writtenForms = WrittenForms(identifier);

        // `~*` = case-insensitive ARE match. `q` is a plain varchar(100), so no cast is needed.
        //
        // employer_list holds 10-DIGIT ORG.NR (write path: ValidateEmployerList →
        // OrganizationNumber.Create) — a sole trader's org.nr IS her personnummer, so an org.nr
        // Art. 17 request must reach the rows that filter on her. The arm is EXACT match on the
        // normalised identifier: `{orgNr}` is NULL for a non-org.nr identifier and `NULL = ANY`
        // is never true, so the arm switches itself off. (Round 5 ran the word-boundary REGEX
        // over this column on the ground that it held employer NAMES — a name never matches a
        // ten-digit string, and the zero was certified as a search result.)
        //
        // #1425 — THE FIVE CONCEPT-ID AXES. They are shape-validated (^[A-Za-z0-9_-]{1,32}) and
        // never taxonomy-resolved, so a hand-edited ?occupationGroup= persists a name or a
        // ten-digit org.nr. `unnest` is not style: a regex against the ARRAY compares against its
        // literal text form and would match on the punctuation between elements
        // (CountResumeMetadataAsync says the same about top_skills).
        //
        // BOTH sub-arms are needed, and neither is redundant. The word-boundary pattern is built
        // from the identifier AS SUPPLIED, so a request for `550928-1234` yields a pattern that
        // can never match a stored `5509281234`; only the exact normalised-org.nr arm reaches it.
        // And the exact arm alone would never reach a NAME. Drop either and an enskild firma's
        // personnummer sits unreachable in a column we certify erased.
        var ids = await _db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM recent_job_searches
                WHERE (q IS NOT NULL AND q ~* {pattern})
                   OR {orgNr} = ANY(coalesce(employer_list, ARRAY[]::text[]))
                   OR EXISTS (
                        SELECT 1 FROM unnest(
                             coalesce(occupation_group_list, ARRAY[]::text[])
                          || coalesce(municipality_list,     ARRAY[]::text[])
                          || coalesce(region_list,           ARRAY[]::text[])
                          || coalesce(employment_type_list,  ARRAY[]::text[])
                          || coalesce(worktime_extent_list,  ARRAY[]::text[])
                        ) AS axis
                        WHERE axis ~* {pattern} OR axis = ANY({writtenForms}))
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        // Which of the matched rows matched on the EMPLOYER channel — per-row evidence for the
        // operator ("a count cannot be reviewed", least of all on a hard-deleted row). A row can
        // match on both channels; the q evidence then rides along too.
        var employerMatched = orgNr is null
            ? []
            : await _db.Database
                .SqlQuery<Guid>($"""
                    SELECT id AS "Value"
                    FROM recent_job_searches
                    WHERE {orgNr} = ANY(coalesce(employer_list, ARRAY[]::text[]))
                    """)
                .ToListAsync(cancellationToken);

        // Same shape, one arm over: WHICH rows matched on a concept-id axis. Scalar ids and not
        // (id, value) pairs because Database.SqlQuery<T> is SCALAR-ONLY (see FindJobAdsAsync), so
        // the matched VALUE is recovered from the projection below rather than from SQL.
        var taxonomyMatched = await _db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM recent_job_searches
                WHERE EXISTS (
                        SELECT 1 FROM unnest(
                             coalesce(occupation_group_list, ARRAY[]::text[])
                          || coalesce(municipality_list,     ARRAY[]::text[])
                          || coalesce(region_list,           ARRAY[]::text[])
                          || coalesce(employment_type_list,  ARRAY[]::text[])
                          || coalesce(worktime_extent_list,  ARRAY[]::text[])
                        ) AS axis
                        WHERE axis ~* {pattern} OR axis = ANY({writtenForms}))
                """)
            .ToListAsync(cancellationToken);

        // The q arm gets its own row set for the same reason the other two have one: the evidence
        // slot must be conditioned on the arm that MATCHED, not on the column being non-null. A row
        // that matched only on employer_list or on an axis, but happens to carry a q, would
        // otherwise emit that q as its evidence line -- a string that does not contain the
        // identifier, on the surface whose review is the ONLY gate before an irreversible
        // hard-delete. That was already true of the employer arm before #1425 and is fixed here.
        var qMatched = await _db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM recent_job_searches
                WHERE q IS NOT NULL AND q ~* {pattern}
                """)
            .ToListAsync(cancellationToken);

        var qSet = qMatched.ToHashSet();
        var employerSet = employerMatched.ToHashSet();
        var taxonomySet = taxonomyMatched.ToHashSet();
        var typedIds = ids.Select(id => new RecentJobSearchId(id)).ToList();

        // EF.Property, never r.OccupationGroup: the public IReadOnlyList getters are
        // builder.Ignore()d (RecentJobSearchConfiguration), so EF cannot translate them and falls
        // back to client evaluation. The backing fields are the mapped properties. Shadow/backing
        // reads via EF.Property are core EF and belong inline (AGENTS.md §2.1); the house pattern
        // is GetParsedResumeOccupationsQueryHandler.
        var rows = await _db.RecentJobSearches
            .AsNoTracking()
            .Where(r => typedIds.Contains(r.Id))
            .Select(r => new
            {
                Id = r.Id.Value,
                r.Q,
                OccupationGroup = EF.Property<List<string>>(r, "_occupationGroup"),
                Municipality = EF.Property<List<string>>(r, "_municipality"),
                Region = EF.Property<List<string>>(r, "_region"),
                EmploymentType = EF.Property<List<string>>(r, "_employmentType"),
                WorktimeExtent = EF.Property<List<string>>(r, "_worktimeExtent"),
            })
            .ToListAsync(cancellationToken);

        // EVERY SQL-matched row is returned — the deletion runs on these ids. Round 5 filtered
        // `.Where(r => r.Q is not null)` here, which threw away the employer-only match (q = NULL
        // is the domain's canonical employer-only form) AFTER the SQL had found it: never deleted,
        // never counted, certified erased.
        return
        [
            .. rows.Select(r => new ErasureRecentSearchMatch(
                r.Id,
                qSet.Contains(r.Id) ? r.Q : null,
                employerSet.Contains(r.Id) ? orgNr : null,
                taxonomySet.Contains(r.Id)
                    ? FirstMatchedAxisValue(r.OccupationGroup, r.Municipality, r.Region,
                        r.EmploymentType, r.WorktimeExtent, needle, writtenForms)
                    : null)),
        ];
    }

    /// <summary>
    /// The concept-id axis element to show the operator for a row the SQL already matched on the
    /// taxonomy arm — the STORED value, not the identifier, because what he is authorising is the
    /// deletion of that string (a request for <c>Karlsson</c> against a stored
    /// <c>Anna-Karlsson</c> must show <c>Anna-Karlsson</c>).
    /// </summary>
    /// <remarks>
    /// <b>The predicate here is deliberately WIDER than the SQL's, and it runs ONLY on rows the
    /// SQL returned</b>, so it cannot attribute a taxonomy match to a row that had none — the
    /// caller gates on the SQL's own row set.
    /// <para>
    /// <b>Why not re-derive the ARE exactly in C#.</b> That is the detector-is-not-the-matcher
    /// trap this class already warns about twice: Postgres's <c>[:alnum:]</c> under the server
    /// locale, ARE's escape rule (which is NOT <c>Regex.Escape</c>) and <c>~*</c>'s case folding
    /// are three places a re-derivation drifts, and every drift is either a throw or a false
    /// evidence line. One rule with two normalisers is two rules (#844).
    /// </para>
    /// <para>
    /// <b>RESIDUAL, named rather than certified away:</b> on a row that ALSO holds an element
    /// which merely CONTAINS the identifier without a word boundary, that element can be the one
    /// shown. It over-discloses to the operator, never under-discloses, on a row already destined
    /// for deletion — the same posture as the ad channels' deliberate over-match. Likewise only
    /// the FIRST matching element is shown; the row is hard-deleted whole either way.
    /// </para>
    /// </remarks>
    private static string? FirstMatchedAxisValue(
        List<string> occupationGroup, List<string> municipality, List<string> region,
        List<string> employmentType, List<string> worktimeExtent, string needle,
        IReadOnlyList<string> writtenForms)
    {
        List<string>[] axes = [occupationGroup, municipality, region, employmentType, worktimeExtent];

        foreach (var axis in axes)
        {
            foreach (var value in axis)
            {
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || writtenForms.Contains(value, StringComparer.Ordinal))
                {
                    return value;
                }
            }
        }

        // TOTAL, deliberately. The SQL is the authority on WHETHER the row matched; this method
        // only chooses WHICH element to show. Returning null on a row the SQL returned would build
        // an ErasureRecentSearchMatch with all three slots null -- which throws, i.e. an Art. 17 dry
        // run 500s. The C# predicate is a superset of the SQL's for ASCII, which the write grammar
        // guarantees, but the NEEDLE is the operator's and need not be: a character Postgres's ctype
        // folds to ASCII while .NET's ordinal casing does not (U+212A KELVIN, U+017F LONG S) would
        // match in SQL and miss here. Falling back to the first non-empty element over-discloses one
        // string to the operator on a row already destined for deletion -- the posture this channel
        // already takes -- instead of failing a rights request.
        foreach (var axis in axes)
        {
            if (axis.Count > 0)
                return axis[0];
        }

        return null;
    }

    public async Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // Deliberately NOT filtered on deleted_at. A soft-deleted saved search still physically
        // holds `criteria` in the row (SoftDelete() hides it; it does not erase it). Reporting only
        // the live ones would under-count what we actually hold — the whole failure mode here.
        //
        // `name` is a separate plaintext column, and a user who names a saved search "Anna Karlssons
        // annonser" holds the recruiter's name in it. It was classified as searched and was not.
        //
        // The criteria arm walks the document's VALUES, never its raw text. A `criteria::text LIKE`
        // also matches the jsonb KEY NAMES — `Region`, `Employer` and `Municipality` are keys in
        // every document — so a four-character identifier (the validator's floor) reported every row
        // that has criteria at all. Walking values names no property, so an additive key stays
        // covered, and drops the key-name over-match with it. `$.**` is lax and unwraps arrays, so
        // it reaches the six concept-id lists' elements as well as `Q`.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM saved_searches
            WHERE lower(coalesce(name, '')) LIKE ANY({patterns})
               OR EXISTS (
                    SELECT 1
                    FROM jsonb_path_query(criteria, '$.**') AS v
                    WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                      AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
            """, cancellationToken);
    }

    public async Task<int> CountApplicationSnapshotsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // We search these four precisely because we do NOT erase them (Art. 17(3)(e) — see the
        // registry's written ground). `AdSnapshot.Capture` performs no validation at all, so
        // whatever the ad body carried is frozen here byte for byte, in whatever written form.
        //
        // snapshot_url is the frozen ad URL, and a URL path carries names routinely — the
        // identical argument that put manual_url in scope. It was classified MatchedRetained
        // ("searched and reported") for one whole round while this query never touched it
        // (round-5 B5-2); the registry's channel list now claims it, and the single-column
        // integration test holds this line here.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE lower(coalesce(snapshot_company, ''))     LIKE ANY({patterns})
               OR lower(coalesce(snapshot_title, ''))       LIKE ANY({patterns})
               OR lower(coalesce(snapshot_description, '')) LIKE ANY({patterns})
               OR lower(coalesce(snapshot_url, ''))         LIKE ANY({patterns})
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindApplicationSnapshotContactsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // #842 Tier A — IDS, not a count: this surface is ERASED surgically
        // (Application.EraseAdSnapshotContacts) and an erase needs its targets. Deliberately NOT
        // part of CountApplicationSnapshotsAsync: the body columns are retained (17(3)(e)); the
        // contact block goes — one surface, one disposition (T2 CTO 2026-07-16).
        //
        // THE LOOSENESS OF A MATCH IS INVERSELY PROPORTIONAL TO THE STRENGTH OF ITS REVIEW GATE, and
        // this arm has the weakest gate in the class: the handler erases what it returns with NO
        // per-id confirmation. A `snapshot_contacts::text LIKE` matched the KEY NAMES — `Name`,
        // `Role`, `Email`, `Phone` — so a four-character identifier destroyed every applicant's
        // frozen contact block, unreviewed. The handler's ground for having no ceremony ("nothing of
        // any USER'S is destroyed") holds only while the match is sound; that is what makes the
        // over-match here a different thing from the same over-match on a confirmed channel.
        return await _db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM applications
                WHERE EXISTS (
                        SELECT 1
                        FROM jsonb_path_query(snapshot_contacts, '$.**') AS v
                        WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                          AND lower(v #>> {WholeJsonbValue}) <> ALL({AdContactOriginLiterals})
                          AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountManualAdEntriesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // ONLY the plaintext columns. cover_letter sits in this same table and is Form-A encrypted —
        // it is NOT scanned here (registry: HeldButNotSearchable; disclosed via UnsearchableSurfaces).
        // A LIKE against it would compare her name to base64 and return 0, forever.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM applications
            WHERE lower(coalesce(manual_company, '')) LIKE ANY({patterns})
               OR lower(coalesce(manual_title, ''))   LIKE ANY({patterns})
               OR lower(coalesce(manual_url, ''))     LIKE ANY({patterns})
            """, cancellationToken);
    }

    public async Task<int> CountCompanyWatchCriteriaAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // No lifecycle predicate to exclude — and unlike CountSavedSearchesAsync there is no choice
        // being made here. A criterion has no deleted_at and no soft-deleted state (delete is HARD,
        // C-D8/G1), so this raw-SQL count over the physical table already counts every row we hold,
        // which is exactly what an erasure disclosure must report. The SavedSearch case above is the
        // one that genuinely has to argue past a filter: that aggregate really does soft-delete.
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM company_watch_criteria
            WHERE lower(coalesce(label, '')) LIKE ANY({patterns})
            """, cancellationToken);
    }

    /// <summary>
    /// The <see cref="Domain.JobAds.AdContactOrigin"/> names as they sit in a serialised
    /// <c>AdContacts</c> document, lower-cased for comparison against a lower-cased jsonb value.
    /// </summary>
    /// <remarks>
    /// <b>Provenance metadata, not advertiser text.</b> <c>Origin</c> is the one member of a frozen
    /// contact whose value comes from a closed system vocabulary rather than from the ad, and its
    /// literals are long enough to contain a name: <c>Declared</c> contains <c>Clare</c>, and four
    /// characters is a legal identifier. Measured on the dev corpus 2026-08-23, a request for
    /// <c>clare</c> reached <b>16 999</b> of 40 983 ads through <c>job_ads.contacts</c> — before and
    /// after the value walk alike, because a value is not a key name.
    /// <para>
    /// This does NOT weaken the property the walk was chosen for. That property is about the KEY set
    /// — an additive member stays covered the day it lands — and this bounds the VALUE domain
    /// instead; the two axes are orthogonal. Naming <c>Origin</c> in a jsonpath would have broken it.
    /// </para>
    /// <para>
    /// ⚠ <b>RESIDUAL, named rather than certified away:</b> the comparison is WHOLE-VALUE equality
    /// applied to every scalar in the document, so a contact ANY of whose fields — <c>Name</c>,
    /// <c>Role</c>, <c>Email</c>, <c>Phone</c> — is exactly an origin literal is not reached through
    /// that field. <c>Declared Andersson</c> still is. Derived from the enum and pinned against it,
    /// so a third origin cannot drift away from this list (#844).
    /// </para>
    /// </remarks>
    private static readonly string[] AdContactOriginLiterals =
        [.. Enum.GetNames<Domain.JobAds.AdContactOrigin>()
            .Select(name => name.ToLowerInvariant())];

    /// <summary>
    /// The empty jsonb path, bound as a parameter. <c>jsonb #>> text[]</c> with an empty path
    /// returns the value's DECODED text, which is what a comparison against a user-supplied
    /// identifier needs: casting to <c>text</c> instead leaves JSON escapes in place, so a stored
    /// <c>Anna "Bea" Berg</c> would not be reached by a request for <c>"Bea"</c>.
    /// </summary>
    private static readonly string[] WholeJsonbValue = [];

    /// <summary>
    /// The LIKE patterns to compare a stored value against: every WRITTEN form of the identifier when
    /// it is an org.nr, and the identifier as supplied when it is not.
    /// </summary>
    /// <remarks>
    /// A column validated on SHAPE ONLY stores whatever was typed, so comparing one normalised
    /// request against an unnormalised store reaches only the form that happens to coincide (#1425).
    /// The five <c>recent_job_searches</c> axes close that with <c>= ANY(writtenForms)</c> because
    /// they are <c>text[]</c>; every other channel needs the same set as LIKE patterns instead.
    /// <b>This does NOT apply to a normalising column</b> — <c>company_watches.organization_number</c>
    /// and <c>recent_job_searches.employer_list</c> both reach the database through a ten-digit gate,
    /// so the ten-digit form is the only stored PLAINTEXT form and their arms stay exact probes. That
    /// qualifier is load-bearing: a personnummer-shaped org.nr is stored in the first of those as a
    /// keyed HMAC token, which is why its arm carries two operands and not one.
    /// <c>job_ads.organization_number</c> looks like a third and is not: its ingest write path runs
    /// <c>JobAdFacets.Normalize</c> (a trim) and never <c>OrganizationNumber.Create</c>.
    /// </remarks>
    private static string[] WrittenFormPatterns(string identifier)
    {
        var orgNr = Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(identifier);

        return orgNr is null
            ? [LikePattern(identifier)]
            : [.. orgNr.WrittenForms().Select(LikePattern)];
    }

    /// <summary>
    /// The written forms themselves, for an EXACT arm — empty when the identifier is not org.nr
    /// shaped, so <c>= ANY('{}')</c> switches that arm off.
    /// </summary>
    /// <remarks>
    /// The empty fallback is the one place this DIFFERS from <see cref="WrittenFormPatterns"/>, and
    /// the difference is deliberate: a substring arm must still reach a plain name, while an exact
    /// arm comparing a whole column against a name would only ever be noise.
    /// </remarks>
    private static string[] WrittenForms(string identifier)
    {
        var orgNr = Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(identifier);

        return orgNr is null ? [] : [.. orgNr.WrittenForms()];
    }

    public async Task<int> CountCompanyWatchFollowsAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);
        var orgNr = Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(identifier);

        // The AT-REST form, decided by the SAME discriminator the WRITE path uses
        // (CompanyWatchFollowExecutor): a pnr-shaped org.nr is stored as a keyed HMAC token, an AB
        // org.nr verbatim — in which case storedKey and plain are the same string and the two
        // operands collapse, exactly as they do in the executor's probe. Both are NULL for a
        // non-org.nr identifier, and `column = NULL` is never true, so both arms switch themselves
        // off and a name falls through to the filter arm alone.
        var plain = orgNr?.Value;
        var storedKey = orgNr is null
            ? null
            : orgNr.IsPersonnummerShaped() ? _tokenizer.Tokenize(orgNr.Value) : orgNr.Value;

        // Deliberately NOT filtered on deleted_at — and the reason DIFFERS per arm, which is why
        // the CountSavedSearchesAsync comment is not copied here.
        //   ORG.NR: unfollowing soft-deletes the row and LEAVES organization_number standing, so a
        //   lifecycle predicate would under-report a key we physically hold. That is the
        //   saved_searches case.
        //   FILTER: the exact opposite. CompanyWatch.SoftDelete() NULLS Filter, so this arm cannot
        //   reach a soft-deleted row BY CONSTRUCTION. A deleted_at predicate would be inert here
        //   while being wrong one line up.
        //
        // The filter arm walks the document's VALUES, never its raw text. A `filter::text LIKE`
        // also matches the jsonb KEY NAMES, and `Regions` or `Remote` as an identifier would then
        // match every row that has a filter at all. Walking values keeps the property the key-name
        // form was chosen for — no property is named in SQL — and drops the over-match with it.
        //
        // The type predicate excludes CONTAINERS, and that is the whole of its job: `#>> '{}'` on an
        // object or an array returns the container's serialised text WITH its key names in it, so
        // walking one would put the key-name over-match straight back. Every SCALAR is let through.
        // An earlier `= 'string'` did the same work by accident and cost the numbers and booleans
        // with it — an under-reach on an Art. 17 channel, where a false negative is a false
        // Art. 12(3) confirmation to a named person and a false positive is a second look at the
        // mandatory dry run. (`'null'` yields SQL NULL, and `NULL LIKE p` is never true.)
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM company_watches
            WHERE organization_number = {storedKey}
               OR organization_number = {plain}
               OR EXISTS (
                    SELECT 1
                    FROM jsonb_path_query(filter, '$.**') AS v
                    WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                      AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
            """, cancellationToken);
    }

    public async Task<int> CountJobSeekerProfilesAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // Three columns, four registry keys: `preferences` is the OwnsOne(...).ToJson() container
        // and `Language` is a JSON property inside it, so the third arm searches both.
        //
        // No lifecycle predicate. A soft-deleted profile IS a reachable state — account deletion
        // soft-deletes and AccountHardDeleter only reaps rows past a 30-day window — and we hold the
        // row for that whole window, so an erasure disclosure owes it. Raw SQL bypasses the
        // aggregate's query filter, which is what makes counting the physical table possible here.
        //
        // Both jsonb arms walk VALUES, never the document text: `preferences` is NOT NULL and always
        // serialised with its full key set, so a `preferences::text LIKE` would match `Language` in
        // EVERY row. An identifier of four characters is legal (the validator's floor) and `Lang` is
        // a surname, so that form turns a genuine no-match into a whole-table match — and
        // Matched.Total is a zero-test, so the reply flips from "we found nothing" to a claim that
        // her data sits in a user's profile. Walking values excludes key names by construction; the
        // container predicate is what keeps it that way (see CountCompanyWatchFollowsAsync).
        return await CountAsync($"""
            SELECT count(*)::int AS "Value"
            FROM job_seekers
            WHERE lower(display_name) LIKE ANY({patterns})
               OR EXISTS (
                    SELECT 1
                    FROM jsonb_path_query(match_preferences, '$.**') AS v
                    WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                      AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
               OR EXISTS (
                    SELECT 1
                    FROM jsonb_path_query(preferences, '$.**') AS v
                    WHERE jsonb_typeof(v) NOT IN ('object', 'array')
                      AND lower(v #>> {WholeJsonbValue}) LIKE ANY({patterns}))
            """, cancellationToken);
    }

    public async Task<int> CountResumeMetadataAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var patterns = WrittenFormPatterns(identifier);

        // The PLAINTEXT metadata around her CV: the two file names (same uploaded file, two tables),
        // the CV's own name, its headline role, and its skill list.
        //
        // The CV BODY is NOT scanned here — raw_text, parsed_content_enc, content_enc and the sealed
        // file bytes are all encrypted (HeldButNotSearchable) and DISCLOSED, never quietly reported
        // as clean.
        //
        // The two file names carry an org.nr in whatever form the uploader typed: PersonnummerRedactor
        // masks them, but its detector is date- and Luhn-gated, so an AB org.nr is never a candidate
        // and `Ansokan_556012-5790.pdf` survives verbatim.
        //
        // top_skills is a text[] and needs `unnest` — a LIKE against the array itself compares against
        // its literal text form and would match on the punctuation between elements.
        return await CountAsync($"""
            SELECT (
                (SELECT count(*) FROM parsed_resumes
                  WHERE lower(coalesce(source_file_name, '')) LIKE ANY({patterns}))
              + (SELECT count(*) FROM resume_files
                  WHERE lower(coalesce(file_name, '')) LIKE ANY({patterns}))
              + (SELECT count(*) FROM resumes
                  WHERE lower(coalesce(name, ''))        LIKE ANY({patterns})
                     OR lower(coalesce(latest_role, '')) LIKE ANY({patterns})
                     OR EXISTS (
                          SELECT 1 FROM unnest(coalesce(top_skills, ARRAY[]::text[])) AS skill
                          WHERE lower(skill) LIKE ANY({patterns})))
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
        var counts = await _db.Database.SqlQuery<int>(sql).ToListAsync(cancellationToken);
        return counts.Count > 0 ? counts[0] : 0;
    }

    /// <summary>
    /// Emits the margin warning when the matching command has eaten at least
    /// <see cref="MarginWarningThreshold"/> of <see cref="CommandTimeoutSeconds"/>. Nothing in the
    /// repo detects that the margin has been consumed, so without this the ceiling is crossed
    /// silently the next time the corpus grows or the box is cold (#1463).
    /// </summary>
    /// <remarks>Internal so a test can cross the threshold without 90 s of wall clock.</remarks>
    internal void WarnIfMarginConsumed(TimeSpan elapsed)
    {
        if (elapsed < MarginWarningThreshold)
            return;

        LogMarginConsumed(_logger, (long)elapsed.TotalMilliseconds, CommandTimeoutSeconds);
    }

    // §5 and Art. 5(1)(c): the elapsed time and the ceiling, and NOTHING else. The identifier this
    // query runs on is the data subject's name, address, phone number or personnummer-shaped org.nr;
    // ADR 0087 D8(c) is written absolutely about any display projection, and a Seq sink is one. The
    // ratio is the reader's to compute — carrying a third number here would only give it something
    // to drift against.
    [LoggerMessage(EventId = 8436, Level = LogLevel.Warning,
        Message = "Art. 17 erasure matching took {ElapsedMs}ms against a {CeilingSeconds}s command "
            + "ceiling. The margin is being consumed — re-measure before it is crossed silently "
            + "(#1463).")]
    private static partial void LogMarginConsumed(
        ILogger logger, long elapsedMs, int ceilingSeconds);
}
