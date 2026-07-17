using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
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
/// </summary>
public class OrgNrRecordLoggingGuardTests
{
    /// <summary>
    /// The product assemblies whose records can reach a logger. Domain + Application + Infrastructure
    /// + Api + Worker. <c>Jobbliggaren.Migrate</c> is excluded on purpose — it holds only EF-generated
    /// <c>Migration</c> subclasses, never a domain record, so scanning it adds false-positive surface
    /// for zero coverage.
    /// </summary>
    private static readonly Assembly[] OwnedAssemblies =
    [
        typeof(JobAd).Assembly,                                 // Jobbliggaren.Domain
        typeof(CompanyWatchDto).Assembly,                       // Jobbliggaren.Application
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly, // Jobbliggaren.Infrastructure
        typeof(CompaniesEndpoints).Assembly,                    // Jobbliggaren.Api
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly, // Jobbliggaren.Worker
    ];

    /// <summary>
    /// Records with an org.nr member that are EXEMPT from the redacted-<c>ToString()</c> rule, each
    /// with an explicit reason. EMPTY today — every inventoried leaking record gets an override, so
    /// nothing needs the escape hatch. A record belongs here ONLY if printing its org.nr via
    /// <c>ToString()</c> is provably safe (never for a value that can be a sole-prop personnummer).
    /// The primary mechanism is not this list — it is "overrode <c>ToString()</c> ⇒ passes
    /// automatically" (make it a property of the type). This mirrors the empty
    /// <c>OrganizationNumberSurfacingGuardTests.ExemptOrgNrDtos</c>.
    /// </summary>
    private static readonly HashSet<Type> ExemptFromRedactedToString = [];

    [Fact]
    public void Every_org_nr_bearing_record_overrides_ToString_to_redact()
    {
        // FAIL-CLOSED. Any record (class or struct) in a product assembly whose member is org.nr-shaped
        // AND whose ToString() is still the compiler-generated one leaks the org.nr through MEL's
        // default {X} rendering. It must override ToString() (redacting the org.nr, keeping an
        // identifying field) or be explicitly exempt with a reason.
        var leaking = OwnedAssemblies
            .SelectMany(SafeGetTypes)
            .Where(OrgNrSurfaceScan.HasOrgNrMember)
            .Where(HasCompilerGeneratedToString)
            .Where(t => !ExemptFromRedactedToString.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        leaking.ShouldBeEmpty(
            "These records hold an organisation number but still have the compiler-generated " +
            "ToString(), which prints every public member. MEL renders a plain {X} placeholder " +
            "through ToString(), so `logger.LogWarning(\"failed {X}\", record)` — with NO @ and NO " +
            "org.nr token — writes the org.nr into the log, and a sole proprietor's org.nr IS a " +
            "personnummer in plaintext (ADR 0087 D8(c); CLAUDE.md §5, highest priority).\n\n" +
            "Override ToString() to redact the org.nr and keep ONE identifying field (parity " +
            "JobAdFacets / JobAdImportItem, #841). Offenders: " +
            string.Join(", ", leaking.Select(t => t.FullName)));
    }

    [Fact]
    public void The_scan_covers_all_owned_assemblies_including_the_snake_case_spelling()
    {
        // Non-vacuity + coverage pin. A silently-empty or single-assembly scan is the exact failure
        // mode #883 exists to fight, so pin that the enumeration actually reaches:
        //   - Application, PascalCase member  -> ScbCompanyRecord
        //   - Api                             -> CompaniesEndpoints.CompanyLookupRequest
        //   - Infrastructure, snake_case member -> BatchRow (organization_number, load-bearing for
        //     jsonb_to_recordset — it cannot be renamed, so the shared detector must recognise the
        //     spelling; this asserts it does and that Infra is scanned at all).
        var scanned = OwnedAssemblies
            .SelectMany(SafeGetTypes)
            .Where(OrgNrSurfaceScan.HasOrgNrMember)
            .ToHashSet();

        scanned.ShouldContain(typeof(ScbCompanyRecord),
            "the scan must reach an Application org.nr record");

        scanned.Any(t => t.Name == "CompanyLookupRequest"
                         && t.Namespace == "Jobbliggaren.Api.Endpoints")
            .ShouldBeTrue("the scan must reach the Api assembly (CompanyLookupRequest)");

        scanned.Any(t => t.Name == "BatchRow"
                         && t.Assembly == typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .ShouldBeTrue(
                "the scan must reach the Infrastructure BatchRow via its snake_case " +
                "organization_number member — proving both that Infra is scanned and that the shared " +
                "OrgNrSurfaceScan detector recognises the snake_case spelling BatchRow is forced to use.");
    }

    [Fact]
    public void Leak_detector_flags_a_record_without_a_redacting_ToString()
    {
        // Self-proving negative (mirrors OrganizationNumberSurfacingGuardTests.UnmaskedSampleDto). A
        // synthetic org.nr record in the TEST assembly with the default ToString() must be caught by
        // both halves of the detector — otherwise the guard above could pass vacuously.
        OrgNrSurfaceScan.HasOrgNrMember(typeof(LeakingSampleRecord)).ShouldBeTrue(
            "the detector must see the org.nr member");
        HasCompilerGeneratedToString(typeof(LeakingSampleRecord)).ShouldBeTrue(
            "a record that does not override ToString() keeps the compiler-generated, member-printing one");
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
    public void Redacted_records_do_not_print_their_organisation_number()
    {
        // BEHAVIORAL proof (CTO bind #883 D6). The structural guard catches a MISSING override but is
        // blind to a LYING one — someone writes `ToString() => $"...{OrganizationNumber}"` and passes
        // structurally. Only constructing each type with a sentinel org.nr and asserting ToString()
        // redacts it closes that. Sentinel = 5592804784 (the live-verified JobStream form used across
        // the sibling guards). Every override is hand-written and can individually lie, so this covers
        // every accessible redacted type explicitly — not a representative sample.
        //
        // The two PRIVATE Infrastructure projection records (BatchRow, EmployerOrgNrRow) cannot be
        // constructed from this assembly; they are covered by the structural guard only, which is
        // sufficient for a lying override there (they are internal projection rows a developer is
        // vanishingly unlikely to hand-write a leaking override for).
        const string sentinel = "5592804784";

        var cases = new (string Type, string Rendered)[]
        {
            (nameof(ScbCompanyRecord), new ScbCompanyRecord(
                sentinel, "Region Stockholm", "0180", null, [], false, "1").ToString()),
            (nameof(CompanyRegistryEntry), new CompanyRegistryEntry(sentinel, "Region Stockholm").ToString()),
            (nameof(CompanyBrowseResult), new CompanyBrowseResult(
                sentinel, "Region Stockholm", "0180", null, []).ToString()),
            (nameof(EmployerAdGroup), new EmployerAdGroup(sentinel, "Region Stockholm", 3).ToString()),
            (nameof(ErasureRecentSearchMatch), new ErasureRecentSearchMatch(
                Guid.NewGuid(), null, sentinel).ToString()),
            (nameof(CompanyLookupDto), new CompanyLookupDto(
                "found", sentinel, false, "Region Stockholm", 2, 1, null).ToString()),
            (nameof(EmployerDisambiguationDto), new EmployerDisambiguationDto(
                sentinel, false, "Region Stockholm", 3).ToString()),
            (nameof(EmployerApplicationHistoryDto), new EmployerApplicationHistoryDto(
                sentinel, false, "Region Stockholm", 2, []).ToString()),
            (nameof(CompanyBrowseDto), new CompanyBrowseDto(
                sentinel, false, "Region Stockholm", "0180", null, []).ToString()),
            (nameof(CompanyWatchDto), new CompanyWatchDto(
                Guid.NewGuid(), sentinel, false, "Region Stockholm", default, 2, 1, null).ToString()),
            (nameof(LookupCompanyQuery), new LookupCompanyQuery(sentinel).ToString()),
            (nameof(FollowCompanyCommand), new FollowCompanyCommand(sentinel).ToString()),
            ("CompanyLookupRequest", new CompaniesEndpoints.CompanyLookupRequest(sentinel).ToString()),
        };

        foreach (var (type, rendered) in cases)
        {
            rendered.Contains(sentinel, StringComparison.Ordinal).ShouldBeFalse(
                $"{type}.ToString() printed the organisation number — MEL would render it into a log " +
                $"for a plain {{X}} placeholder. Redact the org.nr in the override. Rendered: {rendered}");
        }

        // ...and the overrides must still identify the object, or someone "fixes" the lost
        // debuggability by logging the whole object again.
        new ScbCompanyRecord(sentinel, "Region Stockholm", "0180", null, [], false, "1")
            .ToString().Contains("Region Stockholm", StringComparison.Ordinal).ShouldBeTrue(
                "the override must keep an identifying, non-PII field (parity JobAdImportItem's ExternalId)");
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

    /// <summary>
    /// True when <paramref name="type"/> declares its OWN parameterless <c>ToString()</c> and it is
    /// <c>[CompilerGenerated]</c> — i.e. a record (class or struct) that did NOT override
    /// <c>ToString()</c>, so the compiler-synthesised, every-member-printing one is in force. A plain
    /// class inherits <c>object.ToString()</c> (declared on <c>object</c>, not the type → not a leak);
    /// a hand-written override is not <c>[CompilerGenerated]</c> → not a leak. This is exactly the
    /// leaking condition, without needing a separate "is it a record" probe.
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

    // Synthetic org.nr records for the self-proving negatives. In the TEST assembly, so they never
    // reach the real product-assembly scan above.
    private sealed record LeakingSampleRecord(Guid Id, string OrganizationNumber);

    private sealed record RedactedSampleRecord(Guid Id, string OrganizationNumber)
    {
        public override string ToString() => $"RedactedSampleRecord(Id={Id}, org.nr redacted)";
    }
}
