using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0087 D5/D8 (#311 PR-4, PR-3 forward-note; senior-cto-advisor 2026-07-01) — the org.nr
/// surfacing/log-boundary guard. A Swedish sole-proprietorship (enskild firma) org.nr CAN EQUAL the
/// owner's personnummer (CLAUDE.md §5, highest-priority PII rule), so a raw org.nr must never be
/// surfaced un-flagged in a read DTO nor logged in plaintext. The org.nr lives PLAINTEXT at rest (D8,
/// Klas Art. 32 risk-accept) precisely BECAUSE the protection is concentrated here, at the
/// display/log boundary — so this boundary needs a build-time guard, not just discipline.
///
/// <para>
/// <b>Two invariants (the durable version of the PR-3 forward-note):</b>
/// <list type="number">
///   <item><b>DTO partition (fail-closed, forward-looking).</b> EVERY <c>*Dto</c> in the Application
///     assembly that exposes an org.nr-shaped member must be classified — either
///     <see cref="MaskingOrgNrDtos"/> (structurally mask-capable: a nullable <c>string?</c> org.nr it
///     CAN null + a <c>bool</c> protection flag) or <see cref="ExemptOrgNrDtos"/> (explicit, with a
///     reason). A new org.nr-surfacing DTO of ANY shape lands in neither and fails the build until a
///     human classifies it (Saltzer &amp; Schroeder fail-safe default; parity
///     <see cref="AccountHardDeleteCascadeFitnessTests"/>). The one present subject is
///     <see cref="CompanyWatchDto"/> (PR-3); the guard is forward-looking for PR-2b's disambiguation
///     DTO — the "reactive-catch" failure mode #374 was built to end.</item>
///   <item><b>Scan log-boundary (source-scan, the one present real subject).</b>
///     <c>CompanyWatchScanJob</c> reads raw org.nr to build its <c>IN</c>-set — its logging surface
///     (<c>[LoggerMessage]</c> templates + <c>Log*</c> call sites) must never reference an org.nr
///     token, so a raw org.nr is never logged (ADR 0087 D8 / §5). Load-bearing the moment PR-4
///     lands.</item>
/// </list>
/// </para>
///
/// <para>Both invariants are proven non-vacuous by self-proving negatives (a synthetic org.nr DTO the
/// partition must flag; a synthetic log fragment the scan must flag), mirroring the cascade fitness.</para>
/// </summary>
public class OrganizationNumberSurfacingGuardTests
{
    private const string ScanJobRelativePath =
        "src/Jobbliggaren.Application/CompanyWatches/Jobs/CompanyWatchScan/CompanyWatchScanJob.cs";

    /// <summary>
    /// org.nr-surfacing <c>*Dto</c>s that MASK + FLAG a personnummer-shaped org.nr at the boundary
    /// (structurally: a nullable <c>string?</c> org.nr + a <c>bool</c> protection flag; the handler
    /// routes through <c>OrganizationNumber.IsPersonnummerShaped</c>). Add a DTO here only if it
    /// genuinely nulls the raw value + flags it.
    /// </summary>
    private static readonly HashSet<Type> MaskingOrgNrDtos =
    [
        typeof(CompanyWatchDto), // PR-3: OrganizationNumber masked to null + IsProtectedIdentity flag
        typeof(EmployerDisambiguationDto), // PR-2b C2: OrganizationNumber masked to null + IsProtectedIdentity flag
    ];

    /// <summary>
    /// org.nr-surfacing <c>*Dto</c>s that are EXEMPT from masking, each with an explicit reason. Empty
    /// today — a DTO belongs here ONLY if surfacing a raw org.nr is provably safe (never for a value
    /// that can be a sole-prop personnummer).
    /// </summary>
    private static readonly HashSet<Type> ExemptOrgNrDtos = [];

    [Fact]
    public void Every_org_nr_surfacing_dto_is_classified_masking_or_exempt()
    {
        // FAIL-CLOSED partition. Every *Dto exposing an org.nr-shaped member must be consciously
        // classified — a new one of any shape fails here before it can surface a raw personnummer.
        var orgNrDtos = OrgNrSurfaceScan.OrgNrSurfacingDtos(typeof(CompanyWatchDto).Assembly);
        var classified = MaskingOrgNrDtos.Concat(ExemptOrgNrDtos);

        var unclassified = OrgNrSurfaceScan.FindUnclassifiedOrgNrDtos(orgNrDtos, classified);

        unclassified.ShouldBeEmpty(
            "Följande org.nr-surfande *Dto(s) är OKLASSIFICERADE. Per fail-safe default (ADR 0087 " +
            "D8, CLAUDE.md §5 — en enskild-firma org.nr kan vara ett personnummer) måste varje DTO " +
            "som exponerar ett org.nr klassificeras MEDVETET: maskerar+flaggar den ett " +
            "personnummer-format org.nr (nullbar string? + bool-flagga, router:ad via " +
            "IsPersonnummerShaped) → lägg i MaskingOrgNrDtos; annars → ExemptOrgNrDtos med en rad om " +
            "varför surfning av rått org.nr är bevisligen säkert. Oklassificerade: " +
            string.Join(", ", unclassified.Select(t => t.Name)));
    }

    [Fact]
    public void Every_masking_dto_is_structurally_mask_capable()
    {
        // The classification cannot lie: a DTO listed as masking MUST structurally be able to mask
        // (a nullable string org.nr it can null) AND flag (a bool protection flag).
        foreach (var dto in MaskingOrgNrDtos)
        {
            OrgNrSurfaceScan.HasNullableStringOrgNr(dto).ShouldBeTrue(
                $"{dto.Name} är listad i MaskingOrgNrDtos men saknar en nullbar string?-org.nr-" +
                "property att maskera till null (kan då inte maska ett personnummer-format värde).");
            OrgNrSurfaceScan.HasBoolProtectionFlag(dto).ShouldBeTrue(
                $"{dto.Name} är listad i MaskingOrgNrDtos men saknar en bool-skyddsflagga " +
                "(IsProtectedIdentity-liknande) som signalerar att värdet maskerats.");
        }
    }

    [Fact]
    public void Partition_helper_flags_an_unclassified_org_nr_dto()
    {
        // Self-proving negative #1: a synthetic *Dto with a RAW (non-masked) org.nr member, in NEITHER
        // list, must be reported — regardless of shape. Lives in the TEST assembly, so it never
        // pollutes the real Application-assembly reflection.
        Type[] dtos = [typeof(CompanyWatchDto), typeof(UnmaskedSampleDto)];
        Type[] classified = [typeof(CompanyWatchDto)];

        OrgNrSurfaceScan.FindUnclassifiedOrgNrDtos(dtos, classified)
            .ShouldBe([typeof(UnmaskedSampleDto)],
                "partition-helpern ska rapportera en org.nr-surfande DTO som varken maskerar eller " +
                "är exempt — oavsett form (fail-closed default).");
    }

    [Fact]
    public void CompanyWatchScanJob_logging_surface_never_references_org_nr()
    {
        // The one present real subject: the scan reads raw org.nr, so its logging surface must be
        // clean (ADR 0087 D8 / §5 — never log an org.nr un-flagged).
        var source = ReadScanJobSource();

        var offending = OrgNrSurfaceScan.FindOrgNrLoggingFragments(source);

        offending.ShouldBeEmpty(
            "CompanyWatchScanJob loggar (eller kan logga) ett rått org.nr — dess [LoggerMessage]-" +
            "mallar / Log*-anrop får ALDRIG referera ett org.nr-token (ADR 0087 D8, CLAUDE.md §5, " +
            "enskild firma = personnummer). Träffar: " + string.Join(" | ", offending));
    }

    [Fact]
    public void Log_scan_flags_an_org_nr_logging_fragment()
    {
        // Self-proving negative #2: a synthetic LoggerMessage template + a Log call that reference an
        // org.nr token must both be reported (proves the log guard is non-vacuous).
        const string synthetic = """
            [LoggerMessage(Level = LogLevel.Information, Message = "scanned org {OrganizationNumber}")]
            private static partial void LogLeak(ILogger logger, string organizationNumber);
            // usage:
            LogLeak(logger, watch.OrganizationNumber.Value);
            """;

        var offending = OrgNrSurfaceScan.FindOrgNrLoggingFragments(synthetic);

        offending.ShouldNotBeEmpty(
            "log-scannen ska flagga en LoggerMessage-mall / Log-anrop som refererar ett org.nr-token.");
    }

    [Fact]
    public void Scan_job_source_path_exists()
    {
        // Pins the source path — a moved/renamed scan job fails loud here instead of making the log
        // guard silently vacuous.
        File.Exists(ScanJobAbsolutePath()).ShouldBeTrue(
            $"arch-testet pekar på en fil som inte finns: {ScanJobAbsolutePath()}. Uppdatera " +
            "sökvägen om CompanyWatchScanJob flyttats/döpts om.");
    }

    private static string ReadScanJobSource() => File.ReadAllText(ScanJobAbsolutePath());

    private static string ScanJobAbsolutePath()
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, ScanJobRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — arch-testet behöver " +
            "källträdet för källtext-scan");
        return dir!.FullName;
    }

    // A synthetic org.nr-surfacing DTO that does NOT mask (raw non-nullable org.nr, no flag) — the
    // "someone added a DTO that leaks org.nr" case. In the TEST assembly, so it never reaches the
    // real Application-assembly reflection.
    private sealed record UnmaskedSampleDto(Guid Id, string OrganizationNumber);
}

/// <summary>
/// Pure detection helpers for the org.nr surfacing/log guard (#311 PR-4). Side-effect-free so each is
/// independently testable — see the self-proving negatives in
/// <see cref="OrganizationNumberSurfacingGuardTests"/>. Mirrors the <c>HardDeleteCascadeScan</c>
/// idiom.
/// </summary>
internal static class OrgNrSurfaceScan
{
    // Case-insensitive tokens that betray an org.nr / personnummer in a logging fragment.
    private static readonly string[] OrgNrTokens =
        ["organization", "orgnr", "org_nr", "personnummer"];

    /// <summary>
    /// Every concrete <c>*Dto</c> in <paramref name="applicationAssembly"/> that exposes an
    /// org.nr-shaped member (a property named containing <c>OrganizationNumber</c>, or typed
    /// <c>OrganizationNumber</c>).
    /// </summary>
    internal static IReadOnlyList<Type> OrgNrSurfacingDtos(Assembly applicationAssembly) =>
        applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Where(HasOrgNrMember)
            .ToList();

    /// <summary>Fail-closed partition (parity <c>HardDeleteCascadeScan.FindUnclassified</c>).</summary>
    internal static IReadOnlyList<Type> FindUnclassifiedOrgNrDtos(
        IEnumerable<Type> orgNrDtos, IEnumerable<Type> classified)
    {
        var known = classified.ToHashSet();
        return orgNrDtos.Where(t => !known.Contains(t)).ToList();
    }

    internal static bool HasOrgNrMember(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(IsOrgNrProperty);

    private static bool IsOrgNrProperty(PropertyInfo p)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        return p.Name.Contains("OrganizationNumber", StringComparison.Ordinal)
               || string.Equals(t.Name, "OrganizationNumber", StringComparison.Ordinal);
    }

    /// <summary>True when the DTO has a NULLABLE <c>string?</c> org.nr property (it CAN mask to null).</summary>
    internal static bool HasNullableStringOrgNr(Type type)
    {
        var nullabilityContext = new NullabilityInfoContext();
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p =>
            p.Name.Contains("OrganizationNumber", StringComparison.Ordinal)
            && p.PropertyType == typeof(string)
            && nullabilityContext.Create(p).ReadState == NullabilityState.Nullable);
    }

    /// <summary>True when the DTO has a <c>bool</c> protection flag (IsProtectedIdentity-style).</summary>
    internal static bool HasBoolProtectionFlag(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p =>
            p.PropertyType == typeof(bool)
            && (p.Name.Contains("Protected", StringComparison.Ordinal)
                || p.Name.Contains("Masked", StringComparison.Ordinal)));

    /// <summary>
    /// Returns the logging fragments in <paramref name="source"/> (<c>[LoggerMessage]</c> attribute
    /// blocks + <c>Log&lt;Word&gt;(...)</c> call/declaration sites) that reference an org.nr token. A
    /// raw org.nr must never reach a log (ADR 0087 D8 / §5) — an org.nr placeholder in a template or a
    /// org.nr-named argument in a Log call is reported. Non-logging references (e.g. the scan's
    /// <c>EF.Property(..., "OrganizationNumber")</c> query, or doc comments) are NOT fragments and are
    /// ignored.
    /// </summary>
    internal static IReadOnlyList<string> FindOrgNrLoggingFragments(string source)
    {
        var fragments = new List<string>();

        // [LoggerMessage( ... )] attribute blocks (contain the Message = "..." template).
        foreach (Match m in Regex.Matches(source, @"\[LoggerMessage\b[^\]]*\]"))
            fragments.Add(m.Value);

        // Log<Word>( ... ) call sites AND generated partial-method declarations (arg/param list up to
        // the first ')' — a logging call/declaration never crosses a ';').
        foreach (Match m in Regex.Matches(source, @"\bLog[A-Z]\w*\([^;)]*\)"))
            fragments.Add(m.Value);

        return fragments
            .Where(f => OrgNrTokens.Any(tok => f.Contains(tok, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
