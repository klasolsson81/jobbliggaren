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
/// #802 / ADR 0066 / ADR 0124 / #183 — the AWS surface is ZERO. No Amazon package may be declared
/// anywhere, no source file may import an Amazon namespace, and no assembly may depend on an
/// Amazon type. There is no allow-list any more, in either half.
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
    private static readonly string[] ScannedRoots = ["src", "tests"];

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
        files.Select(f => RelativeTo(repoRoot, f))
            .ShouldContain(
                "Directory.Packages.props",
                "the central package-version file is not in the scan set, so a declaration added "
                + "there would be invisible to the guard above");

        var declaredIds = files
            .SelectMany(f => PackageElement.Matches(File.ReadAllText(f)))
            .Select(m => m.Groups["id"].Value)
            .ToArray();

        declaredIds.ShouldNotBeEmpty(
            "the package-element regex matched nothing across every csproj and props file in the "
            + "repo. The guard above would pass vacuously in that state.");

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
        files.Select(f => RelativeTo(repoRoot, f))
            .ShouldContain("src/Jobbliggaren.Infrastructure/Email/ScalewayEmailSender.cs");

        // Crossing the control: the exact lines that must be caught, and near misses that must not
        // be, so the predicate cannot pass by matching everything either.
        ImportsAnAmazonNamespace("using Amazon;").ShouldBeTrue();
        ImportsAnAmazonNamespace("using Amazon.SimpleEmailV2;").ShouldBeTrue();
        ImportsAnAmazonNamespace("    using Amazon.Runtime;").ShouldBeTrue();
        ImportsAnAmazonNamespace("// using Amazon.SimpleEmailV2;").ShouldBeFalse();
        ImportsAnAmazonNamespace("using Jobbliggaren.Infrastructure.Email;").ShouldBeFalse();
    }

    /// <summary>
    /// The fact the text scans cannot express: a fully-qualified <c>Amazon.RegionEndpoint</c> needs
    /// no <c>using</c> line, so it is invisible above. This reads IL.
    /// <para>
    /// It covers Infrastructure too, which the ADR 0124 form had to exempt.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AssembliesThatMayNeverTouchAmazon))]
    public void NoAssemblyDependsOnAnAmazonType(string name, Assembly assembly)
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
    ///     depends on <c>Microsoft</c>, so a search that had stopped matching would be caught.</item>
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

    private static bool ImportsAnAmazonNamespace(string line) =>
        line.TrimStart().StartsWith("using Amazon", StringComparison.Ordinal);

    private static IEnumerable<string> ProjectAndPropsFiles(string repoRoot) =>
        ScannedRoots
            .Select(sub => Path.Combine(repoRoot, sub))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Append(Path.Combine(repoRoot, "Directory.Packages.props"))
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
