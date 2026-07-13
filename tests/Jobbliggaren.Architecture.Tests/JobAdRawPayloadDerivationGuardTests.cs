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
/// <b>Guard 2 — only the purge may bulk-write <c>RawPayload</c>.</b> #841's primary defence is the type
/// system: <c>JobAd.SetSourcePayload</c> writes the payload and its facets atomically, and the facets are
/// a required parameter, so a payload write without them cannot compile. <c>ExecuteUpdateAsync</c> is the
/// one route around that — it bypasses the aggregate entirely. Exactly one such writer exists
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

    [Fact]
    public void No_generated_column_may_be_derived_from_raw_payload()
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

    [Fact]
    public void Only_the_purge_job_may_bulk_write_raw_payload_outside_the_aggregate()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Equals(PurgeJobPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var source = File.ReadAllText(file);

            // `SetProperty(j => j.RawPayload, ...)` — the ExecuteUpdate form, which never touches the
            // aggregate and therefore never runs SetSourcePayload.
            if (Regex.IsMatch(source, @"SetProperty\(\s*\w+\s*=>\s*\w+\.RawPayload\b"))
                offenders.Add(relative);
        }

        offenders.ShouldBeEmpty(
            "A bulk ExecuteUpdate write of JobAd.RawPayload bypasses the aggregate — and therefore " +
            "bypasses SetSourcePayload, which is the ONLY thing keeping the payload and its seven facet " +
            "columns in agreement. #841's type-system guard (facets as a required parameter of " +
            "Import/UpdateFromSource) cannot see through ExecuteUpdate: this write would compile, pass " +
            "every unit test, and silently re-open the defect. If you genuinely need a bulk payload " +
            "write, it must decide explicitly what happens to the seven facets. Offenders: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Purge_job_guard_is_not_vacuous_the_scan_finds_the_one_legitimate_writer()
    {
        // The exemption must be earned, not assumed: if PurgeStaleRawPayloadsJob stopped matching the
        // pattern (refactored to a different EF form), the test above would be guarding an empty set and
        // the allowlist entry would be a fossil.
        var purge = File.ReadAllText(Path.Combine(RepoRoot(), PurgeJobPath));

        Regex.IsMatch(purge, @"SetProperty\(\s*\w+\s*=>\s*\w+\.RawPayload\b").ShouldBeTrue(
            "PurgeStaleRawPayloadsJob no longer matches the bulk-write pattern this guard scans for. " +
            "Either the purge changed shape (then update the pattern — the guard is now blind) or the " +
            "purge no longer bulk-writes raw_payload (then remove its exemption).");
    }

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
