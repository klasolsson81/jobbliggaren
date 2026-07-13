using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #841 — the two guards that keep the fix from being undone. Both were previously PROSE in a comment,
/// and prose is what failed: <c>JobAdConfiguration</c> literally said <i>"Lägg INTE till fler generated
/// columns härledda ur raw_payload"</i> while the defect it warned about was live in the same file. This
/// repo has already written the doctrine down — <i>"a comment that describes a control it does not have
/// is the same defect as a test that pins a fiction"</i> — so here the rule is executable.
///
/// <para>
/// <b>Guard 1 — nothing durable may be derived from <c>raw_payload</c> in the database.</b> That is the
/// precise, minimal, true rule. It is NOT "no generated columns": <c>search_vector</c> (from
/// <c>title</c>/<c>description</c>) and <c>extracted_lexemes</c> (from <c>extracted_terms</c>) are
/// legitimate and must keep passing. What makes <c>raw_payload</c> different is that it is the only
/// column on <c>job_ads</c> with a retention TTL — <c>PurgeStaleRawPayloadsJob</c> nulls it after 30 days
/// (ADR 0032 §8) — and <b>anything computed from it silently inherits that TTL</b>. Seven columns did,
/// and filtered search plus the matching engine lost still-active ads ~21.5 h/day for two releases.
/// </para>
///
/// <para>
/// <b>Guard 2 — nothing outside the aggregate may write the payload or its facets.</b> #841's primary
/// defence is the type system: <c>JobAd.SetSourcePayload</c> writes the payload and its facets
/// atomically, and the facets are a required parameter, so a payload write without them cannot compile.
/// There are TWO routes around that, and an earlier version of this file claimed there was one:
/// <c>ExecuteUpdateAsync</c> (<c>SetProperty</c>) and RAW SQL (<c>ExecuteSqlAsync</c> — a live idiom
/// here; <c>AuditTrailEraser</c> erases Art. 17 data exactly that way). Both bypass the aggregate
/// entirely, and both are now scanned. Exactly one such writer exists
/// (<c>PurgeStaleRawPayloadsJob</c>, which writes NULL and now leaves the facets standing). A second one
/// would re-open #841 with the type-system guard fully intact and green. This test is what makes layer 1
/// a closed guard rather than a leaky one.
/// </para>
/// </summary>
public class JobAdRawPayloadDerivationGuardTests
{
    private const string JobAdConfigurationPath =
        "src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobAdConfiguration.cs";

    /// <summary>
    /// The ONLY file permitted to bulk-write <c>JobAd.RawPayload</c> outside the aggregate. It writes
    /// NULL (the 30-day retention control, GDPR Art. 5(1)(c)/(e)) and — since #841 — leaves the seven
    /// facet columns untouched, which is the entire point of the change.
    /// </summary>
    private const string PurgeJobPath =
        "src/Jobbliggaren.Application/JobAds/Jobs/PurgeRawPayloads/PurgeStaleRawPayloadsJob.cs";

    /// <summary>
    /// The migrations permitted to contain <c>GENERATED ALWAYS AS (… raw_payload …) STORED</c>: the four
    /// that originally created the seven columns (history — they are applied, immutable, and describe the
    /// world as it was), and #841's own migration, whose <c>Down()</c> must legitimately re-create them in
    /// order to be a real rollback. A NEW migration doing this is the defect coming back.
    /// </summary>
    private static readonly string[] HistoricalGeneratedColumnMigrations =
    [
        "20260513111555_F2P9JobAdSearchColumns.cs",
        "20260608155047_F6P6JobAdKlass1SearchColumns.cs",
        "20260608205054_F6P7JobAdKlass2SearchColumns.cs",
        "20260630144631_AddJobAdOrganizationNumber.cs",
        "20260713191535_MaterialiseJobAdSourceFacets.cs", // its Down() restores the old schema, by design
    ];

    [Fact]
    public void No_generated_column_may_be_derived_from_raw_payload_via_EF_configuration()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), JobAdConfigurationPath));

        var offenders = ComputedColumnExpressions(source)
            .Where(sql => sql.Contains("raw_payload", StringComparison.OrdinalIgnoreCase))
            .ToList();

        offenders.ShouldBeEmpty(
            "A generated column derived from raw_payload INHERITS raw_payload's 30-day retention TTL: " +
            "PurgeStaleRawPayloadsJob nulls the payload, Postgres recomputes the generated column, and " +
            "the value is destroyed — silently, on a still-ACTIVE ad. That is #841, which cost filtered " +
            "search and the matching engine ~21.5h of every 24 for two releases. If you need a durable " +
            "value from the payload, parse it in the ACL and write it in C# at the ingest funnel " +
            "(JobAd.SetSourcePayload), the way the seven facets and extracted_terms are written.\n\n" +
            "Offending computed column SQL: " + string.Join(" | ", offenders));
    }

    [Fact]
    public void No_generated_column_may_be_derived_from_raw_payload_via_raw_SQL_in_a_migration()
    {
        // WITHOUT THIS, THE GUARD ABOVE IS NARROWER THAN ITS OWN CLAIM — which is the exact defect class
        // #841 exists to kill, and a reviewer caught it here. The rule is "nothing durable may be derived
        // from raw_payload IN THE DATABASE", but the EF-configuration scan only sees the fluent API. Raw
        // `migrationBuilder.Sql` is this repo's ESTABLISHED idiom for job_ads DDL the fluent API cannot
        // express — all seven partial indexes were created that way. So
        //
        //     migrationBuilder.Sql("ALTER TABLE job_ads ADD COLUMN foo text " +
        //                          "GENERATED ALWAYS AS (raw_payload->>'foo') STORED;");
        //
        // would have re-opened #841 with the EF-side guard fully green. It is not a contrived path; it is
        // the path of least resistance.
        var root = RepoRoot();
        var migrationsDir = Path.Combine(root, "src", "Jobbliggaren.Infrastructure", "Persistence", "Migrations");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(migrationsDir, "*.cs"))
        {
            var name = Path.GetFileName(file);
            if (HistoricalGeneratedColumnMigrations.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            if (DerivesAGeneratedColumnFromRawPayload(File.ReadAllText(file)))
                offenders.Add(name);
        }

        offenders.ShouldBeEmpty(
            "A migration creates a GENERATED column derived from raw_payload in raw SQL. raw_payload is " +
            "the only column on job_ads with a retention TTL (PurgeStaleRawPayloadsJob nulls it after 30 " +
            "days, ADR 0032 §8), and Postgres RECOMPUTES any stored generated column when its base " +
            "changes — so the new column would be silently destroyed on still-ACTIVE ads, exactly as the " +
            "seven facets were for two releases (#841). Parse it in the ACL and write it in C# at the " +
            "ingest funnel instead. Offending migrations: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Migration_scan_is_not_vacuous_it_finds_the_historical_generated_columns()
    {
        // The allowlist must be EARNED. If the pattern stopped matching (SQL reformatted, the fluent API
        // gained partial-index support and the raw SQL disappeared), the scan above would be guarding an
        // empty set and every allowlist entry would be a fossil making it look thorough.
        var root = RepoRoot();
        var migrationsDir = Path.Combine(root, "src", "Jobbliggaren.Infrastructure", "Persistence", "Migrations");

        var found = HistoricalGeneratedColumnMigrations
            .Where(name => File.Exists(Path.Combine(migrationsDir, name)))
            .Where(name => DerivesAGeneratedColumnFromRawPayload(
                File.ReadAllText(Path.Combine(migrationsDir, name))))
            .ToList();

        found.Count.ShouldBe(HistoricalGeneratedColumnMigrations.Length,
            "every allowlisted migration must actually contain a raw_payload-derived generated column — " +
            "otherwise the scan cannot see them, and the guard above is vacuous. Found: " +
            string.Join(", ", found));
    }

    /// <summary>
    /// True when a migration creates a generated column derived from <c>raw_payload</c>, in EITHER of the
    /// two forms this repo actually uses. Both are load-bearing, and the first version of this scan only
    /// saw one — which made the guard narrower than its claim, the very defect it is here to prevent. The
    /// non-vacuity test caught it: only 1 of the 5 allowlisted migrations matched.
    /// <list type="number">
    ///   <item>raw SQL: <c>migrationBuilder.Sql("… GENERATED ALWAYS AS (raw_payload->>'x') STORED …")</c>
    ///     — the form #841's own <c>Down()</c> uses;</item>
    ///   <item>the EF fluent form: <c>migrationBuilder.AddColumn&lt;string&gt;(…, computedColumnSql:
    ///     "raw_payload->…")</c> — the form ALL FOUR historical migrations used, and therefore the form a
    ///     scaffolded migration would use again.</item>
    /// </list>
    /// </summary>
    private static bool DerivesAGeneratedColumnFromRawPayload(string source)
    {
        var code = StripComments(source);

        var rawSql = Regex.Matches(code, @"GENERATED\s+ALWAYS\s+AS\s*\((?<expr>[^)]*(?:\([^)]*\)[^)]*)*)\)",
                RegexOptions.IgnoreCase)
            .Any(m => m.Groups["expr"].Value.Contains("raw_payload", StringComparison.OrdinalIgnoreCase));

        var efFluent = Regex.Matches(code, @"computedColumnSql\s*:\s*""(?<sql>(?:[^""\\]|\\.)*)""")
            .Any(m => m.Groups["sql"].Value.Contains("raw_payload", StringComparison.OrdinalIgnoreCase));

        return rawSql || efFluent;
    }

    [Fact]
    public void Guard_is_not_vacuous_it_can_see_computed_columns()
    {
        // Self-proving negative. If the scan silently stopped matching HasComputedColumnSql (say, the
        // fluent call is renamed, or the file moves), the guard above would pass on an empty set forever
        // — green, and worthless. The two legitimate generated columns must therefore be FOUND here.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), JobAdConfigurationPath));

        var expressions = ComputedColumnExpressions(source);

        expressions.Count.ShouldBeGreaterThanOrEqualTo(2,
            "JobAdConfiguration should still declare its two legitimate generated columns (search_vector " +
            "from title/description, extracted_lexemes from extracted_terms). Finding none means this " +
            "scan no longer sees computed columns at all, so the raw_payload guard has become vacuous.");

        expressions.ShouldContain(sql => sql.Contains("to_tsvector", StringComparison.Ordinal));
        expressions.ShouldContain(sql => sql.Contains("jsonb_path_query_array", StringComparison.Ordinal));
    }

    /// <summary>
    /// The payload and its seven facets are ONE fact ("this is what the source said"), and the aggregate
    /// writes them atomically. <c>ExecuteUpdateAsync</c> is the one route around that — it never touches
    /// the aggregate, so <c>SetSourcePayload</c> never runs and #841's type-system guard cannot see it.
    ///
    /// <para>
    /// Every field of that one fact is therefore listed here, each with the ONE file allowed to bulk-write
    /// it. The first version of this guard covered only <c>RawPayload</c> — which protected one half of an
    /// invariant it claimed to hold whole. A reviewer pointed out the other direction is just as fatal:
    /// <c>SetProperty(j =&gt; j.OrganizationNumber, _ =&gt; null)</c> splits the facets from the payload with
    /// `SetSourcePayload` fully intact and green. That is not hypothetical — <b>CTO bind 3c requires
    /// #842's Tier B tombstone to bulk-clear <c>organization_number</c></b>, so the next lane will write
    /// exactly that line. It should do so deliberately, against a fail-closed allowlist.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> BulkWritableJobAdFields = new(StringComparer.Ordinal)
    {
        // The retention control. Writes NULL, and — since #841 — leaves the facets standing.
        ["RawPayload"] = [PurgeJobPath],

        // The seven facets: nobody may bulk-write them today. When #842 Tier B lands, add its file to
        // OrganizationNumber's list — the build will demand it, which is the point.
        ["SsykConceptId"] = [],
        ["OccupationGroupConceptId"] = [],
        ["MunicipalityConceptId"] = [],
        ["RegionConceptId"] = [],
        ["EmploymentTypeConceptId"] = [],
        ["WorktimeExtentConceptId"] = [],
        ["OrganizationNumber"] = [],
    };

    [Fact]
    public void No_bulk_write_of_the_payload_or_its_facets_outside_the_aggregate()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var code = StripComments(File.ReadAllText(file));

            foreach (var (field, allowed) in BulkWritableJobAdFields)
            {
                if (allowed.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (BulkWrites(code, field))
                    offenders.Add($"{relative} → JobAd.{field}");
            }
        }

        offenders.ShouldBeEmpty(
            "A bulk ExecuteUpdate write of JobAd.RawPayload or one of its seven source facets bypasses " +
            "the aggregate — and therefore bypasses SetSourcePayload, the ONLY thing keeping the payload " +
            "and the facets in agreement. #841's type-system guard (facets as a required parameter of " +
            "Import/UpdateFromSource) CANNOT SEE THROUGH ExecuteUpdate: such a write compiles, passes " +
            "every unit test, and silently splits the two halves of one fact — which is precisely the " +
            "defect #841 exists to close.\n\n" +
            "If you genuinely need one (e.g. #842's Art. 17 tombstone must clear organization_number — " +
            "it no longer self-nulls with the purge), add the file to BulkWritableJobAdFields for that " +
            "field and say why. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Source files permitted to write <c>job_ads</c> with RAW SQL. Empty — and when #842's Tier B
    /// tombstone lands (it MUST clear <c>organization_number</c>, which no longer self-nulls with the
    /// purge — CTO bind 3c), it adds itself here, deliberately.
    /// </summary>
    private static readonly string[] RawSqlJobAdWriters = [];

    [Fact]
    public void No_raw_SQL_write_of_job_ads_outside_the_aggregate()
    {
        // THE SECOND WAY AROUND THE AGGREGATE, and the guard above claimed there was only one.
        //
        // `ExecuteUpdateAsync`/`SetProperty` is not the sole bypass: `Database.ExecuteSqlAsync` is a live
        // idiom in this repo, and it is used for EXACTLY this kind of erasure — AuditTrailEraser wipes
        // Art. 17 data with raw SQL. So #842's Tier B tombstone — the very write the guard above exists to
        // catch — could reasonably be written as
        //
        //     ExecuteSqlAsync($"UPDATE job_ads SET organization_number = NULL WHERE ...")
        //
        // along the path of least resistance AND the repo's own precedent, with every other guard green.
        // The control was narrower than its claim. Again. (Found by code-reviewer, third time this PR —
        // which is itself the finding: the failure mode is not a bug you fix once, it is a habit.)
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            // Migrations are DDL and are reviewed as such (and the generated-column guard covers them).
            if (relative.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (RawSqlJobAdWriters.Contains(relative, StringComparer.OrdinalIgnoreCase))
                continue;

            if (WritesJobAdsWithRawSql(File.ReadAllText(file)))
                offenders.Add(relative);
        }

        offenders.ShouldBeEmpty(
            "A source file writes job_ads with raw SQL. That bypasses the aggregate exactly as an " +
            "ExecuteUpdate does — so JobAd.SetSourcePayload never runs, and the payload and its seven " +
            "facets can silently drift apart, which is the whole defect #841 closes.\n\n" +
            "If you genuinely need one (#842's Art. 17 tombstone must clear organization_number — it no " +
            "longer self-nulls with the purge), add the file to RawSqlJobAdWriters and say why. " +
            "Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Raw_SQL_scan_sees_the_write_it_forbids()
    {
        // Self-proving negative — including the exact line #842 Tier B will want to write.
        WritesJobAdsWithRawSql(
            """await db.Database.ExecuteSqlAsync($"UPDATE job_ads SET organization_number = NULL WHERE id = {id}");""")
            .ShouldBeTrue();

        WritesJobAdsWithRawSql("""ExecuteSqlRawAsync("update job_ads set raw_payload = null")""")
            .ShouldBeTrue("the scan must be case-insensitive — SQL is");

        // ...and it must not fire on a READ, or every query in Infrastructure becomes an offender.
        WritesJobAdsWithRawSql("""FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")""").ShouldBeFalse();
        WritesJobAdsWithRawSql("// UPDATE job_ads SET ... would be forbidden here").ShouldBeFalse(
            "a comment describing the forbidden write is not the forbidden write");
    }

    // `UPDATE job_ads SET …` (or DELETE) in a string literal, in code, not comments.
    private static bool WritesJobAdsWithRawSql(string source) =>
        Regex.IsMatch(
            StripComments(source),
            @"(UPDATE\s+job_ads\s+SET|DELETE\s+FROM\s+job_ads)",
            RegexOptions.IgnoreCase);

    [Fact]
    public void Bulk_write_guard_is_not_vacuous_the_scan_finds_the_one_legitimate_writer()
    {
        // The exemption must be earned, not assumed: if PurgeStaleRawPayloadsJob stopped matching the
        // pattern (refactored to a different EF form), the test above would be guarding an empty set and
        // the allowlist entry would be a fossil.
        var purge = File.ReadAllText(Path.Combine(RepoRoot(), PurgeJobPath));

        BulkWrites(StripComments(purge), "RawPayload").ShouldBeTrue(
            "PurgeStaleRawPayloadsJob no longer matches the bulk-write pattern this guard scans for. " +
            "Either the purge changed shape (then update the pattern — the guard is now blind) or the " +
            "purge no longer bulk-writes raw_payload (then remove its exemption).");
    }

    [Fact]
    public void Bulk_write_guard_sees_a_facet_write_not_just_the_payload()
    {
        // The half of the invariant the first version did not hold. This is the line #842 Tier B will
        // write, and it must be seen.
        BulkWrites(
            "await db.JobAds.ExecuteUpdateAsync(s => s.SetProperty(j => j.OrganizationNumber, _ => null), ct);",
            "OrganizationNumber").ShouldBeTrue();

        BulkWrites(
            "s.SetProperty(j => j.MunicipalityConceptId, _ => null)", "MunicipalityConceptId").ShouldBeTrue();

        // ...and it does not fire on an unrelated field.
        BulkWrites("s.SetProperty(j => j.Status, _ => archived)", "RawPayload").ShouldBeFalse();
    }

    [Fact]
    public void Bulk_write_scan_reads_code_not_comments()
    {
        // Found by mutation-testing this very guard: the first version scanned raw source text, so a
        // COMMENT mentioning the forbidden call failed the build. A guard that fires on the prose
        // explaining it teaches the next author to delete the explanation instead of the leak — and this
        // file is nothing but explanation.
        BulkWrites(StripComments("// never call SetProperty(j => j.RawPayload, _ => null) here"), "RawPayload")
            .ShouldBeFalse("a comment describing the forbidden call is not the forbidden call");

        BulkWrites(
            StripComments("/// <summary>Do not <c>SetProperty(j => j.RawPayload, ...)</c>.</summary>"),
            "RawPayload").ShouldBeFalse();

        // ...and the real thing is still caught, including directly under such a comment.
        BulkWrites(StripComments("""
            // never do this
            await db.JobAds.ExecuteUpdateAsync(s => s.SetProperty(j => j.RawPayload, _ => null), ct);
            """), "RawPayload").ShouldBeTrue("the actual bulk write must still be caught");
    }

    /// <summary>
    /// True when the given source CODE (pass it through <see cref="StripComments"/> first) bulk-writes
    /// <paramref name="field"/> via <c>SetProperty</c> — the <c>ExecuteUpdate</c> form that bypasses the
    /// aggregate.
    /// </summary>
    private static bool BulkWrites(string code, string field) =>
        Regex.IsMatch(code, @"SetProperty\(\s*\w+\s*=>\s*\w+\." + Regex.Escape(field) + @"\b");

    // Remove line, block and XML-doc comments while leaving string literals intact.
    private static string StripComments(string source) =>
        Regex.Replace(
            source,
            @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)|(?<keep>@?""(?:[^""\\\n]|\\.|"""")*""|""""""[\s\S]*?"""""")",
            m => m.Groups["keep"].Success ? m.Groups["keep"].Value : " ");

    // Every `.HasComputedColumnSql("<sql>"...)` argument in the file.
    private static List<string> ComputedColumnExpressions(string source) =>
        Regex.Matches(source, @"HasComputedColumnSql\(\s*(?:""(?<sql>(?:[^""\\]|\\.)*)""|""""""(?<sql>[\s\S]*?)"""""")")
            .Select(m => m.Groups["sql"].Value)
            .ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not locate the repo root (Jobbliggaren.sln) from the test output dir");
        return dir!.FullName;
    }
}
