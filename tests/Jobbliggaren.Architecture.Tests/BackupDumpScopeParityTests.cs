using System.Text.RegularExpressions;
using Jobbliggaren.Migrate;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The offsite artefact's schema scope, bound to the schemas the provisioner actually creates.
///
/// <para>
/// <b>Why this exists (#1285, PR #1532).</b> `security-auditor`'s Major 1 on PR #1530 was that the
/// nightly dump carried a whole schema — <c>hangfire</c>, ~13 tables, not EF-mapped and therefore
/// outside <c>MappedPlaintextExposureRegistry</c> entirely — whose rows the plaintext enumeration
/// Klas signs against does not describe. Narrowing the dump closed that. It did not close the
/// question underneath it, which she graded separately: <i>nothing measures that the scope still
/// covers the schemas that exist.</i> A fourth schema would join the artefact unclassified, and no
/// gate would say so.
/// </para>
///
/// <para>
/// <b>Why it is derived rather than a list.</b> `senior-cto-advisor` rejected a hand-written
/// classification list on PR #1530 as "prosa i C#-kostym" — ADR 0024's error, a thing that reads
/// as pinned and decays. The provisioned set here is read off
/// <see cref="PhaseASchemaGrants"/> by reflection, so it is the provisioner's own answer rather
/// than a copy of it: adding a fourth property there breaks this build until someone decides
/// whether the artefact carries that schema or excludes it. <see cref="CarriedByTheArtefact"/> is
/// the one thing that cannot be derived, because it IS the decision — and it is two words long
/// precisely so the derivation does the work instead.
/// </para>
///
/// <para>
/// <b>The polarity is asserted, not assumed.</b> `--exclude-schema` and never
/// `--schema`: measured 2026-08-28, the allow-list form makes pg_dump drop objects the selected
/// schemas depend on, so the artefact lost <c>CREATE EXTENSION pg_trgm</c> while still emitting the
/// two GIN indexes that need it — <c>pg_restore</c> exit 1, three ignored errors, against exit 0
/// for the exclusion form.
/// </para>
/// </summary>
public class BackupDumpScopeParityTests
{
    private const string BackupScript = "deploy/systemd/jobbliggaren-backup.sh";

    private const string Drill =
        "tests/Jobbliggaren.Worker.IntegrationTests/Backup/BackupRestoreDrillTests.cs";

    /// <summary>
    /// The schemas the main artefact is meant to carry. This is the DECISION and so it is the one
    /// set here that is written rather than derived; everything it is checked against is read off
    /// the provisioner. Extending it is how a future schema gets classified INTO the artefact —
    /// deliberately, in a diff, rather than by arriving.
    /// </summary>
    private static readonly HashSet<string> CarriedByTheArtefact =
        new(StringComparer.Ordinal) { "public", "identity" };

    /// <summary>
    /// Every schema <see cref="PhaseASchemaGrants"/> provisions, read from the SQL itself. Each of
    /// its statement lists names its schema in an <c>ON SCHEMA x</c> / <c>IN SCHEMA x</c> /
    /// <c>CREATE SCHEMA IF NOT EXISTS x</c> clause, so the name never has to be repeated here.
    /// </summary>
    private static HashSet<string> ProvisionedSchemas()
    {
        var pattern = new Regex(
            @"(?:CREATE SCHEMA IF NOT EXISTS|ON SCHEMA|IN SCHEMA)\s+(\w+)",
            RegexOptions.IgnoreCase);

        var lists = typeof(PhaseASchemaGrants)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(IReadOnlyList<PrivilegeStatement>))
            .ToList();

        // An empty reflection result would make every assertion below vacuously true, which this
        // repo treats as a finding rather than a pass: an absence must never read as a verdict.
        lists.ShouldNotBeEmpty(
            "no PrivilegeStatement lists found on PhaseASchemaGrants — the shape moved and this " +
            "gate measured nothing.");

        var schemas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            var statements = (IReadOnlyList<PrivilegeStatement>)list.GetValue(null)!;
            var named = statements
                .SelectMany(s => pattern.Matches(s.Sql).Select(m => m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            named.Count.ShouldBe(1,
                $"PhaseASchemaGrants.{list.Name} names {named.Count} schemas " +
                $"({string.Join(", ", named)}); this gate reads one schema per list and cannot " +
                "classify a list that spans several.");

            schemas.Add(named[0]);
        }

        return schemas;
    }

    /// <summary>
    /// The script's own lines with comments removed. Load-bearing: the call site deliberately
    /// spells out BOTH flag forms in prose (which one is used and which must never be), so a
    /// naive search of the file text finds the rejected form and reads it as live.
    /// </summary>
    private static List<string> ScriptCommandLines() =>
        [.. File.ReadAllLines(Path.Combine(RepositoryRoot(), BackupScript))
            .Where(line => !line.TrimStart().StartsWith('#'))];

    [Fact]
    public void EverySchemaTheProvisionerCreates_IsEitherCarriedOrExcluded()
    {
        var provisioned = ProvisionedSchemas();

        var excluded = ScriptCommandLines()
            .SelectMany(line => Regex.Matches(line, @"--exclude-schema=(\w+)")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        excluded.ShouldNotBeEmpty(
            "the main dump excludes no schema at all. Either the flag was dropped — in which case " +
            "the artefact is carrying hangfire's job arguments and stack traces again (#1285) — or " +
            "the invocation moved and this gate is now blind.");

        excluded.ShouldBeSubsetOf(provisioned,
            "the dump excludes a schema the provisioner never creates, so the exclusion does " +
            "nothing and the schema it was meant to name is still in the artefact.");

        var classified = new HashSet<string>(CarriedByTheArtefact, StringComparer.Ordinal);
        classified.UnionWith(excluded);

        provisioned.ShouldBe(classified, ignoreOrder: true,
            "a schema this system provisions is neither carried by the backup deliberately nor " +
            "excluded from it deliberately. Unclassified means it JOINS the offsite artefact " +
            "silently, which is exactly the defect #1285 closed one layer down. Decide: add it to " +
            "CarriedByTheArtefact, or give the dump another --exclude-schema.");
    }

    [Fact]
    public void TheDumpExcludesSchemas_RatherThanSelectingThem()
    {
        var selecting = ScriptCommandLines()
            .Where(line => Regex.IsMatch(line, @"--schema="))
            .ToList();

        selecting.ShouldBeEmpty(
            "the main dump SELECTS schemas. pg_dump makes no attempt to dump objects the selected " +
            "schemas depend upon, so the allow-list form drops CREATE EXTENSION while keeping the " +
            "indexes that need it — measured 2026-08-28: pg_restore exit 1 and three ignored " +
            "errors, against exit 0 for --exclude-schema. Offending line(s): " +
            string.Join(" | ", selecting));
    }

    [Fact]
    public void TheDrill_DumpsWithTheMechanismsOwnScope()
    {
        var drillSource = File.ReadAllText(Path.Combine(RepositoryRoot(), Drill));

        var declared = Regex.Match(drillSource, @"BackupDumpScope\s*=\s*""([^""]*)""");
        declared.Success.ShouldBeTrue(
            "BackupRestoreDrillTests no longer declares BackupDumpScope, so the drill's dump " +
            "scope is a literal again and can drift from the mechanism's without any gate saying " +
            "so. That drift is what left the drill restoring a WIDER artefact than the box " +
            "produces while its own 'errors ignored on restore' oracle stayed green.");

        var scriptScope = string.Join(' ', ScriptCommandLines()
            .SelectMany(line => Regex.Matches(line, @"--exclude-schema=\w+").Select(m => m.Value))
            .Distinct(StringComparer.Ordinal));

        declared.Groups[1].Value.ShouldBe(scriptScope,
            "the drill dumps with different flags than deploy/systemd/jobbliggaren-backup.sh, so " +
            "gate M-4 proves a restore of an artefact the box does not produce. A backup is a " +
            "hypothesis until a restore has run — of the real artefact.");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(dir.FullName, "src"))))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
