using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;
using Jobbliggaren.Application.JobAds.Queries.GetJobAd;
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
///     reason). The scan is NAME-, SHAPE- and VISIBILITY-independent (class or record struct, public
///     or internal, nested or not, property or field), so a new org.nr-surfacing type lands in
///     neither and fails the build until a human classifies it (Saltzer &amp; Schroeder fail-safe
///     default; parity
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
        // #311 PR-5 (ADR 0087 D4 / #544 gap-closure) — the per-ad follow-state overlay reads each page
        // ad's raw org.nr (via IJobAdEmployerReader) into scope to correlate it against the user's
        // watches, and now tokenises it (the enskild token-probe channel). It is NEVER surfaced (the DTO
        // is Guid? + bool, no org.nr member) nor logged; this scan makes that a build gate. It was a
        // latent gap before PR-5 — the handler already read a raw org.nr but was not on this list.
        "src/Jobbliggaren.Application/CompanyWatches/Queries/GetCompanyWatchStatusBatch/GetCompanyWatchStatusBatchQueryHandler.cs",
        // #544 (ADR 0090 D5) — the personnummer-token tokeniser reads a raw org.nr into scope: it
        // HMACs the verbatim plaintext value. It has no logging surface at all, so this scan proves it
        // never grows one.
        "src/Jobbliggaren.Infrastructure/Security/HmacProtectedIdentityTokenizer.cs",
        // #544 follow-up (security-gated, 2026-07-18) — the KLAS-gated backfill ALSO reads a raw org.nr
        // into scope (it HMACs the plaintext value in-process). It was held out of this list until
        // FindOrgNrLoggingFragments was refined: the earlier block-wide substring scan false-positived on
        // the job's OWN counts-only surface — its [LoggerMessage] Message prose carries the class-name
        // prefix "…OrgNrToken" and the words "org.nr/personnummer" (the "aldrig ett org.nr/personnummer i
        // loggen" reassurance), none of which is a {placeholder} or a logged argument. The scan now flags
        // an org.nr token ONLY inside a {placeholder} of the Message template OR inside the ARGUMENT
        // list of a Log*() call / generated [LoggerMessage]-partial declaration (the value carriers) —
        // so this job's counts-only surface passes
        // cleanly while a real {OrganizationNumber} placeholder or an org.nr-named argument is still
        // caught (self-proving negatives Log_scan_flags_an_org_nr_logging_fragment +
        // Log_scan_flags_an_org_nr_argument_under_a_token_free_template fire; the counts-only prose case
        // Log_scan_does_not_flag_org_nr_tokens_in_prose_or_names does not).
        "src/Jobbliggaren.Application/CompanyWatches/Jobs/BackfillCompanyWatchOrgNrToken/BackfillCompanyWatchOrgNrTokenJob.cs",
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
        // #841 — THE INGEST FUNNEL, and it is a genuinely NEW read path: before 2026-07-13 no C# code
        // ever held an inbound org.nr, because Postgres derived organization_number straight out of the
        // raw_payload JSON blob. The ACL now parses it (PlatsbankenJobSource.MapFacets reads
        // hit.Employer?.OrganizationNumber), and the upsert handler carries it transitively on
        // JobAdImportItem.Facets. Both were missing from this list on the first pass — which would have
        // left the org.nr entering the system through a file this scan does not read. Found by
        // security-auditor.
        "src/Jobbliggaren.Infrastructure/JobSources/Platsbanken/PlatsbankenJobSource.cs",
        "src/Jobbliggaren.Application/JobAds/Commands/UpsertExternalJobAd/UpsertExternalJobAdCommandHandler.cs",
        // #560 kriterie-vågen PR-2 (DPIA C-D5, counts-only logging) — the criteria browse read-path
        // reads every matched company's raw org.nr into scope: the port materialises it from
        // company_register, and the handler masks + flags it before it reaches CompanyBrowseDto. This
        // scan is what makes "the browse never logs an org.nr" a build gate rather than a discipline.
        "src/Jobbliggaren.Application/CompanyWatches/Queries/BrowseCompanies/BrowseCompaniesQueryHandler.cs",
        "src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyWatchBrowseQuery.cs",
        // #883 F8 (security-auditor follow-up) — the Art. 17 recruiter-erasure read-paths both hold a
        // raw org.nr in scope and were missing from this list: RecruiterErasureMatchQuery runs the
        // raw-SQL match (the normalised org.nr the requester submitted + the ads' org.nr) and
        // EraseRecruiterAdsCommandHandler derives the evidence from it. The org.nr is the requester's
        // own identifier — a possible sole-prop personnummer (ADR 0087 D8(c); §5) — and both surfaces
        // log counts/GUIDs only today; this scan makes "the erasure path never logs an org.nr" a build
        // gate rather than a discipline. It is also the compensating control the #883 CTO bind (D2)
        // leaned on to keep the OrganizationNumber VO's raw ToString() out of scope — so the list must
        // actually cover the VO's callers.
        "src/Jobbliggaren.Infrastructure/JobAds/RecruiterErasureMatchQuery.cs",
        "src/Jobbliggaren.Application/JobAds/Commands/EraseRecruiterAds/EraseRecruiterAdsCommandHandler.cs",
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
        // #842 round 6: the Art. 17 match port's recent-search row. MatchedEmployerOrgNr is the
        // NORMALISED org.nr the erasure REQUESTER herself submitted as her identifier, echoed with
        // the matched row so the operator can review a hard-delete ("a count cannot be reviewed").
        // It never reaches a Mediator response (the walker below enforces that): the response
        // carries only the evidence STRINGS the handler derives, and those are flagged through
        // OrganizationNumber.IsPersonnummerShaped before they surface (EmployerFilterEvidence /
        // OrgNrEvidence — "(personnummer-format)"). Flag-not-mask is deliberate there: the value is
        // the operator's own request input coming back, not a disclosure — masking would hide the
        // string from the person who typed it. This classification makes
        // IRecruiterErasureMatchQuery a derived carrier-producing port, so the Api project can
        // never inject it around the handler's evidence derivation.
        typeof(Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds.ErasureRecentSearchMatch),
        // #311 PR-5 (ADR 0087 D4): the curated brand-group's member org.nrs. Internal catalogue data —
        // the members are public legal-entity (AB) org.nrs (the loader rejects personnummer-shaped ones
        // fail-loud at host build), and they NEVER reach a Mediator response: the read handler resolves a
        // group's DISPLAY NAME + summed counts into CompanyWatchDto, never the member list (ADR 0087 D4
        // D5d). The walker below enforces that BrandGroup is unreachable from any response. A public
        // read-endpoint for groups (FE-PR) would surface only id + display name + counts, never members.
        typeof(Jobbliggaren.Application.CompanyWatches.Abstractions.BrandGroup),
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

        var offenders = OrgNrSurfaceScan.FindRawCarriersInResponses(
            applicationAsm, [.. MaskingOrgNrDtos, .. ExemptOrgNrDtos]);

        offenders.ShouldBeEmpty(
            "Följande Mediator-svar exponerar (transitivt) en RÅ org.nr-bärare. En rå bärare får bara " +
            "leva INUTI Application-gränsen — handlern måste maska den till ett *Dto (nullbar string? " +
            "+ bool-flagga) innan den blir ett svar. Ett rått org.nr på tråden kan vara ett " +
            "personnummer (enskild firma, ADR 0087 D8 / §5). Överträdelser: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Non-vacuity pin for the SHARED enumeration seam (CTO F4 (i)/(iii), 2026-07-17). Both
    /// response-graph guards — this file's org.nr carrier scan and the L4b domain-contact lock in
    /// <c>RecruiterContactFtsLockTests</c> — are absence assertions over
    /// <see cref="OrgNrSurfaceScan.MediatorResponses"/>. If that enumeration silently matched
    /// nothing (a broken predicate, an over-eager type filter), BOTH guards would pass vacuously.
    /// One known pair anchors them counterfactually: <c>GetJobAdQuery</c> is enumerated, and its
    /// response graph reaches <c>JobAdDetailDto</c> through the <c>Result&lt;&gt;</c> wrapper.
    /// </summary>
    [Fact]
    public void Mediator_response_enumeration_is_not_vacuous()
    {
        var known = OrgNrSurfaceScan.MediatorResponses(typeof(JobAdDetailDto).Assembly)
            .Where(p => p.Request == typeof(GetJobAdQuery))
            .ToList();

        known.ShouldNotBeEmpty(
            "MediatorResponses no longer enumerates GetJobAdQuery — the shared seam both "
            + "response-graph guards run on is broken, and every absence assertion built on it is "
            + "passing vacuously.");

        known.ShouldContain(
            p => OrgNrSurfaceScan.ReachableTypes(p.Response).Contains(typeof(JobAdDetailDto)),
            "GetJobAdQuery's response graph must reach JobAdDetailDto (through Result<>) — "
            + "otherwise the walker no longer pierces the wrapper both guards depend on.");

        // The ICommand<> arm needs its own anchor (test-writer Minor 1, CTO in-block 2026-07-17):
        // the two assertions above exercise only the IQuery<> arm, so a predicate edit dropping the
        // command arm would silently narrow BOTH guards to queries-only while this test stayed
        // green. Shape-based on purpose — any command anchors it, no name coupling.
        OrgNrSurfaceScan.MediatorResponses(typeof(JobAdDetailDto).Assembly)
            .ShouldContain(
                p => p.Request.GetInterfaces().Any(i => i.IsGenericType
                     && i.GetGenericTypeDefinition() == typeof(Mediator.ICommand<>)),
                "MediatorResponses no longer enumerates any ICommand<> request — the command arm "
                + "of the shared predicate is broken and command responses are un-guarded in BOTH "
                + "guards.");
    }

    [Fact]
    public void Api_project_names_neither_a_raw_org_nr_carrier_nor_a_port_that_produces_one()
    {
        // The response walker guards the MEDIATOR boundary. That is NOT the Api boundary, and the gap is
        // a live hole: ICompanyWatchBrowseQuery is public and DI-registered, so a minimal-API delegate
        // can inject the port DIRECTLY and return PagedResult<CompanyBrowseResult> with no Mediator
        // request in sight — walker blind, build green, raw org.nr on the wire. Nothing in the repo
        // forces Api -> Application through Mediator; it is a convention, and it is ALREADY broken
        // (ISessionStore is injected straight into three endpoints).
        //
        // Grepping for the CARRIER's name does not close it: `var page = await browse.BrowseAsync(...);
        // return Results.Ok(page);` never names CompanyBrowseResult. Grepping for the PORT's name does:
        // you cannot resolve a service from DI without writing its type, and there is no `var` for a
        // parameter declaration. That is the "cannot be done without naming it" property this guard
        // needs — and the first version of it claimed to have but did not (code-reviewer re-review #2).
        var applicationAsm = typeof(CompanyWatchDto).Assembly;
        var ports = OrgNrSurfaceScan.CarrierProducingPorts(applicationAsm, RawOrgNrCarriers);

        // Sanity: if this ever came back empty the guard would be vacuous by degenerating to "grep for
        // nothing" — the failure mode the whole PR is about.
        ports.ShouldNotBeEmpty(
            "inga bärar-producerande portar hittades — härledningen är trasig och grinden vakuös.");

        // Hand-written sources only. obj/ and bin/ hold generated files (AssemblyInfo, GlobalUsings, and
        // — the day the request-delegate generator is switched on — generated endpoint code that could
        // legitimately name an Application type). Scanning them makes the guard environment-dependent
        // and gives it a future false-positive vector, and a guard that goes red for the wrong reason is
        // a guard someone weakens (code-reviewer re-review #3). The direction is fail-closed either way;
        // this is about keeping the guard credible.
        var apiSources = Directory
            .EnumerateFiles(
                Path.Combine(FindRepoRoot(), "src", "Jobbliggaren.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var offenders = new List<string>();
        foreach (var file in apiSources)
        {
            foreach (var name in OrgNrSurfaceScan.FindForbiddenApiNames(
                         File.ReadAllText(file), RawOrgNrCarriers, ports))
            {
                offenders.Add($"{Path.GetFileName(file)}: {name}");
            }
        }

        offenders.ShouldBeEmpty(
            "Api-projektet namnger en RÅ org.nr-bärare eller en PORT som kan lämna ut en. En bärare får " +
            "bara leva INUTI Application-gränsen: endpointen måste gå via Mediator och returnera det " +
            "maskerade *Dto:t handlern producerar, aldrig portens råa radtyp. Att injicera porten direkt " +
            "i en endpoint kringgår maskeringen helt — och `Results.Ok(var)` namnger aldrig typen den " +
            "serialiserar, så det syns inte i en bärar-grep. Ett rått org.nr på tråden kan vara ett " +
            "personnummer (enskild firma, ADR 0087 D8 / §5). Överträdelser: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Api_scan_flags_a_port_injected_without_naming_the_carrier()
    {
        // Self-proving negative #5. THE one that matters: the synthetic source below is the exact shape
        // the previous (carrier-name-only) guard waved through — a port injected into a delegate, the
        // result bound with `var`, returned with Results.Ok(). CompanyBrowseResult appears NOWHERE in it.
        // If this ever goes green, the guard has regressed to the vacuous form.
        const string synthetic = """
            group.MapGet("/{id:guid}/foretag", async (
                Guid id, ICompanyWatchBrowseQuery browse, CancellationToken ct) =>
            {
                var page = await browse.BrowseAsync(new CompanyBrowseCriteria(spec, 1, 20), ct);
                return Results.Ok(page);
            });
            """;

        synthetic.Contains(
            nameof(Jobbliggaren.Application.CompanyWatches.Abstractions.CompanyBrowseResult),
            StringComparison.Ordinal)
            .ShouldBeFalse(
                "probens hela poäng är att den ALDRIG namnger bäraren — annars bevisar den ingenting.");

        var ports = OrgNrSurfaceScan.CarrierProducingPorts(
            typeof(CompanyWatchDto).Assembly, RawOrgNrCarriers);

        OrgNrSurfaceScan.FindForbiddenApiNames(synthetic, RawOrgNrCarriers, ports).ShouldNotBeEmpty(
            "Api-scannen måste flagga en endpoint som injicerar en bärar-producerande port, ÄVEN när " +
            "bärartypen aldrig nämns (`var` + Results.Ok). Det var precis hålet i den första versionen " +
            "av den här grinden.");
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
    public void Log_scan_does_not_flag_org_nr_tokens_in_prose_or_names()
    {
        // Self-proving POSITIVE (#544 follow-up 2026-07-18) — the placeholder-or-argument scoping's
        // reason to exist. A counts-only logging surface whose org.nr tokens live ONLY in the Message
        // PROSE, a class-name prefix, or the Log<Word> method name (none of which interpolates a runtime
        // value) must NOT be flagged. This mirrors BackfillCompanyWatchOrgNrTokenJob's real surface —
        // which is exactly why it can now join RawOrgNrReadingSourcePaths. The earlier block-wide
        // substring scan flagged all of these (false positive); reverting the scoping reddens THIS test.
        const string countsOnly = """
            [LoggerMessage(EventId = 6161, Level = LogLevel.Information,
                Message = "BackfillCompanyWatchOrgNrToken: startad — dryRun={DryRun}, max={Max}. "
                    + "Counts only — aldrig ett org.nr/personnummer i loggen.")]
            private static partial void LogStarted(ILogger logger, bool dryRun, int max);
            // usage:
            LogStarted(logger, dryRun, o.MaxItemsPerRun);
            """;

        OrgNrSurfaceScan.FindOrgNrLoggingFragments(countsOnly).ShouldBeEmpty(
            "log-scannen får INTE flagga org.nr-token som bara står i Message-prosa, ett klassnamn-" +
            "prefix eller ett Log<Word>-metodnamn — inget av dem bär ett runtime-värde. Bara ett " +
            "{placeholder} eller ett loggat argument kan, och de flaggas fortfarande.");
    }

    [Fact]
    public void Log_scan_flags_an_org_nr_argument_under_a_token_free_template()
    {
        // The ARGUMENT half of the scan, isolated: MEL logs an argument positionally, so a token-free
        // template ("{Count}") over an org.nr-named argument is a real leak the placeholder scan alone
        // would miss. The refinement must still flag it.
        const string synthetic = """
            logger.LogWarning("scanned {Count} employers", watch.OrganizationNumber.Value);
            """;

        OrgNrSurfaceScan.FindOrgNrLoggingFragments(synthetic).ShouldNotBeEmpty(
            "ett org.nr-namngivet argument måste flaggas även under en token-fri mall — MEL loggar " +
            "argumentet positionellt.");
    }

    [Fact]
    public void Log_scan_flags_an_org_nr_placeholder_under_a_token_free_arg_list()
    {
        // Isolates the PLACEHOLDER branch (Arm 1) so deleting the placeholder loop reddens a test. The
        // existing Log_scan_flags_an_org_nr_logging_fragment carries BOTH a {OrganizationNumber}
        // placeholder AND an org.nr-named argument, so the argument scan alone keeps it green — the
        // placeholder loop was exercised but not deletion-pinned (senior-cto-advisor 2026-07-18). Here
        // the token lives ONLY in the {OrganizationNumber} placeholder; the partial declaration's arg
        // list is token-free, so ONLY the placeholder scan can flag it.
        const string synthetic = """
            [LoggerMessage(Level = LogLevel.Information, Message = "scanned {OrganizationNumber}")]
            private static partial void LogX(ILogger logger, string value);
            """;

        OrgNrSurfaceScan.FindOrgNrLoggingFragments(synthetic).ShouldNotBeEmpty(
            "en {OrganizationNumber}-placeholder måste flaggas av placeholder-grenen även när arg-listan " +
            "är token-fri — annars är placeholder-loopen oskyddad mot borttagning.");
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
            // No visibility filter and value types INCLUDED (security-auditor re-review, 2026-07-13).
            // Three cracks were closed here at once: (1) `IsClass` excluded record structs — while
            // FindRawCarriersInResponses, in this same file, explicitly includes value types, so the two
            // scanners disagreed about what a type even is, and §3 prescribes `record struct` for value
            // objects; (2) `Type.IsPublic` is FALSE for a nested public type (it needs IsNestedPublic —
            // the sibling guard in this repo gets that right); (3) filtering on public at all NARROWED
            // coverage versus the scan this replaced, which saw internal *Dtos too. A type that slips
            // the partition slips BOTH guards, because the response walker only looks for types already
            // in RawOrgNrCarriers.
            .Where(t => (t is { IsClass: true, IsAbstract: false }) || (t.IsValueType && !t.IsEnum))
            // Compiler artifacts only — closure display-classes and async state machines CAPTURE locals
            // (org.nr among them) as fields, so dropping the visibility filter made them visible. They
            // are not types anyone can name, return, or serialize; excluding them narrows nothing real.
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
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
        Assembly applicationAssembly, IReadOnlyCollection<Type> maskedOrExempt)
    {
        // A DETECTOR, not a matcher (code-reviewer re-review, 2026-07-13). The first version asked "is
        // any of the four types I was TOLD about reachable from a response?" — its coverage was
        // inherited from whoever remembered to fill the list, which is the fail-open shape all over
        // again. It now asks the question that actually matters: "does any response reach a type that
        // exposes an org.nr and is NOT a classified masking DTO?" Name-independent, shape-independent,
        // list-independent — a new raw carrier of any form is caught by construction, whether or not a
        // human ever classified it.
        var safe = maskedOrExempt.ToHashSet();
        var offenders = new List<string>();

        foreach (var (request, response) in MediatorResponses(applicationAssembly))
        {
            foreach (var reached in ReachableTypes(response)
                         .Where(HasOrgNrMember)
                         .Where(t => !safe.Contains(t)))
            {
                offenders.Add($"{request.Name} -> {reached.Name}");
            }
        }

        return offenders.Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The names the Api project must never mention: the raw carriers themselves, AND every Application
    /// PORT that can hand one out.
    ///
    /// <para>
    /// <b>The port half is the load-bearing half, and the first version of this guard did not have it</b>
    /// (code-reviewer re-review #2, 2026-07-13). That version greped for carrier NAMES and claimed "an
    /// endpoint cannot return a type it cannot name". That claim is FALSE, and this repo's own endpoint
    /// idiom is the counter-example: <c>var page = await browse.BrowseAsync(...); return
    /// Results.Ok(page);</c> serialises <c>PagedResult&lt;CompanyBrowseResult&gt;</c> while naming
    /// <c>CompanyBrowseResult</c> nowhere. The guard would have been green and a personnummer-shaped
    /// org.nr would have been on the wire — the exact accident it was written to prevent. (And the
    /// convention it leaned on — "everything goes through Mediator" — is already broken in production:
    /// <c>ISessionStore</c> is injected straight into three endpoints.)
    /// </para>
    ///
    /// <para>
    /// Naming the PORT, however, is unavoidable: to get a carrier inside Api you must obtain it from the
    /// port, and to resolve a service from DI you MUST write its type — a delegate parameter,
    /// <c>[FromServices]</c>, or <c>GetRequiredService&lt;T&gt;()</c>. There is no <c>var</c> for a
    /// parameter declaration. THAT is the "cannot be done without naming it" property the guard needs.
    /// </para>
    ///
    /// <para>
    /// The port list is DERIVED, never hand-maintained: any Application interface with a method whose
    /// return type transitively reaches a carrier is one. A new carrier-producing port is covered the
    /// day it is written, whether or not anyone remembers this file.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> FindForbiddenApiNames(
        string source, IEnumerable<Type> rawCarriers, IEnumerable<Type> carrierProducingPorts) =>
        rawCarriers.Concat(carrierProducingPorts)
            .Where(t => Regex.IsMatch(source, @"\b" + Regex.Escape(t.Name) + @"\b"))
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every Application interface that can hand a caller a raw carrier — derived from the method return
    /// types, so the set cannot drift away from reality.
    /// </summary>
    internal static IReadOnlyList<Type> CarrierProducingPorts(
        Assembly applicationAssembly, IReadOnlyCollection<Type> rawCarriers)
    {
        var carriers = rawCarriers.ToHashSet();

        return applicationAssembly.GetTypes()
            .Where(t => t.IsInterface)
            // Inherited members included: Type.GetMethods() on an interface returns only what THAT
            // interface declares. An `IChildPort : IParentPort` whose carrier-returning method lives on
            // the parent would derive the parent and not the child — and an endpoint injecting the child
            // names only the child (code-reviewer re-review #3). No such port exists today; the guard
            // should not depend on that staying true.
            .Where(i => i.GetMethods()
                .Concat(i.GetInterfaces().SelectMany(b => b.GetMethods()))
                .Any(m => ReachableTypes(m.ReturnType).Any(carriers.Contains)))
            .ToList();
    }

    /// <summary>
    /// Every (request, response) pair the Mediator boundary exposes in <paramref name="assembly"/> —
    /// THE single enumeration both response-graph guards run on (this file's org.nr carrier scan and
    /// <c>RecruiterContactFtsLockTests</c>' L4b domain-contact lock). Before 2026-07-17 each guard
    /// carried its own copy of this loop and predicate; one rule with two normalisers IS two rules,
    /// and a predicate widened in one copy would have silently blinded the other (CTO F4/A).
    ///
    /// <para>
    /// DELIBERATE, CURRENTLY-VACUOUS BOUNDARY: only <c>IQuery&lt;&gt;</c>/<c>ICommand&lt;&gt;</c> are
    /// matched — the Application assembly has zero <c>IStreamQuery&lt;&gt;</c>/
    /// <c>IStreamCommand&lt;&gt;</c> requests today, and widening coverage is a separate decision
    /// that must not ride a behaviour-preserving refactor (it would silently expand the org.nr
    /// guard's scope). The day streaming arrives, add the stream forms HERE and both guards widen
    /// together — that single point of extension is this method's reason to exist.
    /// </para>
    /// </summary>
    internal static IEnumerable<(Type Request, Type Response)> MediatorResponses(Assembly assembly)
    {
        foreach (var request in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } || (t.IsValueType && !t.IsEnum)))
        {
            foreach (var iface in request.GetInterfaces().Where(IsMediatorRequest))
                yield return (request, iface.GetGenericArguments()[0]);
        }
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

            // Fields too — a walker whose entire job is "no path from a response to a carrier" cannot
            // have a whole member kind it does not look at (security-auditor re-review, 2026-07-13).
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                pending.Push(field.FieldType);
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
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(IsOrgNrProperty)
        // Fields too — otherwise a field-based carrier is invisible to the detector whose entire job is
        // finding carriers (security-auditor re-review, 2026-07-13).
        || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(IsOrgNrField);

    /// <summary>
    /// Tokens that betray an org.nr in a MEMBER NAME. Deliberately NARROWER than
    /// <see cref="OrgNrTokens"/>, which the log scan uses — and the difference is now written down
    /// instead of being an accident (code-reviewer re-review #2, 2026-07-13).
    ///
    /// <para>
    /// The reviewer was right that the two halves of this guard disagreed about what an org.nr looks
    /// like, and that a carrier spelling it <c>OrgNr</c> was invisible to the half that decides whether
    /// it must be masked. But the fix is NOT to reuse the log list: the log scan's bare
    /// <c>"organization"</c> token is broad ON PURPOSE (a log fragment mentioning an organization may
    /// well carry its number, so flag and let a human look). A MEMBER called <c>Organization</c> is
    /// something else entirely — it is the EMPLOYER'S NAME. Reusing the log tokens flagged seven types
    /// on that basis alone (<c>ParsedExperienceDto</c>, <c>ReviewableExperience</c>,
    /// <c>CvReviewContext</c>, …), none of which carries an org.nr. A guard that cries wolf on the CV
    /// parser is a guard someone switches off.
    /// </para>
    ///
    /// <para>
    /// <c>"personnummer"</c> is likewise absent, and for the same kind of reason. In a LOG fragment it
    /// is a red flag; as a MEMBER NAME it names the personnummer guard's own PII-safe reporting surface
    /// — <c>PersonnummerScanDto(bool Found, int Count, IReadOnlyList&lt;string&gt; Kinds)</c> ("count +
    /// kinds, never a raw value", ADR 0074 Invariant 1) and <c>RowsExcludedPersonnummerShaped</c>, an
    /// <c>int</c>. Those members ARE the protection. Flagging them as suspected carriers would have the
    /// guard demanding that the guard be masked.
    /// </para>
    ///
    /// <para>
    /// <b>What "complete" can and cannot mean here.</b> This list covers the spellings the repo actually
    /// uses and §1's English-identifier rule permits — <c>OrgNr</c>, <c>OrgNumber</c> and the snake_case
    /// <c>organization_number</c> included (the last added by #883: <c>ScbCompanyRegisterStore.BatchRow</c>
    /// carries a member literally named <c>organization_number</c> because <c>jsonb_to_recordset</c>
    /// matches recordset columns by property NAME — it cannot be renamed to PascalCase without breaking
    /// the SQL projection, so the detector must recognise the spelling instead);
    /// <c>Organization</c> and <c>Personnummer</c> alone excluded. But NO name detector can be complete:
    /// a member called <c>EmployerKey</c> holding a raw org.nr would slip it, and no token list fixes
    /// that. What carries the guarantee is the STRUCTURAL half beside it — a member TYPED
    /// <see cref="OrganizationNumber"/> is caught whatever it is called. The name half is a heuristic and
    /// is scoped as one; the type half is the invariant.
    /// </para>
    /// </summary>
    private static readonly string[] OrgNrMemberNameTokens =
        ["organizationnumber", "organization_number", "orgnr", "org_nr", "orgnumber"];

    private static bool IsOrgNrProperty(PropertyInfo p)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        return HasOrgNrName(p.Name)
               || string.Equals(t.Name, "OrganizationNumber", StringComparison.Ordinal);
    }

    private static bool HasOrgNrName(string memberName) =>
        OrgNrMemberNameTokens.Any(tok => memberName.Contains(tok, StringComparison.OrdinalIgnoreCase));

    private static bool IsOrgNrField(FieldInfo f)
    {
        var t = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;
        return HasOrgNrName(f.Name)
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
    /// Returns the logging fragments in <paramref name="source"/> that could put a raw org.nr in a log:
    /// an org.nr token inside a <c>{placeholder}</c> of a <c>[LoggerMessage]</c> Message template, or
    /// inside the ARGUMENT list of a <c>Log&lt;Word&gt;(...)</c> call / generated partial-method
    /// declaration. A raw org.nr must never reach a log (ADR 0087 D8 / §5).
    ///
    /// <para>
    /// <b>Scoped to the value carriers</b> — the <c>{placeholder}</c> and the argument list — NOT the
    /// whole match. Message PROSE, a class-name prefix inside it, and the <c>Log&lt;Word&gt;</c> METHOD
    /// NAME are compile-time constants that cannot interpolate a runtime value, so a token there is a
    /// false positive. The earlier block-wide substring form flagged all of them, which false-positived
    /// on the counts-only <c>BackfillCompanyWatchOrgNrTokenJob</c> (class-name prefix <c>…OrgNrToken</c>
    /// + the prose "aldrig ett org.nr/personnummer i loggen" in its Message templates, while it logs
    /// only counts/GUIDs). Same template-vs-member split as <see cref="OrgNrTokens"/> (log) vs
    /// <see cref="OrgNrMemberNameTokens"/> (member name). A real <c>{OrganizationNumber}</c> placeholder
    /// or an org.nr-named argument is still caught; non-logging references (the scan's own
    /// <c>EF.Property(..., "OrganizationNumber")</c> query, doc comments) are ignored as before. The
    /// scan is a strict NARROWING of the old form (it can only flag a subset), so no already-green
    /// source path can regress.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> FindOrgNrLoggingFragments(string source)
    {
        var fragments = new List<string>();

        // [LoggerMessage(...)] templates — flag an org.nr token ONLY inside a {placeholder} of the
        // Message string (the sole interpolation point), never the surrounding prose or a class-name
        // prefix (compile-time constants that carry no runtime value).
        foreach (Match block in Regex.Matches(source, @"\[LoggerMessage\b[^\]]*\]"))
        {
            foreach (Match placeholder in Regex.Matches(block.Value, @"\{[^{}]*\}"))
            {
                if (OrgNrTokens.Any(tok => placeholder.Value.Contains(tok, StringComparison.OrdinalIgnoreCase)))
                    fragments.Add(placeholder.Value);
            }
        }

        // Log<Word>(...) call sites AND generated partial-method declarations — flag an org.nr token in
        // the ARGUMENT list only (the logged values/params), never the Log<Word> method name. A logging
        // call/declaration never crosses a ';', so [^;)]* bounds the arg list.
        foreach (Match call in Regex.Matches(source, @"\bLog[A-Z]\w*\(([^;)]*)\)"))
        {
            if (OrgNrTokens.Any(tok => call.Groups[1].Value.Contains(tok, StringComparison.OrdinalIgnoreCase)))
                fragments.Add(call.Value);
        }

        return fragments;
    }
}
