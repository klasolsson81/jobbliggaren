using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
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
///   <item><b>Log-boundary (source-scan over every path that reads a raw org.nr into scope).</b>
///     A source file that reads a raw org.nr server-side must never log it — its logging surface
///     (<c>[LoggerMessage]</c> templates + <c>Log*</c> call sites) must never reference an org.nr
///     token (ADR 0087 D8 / §5). The scanned set (<see cref="RawOrgNrReadingSourcePaths"/>):
///     <c>CompanyWatchScanJob</c> reads raw org.nr to build its <c>IN</c>-set (PR-4), and
///     <c>ListCompanyWatchesQueryHandler</c> reads raw org.nr to resolve <c>company_name</c> (PR-3)
///     AND to compute the #447 active-ad count (the "second read path" the PR-3 forward-note
///     anticipated). Extend the set whenever a new path reads a raw org.nr into scope.</item>
/// </list>
/// </para>
///
/// <para>Both invariants are proven non-vacuous by self-proving negatives (a synthetic org.nr DTO the
/// partition must flag; a synthetic log fragment the scan must flag), mirroring the cascade fitness.</para>
/// </summary>
public class OrganizationNumberSurfacingGuardTests
{
    /// <summary>
    /// Source files that read a raw org.nr into scope (server-side) and therefore must never log one.
    /// The log-boundary invariant scans each. Add a path here whenever a new read path reads a raw
    /// org.nr into scope (ADR 0087 D8 / §5, enskild firma = personnummer).
    /// </summary>
    private static readonly IReadOnlyList<string> RawOrgNrReadingSourcePaths =
    [
        "src/Jobbliggaren.Application/CompanyWatches/Jobs/CompanyWatchScan/CompanyWatchScanJob.cs",
        "src/Jobbliggaren.Application/CompanyWatches/Queries/ListCompanyWatches/ListCompanyWatchesQueryHandler.cs",
        // #444 (ADR 0087 D2 / ADR 0090 D1) — the employer application-history projection reads the
        // raw org.nr from the job_ads shadow column server-side to GROUP BY (masked + flagged before
        // it leaves the handler; never logged).
        "src/Jobbliggaren.Application/Applications/Queries/GetEmployerApplicationHistory/GetEmployerApplicationHistoryQueryHandler.cs",
        // #446 (ADR 0087 D8 / ADR 0090 D1) — the /jobb card count overlay reads the page ads' raw
        // org.nr (via IJobAdEmployerReader) + the caller's applications' org.nr server-side, purely as
        // the in-memory GROUP key. It is NEVER surfaced (the DTO is Guid -> int, no org.nr member) nor
        // logged — this scan pins that.
        "src/Jobbliggaren.Application/Applications/Queries/GetEmployerApplicationCountBatch/GetEmployerApplicationCountBatchQueryHandler.cs",
        // #454 (ADR 0088) — the lookup handler reads the raw org.nr (VO + registry entry) into
        // scope; the cache decorator + providers see the raw value inside Infrastructure.
        "src/Jobbliggaren.Application/Companies/Queries/LookupCompany/LookupCompanyQueryHandler.cs",
        "src/Jobbliggaren.Infrastructure/CompanyRegistry/CachedCompanyRegistry.cs",
        "src/Jobbliggaren.Infrastructure/CompanyRegistry/FakeCompanyRegistry.cs",
        // #560 kriterie-vågen PR-2 (DPIA C-D5, counts-only logging) — the criteria browse read-path
        // reads every matched company's raw org.nr into scope: the port materialises it from
        // company_register, and the handler masks + flags it before it reaches CompanyBrowseDto. This
        // scan is what makes "the browse never logs an org.nr" a build gate rather than a discipline.
        "src/Jobbliggaren.Application/CompanyWatches/Queries/BrowseCompanies/BrowseCompaniesQueryHandler.cs",
        "src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyWatchBrowseQuery.cs",
    ];

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
        // #444 (ADR 0090 D1 M1): the employer application-history row nulls a personnummer-shaped
        // org.nr + flags it via IsProtectedIdentity (IsPersonnummerShaped).
        typeof(EmployerApplicationHistoryDto),
        // #454 (ADR 0088 D4/D5): mask-capable defense-in-depth — the handler REFUSES pnr-shaped
        // input upstream (Posture A), so the masked branch is normally unreachable, but the DTO
        // still nulls+flags via IsPersonnummerShaped so no future path can surface a raw value.
        typeof(Jobbliggaren.Application.Companies.Queries.LookupCompany.CompanyLookupDto),
        // #560 kriterie-vågen PR-2: same defense-in-depth posture as CompanyLookupDto. ADR 0091 keeps
        // sole traders OUT of company_register at ingest (SCB Juridisk form filter + an
        // IsPersonnummerShaped guard), so the masked branch should be unreachable — but that is an
        // ingest-time invariant in a DIFFERENT subsystem, and a personnummer exposure must not rest on
        // it staying correct. The DTO nulls + flags; the handler routes every row through
        // IsPersonnummerShaped.
        typeof(Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies.CompanyBrowseDto),
    ];

    /// <summary>
    /// org.nr-surfacing <c>*Dto</c>s that are EXEMPT from masking, each with an explicit reason. Empty
    /// today — a DTO belongs here ONLY if surfacing a raw org.nr is provably safe (never for a value
    /// that can be a sole-prop personnummer).
    /// </summary>
    private static readonly HashSet<Type> ExemptOrgNrDtos = [];

    /// <summary>
    /// INBOUND request types whose org.nr is CLIENT-SUPPLIED input, not something we surface. Masking
    /// is meaningless on an input (the caller already knows the value they sent); what protects these
    /// is the handler's refuse-posture and the log-boundary scan. They enter the org.nr-surfacing set
    /// only because the name-independent scan (correctly) cannot tell a request from a response.
    /// </summary>
    private static readonly HashSet<Type> InboundOrgNrRequests =
    [
        typeof(Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany.FollowCompanyCommand),
        typeof(Jobbliggaren.Application.Companies.Queries.LookupCompany.LookupCompanyQuery),
    ];

    /// <summary>
    /// RAW carriers: types that hold an UN-masked org.nr strictly INSIDE the Application boundary, to
    /// be masked by a handler before it reaches a DTO. They must never themselves be a serialized
    /// response — that is not a convention here, it is
    /// <see cref="No_raw_org_nr_carrier_is_reachable_from_a_mediator_response"/>, which walks every
    /// Mediator response type's transitive graph and fails the build if one of these appears in it.
    /// Without that test this bucket would be a promise rather than a partition.
    ///
    /// <para>
    /// Added when the <c>*Dto</c> name filter was removed (security-auditor Major, #560 PR-2): the old
    /// scan saw only types NAMED <c>*Dto</c>, so every one of these raw carriers was invisible to the
    /// fail-closed partition it was supposed to be inside.
    /// </para>
    /// </summary>
    private static readonly HashSet<Type> RawOrgNrCarriers =
    [
        // The employer-disambiguation port's row type — the handler masks it into
        // EmployerDisambiguationDto (which IS classified as masking).
        typeof(Jobbliggaren.Application.JobAds.Abstractions.EmployerAdGroup),
        // The SCB population channel's ACL record (Worker-side ingest; never a response).
        typeof(Jobbliggaren.Application.CompanyRegister.Abstractions.ScbCompanyRecord),
        // ICompanyRegistry's lookup hit — the handler masks it into CompanyLookupDto.
        typeof(Jobbliggaren.Application.Companies.Abstractions.CompanyRegistryEntry),
        // #560 PR-2: the browse port's row type — the handler masks it into CompanyBrowseDto.
        typeof(Jobbliggaren.Application.CompanyWatches.Abstractions.CompanyBrowseResult),
    ];

    [Fact]
    public void Every_org_nr_surfacing_dto_is_classified_masking_or_exempt()
    {
        // FAIL-CLOSED partition. Every *Dto exposing an org.nr-shaped member must be consciously
        // classified — a new one of any shape fails here before it can surface a raw personnummer.
        var orgNrTypes = OrgNrSurfaceScan.OrgNrSurfacingTypes(typeof(CompanyWatchDto).Assembly);
        var classified = MaskingOrgNrDtos
            .Concat(ExemptOrgNrDtos)
            .Concat(InboundOrgNrRequests)
            .Concat(RawOrgNrCarriers);

        var unclassified = OrgNrSurfaceScan.FindUnclassifiedOrgNrDtos(orgNrTypes, classified);

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
    public void No_raw_org_nr_carrier_is_reachable_from_a_mediator_response()
    {
        // THE test that makes RawOrgNrCarriers a partition rather than a promise (security-auditor +
        // code-reviewer Major, 2026-07-13). Listing a type as "internal carrier, never serialized" is
        // worth nothing unless something ENFORCES the "never serialized" half. This walks the transitive
        // type graph of EVERY Mediator response in the Application assembly and fails the build if a raw
        // carrier appears in one.
        //
        // The concrete accident it prevents: #560 PR-3 builds an endpoint over ICompanyWatchBrowseQuery,
        // whose port returns PagedResult<CompanyBrowseResult> — the RAW row. Returning the port's shape
        // straight out of the handler instead of the masked PagedResult<CompanyBrowseDto> compiles,
        // passes every other test, and puts a raw (possibly personnummer-shaped) org.nr on the wire.
        // Only the handler's Mask() stands between — and discipline is not a guard.
        var applicationAsm = typeof(CompanyWatchDto).Assembly;

        var offenders = OrgNrSurfaceScan.FindRawCarriersInResponses(applicationAsm, RawOrgNrCarriers);

        offenders.ShouldBeEmpty(
            "Följande Mediator-svar exponerar (transitivt) en RÅ org.nr-bärare. En rå bärare får bara " +
            "leva INUTI Application-gränsen — handlern måste maska den till ett *Dto (nullbar string? " +
            "+ bool-flagga) innan den blir ett svar. Ett rått org.nr på tråden kan vara ett " +
            "personnummer (enskild firma, ADR 0087 D8 / §5). Överträdelser: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Response_scan_flags_a_raw_carrier_in_a_response()
    {
        // Self-proving negative #3: the walker must actually find a carrier nested inside a generic
        // response, not just a top-level one — otherwise the guard above is vacuous for exactly the
        // shape it exists to catch (PagedResult<CompanyBrowseResult>).
        var reachable = OrgNrSurfaceScan.ReachableTypes(
            typeof(Jobbliggaren.Application.Common.PagedResult<
                Jobbliggaren.Application.CompanyWatches.Abstractions.CompanyBrowseResult>));

        reachable.ShouldContain(
            typeof(Jobbliggaren.Application.CompanyWatches.Abstractions.CompanyBrowseResult),
            "type-walkern måste hitta en rå bärare som ligger NÄSTLAD i ett generiskt svar " +
            "(PagedResult<T>) — annars är svars-scannen vakuös för precis den form den finns för.");

        // ...and the masked shape the handler actually returns must NOT contain one.
        OrgNrSurfaceScan.ReachableTypes(
                typeof(Jobbliggaren.Application.Common.PagedResult<
                    Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies.CompanyBrowseDto>))
            .ShouldNotContain(
                typeof(Jobbliggaren.Application.CompanyWatches.Abstractions.CompanyBrowseResult));
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
    public void Watch_filter_dto_never_surfaces_an_org_nr()
    {
        // Bevakning F4b (#803) — the per-watch filter DTO rides ALONG the org.nr-bearing CompanyWatchDto
        // but is itself a taxonomy-reference carrier (concept-ids + a bool). It therefore needs NO
        // classification above: it never enters the org.nr-surfacing set at all.
        //
        // The partition test would flag an org.nr member on it as unclassified — but the fix a hurried
        // developer would reach for is to add the type to one of the two lists. This pins the STRONGER
        // rule: the filter DTO may never carry an org.nr member in the first place, so there is no
        // list-entry that can make it legal. (D8 / §5 — an enskild-firma org.nr can be a personnummer,
        // and a notification filter has no business holding one.)
        OrgNrSurfaceScan.HasOrgNrMember(typeof(WatchFilterDto)).ShouldBeFalse(
            "WatchFilterDto exponerar ett org.nr-format medlem. Filtret bär taxonomi-referenser " +
            "(concept-id) — aldrig en identitet. Ta bort medlemmen; klassificera den INTE i " +
            "MaskingOrgNrDtos/ExemptOrgNrDtos.");
    }

    [Fact]
    public void Raw_org_nr_reading_sources_never_log_an_org_nr()
    {
        // Every source that reads a raw org.nr into scope must keep its logging surface clean (ADR
        // 0087 D8 / §5 — never log an org.nr un-flagged). CompanyWatchScanJob (IN-set, PR-4) +
        // ListCompanyWatchesQueryHandler (company_name projection PR-3 + #447 active-ad count).
        foreach (var relativePath in RawOrgNrReadingSourcePaths)
        {
            var source = ReadSource(relativePath);

            var offending = OrgNrSurfaceScan.FindOrgNrLoggingFragments(source);

            offending.ShouldBeEmpty(
                $"{relativePath} loggar (eller kan logga) ett rått org.nr — dess [LoggerMessage]-" +
                "mallar / Log*-anrop får ALDRIG referera ett org.nr-token (ADR 0087 D8, CLAUDE.md §5, " +
                "enskild firma = personnummer). Träffar: " + string.Join(" | ", offending));
        }
    }

    /// <summary>
    /// The #560 browse read-path files, and the ONE logging call they are allowed to make.
    /// <c>LogCrossUserAttempt</c> is the ADR 0031 failed-access signal; it carries only pseudonymous
    /// GUIDs (criterion id + the REQUESTING user's id) — no org.nr, no company name, no criterion
    /// content.
    /// </summary>
    private static readonly IReadOnlyList<string> CountsOnlyLoggingSourcePaths =
    [
        "src/Jobbliggaren.Application/CompanyWatches/Queries/BrowseCompanies/BrowseCompaniesQueryHandler.cs",
        "src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyWatchBrowseQuery.cs",
    ];

    private static readonly string[] AllowedBrowseLogCalls = ["LogCrossUserAttempt"];

    [Fact]
    public void Browse_read_path_has_no_logging_surface_at_all()
    {
        // DPIA C-D5 (counts-only) — fail-CLOSED (security-auditor Major, 2026-07-13).
        //
        // The org.nr token scan below is a BLOCKLIST, and a blocklist is fail-open by construction:
        // its token list is org.nr-only, so `LogInformation("matched {CompanyName}", row.Name)` passes
        // it GREEN — while C-D5 covers the company name and the criterion's SNI/kommun content too. Its
        // regex also truncates at the first ')', so a nested call in the argument list hides everything
        // after it. Relying on it alone would make "counts-only is mechanically discharged" a claim
        // stronger than the code — which is the exact vacuity this PR exists to fight.
        //
        // The browse read-path logs NOTHING today. Asserting exactly that is fail-closed and covers all
        // three data classes at once: any new log call in these two files fails the build, whatever it
        // carries.
        foreach (var relativePath in CountsOnlyLoggingSourcePaths)
        {
            var source = ReadSource(relativePath);

            var surface = OrgNrSurfaceScan.FindLoggingSurface(source, AllowedBrowseLogCalls);

            surface.ShouldBeEmpty(
                $"{relativePath} har fått en logg-yta. Browse-läsvägen är COUNTS-ONLY (DPIA C-D5): den " +
                "får inte logga org.nr, företagsnamn ELLER kriterie-innehåll (SNI/kommun). Regeln är " +
                "fail-closed — ingen loggning alls, inte 'ingen loggning av vissa tokens' (en " +
                "token-blocklist är per konstruktion fail-open). Enda tillåtna anropet är " +
                $"{string.Join("/", AllowedBrowseLogCalls)} (ADR 0031, bär bara pseudonyma GUID:n). " +
                "Hittat: " + string.Join(", ", surface));
        }
    }

    [Fact]
    public void Logging_surface_scan_flags_a_new_log_call()
    {
        // Self-proving negative #4: the fail-closed scan must flag a log call that the org.nr TOKEN scan
        // would wave through — that difference IS the finding it was written for.
        const string synthetic = """
            logger.LogInformation("browse matched {CompanyName} in {Kommun}", row.Name, row.SeatMunicipalityCode);
            """;

        OrgNrSurfaceScan.FindLoggingSurface(synthetic, AllowedBrowseLogCalls).ShouldNotBeEmpty(
            "logg-yte-scannen ska flagga ett NYTT Log*-anrop även när det inte bär ett org.nr-token — " +
            "företagsnamn och kriterie-innehåll är också C-D5-skyddat.");

        // ...and it must NOT flag the one allowed call, or the guard would be unusable.
        OrgNrSurfaceScan.FindLoggingSurface(
                "failedAccessLogger.LogCrossUserAttempt(\"X\", id, userId, \"Op\");", AllowedBrowseLogCalls)
            .ShouldBeEmpty("det whitelistade ADR 0031-anropet får inte flaggas.");
    }

    [Fact]
    public void Counts_only_logging_source_paths_all_exist()
    {
        foreach (var relativePath in CountsOnlyLoggingSourcePaths)
        {
            File.Exists(SourceAbsolutePath(relativePath)).ShouldBeTrue(
                $"arch-testet pekar på en fil som inte finns: {relativePath}. En flyttad/omdöpt fil gör " +
                "counts-only-guarden tyst vakuös.");
        }
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
    public void Raw_org_nr_reading_source_paths_all_exist()
    {
        // Pins every scanned source path — a moved/renamed source fails loud here instead of making
        // the log guard silently vacuous.
        foreach (var relativePath in RawOrgNrReadingSourcePaths)
        {
            var absolute = SourceAbsolutePath(relativePath);
            File.Exists(absolute).ShouldBeTrue(
                $"arch-testet pekar på en fil som inte finns: {absolute}. Uppdatera sökvägen i " +
                "RawOrgNrReadingSourcePaths om källfilen flyttats/döpts om.");
        }
    }

    private static string ReadSource(string relativePath) => File.ReadAllText(SourceAbsolutePath(relativePath));

    private static string SourceAbsolutePath(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
    /// Every concrete PUBLIC type in <paramref name="applicationAssembly"/> that exposes an
    /// org.nr-shaped member (a property named containing <c>OrganizationNumber</c>, or typed
    /// <c>OrganizationNumber</c>).
    ///
    /// <para>
    /// <b>Deliberately NOT filtered on the <c>*Dto</c> name suffix</b> (security-auditor Major,
    /// 2026-07-13, #560 PR-2). The name filter was FAIL-OPEN: a public Application record carrying a
    /// raw org.nr that simply is not called <c>*Dto</c> — <c>CompanyBrowseResult</c>, the browse
    /// port's row type, is exactly one — was invisible to the partition. It could then be returned
    /// straight out of a future endpoint, serialising a raw (possibly personnummer-shaped) org.nr with
    /// every architecture test green. That is the same fail-open class this codebase re-architected
    /// <c>ScbCompanyRegisterLayerTests</c> away from, and the same class that shipped #805-3 and #842:
    /// a guard whose coverage does not reach the path it claims to protect.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<Type> OrgNrSurfacingTypes(Assembly applicationAssembly) =>
        applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(HasOrgNrMember)
            .ToList();

    /// <summary>Fail-closed partition (parity <c>HardDeleteCascadeScan.FindUnclassified</c>).</summary>
    internal static IReadOnlyList<Type> FindUnclassifiedOrgNrDtos(
        IEnumerable<Type> orgNrTypes, IEnumerable<Type> classified)
    {
        var known = classified.ToHashSet();
        return orgNrTypes.Where(t => !known.Contains(t)).ToList();
    }

    /// <summary>
    /// Every Mediator response type in <paramref name="applicationAssembly"/> whose transitive type
    /// graph contains one of <paramref name="rawCarriers"/>. Returns "QueryName -> CarrierName" strings.
    /// </summary>
    internal static IReadOnlyList<string> FindRawCarriersInResponses(
        Assembly applicationAssembly, IReadOnlyCollection<Type> rawCarriers)
    {
        var carriers = rawCarriers.ToHashSet();
        var offenders = new List<string>();

        foreach (var request in applicationAssembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } || (t.IsValueType && !t.IsEnum)))
        {
            foreach (var iface in request.GetInterfaces().Where(IsMediatorRequest))
            {
                var response = iface.GetGenericArguments()[0];
                foreach (var reached in ReachableTypes(response).Where(carriers.Contains))
                    offenders.Add($"{request.Name} -> {reached.Name}");
            }
        }

        return offenders.Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList();
    }

    private static bool IsMediatorRequest(Type iface) =>
        iface.IsGenericType
        && (iface.GetGenericTypeDefinition() == typeof(Mediator.IQuery<>)
            || iface.GetGenericTypeDefinition() == typeof(Mediator.ICommand<>));

    /// <summary>
    /// The transitive type closure of <paramref name="root"/>: generic arguments, array elements, and
    /// the property types of any type we own (Application/Domain). BCL types are terminal — we do not
    /// walk into <c>string</c> or <c>int</c>, and stopping there is what keeps the walk finite.
    /// </summary>
    internal static IReadOnlyList<Type> ReachableTypes(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (type is null || !seen.Add(type))
                continue;

            if (type.IsGenericType)
            {
                foreach (var arg in type.GetGenericArguments())
                    pending.Push(arg);
            }

            if (type.IsArray && type.GetElementType() is { } element)
                pending.Push(element);

            // Only descend into types we author — otherwise the walk wanders through the BCL forever.
            if (!IsOwnedType(type))
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                pending.Push(property.PropertyType);
        }

        return [.. seen];
    }

    private static bool IsOwnedType(Type type) =>
        type.Namespace?.StartsWith("Jobbliggaren.", StringComparison.Ordinal) ?? false;

    /// <summary>
    /// Every <c>Log*(</c> call site in <paramref name="source"/> whose method name is not in
    /// <paramref name="allowed"/>, plus any <c>ILogger</c> declaration. Used to assert a source file has
    /// NO logging surface at all.
    ///
    /// <para>
    /// <b>Why "no logging surface" and not "no org.nr token" for the browse read-path</b>
    /// (security-auditor Major, 2026-07-13): the token scan is a BLOCKLIST, and a blocklist is
    /// fail-open by construction. Its token list is org.nr-only, so a
    /// <c>LogInformation("matched {CompanyName}", row.Name)</c> would pass it green — while DPIA C-D5
    /// says COUNTS ONLY, which covers the company name and the criterion's SNI/kommun content too. Its
    /// regex also truncates at the first <c>)</c>, so a nested call in the argument list hides
    /// everything after it. The browse path logs nothing today; asserting exactly that is fail-CLOSED
    /// and covers all three data classes at once.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> FindLoggingSurface(string source, IReadOnlyCollection<string> allowed)
    {
        var found = new List<string>();

        foreach (Match m in Regex.Matches(source, @"\bILogger\b"))
            found.Add(m.Value);

        foreach (Match m in Regex.Matches(source, @"\b(Log[A-Z]\w*)\s*\("))
        {
            var method = m.Groups[1].Value;
            if (!allowed.Contains(method))
                found.Add(method);
        }

        return found.Distinct(StringComparer.Ordinal).ToList();
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
