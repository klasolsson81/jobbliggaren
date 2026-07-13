using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #841 — the guards that make <see cref="JobAd"/>'s public surface a deliberate object rather than an
/// accumulating one, and that close a PII hole which has been open since #311.
///
/// <para>
/// <b>Why now.</b> Before #841 the seven payload-derived facets were EF SHADOW properties. That had one
/// genuine security property nobody wrote down: reading the org.nr required a deliberate, greppable
/// <c>EF.Property&lt;string?&gt;(j, "OrganizationNumber")</c>, and <c>IAppDbContext</c> exposes no
/// <c>Entry()</c>, so there was no other route. #841 promotes them to ordinary properties (it must — a
/// value derived in the database from a column with a 30-day TTL cannot survive that TTL), and
/// <c>jobAd.OrganizationNumber</c> is now reachable from anything holding a <see cref="JobAd"/>, offered
/// by autocomplete. <b>A sole proprietor's org.nr IS the owner's personnummer, in plaintext</b>
/// (ADR 0087 D8; CLAUDE.md §5 makes the personnummer guard the highest-priority rule).
/// </para>
///
/// <para>
/// <b>The existing org.nr guard cannot see any of this</b>, twice over.
/// <c>OrgNrSurfaceScan.OrgNrSurfacingDtos</c> scans the *Application* assembly for types whose name ends
/// in <c>Dto</c> — <see cref="JobAd"/> is a Domain type and is not a DTO, and
/// <see cref="JobAdImportItem"/> is an Application record that is not a DTO either. And the log-boundary
/// scan matches the tokens <c>organization</c>/<c>orgnr</c>/<c>org_nr</c>/<c>personnummer</c>, none of
/// which appear in <c>LogInformation("{@JobAd}", jobAd)</c>. So both new surfaces would pass unseen.
/// </para>
///
/// <para>
/// Note the honest part: whole-aggregate destructuring was ALREADY a leak before #841 — <c>{@JobAd}</c>
/// would serialise <c>RawPayload</c>, which carries the org.nr and the recruiter contact text that all of
/// #842 is about. #841 does not create that hole. It makes leaving it open indefensible.
/// </para>
/// </summary>
public class JobAdPublicSurfaceGuardTests
{
    /// <summary>
    /// FAIL-CLOSED classification of every public instance property on <see cref="JobAd"/>. Each entry
    /// answers the question that went unasked across FOUR separate column additions (F2P9, F6P6, F6P7,
    /// #311 D1): <b>"is this derived from raw_payload, and can it therefore be destroyed by the purge?"</b>
    ///
    /// <para>
    /// A new property on the aggregate fails the build until a human answers it here. That is the whole
    /// mechanism — Saltzer &amp; Schroeder fail-safe defaults, and the same shape as
    /// <c>ResumeRootPlainColumnGuardTests</c>.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> ClassifiedJobAdProperties = new(StringComparer.Ordinal)
    {
        // --- identity + ordinary ad content: written by the constructor / UpdateFromSource ---
        ["Id"] = "aggregate identity",
        ["Title"] = "ad content; no TTL; search_vector derives from it (legitimately — title has no TTL)",
        ["Company"] = "ad content (owned value object)",
        ["Description"] = "ad content; no TTL. Carries recruiter free-text — see #842 (Art. 17 redaction)",
        ["Url"] = "ad content",
        ["Source"] = "provenance",
        ["Status"] = "THE lifecycle axis (#821 retired the dead DeletedAt axis; Status is the only one)",
        ["PublishedAt"] = "ad content; the purge's cutoff is measured from it",
        ["ExpiresAt"] = "ad content",
        ["CreatedAt"] = "audit",
        ["External"] = "external reference (Source + ExternalId); the upsert idempotency key",
        ["DomainEvents"] = "aggregate plumbing (EF-ignored)",

        // --- the TTL column, and the state derived from it ---
        ["RawPayload"] = "THE ONLY COLUMN WITH A RETENTION TTL. PurgeStaleRawPayloadsJob nulls it 30 days " +
                         "after publication (ADR 0032 §8). NOTHING DURABLE MAY BE DERIVED FROM IT IN THE " +
                         "DATABASE — see JobAdRawPayloadDerivationGuardTests. Written only by " +
                         "SetSourcePayload, atomically with the seven facets below.",

        // --- #841: the seven source facets. Ordinary columns, C#-written, purge-surviving. ---
        ["SsykConceptId"] = "source facet (#841): parsed by the ACL, written by SetSourcePayload with the " +
                            "payload, MUST survive the purge",
        ["OccupationGroupConceptId"] = "source facet (#841): same",
        ["MunicipalityConceptId"] = "source facet (#841): same. Frozen into AdSnapshot at apply time, so a " +
                                    "NULL here becomes permanent in an application's history (#824)",
        ["RegionConceptId"] = "source facet (#841): same",
        ["EmploymentTypeConceptId"] = "source facet (#841): same",
        ["WorktimeExtentConceptId"] = "source facet (#841): same",

        // --- the PII one ---
        ["OrganizationNumber"] = "source facet (#841) AND PII, HIGHEST PRIORITY: a sole proprietor's org.nr " +
                                 "IS a personnummer in plaintext (ADR 0087 D8(c), CLAUDE.md §5). Plaintext " +
                                 "at rest by Klas's Art. 32 risk-accept (public Platsbanken source + " +
                                 "queryability necessity — a DEK-encrypted column could not carry " +
                                 "ix_job_ads_organization_number nor serve the IN-set / GROUP BY). The " +
                                 "protection is at the SURFACING and LOG boundary: never logged, never " +
                                 "surfaced un-flagged; consumers mask via IsPersonnummerShaped. " +
                                 "RETENTION CHANGED in #841: it used to self-null with the purge; it now " +
                                 "persists indefinitely, so an Art. 17 erasure path must clear it " +
                                 "EXPLICITLY (#842 Tier B).",

        // --- derived-but-safe ---
        ["ExtractedTerms"] = "derived from Title/Description (no TTL) + ACL requirements. C#-written at " +
                             "ingest. Structurally decoupled from UpdateFromSource — a known, tracked " +
                             "asymmetry (#874), milder than #841: staleness, not destruction.",
    };

    /// <summary>
    /// Source files permitted to structured-log (<c>{@…}</c>-destructure) ANYTHING. Empty, and it should
    /// stay that way — see <see cref="No_source_file_structured_logs_anything"/> for why the guard is a
    /// blanket prohibition rather than a list of forbidden type names.
    /// </summary>
    private static readonly string[] DestructuringAllowlist = [];

    [Fact]
    public void Every_public_property_on_JobAd_is_classified()
    {
        var actual = typeof(JobAd)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        var unclassified = actual.Where(n => !ClassifiedJobAdProperties.ContainsKey(n)).ToList();

        unclassified.ShouldBeEmpty(
            "A new public property has appeared on the JobAd aggregate and nobody has answered the " +
            "question that four column additions in a row failed to ask:\n\n" +
            "    IS IT DERIVED FROM raw_payload?\n\n" +
            "If yes: it must NOT be a database generated column (the purge nulls raw_payload after 30 " +
            "days and Postgres recomputes the derived column to NULL — that is #841, which silently cost " +
            "filtered search and the matching engine ~21.5h of every 24). Parse it in the ACL and write " +
            "it through JobAd.SetSourcePayload, atomically with the payload.\n\n" +
            "And if it can hold an organisation number, it can hold a sole proprietor's personnummer — " +
            "classify it as PII and keep it off every log and every DTO (ADR 0087 D8, CLAUDE.md §5).\n\n" +
            "Classify it in ClassifiedJobAdProperties with a reason. Unclassified: " +
            string.Join(", ", unclassified));
    }

    [Fact]
    public void Classification_has_no_fossils()
    {
        // The inverse direction: a classification entry for a property that no longer exists is a lie
        // that makes the dictionary look more thorough than it is.
        var actual = typeof(JobAd)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var fossils = ClassifiedJobAdProperties.Keys.Where(k => !actual.Contains(k)).ToList();

        fossils.ShouldBeEmpty(
            "These classified properties no longer exist on JobAd — remove them, or the classification " +
            "overstates its own coverage: " + string.Join(", ", fossils));
    }

    [Fact]
    public void The_seven_facets_are_all_present_and_classified_as_facets()
    {
        // Non-vacuity for the guard above: if someone deleted a facet property, the classification test
        // would still pass (it only checks the forward direction), and this suite would quietly stop
        // guarding the thing it exists for.
        string[] facets =
        [
            "SsykConceptId", "OccupationGroupConceptId", "MunicipalityConceptId", "RegionConceptId",
            "EmploymentTypeConceptId", "WorktimeExtentConceptId", "OrganizationNumber",
        ];

        foreach (var facet in facets)
        {
            typeof(JobAd).GetProperty(facet, BindingFlags.Public | BindingFlags.Instance)
                .ShouldNotBeNull($"JobAd.{facet} is one of the seven #841 source facets and must exist");

            ClassifiedJobAdProperties[facet].Contains("#841", StringComparison.Ordinal).ShouldBeTrue(
                $"JobAd.{facet} must stay classified as a source facet");
        }
    }

    [Fact]
    public void No_source_file_structured_logs_anything()
    {
        // A BLANKET prohibition on `{@…}` destructuring anywhere in src/ — not a list of forbidden type
        // names. That distinction is the entire finding of this guard's own review round, and it is worth
        // stating plainly because the first version got it wrong in the exact way it was built to prevent.
        //
        // v1 matched `{@JobAd}`, `{@JobAdImportItem}`, `{@JobAdFacets}` — three PascalCase spellings. But
        // an MEL / [LoggerMessage] placeholder is named after the VARIABLE, not the type, and the source
        // generator binds it case-insensitively. So `{@jobAd}` — the most natural thing a developer
        // actually writes — sailed straight through, as did `{@ad}` and `{@item}`. The guard that was the
        // agreed PRICE for promoting a personnummer-shaped org.nr onto the aggregate could be defeated by
        // a lower-case letter. Both reviewers found it independently; the code reviewer called it "the
        // mutation you did not run", and it was.
        //
        // The fix costs nothing, which is what makes the original choice indefensible: `grep -rn "{@" src`
        // returns ZERO code hits today. A blanket ban therefore needs an EMPTY allowlist, and the
        // invariant becomes SHAPE-based (destructuring is forbidden) instead of NAME-based (these three
        // spellings are forbidden). A name-based guard on an unbounded set of names is not a prohibition;
        // it is a spelling test.
        //
        // Why forbid destructuring outright rather than only on PII types: `{@x}` serialises an object's
        // whole public surface into the log. On a JobAd that is an org.nr (a sole proprietor's org.nr IS
        // a personnummer, in plaintext — ADR 0087 D8(c), CLAUDE.md §5, highest priority) plus raw_payload
        // plus the recruiter free-text in description (#842). We have no legitimate use for `{@…}`
        // anywhere, so the cheap, unforgeable rule is: none.
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (DestructuringAllowlist.Contains(relative, StringComparer.OrdinalIgnoreCase))
                continue;

            if (Destructures(File.ReadAllText(file)))
                offenders.Add(relative);
        }

        offenders.ShouldBeEmpty(
            "A source file structured-logs with `{@…}` (destructuring). That serialises the object's " +
            "whole public surface into the log. On a JobAd or a JobAdImportItem that includes an " +
            "ORGANISATION NUMBER — and a sole proprietor's org.nr IS a personnummer, in plaintext " +
            "(ADR 0087 D8(c); CLAUDE.md §5 makes the personnummer guard the highest-priority rule) — " +
            "plus raw_payload and the recruiter free-text in description (#842).\n\n" +
            "Log the id, or named non-PII fields: `{JobAdId}`, `{Title}`. Never `{@object}`. " +
            "Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Log_destructuring_guard_catches_every_spelling_including_the_lower_case_one()
    {
        // Self-proving negative — and specifically the mutations that DEFEATED the first version of this
        // guard. Each of these was green before the guard became shape-based.
        Destructures("""LogInformation("processing {@JobAd} now", jobAd);""").ShouldBeTrue();
        Destructures("""LogInformation("processing {@jobAd} now", jobAd);""").ShouldBeTrue(
            "lower-case is the form a developer naturally writes — the MEL placeholder is named after " +
            "the variable, not the type, and it binds case-insensitively. A name-based guard misses it.");
        Destructures("""LogWarning("skipped {@item}", item);""").ShouldBeTrue(
            "an alias defeats any guard keyed on type names — hence the blanket rule");
        Destructures("""LogWarning("upserting {@ad}", jobAd);""").ShouldBeTrue();
        Destructures("""LogInformation("item {@JobAdImportItem} skipped", item);""").ShouldBeTrue();

        // ...and it must not fire on the safe forms, or people will route around it.
        Destructures("""LogInformation("archived {JobAdId}", jobAd.Id);""").ShouldBeFalse();
        Destructures("""LogInformation("ad {Title} imported", jobAd.Title);""").ShouldBeFalse();
        Destructures("""LogInformation("count {Count} of {Total}", n, total);""").ShouldBeFalse();
    }

    [Fact]
    public void Log_destructuring_guard_reads_only_string_literals_never_prose()
    {
        // The scan looks INSIDE string literals only. It must not fire on a comment that merely names the
        // forbidden pattern — otherwise the guard punishes the very documentation that explains it (and
        // it did exactly that on its first run, tripping over this file's own XML docs), and the next
        // author's fix would be to delete the explanation rather than the leak.
        Destructures("// never write {@JobAd} in a log template").ShouldBeFalse();
        Destructures("/// <summary>Do not <c>{@JobAdFacets}</c>-destructure.</summary>").ShouldBeFalse();

        // ...while still catching a real leak that sits directly beneath such a comment.
        Destructures("""
            // never write {@JobAd} in a log template
            LogInformation("ad {@JobAd}", jobAd);
            """).ShouldBeTrue();
    }

    /// <summary>
    /// True when ANY <c>{@…}</c> destructuring placeholder appears inside an actual string literal.
    /// Shape-based, not name-based — see <see cref="No_source_file_structured_logs_anything"/>.
    /// Comments are excluded deliberately: a guard that fires on the prose describing it teaches people
    /// to delete the prose.
    /// </summary>
    private static bool Destructures(string source) =>
        StringLiterals(source).Any(literal => Regex.IsMatch(literal, @"\{@\s*\w"));

    // One pass over the source, classifying comments and string literals. Only the literals come back:
    // matching a comment and a log template with the same regex is how this guard first went wrong.
    private static IEnumerable<string> StringLiterals(string source)
    {
        const string pattern =
            @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +          // line + block comments (incl. /// docs)
            @"|(?<raw>""""""[\s\S]*?"""""")" +                 // raw string literals
            @"|(?<str>@?""(?:[^""\\\n]|\\.|"""")*"")";         // regular + verbatim literals

        foreach (Match m in Regex.Matches(source, pattern))
        {
            if (m.Groups["raw"].Success)
                yield return m.Groups["raw"].Value;
            else if (m.Groups["str"].Success)
                yield return m.Groups["str"].Value;
            // comments: dropped
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not locate the repo root (Jobbliggaren.sln)");
        return dir!.FullName;
    }
}
