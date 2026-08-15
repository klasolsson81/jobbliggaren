using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.Authorization;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Migrate;
using Jobbliggaren.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #802 / ADR 0066 / ADR 0124 / #183 — the AWS surface is ZERO, and there is no allow-list any
/// more in either half. Three facts, and each one's REACH is stated rather than implied, because
/// this file's previous revision claimed "anywhere" while scanning less than that (code-reviewer
/// Major, PR #1339):
/// <list type="number">
///   <item><b>No package declaration</b> in any <c>.csproj</c>, <c>.props</c> or <c>.targets</c>
///     under <c>src/</c>, <c>tests/</c> or <c>perf/</c>, plus the repo-root build files — which is
///     every place a declaration can reach a project.</item>
///   <item><b>No import</b>, in any C# form, in any <c>.cs</c> file under those same roots.</item>
///   <item><b>No IL dependency</b> in the six PRODUCTION assemblies. Test assemblies are out of
///     reach here and covered by (1) and (2) instead — see
///     <see cref="NoProductionAssemblyDependsOnAnAmazonType"/> for why, rather than a claim that
///     they are covered.</item>
/// </list>
///
/// <para>
/// <b>What changed, and why this is a RATCHET rather than a rewrite.</b> ADR 0124 narrowed a
/// blanket ban into an allow-list of exactly one package id
/// (<c>AWSSDK.SimpleEmailV2</c>) in exactly two files, plus one directory allowed to import
/// <c>Amazon.*</c>, because transactional mail ran on Amazon SES v2. #183 moved transactional mail
/// to Scaleway Transactional Email, whose arm is a hand-rolled <c>HttpClient</c> — Scaleway ships
/// no .NET SDK, so the replacement adds no package. The allow-list's only member is gone, so the
/// allow-list is gone with it. The invariant #802 established is untouched and still the reason
/// this file exists: <b>field encryption is Local-only</b> (<c>LocalDataKeyProvider</c>) — no KMS,
/// no Secrets Manager.
/// </para>
///
/// <para>
/// <b>The old file warned that an allow-list outliving its only member silently permits AWS
/// forever.</b> That warning is being honoured here rather than quoted: this is the change it was
/// written for.
/// </para>
///
/// <para>
/// <b>What the IL fact can and cannot anchor now, said plainly.</b> While the SES arm existed, the
/// IL fact's non-vacuity anchor was a POSITIVE match — the arm's own types provably depended on
/// <c>Amazon</c>, so a search that had silently emptied could be detected. With zero Amazon
/// anywhere, no positive match is constructible and that anchor is not merely weaker, it is
/// unavailable: the one-character <c>"Amazon"</c> → <c>"Amazon."</c> edit measured in ADR 0124
/// would now leave every fact here green whether or not it was made. What remains anchorable is
/// the MECHANISM — that the type sets are non-empty, and that NetArchTest still matches on
/// dot-delimited segments so a trailing dot still empties a search — and that is what
/// <see cref="TheDependencySearchMechanismIsLiveAndSegmentMatched"/> pins, over a namespace that
/// IS present. The real defence against a package returning is the text scan below, which needs no
/// compiled reference to see a declaration.
/// </para>
/// </summary>
public class NoAmazonReferenceTests
{
    /// <summary>
    /// <c>perf</c> joined 2026-08-15: <c>perf/Jobbliggaren.LoadTests</c> is a tracked csproj with
    /// live <c>PackageReference</c>s that sat outside the scan while this file's messages claimed
    /// to adjudicate "anywhere" (code-reviewer Major, PR #1339).
    /// </summary>
    private static readonly string[] ScannedRoots = ["src", "tests", "perf"];

    /// <summary>
    /// Every build-file shape that can declare a package. <c>.props</c>/<c>.targets</c> were absent
    /// until 2026-08-15, and that was not theoretical: <c>tests/Directory.Build.props</c> ALREADY
    /// carries a live <c>PackageReference</c> (the MTP coverage extension), so one line added there
    /// would have handed the package to all seven test projects invisibly.
    /// </summary>
    private static readonly string[] BuildFilePatterns = ["*.csproj", "*.props", "*.targets"];

    /// <summary>
    /// A namespace every scanned assembly genuinely depends on, used to prove the dependency search
    /// still finds anything at all. Not an assertion about the BCL.
    /// <para>
    /// <b><c>Microsoft</c> was the obvious choice and is WRONG</b>, measured 2026-08-15:
    /// <c>Jobbliggaren.Domain</c> depends on it nowhere, which is CLAUDE.md §2.1's whole point —
    /// "Domain depends on nothing". <c>System</c> is the only namespace that survives that rule,
    /// since every type in every assembly derives from <c>System.Object</c>.
    /// </para>
    /// </summary>
    private const string ProbeNamespace = "System";

    /// <summary>
    /// Matches a package element and captures its <c>Include</c> id. Scans the WHOLE file text
    /// rather than line-by-line: a line-scoped form required the element name and the
    /// <c>Include="…"</c> to sit on the SAME line, so an element whose attribute wrapped onto a
    /// continuation line slipped through entirely.
    /// </summary>
    private static readonly Regex PackageElement = new(
        @"<Package(?:Reference|Version)\b[^>]*?Include\s*=\s*""(?<id>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [Fact]
    public void NoProjectDeclaresAnAmazonPackage()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in ProjectAndPropsFiles(repoRoot))
        {
            var text = File.ReadAllText(file);
            var relative = RelativeTo(repoRoot, file);

            foreach (Match match in PackageElement.Matches(text))
            {
                var id = match.Groups["id"].Value;
                if (!IsAmazonPackageId(id))
                {
                    continue;
                }

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{relative}:{line} ({id})");
            }
        }

        offenders.ShouldBeEmpty(
            "No Amazon package may be declared anywhere (#183 — transactional mail is Scaleway over "
            + "a hand-rolled HttpClient, and it adds no package). Field encryption stays Local-only "
            + "(#802): KeyManagementService, SecretsManager, S3, Bedrock, SimpleEmailV2 and every "
            + "AWSSDK.Extensions.* / AWS.Logger.* alike. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Non-vacuity for the package scan, and it replaces the retired allow-list anchor. Two ways
    /// that scan can pass while asserting nothing: the file enumeration comes back empty (wrong
    /// repo root, a moved directory), or the regex stops matching package elements at all. Both are
    /// checked here against files that certainly exist and certainly declare packages.
    /// </summary>
    [Fact]
    public void ThePackageScanActuallyReadsPackageDeclarations()
    {
        var repoRoot = FindRepoRoot();
        var files = ProjectAndPropsFiles(repoRoot).ToArray();

        files.ShouldNotBeEmpty("the package scan enumerated no files — it is asserting over nothing");

        var scanned = files.Select(f => RelativeTo(repoRoot, f)).ToArray();

        // Each of these is a place a package CAN be declared and reach real projects, and each was
        // absent from the scan set before 2026-08-15 while this file's messages said "anywhere"
        // (code-reviewer Major, PR #1339). A guard cannot see its own under-reach when every item it
        // does scan passes, so the reach is asserted directly rather than inferred from a green run.
        scanned.ShouldContain(
            "Directory.Packages.props",
            "the central package-version file is not scanned — under CPM every version lives there");
        scanned.ShouldContain(
            "Directory.Build.props",
            "the repo-root build file is not scanned — an ItemGroup there reaches EVERY project");
        scanned.ShouldContain(
            "tests/Directory.Build.props",
            "the test-tree build file is not scanned — it already carries a live PackageReference, "
            + "so one line there reaches all seven test projects");
        scanned.ShouldContain(
            f => f.StartsWith("perf/", StringComparison.Ordinal) && f.EndsWith(".csproj", StringComparison.Ordinal),
            "no perf project is scanned — perf/Jobbliggaren.LoadTests is tracked and declares packages");

        var declaredIds = files
            .SelectMany(f => PackageElement.Matches(File.ReadAllText(f)))
            .Select(m => m.Groups["id"].Value)
            .ToArray();

        declaredIds.ShouldNotBeEmpty(
            "the package-element regex matched nothing across the scanned build files. The guard "
            + "above would pass vacuously in that state.");

        // A concrete id the repo certainly declares, so "the regex matches something" cannot be
        // satisfied by an accident of parsing.
        declaredIds.ShouldContain("QuestPDF");
    }

    [Fact]
    public void NoSourceFileImportsAnAmazonNamespace()
    {
        var repoRoot = FindRepoRoot();

        var offenders = SourceFiles(repoRoot)
            .Where(file => File.ReadLines(file).Any(ImportsAnAmazonNamespace))
            .Select(file => RelativeTo(repoRoot, file))
            .ToArray();

        offenders.ShouldBeEmpty(
            "No source file may import an Amazon namespace (#183). The directory allow-list that "
            + "existed for the SES arm was removed with the arm. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Non-vacuity for the import scan, in both halves: the enumeration reaches real files, and the
    /// predicate itself still recognises the thing it bans.
    /// <para>
    /// The predicate probe is the half that matters. A scan whose predicate has quietly stopped
    /// matching passes over a repo full of violations and looks exactly like a clean repo — and the
    /// file-count assertion alone cannot tell those apart, because it never crosses the control it
    /// is testing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheImportScanReachesFilesAndItsPredicateStillMatches()
    {
        var repoRoot = FindRepoRoot();
        var files = SourceFiles(repoRoot).ToArray();

        files.ShouldNotBeEmpty("the import scan enumerated no .cs files");
        files.Length.ShouldBeGreaterThan(
            100,
            "the import scan reached suspiciously few files — check the roots and the bin/obj filter");
        var scanned = files.Select(f => RelativeTo(repoRoot, f)).ToArray();
        scanned.ShouldContain("src/Jobbliggaren.Infrastructure/Email/ScalewayEmailSender.cs");
        // The tests/ tree is scanned too since 2026-08-15, and a GlobalUsings.cs is exactly where
        // one line would hand a whole project the namespace — so its reach is asserted, not assumed.
        scanned.ShouldContain("tests/Jobbliggaren.Application.UnitTests/GlobalUsings.cs");

        // Crossing the control: every FORM that must be caught, and near misses that must not be,
        // so the predicate cannot pass by matching everything either. The last three positives are
        // the ones the old StartsWith predicate missed entirely (code-reviewer Major, PR #1339) —
        // and `global using` is live house idiom here, not a hypothetical.
        ImportsAnAmazonNamespace("using Amazon;").ShouldBeTrue();
        ImportsAnAmazonNamespace("using Amazon.SimpleEmailV2;").ShouldBeTrue();
        ImportsAnAmazonNamespace("    using Amazon.Runtime;").ShouldBeTrue();
        ImportsAnAmazonNamespace("global using Amazon.SimpleEmailV2;").ShouldBeTrue();
        ImportsAnAmazonNamespace("using static Amazon.RegionEndpoint;").ShouldBeTrue();
        ImportsAnAmazonNamespace("using Ses = Amazon.SimpleEmailV2;").ShouldBeTrue();

        ImportsAnAmazonNamespace("// using Amazon.SimpleEmailV2;").ShouldBeFalse();
        ImportsAnAmazonNamespace("using Jobbliggaren.Infrastructure.Email;").ShouldBeFalse();
        // Segment boundary, stated as a decision rather than left to be discovered: a root namespace
        // that merely begins with those letters belongs to somebody else. The package half still
        // bans every id starting with "Amazon".
        ImportsAnAmazonNamespace("using AmazonianRiver.Things;").ShouldBeFalse();
    }

    /// <summary>
    /// The fact the text scans cannot express: a fully-qualified <c>Amazon.RegionEndpoint</c> needs
    /// no <c>using</c> line, so it is invisible above. This reads IL.
    /// <para>
    /// It covers Infrastructure too, which the ADR 0124 form had to exempt.
    /// </para>
    /// <para>
    /// <b>PRODUCTION assemblies only, and that is a limit rather than an oversight.</b> This project
    /// references the six <c>src/</c> projects and no test project, so no test assembly is reachable
    /// here — and referencing sibling test projects to reach them would invert the dependency
    /// direction between suites for a fact the text scans already cover: since 2026-08-15 those scan
    /// <c>tests/</c> (and <c>perf/</c>) for both package declarations and imports, in every import
    /// FORM. The message below therefore says what this fact adjudicates, not "no assembly anywhere".
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AssembliesThatMayNeverTouchAmazon))]
    public void NoProductionAssemblyDependsOnAnAmazonType(string name, Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .Should().NotHaveDependencyOn("Amazon")
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty(
            $"{name} must not depend on an Amazon SDK type — there is no Amazon SDK in this repo "
            + $"(#183). Offenders: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// Non-vacuity for the IL facts, at the only level still anchorable — see the class comment for
    /// why the ADR 0124 anchor (a positive Amazon match) cannot exist once the SDK is gone.
    /// <list type="bullet">
    ///   <item><b>The type sets are not empty</b>, so <c>NotHaveDependencyOn</c> is being asked
    ///     about real types rather than passing over nothing.</item>
    ///   <item><b>The search still finds a dependency that IS present</b> — every scanned assembly
    ///     depends on <c>System</c>, so a search that had stopped matching would be caught.</item>
    ///   <item><b>A trailing dot still empties the search</b>, which is the hazard that made the
    ///     <c>"Amazon"</c> spelling load-bearing in the first place. Measured in ADR 0124 on the
    ///     Amazon literal; pinned here on a literal that survives the SDK's removal, and it fails if
    ///     NetArchTest ever changes that matching semantics under us.</item>
    /// </list>
    /// </summary>
    [Theory]
    [MemberData(nameof(AssembliesThatMayNeverTouchAmazon))]
    public void TheDependencySearchMechanismIsLiveAndSegmentMatched(string name, Assembly assembly)
    {
        Types.InAssembly(assembly).GetTypes().ShouldNotBeEmpty(
            $"{name} presented no types at all, so the IL fact above asserts over an empty set");

        var matched = Types.InAssembly(assembly)
            .Should().NotHaveDependencyOn(ProbeNamespace)
            .GetResult();

        (matched.FailingTypeNames ?? []).ShouldNotBeEmpty(
            $"{name} registered no dependency on '{ProbeNamespace}', which every assembly has by "
            + "construction — every type derives from System.Object. The dependency search is not "
            + "matching, so the Amazon facts above are passing vacuously.");

        var trailingDot = Types.InAssembly(assembly)
            .Should().NotHaveDependencyOn(ProbeNamespace + ".")
            .GetResult();

        (trailingDot.FailingTypeNames ?? []).ShouldBeEmpty(
            $"'{ProbeNamespace}.' matched something. NetArchTest matched on dot-delimited segments "
            + "when this was written, so a trailing dot matched no segment and silently emptied the "
            + "search — that is why the Amazon literal above must never grow one. If this fails, the "
            + "semantics changed and the hazard this file guards against has moved.");
    }

    public static TheoryData<string, Assembly> AssembliesThatMayNeverTouchAmazon() =>
        new()
        {
            { "Jobbliggaren.Domain", typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly },
            { "Jobbliggaren.Application", typeof(Jobbliggaren.Application.AssemblyMarker).Assembly },
            // Infrastructure joins the list with #183: it held the ONLY exemption, for the SES arm's
            // namespace, and that arm is gone.
            { "Jobbliggaren.Infrastructure", typeof(EmailOptions).Assembly },
            { "Jobbliggaren.Api", typeof(AdminRoleRequirement).Assembly },
            { "Jobbliggaren.Worker", typeof(WorkerSystemUser).Assembly },
            // ConnectionStringFactory is Migrate's only public type — every other one is internal,
            // so it is the assembly handle, not a choice about what to test.
            { "Jobbliggaren.Migrate", typeof(ConnectionStringFactory).Assembly },
        };

    /// <summary>
    /// Which package ids this guard adjudicates.
    /// <para>
    /// <b><c>AWS.Logger.*</c> is here because of what those packages DO</b>, not for tidiness
    /// (security-auditor Minor 2, 2026-08-08). <c>AWS.Logger.AspNetCore</c>, <c>AWS.Logger.Core</c>
    /// and <c>AWS.Logger.SeriLog</c> are MEL providers that ship the application log to CloudWatch:
    /// exactly the pair "PII in a log" plus "a region nobody chose". They match neither
    /// <c>AWSSDK</c> nor <c>Amazon</c>.
    /// </para>
    /// </summary>
    private static bool IsAmazonPackageId(string id) =>
        id.StartsWith("AWSSDK", StringComparison.Ordinal)
        || id.StartsWith("Amazon", StringComparison.Ordinal)
        || id.StartsWith("AWS.", StringComparison.Ordinal);

    /// <summary>
    /// An Amazon import in every FORM C# offers, not just the one shape the old predicate knew.
    /// <para>
    /// <b><c>StartsWith("using Amazon")</c> was a NAME-based guard carrying a FORM-based ban</b>, and
    /// it missed <c>global using</c>, <c>using static</c> and aliased <c>using X = Amazon…</c>
    /// (code-reviewer Major, PR #1339). The first of those is not hypothetical: <c>global using</c>
    /// is live house idiom in five files, so a single line in a <c>GlobalUsings.cs</c> would have
    /// given a whole project the namespace with every fact here green.
    /// </para>
    /// <para>
    /// <c>Amazon</c> must be a whole namespace SEGMENT — followed by <c>.</c>, <c>;</c>, whitespace
    /// or end of line. That deliberately does NOT match <c>using AmazonFoo;</c>: a root namespace
    /// merely beginning with those letters is somebody else's, and the package half of this guard
    /// still bans any id starting with <c>Amazon</c>.
    /// </para>
    /// </summary>
    private static readonly Regex AmazonImport = new(
        @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_]\w*\s*=\s*)?Amazon(?:\s*[.;]|\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool ImportsAnAmazonNamespace(string line) => AmazonImport.IsMatch(line);

    private static IEnumerable<string> ProjectAndPropsFiles(string repoRoot) =>
        ScannedRoots
            .Select(sub => Path.Combine(repoRoot, sub))
            .Where(Directory.Exists)
            .SelectMany(root => BuildFilePatterns.SelectMany(
                pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)))
            // Repo-root build files sit under NO scanned root and are the strongest place to declare
            // a package: Directory.Packages.props holds every version under CPM, and a root
            // Directory.Build.props ItemGroup reaches every project beneath it. Enumerated by
            // PATTERN rather than by name, so a Directory.Build.targets added later is covered
            // without anyone remembering to add it here.
            .Concat(BuildFilePatterns.SelectMany(
                pattern => Directory.EnumerateFiles(repoRoot, pattern, SearchOption.TopDirectoryOnly)))
            .Where(File.Exists)
            .Where(p => !IsUnderBinOrObj(p));

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/</c> AND <c>tests/</c>. The test tree is scanned too since
    /// #183 — under the allow-list only production code was covered, which was defensible while a
    /// legitimate SDK existed and is not now.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string repoRoot) =>
        ScannedRoots
            .Select(sub => Path.Combine(repoRoot, sub))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(p => !IsUnderBinOrObj(p));

    private static bool IsUnderBinOrObj(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RelativeTo(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — arch-testet "
            + "behöver källträdet för källtext-scan");
        return dir!.FullName;
    }
}
