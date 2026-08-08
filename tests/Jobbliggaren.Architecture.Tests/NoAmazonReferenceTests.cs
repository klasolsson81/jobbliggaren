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
/// #802 / ADR 0066 / ADR 0124 — the AWS surface is an ALLOW-LIST of exactly one package id and
/// exactly one namespace prefix, in exactly the places named below. It is not a blanket ban, and
/// it was never quite the invariant the old message claimed.
///
/// <para>
/// <b>What changed, and why the ratchet was narrowed rather than loosened (ADR 0124, #1237).</b>
/// This file used to fail on ANY <c>AWSSDK.*</c>/<c>Amazon*</c> package element and ANY
/// <c>using Amazon</c> in production code, citing "AWS-exiten är slutförd (#802 / ADR 0066)".
/// Two corrections. First, the invariant #802 actually established is the one this file's own
/// doc comment always stated: <b>field encryption is Local-only</b> (<c>LocalDataKeyProvider</c>);
/// no KMS, no Secrets Manager. Second, <b>ADR 0066 never decided anything about SES</b> — measured
/// 2026-08-08, it contains zero occurrences of "SES" or "SimpleEmail". It tore down the DEPLOYED
/// dev stack (ECS/RDS/ALB/NAT/ECR) and explicitly PRESERVED the account-level surface. The SES
/// package's 2026-06-06 deletion was a correct consequence of dead code under a Hetzner premise,
/// not a rule; the rule was inferred afterwards and outgrew its warrant.
/// </para>
///
/// <para>
/// <b>Both predicates are load-bearing, and only one of them is a FORM rule.</b> Location is a
/// genuine form rule and carries the Clean-Architecture confinement (parity Refit/PdfPig/QuestPDF).
/// Identity is irreducibly a NAME: nothing structural separates <c>AWSSDK.SimpleEmailV2</c> from
/// <c>AWSSDK.KeyManagementService</c> — same publisher, same id shape — so inventing a "form" for
/// that half would be manufacturing a distinction that does not exist. Say so rather than pretend.
/// </para>
///
/// <para>
/// <b>Why there is now a NetArchTest fact, when the old comment said one was impossible.</b> That
/// comment was right while zero Amazon packages existed: an absent type cannot be referenced, so
/// there was no dependency to assert against. The moment <c>AWSSDK.Core</c> lands on the compile
/// surface the premise dies — <c>Amazon.RegionEndpoint</c>, <c>Amazon.Runtime.BasicAWSCredentials</c>
/// and <c>AWSConfigs</c> become referenceable FULLY QUALIFIED, with no <c>using</c> line at all,
/// from every project that transitively sees Infrastructure. Both text scans below are structurally
/// blind to that. This change opens that hole, so this change closes it.
/// </para>
///
/// <para>
/// <b>The anchors are not decoration.</b> Two facts assert the allow-list is non-vacuous. Without
/// them, removing the SES arm later would leave an allow-list that silently permits AWS forever —
/// the same quiet-death failure mode the i18n tripwires carry vacuity guards against.
/// </para>
/// </summary>
public class NoAmazonReferenceTests
{
    private static readonly string[] ScannedRoots = ["src", "tests"];

    /// <summary>
    /// The ONLY Amazon package ids this repo may declare. <c>AWSSDK.Core</c> is listed even though
    /// it currently resolves transitively (SimpleEmailV2 4.0.102.1 requires
    /// <c>AWSSDK.Core [4.0.100.9, 5.0.0)</c>, and 4.0.100.9 is latest), so that a future CVE pin
    /// does not require re-amending this guard under time pressure.
    /// </summary>
    private static readonly HashSet<string> AllowedAmazonPackageIds =
        new(StringComparer.Ordinal) { "AWSSDK.SimpleEmailV2", "AWSSDK.Core" };

    /// <summary>Repo-relative paths (forward slashes) allowed to declare an Amazon package.</summary>
    private static readonly HashSet<string> FilesAllowedToDeclareAmazonPackages =
        new(StringComparer.Ordinal)
        {
            "Directory.Packages.props",
            "src/Jobbliggaren.Infrastructure/Jobbliggaren.Infrastructure.csproj",
        };

    /// <summary>The ONLY production directory allowed to import an Amazon namespace.</summary>
    private const string AllowedAmazonImportDirectory = "src/Jobbliggaren.Infrastructure/Email/";

    /// <summary>The namespace the SES arm lives in — everything else in Infrastructure is barred.</summary>
    private const string SesNamespace = "Jobbliggaren.Infrastructure.Email";

    /// <summary>
    /// Matches a package element and captures its <c>Include</c> id. Scans the WHOLE file text
    /// rather than line-by-line: the previous form required the element name and the
    /// <c>Include="…"</c> to sit on the SAME line, so an element whose attribute wrapped onto a
    /// continuation line slipped through entirely. That is a genuine strengthening of this guard,
    /// named here so it is not mistaken for refactoring.
    /// </summary>
    private static readonly Regex PackageElement = new(
        @"<Package(?:Reference|Version)\b[^>]*?Include\s*=\s*""(?<id>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [Fact]
    public void NoProjectDeclaresAnUnapprovedAmazonPackage()
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
                if (!id.StartsWith("AWSSDK", StringComparison.Ordinal)
                    && !id.StartsWith("Amazon", StringComparison.Ordinal))
                {
                    continue;
                }

                if (AllowedAmazonPackageIds.Contains(id)
                    && FilesAllowedToDeclareAmazonPackages.Contains(relative))
                {
                    continue;
                }

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{relative}:{line} ({id})");
            }
        }

        offenders.ShouldBeEmpty(
            "The AWS surface is an allow-list of exactly two package ids in exactly two files "
            + "(ADR 0124 — SES v2 is the transactional mail provider, #1237). Field encryption stays "
            + "Local-only (#802): KeyManagementService, SecretsManager, S3, Bedrock and every "
            + "AWSSDK.Extensions.* — the logging adaptors especially — remain banned. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheAmazonPackageAllowlistIsNotVacuous()
    {
        var repoRoot = FindRepoRoot();

        foreach (var relative in FilesAllowedToDeclareAmazonPackages)
        {
            var text = File.ReadAllText(Path.Combine(repoRoot, relative));
            PackageElement.Matches(text)
                .Any(m => m.Groups["id"].Value == "AWSSDK.SimpleEmailV2")
                .ShouldBeTrue(
                    $"{relative} no longer declares AWSSDK.SimpleEmailV2. If the SES arm was removed, "
                    + "REMOVE THIS ALLOW-LIST TOO — an allow-list that outlives its only member "
                    + "silently permits every future AWS package (ADR 0124).");
        }
    }

    [Fact]
    public void NoProductionSourceOutsideTheSesArmImportsAnAmazonNamespace()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        var offenders = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsUnderBinOrObj(p))
            .Where(file => !RelativeTo(repoRoot, file)
                .StartsWith(AllowedAmazonImportDirectory, StringComparison.Ordinal))
            .Where(file => File.ReadLines(file).Any(line =>
                line.TrimStart().StartsWith("using Amazon", StringComparison.Ordinal)))
            .Select(file => RelativeTo(repoRoot, file))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Amazon namespaces may be imported only under {AllowedAmazonImportDirectory} "
            + "(ADR 0124). In particular the DI composition root must stay textually Amazon-free — "
            + "client construction belongs in Email/SesClientRegistration.cs. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheAmazonImportAllowlistIsNotVacuous()
    {
        var repoRoot = FindRepoRoot();
        var allowedDirectory = Path.Combine(repoRoot, AllowedAmazonImportDirectory);

        Directory.EnumerateFiles(allowedDirectory, "*.cs", SearchOption.AllDirectories)
            .Any(file => File.ReadLines(file).Any(line =>
                line.TrimStart().StartsWith("using Amazon.SimpleEmailV2", StringComparison.Ordinal)))
            .ShouldBeTrue(
                $"No file under {AllowedAmazonImportDirectory} imports Amazon.SimpleEmailV2. If the "
                + "SES arm was removed, REMOVE THIS ALLOW-LIST TOO (ADR 0124).");
    }

    /// <summary>
    /// The fact the text scans cannot express. A fully-qualified <c>Amazon.RegionEndpoint</c> needs
    /// no <c>using</c> line, so it is invisible above; this reads IL.
    /// </summary>
    [Fact]
    public void NoInfrastructureTypeOutsideTheSesArmDependsOnAnAmazonType()
    {
        var result = Types.InAssembly(typeof(EmailOptions).Assembly)
            .That().DoNotResideInNamespaceStartingWith(SesNamespace)
            .Should().NotHaveDependencyOn("Amazon")
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty(
            $"Amazon SDK types are confined to {SesNamespace} (ADR 0124). This fact exists BECAUSE "
            + "the package is back: with AWSSDK.Core on the compile surface a fully-qualified "
            + "Amazon.* reference needs no using line and is invisible to the text scans above. "
            + $"Offenders: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [MemberData(nameof(AssembliesThatMayNeverTouchAmazon))]
    public void NoAssemblyOutsideInfrastructureDependsOnAnAmazonType(string name, Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .Should().NotHaveDependencyOn("Amazon")
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty(
            $"{name} must never depend on an Amazon SDK type — the SES arm is Infrastructure-only "
            + $"and never crosses the IEmailSender port (ADR 0124). Offenders: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    public static TheoryData<string, Assembly> AssembliesThatMayNeverTouchAmazon() =>
        new()
        {
            { "Jobbliggaren.Domain", typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly },
            { "Jobbliggaren.Application", typeof(Jobbliggaren.Application.AssemblyMarker).Assembly },
            { "Jobbliggaren.Api", typeof(AdminRoleRequirement).Assembly },
            { "Jobbliggaren.Worker", typeof(WorkerSystemUser).Assembly },
            // ConnectionStringFactory is Migrate's only public type — every other one is internal,
            // so it is the assembly handle, not a choice about what to test.
            { "Jobbliggaren.Migrate", typeof(ConnectionStringFactory).Assembly },
        };

    private static IEnumerable<string> ProjectAndPropsFiles(string repoRoot) =>
        ScannedRoots
            .Select(sub => Path.Combine(repoRoot, sub))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Append(Path.Combine(repoRoot, "Directory.Packages.props"))
            .Where(File.Exists)
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
