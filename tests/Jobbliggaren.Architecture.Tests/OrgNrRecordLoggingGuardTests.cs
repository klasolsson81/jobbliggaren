using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Application.Companies.Queries.LookupCompany;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #883 — a C# <c>record</c>'s compiler-generated <c>ToString()</c> prints EVERY public member, and
/// MEL renders a plain <c>{X}</c> placeholder through <c>ToString()</c>. So
/// <c>logger.LogWarning("lookup failed {Company}", dto)</c> — with NO <c>@</c> and NO org.nr token —
/// writes the organisation number into the log through the framework's default formatting. A sole
/// proprietor's org.nr IS the owner's personnummer, in plaintext (ADR 0087 D8(c); CLAUDE.md §5 makes
/// the personnummer guard the highest-priority rule).
///
/// <para>
/// It slips past BOTH existing guards: <c>JobAdPublicSurfaceGuardTests</c> bans <c>{@…}</c>
/// destructuring (there is no <c>@</c> here), and <c>OrganizationNumberSurfacingGuardTests</c>
/// token-scans log templates (the template carries no org.nr token). The hole is in the FORM, not the
/// spelling — the same lesson as the guard that missed <c>{@jobAd}</c>, one level down in the
/// framework's default rendering. #841 closed it for <c>JobAdFacets</c> and <c>JobAdImportItem</c> by
/// overriding <c>ToString()</c>; this guard generalises that to EVERY org.nr-bearing record across
/// every product assembly, so the NEXT such record fails the build rather than being caught by review.
/// </para>
///
/// <para>
/// <b>This is orthogonal to <c>OrganizationNumberSurfacingGuardTests</c></b> (CTO bind #883 D4). That
/// guard answers "is the org.nr masked on the wire"; this one answers "does <c>ToString()</c> redact
/// it so MEL's default rendering can't leak it". Same detector (<c>OrgNrSurfaceScan</c>, reused —
/// DRY), different boundary. Fail-safe default (Saltzer &amp; Schroeder 1975), parity with the sibling
/// fail-closed partitions in this project.
/// </para>
///
/// <para>
/// <b>Both halves are fail-closed</b> — the structural half (a MISSING override fails the build) AND
/// the behavioral half (a LYING override that redacts nothing is caught): the set of behavioral cases
/// is itself enumerated against the scan (see
/// <see cref="Every_redacted_org_nr_record_has_a_behavioral_case_or_is_covered_elsewhere"/>), so a new
/// override without a behavioral case fails too. A hand-maintained coverage list is the exact
/// fail-open the sibling guard was re-architected away from (test-writer, #883).
/// </para>
/// </summary>
public class OrgNrRecordLoggingGuardTests
{
    /// <summary>The live-verified JobStream org.nr form, shared across the sibling guards.</summary>
    private const string Sentinel = "5592804784";

    /// <summary>A fixed, all-letter GUID so a kept-<c>Id</c> field never coincidentally contains the
    /// numeric <see cref="Sentinel"/> (no false failure, and none possible).</summary>
    private static readonly Guid KnownId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Assembly InfrastructureAssembly =
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

    /// <summary>
    /// The product assemblies whose records can reach a logger. Domain + Application + Infrastructure
    /// + Api + Worker. <c>Jobbliggaren.Migrate</c> is excluded on purpose — it holds only EF-generated
    /// <c>Migration</c> subclasses, never a domain record, so scanning it adds false-positive surface
    /// for zero coverage. This list is load-bearing (dropping an entry silently narrows coverage), so
    /// <see cref="The_scan_covers_all_five_owned_assemblies_including_the_snake_case_spelling"/> pins
    /// all five by name.
    /// </summary>
    private static readonly Assembly[] OwnedAssemblies =
    [
        typeof(JobAd).Assembly,                 // Jobbliggaren.Domain
        typeof(CompanyWatchDto).Assembly,       // Jobbliggaren.Application
        InfrastructureAssembly,                 // Jobbliggaren.Infrastructure
        typeof(CompaniesEndpoints).Assembly,    // Jobbliggaren.Api
        // Worker has no org.nr-bearing record to anchor a type-reach assertion on; it is scanned
        // fail-closed so a future one is caught, and the by-name pin keeps it in the list.
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly, // Jobbliggaren.Worker
    ];

    private static readonly Type BatchRowType = ResolveNestedType(
        "Jobbliggaren.Infrastructure.CompanyRegister.ScbCompanyRegisterStore", "BatchRow");

    private static readonly Type EmployerOrgNrRowType = ResolveNestedType(
        "Jobbliggaren.Infrastructure.JobAds.JobAdEmployerReader", "EmployerOrgNrRow");

    /// <summary>
    /// Records with an org.nr member that are EXEMPT from the redacted-<c>ToString()</c> rule
    /// (structural half), each with an explicit reason. EMPTY today — every inventoried leaking record
    /// gets an override. The primary mechanism is not this list — it is "overrode <c>ToString()</c> ⇒
    /// passes automatically" (make it a property of the type). Mirrors the empty
    /// <c>OrganizationNumberSurfacingGuardTests.ExemptOrgNrDtos</c>.
    /// </summary>
    private static readonly HashSet<Type> ExemptFromRedactedToString = [];

    /// <summary>
    /// org.nr-bearing records whose redaction is proven BEHAVIORALLY somewhere other than
    /// <see cref="Redacted_records_do_not_print_their_organisation_number_and_keep_an_id"/> — each
    /// with a reason. The fail-closed behavioral enumeration
    /// (<see cref="Every_redacted_org_nr_record_has_a_behavioral_case_or_is_covered_elsewhere"/>)
    /// accepts a type here instead of a local case.
    /// </summary>
    private static readonly IReadOnlyList<(Type Type, string Reason)> BehaviorallyCoveredElsewhere =
    [
        (typeof(JobAdFacets),
            "#841 — behaviorally covered by JobAdPublicSurfaceGuardTests." +
            "The_PII_bearing_records_do_not_print_their_contents_in_ToString (which also asserts the " +
            "#842 free-text / raw-payload redaction, so it is not pure duplication)."),
        (BatchRowType,
            "Private SCB projection row (jsonb_to_recordset). Its org.nr is legal-entity-only — the " +
            "SCB ingest excludes sole traders (ADR 0091) — so it is not a personnummer, unlike " +
            "EmployerOrgNrRow. Structural coverage (a redacting override is enforced) suffices."),
    ];

    [Fact]
    public void Every_org_nr_bearing_record_overrides_ToString_to_redact()
    {
        // FAIL-CLOSED (the MISSING-override half). Any record (class or struct) in a product assembly
        // whose member is org.nr-shaped AND whose ToString() still prints its members leaks the org.nr
        // through MEL's default {X} rendering. It must override ToString() (redacting the org.nr,
        // keeping an identifying field) or be explicitly exempt with a reason.
        var leaking = OrgNrBearingRecords()
            .Where(HasCompilerGeneratedToString)
            .Where(t => !ExemptFromRedactedToString.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        leaking.ShouldBeEmpty(
            "These records hold an organisation number but their ToString() still prints every public " +
            "member. MEL renders a plain {X} placeholder through ToString(), so " +
            "`logger.LogWarning(\"failed {X}\", record)` — with NO @ and NO org.nr token — writes the " +
            "org.nr into the log, and a sole proprietor's org.nr IS a personnummer in plaintext " +
            "(ADR 0087 D8(c); CLAUDE.md §5, highest priority).\n\n" +
            "Override ToString() to redact the org.nr and keep ONE identifying field (parity " +
            "JobAdFacets / JobAdImportItem, #841). Offenders: " +
            string.Join(", ", leaking.Select(t => t.FullName)));
    }

    [Fact]
    public void The_scan_covers_all_five_owned_assemblies_including_the_snake_case_spelling()
    {
        // Non-vacuity + coverage pin. OwnedAssemblies is load-bearing — dropping an entry silently
        // narrows coverage with a green suite, the exact failure mode #883 exists to fight. Pin all
        // five by name, then pin that the scan actually REACHES a record in the layers that have one:
        //   - Domain, PascalCase          -> JobAdFacets
        //   - Application, PascalCase      -> ScbCompanyRecord
        //   - Api                          -> CompaniesEndpoints.CompanyLookupRequest
        //   - Infrastructure, snake_case   -> BatchRow (organization_number, load-bearing for
        //     jsonb_to_recordset — it cannot be renamed, so the shared detector must recognise the
        //     spelling; this proves it does and that Infra is scanned at all).
        // Worker is pinned by name only: it has no org.nr record to anchor on today, and the fail-closed
        // scan catches a future one.
        OwnedAssemblies.Select(a => a.GetName().Name).OrderBy(n => n, StringComparer.Ordinal).ToList()
            .ShouldBe(
            [
                "Jobbliggaren.Api",
                "Jobbliggaren.Application",
                "Jobbliggaren.Domain",
                "Jobbliggaren.Infrastructure",
                "Jobbliggaren.Worker",
            ], "OwnedAssemblies must scan exactly the five product assemblies (Migrate excluded)");

        var scanned = OrgNrBearingRecords().ToHashSet();

        scanned.ShouldContain(typeof(JobAdFacets), "the scan must reach the Domain assembly");
        scanned.ShouldContain(typeof(ScbCompanyRecord), "the scan must reach an Application org.nr record");
        scanned.ShouldContain(typeof(CompaniesEndpoints.CompanyLookupRequest),
            "the scan must reach the Api assembly");
        scanned.ShouldContain(BatchRowType,
            "the scan must reach the Infrastructure BatchRow via its snake_case organization_number " +
            "member — proving both that Infra is scanned and that the shared OrgNrSurfaceScan detector " +
            "recognises the snake_case spelling BatchRow is forced to use.");
    }

    [Fact]
    public void Every_redacted_org_nr_record_has_a_behavioral_case_or_is_covered_elsewhere()
    {
        // FAIL-CLOSED (the LYING-override half, test-writer MAJOR #883). The structural guard catches a
        // MISSING override; a LYING one — `ToString() => $"...{OrganizationNumber}"` — passes it. Only a
        // behavioral case catches that, and a HAND-MAINTAINED list of behavioral cases is fail-OPEN: a
        // new override with no case ships silently. That is the exact "coverage inherited from whoever
        // remembered to fill the list" pattern the sibling OrganizationNumberSurfacingGuardTests was
        // re-architected away from. So enumerate: every org.nr-bearing RECORD with a redacting override
        // must be behaviorally proven — either a local case or a documented BehaviorallyCoveredElsewhere.
        var behaviorallyTested = BehavioralCases().Select(c => c.Type).ToHashSet();
        var coveredElsewhere = BehaviorallyCoveredElsewhere.Select(e => e.Type).ToHashSet();

        var uncovered = OrgNrBearingRecords()
            .Where(IsRecord)
            .Where(t => !HasCompilerGeneratedToString(t)) // has a redacting override
            .Where(t => !behaviorallyTested.Contains(t) && !coveredElsewhere.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        uncovered.ShouldBeEmpty(
            "These org.nr-bearing records have a redacting ToString() override but NO behavioral proof " +
            "that the override actually redacts. The structural guard is blind to a LYING override " +
            "(`ToString() => $\"...{OrganizationNumber}\"` passes it). A hand-maintained case list is " +
            "fail-OPEN — the pattern the sibling guard was re-architected away from. Add a behavioral " +
            "case in BehavioralCases(), or document it in BehaviorallyCoveredElsewhere with a reason. " +
            "Uncovered: " + string.Join(", ", uncovered.Select(t => t.FullName)));
    }

    [Fact]
    public void Redacted_records_do_not_print_their_organisation_number_and_keep_an_id()
    {
        // BEHAVIORAL proof (CTO bind #883 D6). Construct each redacted record with the sentinel org.nr
        // and assert ToString() (a) does NOT print it and (b) DOES keep an identifying, non-PII field —
        // losing the id is what pressures a future dev to re-log the whole object and re-open the leak
        // (F4). Every override is hand-written and can individually lie, so this covers every accessible
        // redacted type explicitly, not a representative sample.
        foreach (var (type, rendered, keptField) in BehavioralCases())
        {
            rendered.Contains(Sentinel, StringComparison.Ordinal).ShouldBeFalse(
                $"{type.Name}.ToString() printed the organisation number — MEL would render it into a " +
                $"log for a plain {{X}} placeholder. Redact the org.nr in the override. Rendered: {rendered}");

            rendered.Contains(keptField, StringComparison.Ordinal).ShouldBeTrue(
                $"{type.Name}.ToString() must keep an identifying, non-PII field ('{keptField}') for " +
                $"debugging — otherwise the 'fix' for lost debuggability is to log the whole object " +
                $"again. Rendered: {rendered}");
        }
    }

    [Fact]
    public void Leak_detector_flags_a_flat_record_without_a_redacting_ToString()
    {
        // Self-proving negative (mirrors OrganizationNumberSurfacingGuardTests.UnmaskedSampleDto). A
        // synthetic org.nr record in the TEST assembly with the default ToString() must be caught by
        // both halves of the detector — otherwise the structural guard could pass vacuously.
        OrgNrSurfaceScan.HasOrgNrMember(typeof(LeakingSampleRecord)).ShouldBeTrue(
            "the detector must see the org.nr member");
        HasCompilerGeneratedToString(typeof(LeakingSampleRecord)).ShouldBeTrue(
            "a record that does not override ToString() keeps the compiler-generated, member-printing one");
    }

    [Fact]
    public void Leak_detector_flags_a_derived_leaking_record()
    {
        // Self-proving negative for the INHERITANCE case, and a permanent record of a review finding
        // that was empirically FALSE (#883, mutation-verified 2026-07-17). The code-reviewer and
        // test-writer both claimed that for `record Derived : Base` the compiler synthesises ToString()
        // on the BASE and only a PrintMembers override on Derived, so `DeclaringType == type` would MISS
        // a derived leaking record. That is NOT how .NET 10 / C# 14 behaves: a derived record synthesises
        // its OWN [CompilerGenerated] ToString(), so LeakingDerivedOrgNrRecord.ToString().DeclaringType
        // is the DERIVED type — the reflection diagnostic confirmed it, and the mutation that reverted a
        // (briefly-added) PrintMembers-walking widening back to `DeclaringType == type` did NOT go red,
        // because there was nothing to fix. This test pins the true codegen so the phantom hole is not
        // re-raised from a half-remembered "records inherit ToString".
        new LeakingDerivedOrgNrRecord(KnownId, Sentinel).ToString()
            .Contains(Sentinel, StringComparison.Ordinal).ShouldBeTrue(
                "ground truth: a derived record prints its own members (incl. the org.nr) via its own " +
                "synthesised ToString");

        OrgNrSurfaceScan.HasOrgNrMember(typeof(LeakingDerivedOrgNrRecord)).ShouldBeTrue();
        HasCompilerGeneratedToString(typeof(LeakingDerivedOrgNrRecord)).ShouldBeTrue(
            "the detector catches a derived leaking record because the derived synthesises its own " +
            "[CompilerGenerated] ToString (DeclaringType == the derived type) — no inheritance special-case needed");
    }

    [Fact]
    public void Leak_detector_passes_a_record_that_overrides_ToString()
    {
        // ...and it must NOT fire on a record that DID override ToString(), or every override would be
        // pointless and people would route around a guard that never goes green.
        OrgNrSurfaceScan.HasOrgNrMember(typeof(RedactedSampleRecord)).ShouldBeTrue();
        HasCompilerGeneratedToString(typeof(RedactedSampleRecord)).ShouldBeFalse(
            "a hand-written ToString() override is not [CompilerGenerated]");
    }

    [Fact]
    public void OrganizationNumber_value_object_ToString_returns_the_raw_value_by_design()
    {
        // CTO bind #883 D2 — the OrganizationNumber VO is OUT of #883's override scope, pinned
        // documented-safe here. It is INVISIBLE to the structural guard (its only member is Value, not
        // org.nr-named, so HasOrgNrMember is false) and its ToString() is HAND-WRITTEN `=> Value`
        // (formatting / equality / IN-set / query-interpolation callers depend on it), so even if it
        // were detected it would pass. Redacting it is a DIFFERENT change-reason with a required full
        // caller-audit of a core Domain VO — a separate issue, not a rider on #883.
        //
        // This pin makes the decision explicit: a future dev who "fixes" the VO into a redacting
        // ToString() hits a red test that forces them to confront (a) the caller dependency and (b)
        // the audit. And it documents that the VO's log protection is the source-scan
        // (OrganizationNumberSurfacingGuardTests.RawOrgNrReadingSourcePaths / FindOrgNrLoggingFragments)
        // — NOT this ToString(). "The guard is green" does not mean "the VO is safe".
        OrganizationNumber.FromTrusted("5592804784").ToString().ShouldBe("5592804784");
    }

    // ----- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Every org.nr-bearing type across the product assemblies, with compiler-generated types
    /// (closures, async state machines, anonymous types) excluded — parity with
    /// <c>OrgNrSurfaceScan.OrgNrSurfacingTypes</c>, so the two guards agree on what a candidate is.
    /// </summary>
    private static IEnumerable<Type> OrgNrBearingRecords() =>
        OwnedAssemblies
            .SelectMany(SafeGetTypes)
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(OrgNrSurfaceScan.HasOrgNrMember);

    /// <summary>
    /// True when <paramref name="type"/> declares its OWN parameterless <c>ToString()</c> and it is
    /// <c>[CompilerGenerated]</c> — i.e. a record (class or struct) that did NOT override
    /// <c>ToString()</c>, so the compiler-synthesised, every-member-printing one is in force. A plain
    /// class inherits <c>object.ToString()</c> (declared on <c>object</c>, not the type → not a leak);
    /// a hand-written override is not <c>[CompilerGenerated]</c> → not a leak. This is exactly the
    /// leaking condition, without needing a separate "is it a record" probe.
    ///
    /// <para>
    /// <b>Inheritance-safe as-is</b> (mutation-verified 2026-07-17, #883). A derived record synthesises
    /// its OWN <c>[CompilerGenerated] ToString()</c> — on .NET 10 / C# 14,
    /// <see cref="LeakingDerivedOrgNrRecord"/>'s <c>ToString</c> has <c>DeclaringType == the derived
    /// type</c> — so <c>DeclaringType == type</c> catches a derived leaking record. The review-round
    /// claim that the compiler leaves <c>ToString</c> on the base and overrides only <c>PrintMembers</c>
    /// on the derived is FALSE on this runtime: a mutation reverting a widened, PrintMembers-walking
    /// form back to this one did not go red, because there was nothing to fix
    /// (<see cref="Leak_detector_flags_a_derived_leaking_record"/> pins the true behaviour).
    /// </para>
    ///
    /// <para>
    /// A record that redacts via a hand-written <c>PrintMembers</c> override (leaving <c>ToString</c>
    /// compiler-generated) IS flagged as leaking — deliberately over-strict per CTO D3, which makes a
    /// <c>ToString()</c> override the single sanctioned redaction mechanism (parity #841). Fail-closed:
    /// the dev switches to the ToString form or exempts with a reason. No product record uses
    /// <c>PrintMembers</c>-redaction.
    /// </para>
    /// </summary>
    private static bool HasCompilerGeneratedToString(Type type)
    {
        var toString = type.GetMethod(
            nameof(ToString),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        return toString is not null
            && toString.DeclaringType == type
            && toString.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
    }

    /// <summary>True for a C# record (class or struct): the compiler always synthesises a
    /// <c>PrintMembers(StringBuilder)</c>. A plain class/struct has none. Used to require behavioral
    /// coverage only for records (a plain class's member-free <c>object.ToString()</c> cannot leak).</summary>
    private static bool IsRecord(Type type) =>
        type.GetMethod(
            "PrintMembers",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(StringBuilder)],
            modifiers: null) is not null;

    /// <summary>
    /// Every accessible redacted org.nr record, constructed with the sentinel org.nr, plus its rendered
    /// <c>ToString()</c> and the non-PII field its override must keep. The one private Infrastructure
    /// projection record with a personnummer-capable org.nr — <c>EmployerOrgNrRow</c> — is reflection-
    /// constructed here (F3): it is the only private record whose value can be a raw personnummer, so it
    /// gets a real behavioral case, not just structural coverage. (The other private one, <c>BatchRow</c>,
    /// is legal-entity-only and covered structurally via <see cref="BehaviorallyCoveredElsewhere"/>.)
    /// </summary>
    private static List<(Type Type, string Rendered, string KeptField)> BehavioralCases()
    {
        var cases = new List<(Type Type, string Rendered, string KeptField)>
        {
            (typeof(ScbCompanyRecord),
                new ScbCompanyRecord(Sentinel, "Region Stockholm", "0180", null, [], false, "1").ToString(),
                "Region Stockholm"),
            (typeof(CompanyRegistryEntry),
                new CompanyRegistryEntry(Sentinel, "Region Stockholm").ToString(), "Region Stockholm"),
            (typeof(CompanyBrowseResult),
                new CompanyBrowseResult(Sentinel, "Region Stockholm", "0180", null, []).ToString(),
                "Region Stockholm"),
            (typeof(EmployerAdGroup),
                new EmployerAdGroup(Sentinel, "Region Stockholm", 3).ToString(), "Region Stockholm"),
            (typeof(ErasureRecentSearchMatch),
                new ErasureRecentSearchMatch(KnownId, null, Sentinel).ToString(), KnownId.ToString()),
            (typeof(CompanyLookupDto),
                new CompanyLookupDto("found", Sentinel, false, "Region Stockholm", 2, 1, null).ToString(),
                "Region Stockholm"),
            (typeof(EmployerDisambiguationDto),
                new EmployerDisambiguationDto(Sentinel, false, "Region Stockholm", 3).ToString(),
                "Region Stockholm"),
            (typeof(EmployerApplicationHistoryDto),
                new EmployerApplicationHistoryDto(Sentinel, false, "Region Stockholm", 2, []).ToString(),
                "Region Stockholm"),
            (typeof(CompanyBrowseDto),
                new CompanyBrowseDto(Sentinel, false, "Region Stockholm", "0180", null, []).ToString(),
                "Region Stockholm"),
            (typeof(CompanyWatchDto),
                new CompanyWatchDto(KnownId, Sentinel, false, "Region Stockholm", default, 2, 1, null).ToString(),
                "Region Stockholm"),
            (typeof(LookupCompanyQuery), new LookupCompanyQuery(Sentinel).ToString(), "LookupCompanyQuery"),
            (typeof(FollowCompanyCommand), new FollowCompanyCommand(Sentinel).ToString(), "FollowCompanyCommand"),
            (typeof(CompaniesEndpoints.CompanyLookupRequest),
                new CompaniesEndpoints.CompanyLookupRequest(Sentinel).ToString(), "CompanyLookupRequest"),
            // #311 PR-5: the curated brand-group carrier. Its members are public AB org.nrs (the loader
            // rejects personnummer-shaped ones), but the redacting ToString() is enforced structurally —
            // this proves it does not print the member org.nr and keeps the display name.
            (typeof(Jobbliggaren.Application.CompanyWatches.Abstractions.BrandGroup),
                new Jobbliggaren.Application.CompanyWatches.Abstractions.BrandGroup(
                    "volvo-koncernen", "Volvo (koncern)", [Sentinel]).ToString(),
                "Volvo (koncern)"),
        };

        // F3 — the private, personnummer-capable Infrastructure projection record. Reflection reaches it
        // exactly as the scan does (Assembly.GetTypes returns private nested types); JobAdId is public.
        var id = new JobAdId(KnownId);
        var row = Activator.CreateInstance(
            EmployerOrgNrRowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [id, Sentinel],
            culture: null)!;
        cases.Add((EmployerOrgNrRowType, row.ToString()!, KnownId.ToString()));

        return cases;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        // Multi-assembly reflection over Api/Worker can, in principle, hit a type whose dependency
        // fails to load. Degrade to the loadable types rather than throwing — the guard stays
        // fail-closed on what it CAN see. (First-party project references load cleanly today; this is
        // defence-in-depth against a future dependency.)
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static Type ResolveNestedType(string outerFullName, string nestedName)
    {
        var outer = InfrastructureAssembly.GetType(outerFullName, throwOnError: true)!;
        return outer.GetNestedType(nestedName, BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"{outerFullName}+{nestedName} not found — a moved/renamed private projection record " +
                "makes this guard's coverage silently vacuous. Update the name.");
    }

    // ----- synthetic records for the self-proving negatives (TEST assembly only, never scanned) ----

    private sealed record LeakingSampleRecord(Guid Id, string OrganizationNumber);

    private sealed record RedactedSampleRecord(Guid Id, string OrganizationNumber)
    {
        public override string ToString() => $"RedactedSampleRecord(Id={Id}, org.nr redacted)";
    }

    private abstract record LeakingBaseRecord(Guid Id);

    private sealed record LeakingDerivedOrgNrRecord(Guid Id, string OrganizationNumber)
        : LeakingBaseRecord(Id);
}
