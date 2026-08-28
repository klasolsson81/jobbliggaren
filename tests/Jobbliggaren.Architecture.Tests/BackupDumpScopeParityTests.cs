using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Migrate;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The offsite artefact's schema scope, bound to the schemas this system actually creates.
///
/// <para>
/// <b>Why this exists (#1285, PR #1532).</b> `security-auditor`'s Major 1 on PR #1530 was that the
/// nightly dump carried a whole schema — <c>hangfire</c>, ~13 tables, not EF-mapped and therefore
/// outside <c>MappedPlaintextExposureRegistry</c> entirely — whose rows the plaintext enumeration
/// Klas signs against does not describe. Narrowing the dump closed that. It did not close the
/// question underneath it, which she graded separately: <i>nothing measures that the scope still
/// covers the schemas that exist.</i> A new schema would join the artefact unclassified, and no gate
/// would say so.
/// </para>
///
/// <para>
/// <b>Why it is derived rather than a list.</b> `senior-cto-advisor` rejected a hand-written
/// classification list on PR #1530 as "prosa i C#-kostym" — ADR 0024's error, a thing that reads as
/// pinned and decays. The created set here is read off the two actors that create schemas, so it is
/// their own answer rather than a copy of it. <see cref="CarriedByTheArtefact"/> is the one thing
/// that cannot be derived, because it IS the decision — and it is two words long precisely so the
/// derivation does the work instead.
/// </para>
///
/// <para>
/// <b>Two creating actors, not one, and the second is the one that has actually been used.</b>
/// <see cref="PhaseASchemaGrants"/> is the provisioner. But <c>identity</c> was created by an EF
/// migration's <c>EnsureSchema</c> under <c>bootstrap</c> mode's master credentials, which never
/// touches the provisioner — so deriving from the provisioner alone leaves exactly the path this
/// repository has already taken once. Both are read.
/// </para>
///
/// <para>
/// <b>What remains outside, stated rather than implied:</b> DDL applied by hand on the box, and an
/// extension that creates a schema of its own. Neither is visible to any test suite, and the gate
/// that could see them would have to live in the backup script — which `senior-cto-advisor` ruled
/// against, because a gate that fails the nightly run trades disclosure risk for total data loss.
/// The residual is a consequence of that placement decision, not an oversight in this file.
/// </para>
/// </summary>
public class BackupDumpScopeParityTests
{
    private const string BackupScript = "deploy/systemd/jobbliggaren-backup.sh";

    private const string Drill =
        "tests/Jobbliggaren.Worker.IntegrationTests/Backup/BackupRestoreDrillTests.cs";

    private const string MigrationsRoot = "src";

    /// <summary>
    /// The schemas the main artefact is meant to carry. This is the DECISION and so it is the one
    /// set here that is written rather than derived; everything it is checked against is read off
    /// the actors that create schemas. Extending it is how a future schema gets classified INTO the
    /// artefact — deliberately, in a diff, rather than by arriving.
    /// </summary>
    private static readonly HashSet<string> CarriedByTheArtefact =
        new(StringComparer.Ordinal) { "public", "identity" };

    /// <summary>
    /// Every schema <see cref="PhaseASchemaGrants"/> provisions, read from the SQL itself. Each of
    /// its statement lists names its schema in an <c>ON SCHEMA x</c> / <c>IN SCHEMA x</c> /
    /// <c>CREATE SCHEMA IF NOT EXISTS x</c> clause, so the name never has to be repeated here.
    /// </summary>
    private static HashSet<string> ProvisionerSchemas()
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
                named.Count == 0
                    ? $"PhaseASchemaGrants.{list.Name} names no schema in any of its statements, so " +
                      "this gate cannot tell which schema it provisions. The SQL shape moved."
                    : $"PhaseASchemaGrants.{list.Name} names {named.Count} schemas " +
                      $"({string.Join(", ", named)}); this gate reads one schema per list and " +
                      "cannot classify a list that spans several.");

            schemas.Add(named[0]);
        }

        return schemas;
    }

    /// <summary>
    /// Every schema an EF migration creates with <c>EnsureSchema</c>. This is the second creating
    /// actor and the one the provisioner cannot see: `bootstrap` mode applies the Identity
    /// migrations under master credentials, which hold <c>CREATE ON DATABASE</c>, so a schema can
    /// arrive here without <see cref="PhaseASchemaGrants"/> ever mentioning it. That is how
    /// <c>identity</c> arrived.
    /// </summary>
    private static HashSet<string> MigrationSchemas()
    {
        var files = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), MigrationsRoot), "*.cs", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();

        files.ShouldNotBeEmpty(
            "no migration files found under src/ — the layout moved and this half of the " +
            "derivation measured nothing, which would let a migration-created schema pass.");

        return [.. files
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"EnsureSchema\s*\(\s*name:\s*""(\w+)""")
                .Select(m => m.Groups[1].Value))];
    }

    /// <summary>
    /// The script's <c>pg_dump</c> invocations, comments stripped and line continuations joined,
    /// each cut at the first pipe so only the dump's own argv is measured.
    ///
    /// <para>
    /// <b>Scoping to the invocation is load-bearing twice over.</b> A file-wide search cannot tell
    /// the main dump from the DEK dump, so a scope flag MOVED from one call to the other would leave
    /// a file-wide gate green while the main artefact carried the schema again. And a file-wide
    /// search cannot read <c>-n</c> as pg_dump's <c>--schema</c>, because the same two characters
    /// are <c>journalctl -n 50</c> and <c>flock -n 9</c> elsewhere in this script — measured, three
    /// such lines today. Inside a pg_dump argv the reading is unambiguous.
    /// </para>
    /// </summary>
    private static List<string> DumpInvocations()
    {
        var invocations = new List<string>();
        var current = new StringBuilder();
        var inInvocation = false;

        foreach (var raw in File.ReadAllLines(Path.Combine(RepositoryRoot(), BackupScript)))
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            if (!inInvocation)
            {
                if (!trimmed.Contains("pg_dump", StringComparison.Ordinal))
                {
                    continue;
                }

                inInvocation = true;
                current.Clear();
            }

            current.Append(' ').Append(trimmed.TrimEnd('\\'));

            if (!trimmed.EndsWith('\\'))
            {
                var whole = current.ToString();
                var pipe = whole.IndexOf('|', StringComparison.Ordinal);
                invocations.Add(pipe >= 0 ? whole[..pipe] : whole);
                inInvocation = false;
            }
        }

        return invocations;
    }

    /// <summary>
    /// The main artefact's dump — the one narrowed by scope. Identified by the polarity flag that
    /// only it carries, rather than by position, so reordering the two calls cannot silently point
    /// this gate at the DEK dump.
    /// </summary>
    private static string MainDumpInvocation()
    {
        var invocations = DumpInvocations();

        invocations.Count.ShouldBeGreaterThanOrEqualTo(2,
            $"expected the script's two pg_dump invocations, found {invocations.Count}. The shape " +
            "moved and this gate cannot tell the main dump from the DEK dump.");

        var main = invocations
            .Where(i => i.Contains("--exclude-table-data", StringComparison.Ordinal))
            .ToList();

        main.Count.ShouldBe(1,
            $"expected exactly one pg_dump carrying --exclude-table-data (the main artefact), " +
            $"found {main.Count}.");

        return main[0];
    }

    private static HashSet<string> ExcludedSchemas() =>
        [.. Regex.Matches(MainDumpInvocation(), @"--exclude-schema=(\w+)").Select(m => m.Groups[1].Value)];

    [Fact]
    public void EverySchemaThisSystemCreates_IsEitherCarriedOrExcluded()
    {
        var created = ProvisionerSchemas();
        created.UnionWith(MigrationSchemas());

        var excluded = ExcludedSchemas();

        excluded.ShouldNotBeEmpty(
            "the main dump excludes no schema at all. Either the flag was dropped — in which case " +
            "the artefact is carrying hangfire's job arguments and stack traces again (#1285) — or " +
            "the invocation moved and this gate is now blind.");

        excluded.ShouldBeSubsetOf(created,
            "the main dump excludes a schema nothing in this system creates, so the exclusion does " +
            "nothing and the schema it was meant to name is still in the artefact.");

        var classified = new HashSet<string>(CarriedByTheArtefact, StringComparer.Ordinal);
        classified.UnionWith(excluded);

        created.ShouldBe(classified, ignoreOrder: true,
            "a schema this system creates is neither carried by the backup deliberately nor " +
            "excluded from it deliberately. Unclassified means it JOINS the offsite artefact " +
            "silently, which is exactly the defect #1285 closed one layer down. Decide: add it to " +
            "CarriedByTheArtefact, or give the main dump another --exclude-schema.");
    }

    [Fact]
    public void TheMainDumpExcludesSchemas_RatherThanSelectingThem()
    {
        var selecting = Regex.Matches(MainDumpInvocation(), @"(?<![\w-])(?:--schema\b|-n\b)")
            .Select(m => m.Value)
            .ToList();

        selecting.ShouldBeEmpty(
            "the main dump SELECTS schemas. pg_dump makes no attempt to dump objects the selected " +
            "schemas depend upon, so the allow-list form drops CREATE EXTENSION while keeping the " +
            "indexes that need it — measured 2026-08-28: pg_restore exit 1 and three ignored " +
            "errors, against exit 0 for --exclude-schema. Offending token(s): " +
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

        // Compared as SETS: the declaration and the script are two orderings of the same argv, and
        // a second exclusion must not fail this gate merely for being written in the other order.
        var declaredTokens = declared.Groups[1].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        var scriptTokens = Regex.Matches(MainDumpInvocation(), @"--exclude-schema=\w+")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);

        declaredTokens.ShouldBe(scriptTokens, ignoreOrder: true,
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
