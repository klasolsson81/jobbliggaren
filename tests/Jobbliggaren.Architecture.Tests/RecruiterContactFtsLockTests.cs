using System.Reflection;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// FTS locks L1 + L4 (#842 Tier A, CTO re-bind R3): removing the recruiter from the reverse-lookup
/// index is THE sharpest exposure in the issue, and each lock pins one structural guarantee. L2/L3
/// (extraction and the funnel round-trip) are integration-level and live in
/// <c>RecruiterContactIngestTests</c>. The original L4b (detail DTO carries NO contact member)
/// was spent by design when PR4 landed the member with its reader (ADR 0108 §3) — its
/// replacement below is STRONGER: no Mediator response may ever reach the domain contact types;
/// <see cref="JobAdContactDto"/> is the single sanctioned crossing type (§2.3).
/// </summary>
public class RecruiterContactFtsLockTests
{
    /// <summary>
    /// L1 — <c>search_vector</c> is generated from <c>title || description</c> ONLY. If someone
    /// ever adds <c>contacts</c> to the generation expression, the whole Tier-A design inverts:
    /// the bounded, un-indexed carrier becomes reverse-queryable by every logged-in user, which
    /// is exactly the exposure the field exists to end.
    /// </summary>
    [Fact]
    public void L1_search_vector_is_generated_from_title_and_description_only()
    {
        var configSource = File.ReadAllText(
            SourcePath("src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobAdConfiguration.cs"));

        var expressionLine = configSource.Split('\n')
            .FirstOrDefault(l => l.Contains("to_tsvector('swedish'"));

        expressionLine.ShouldNotBeNull(
            "search_vector's HasComputedColumnSql expression has moved — re-pin this lock");
        var line = expressionLine!;
        line.ShouldContain("coalesce(title,'')");
        line.ShouldContain("coalesce(description,'')");
        line.ShouldNotContain("contacts",
            customMessage: "L1 (#842): contacts must NEVER enter the FTS generation expression");
    }

    /// <summary>
    /// L4 — the LIST DTO is structurally incapable of carrying a contact, and the detail
    /// projection is a DISTINCT type. A shared DTO would put ~37k recruiters' structured contacts
    /// on the search wire ~20 per page the day PR4 adds the member (re-bind R2/B2 — the
    /// bulk-harvest hazard). Conventions drift; types do not.
    /// </summary>
    [Fact]
    public void L4_the_list_dto_cannot_carry_a_contact_and_detail_is_a_distinct_type()
    {
        typeof(JobAdDto).ShouldNotBe(typeof(JobAdDetailDto));

        var contactBearing = typeof(JobAdDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase)
                        || p.PropertyType == typeof(AdContacts)
                        || p.PropertyType == typeof(AdContact)
                        || (p.PropertyType.IsGenericType
                            && p.PropertyType.GetGenericArguments().Contains(typeof(AdContact))))
            .ToList();

        contactBearing.ShouldBeEmpty(
            "the LIST DTO must stay structurally contact-incapable (FTS lock L4, re-bind R2)");
    }

    /// <summary>
    /// L4b (PR4 replacement) — no Mediator response, walked transitively, ever reaches the DOMAIN
    /// contact types. The original L4b pinned the detail DTO's contact-absence until its reader
    /// existed; PR4 spent that fact by design (member + UI in the same change, ADR 0108 §3) and
    /// this stronger lock took its place: <see cref="JobAdContactDto"/> is the single sanctioned
    /// crossing type (§2.3 — no domain object past the Application boundary; CTO 2026-07-17), so
    /// <see cref="AdContact"/>/<see cref="AdContacts"/> in ANY response graph — today's DTOs or a
    /// future one — is a build break, not a review catch. Runs on the SAME enumeration seam as
    /// <c>OrgNrSurfaceScan.FindRawCarriersInResponses</c> (<c>MediatorResponses</c> + the reused
    /// walker — CTO F4/A, 2026-07-17); only the offender predicate is this lock's own.
    /// </summary>
    [Fact]
    public void L4b_no_mediator_response_reaches_a_domain_contact_type()
    {
        var offenders = new List<string>();

        // Shared enumeration seam (CTO F4/A, 2026-07-17): the SAME (request, response) walk the
        // org.nr carrier scan runs on — a predicate widened for one guard can no longer silently
        // miss the other. Non-vacuity of the enumeration itself is pinned once, on the seam
        // (Mediator_response_enumeration_is_not_vacuous in OrganizationNumberSurfacingGuardTests).
        foreach (var (request, response) in
                 OrgNrSurfaceScan.MediatorResponses(typeof(JobAdDetailDto).Assembly))
        {
            foreach (var reached in OrgNrSurfaceScan.ReachableTypes(response)
                         .Where(t => t == typeof(AdContact) || t == typeof(AdContacts)))
            {
                offenders.Add($"{request.Name} -> {reached.Name}");
            }
        }

        offenders.ShouldBeEmpty(
            "Ett Mediator-svar når (transitivt) domänens AdContact/AdContacts. Rekryterarkontakter "
            + "korsar Application-gränsen ENBART som JobAdContactDto (§2.3, fail-closed IsDerived + "
            + "redacted ToString) — en domäntyp i ett svar serialiserar Origin-enumens interna namn "
            + "och kringgår båda skydden. Överträdelser: " + string.Join(", ", offenders.Distinct()));
    }

    /// <summary>
    /// Self-proving negative for the L4b walker: it must find a domain contact type NESTED inside
    /// a response-shaped graph (the exact shape a lazy handler would return), and the sanctioned
    /// DTOs must be clean — otherwise the lock above is vacuous.
    /// </summary>
    [Fact]
    public void L4b_walker_flags_a_nested_domain_contact_and_clears_the_sanctioned_dto()
    {
        OrgNrSurfaceScan.ReachableTypes(typeof(SyntheticContactLeakDto))
            .ShouldContain(typeof(AdContacts),
                "walkern måste hitta en domän-kontakttyp nästlad i en svarsform — annars är "
                + "L4b-låset vakuöst för precis den form det finns för.");

        OrgNrSurfaceScan.ReachableTypes(typeof(JobAdDetailDto))
            .ShouldNotContain(typeof(AdContacts));
        OrgNrSurfaceScan.ReachableTypes(typeof(JobAdDetailDto))
            .ShouldNotContain(typeof(AdContact));
    }

    /// <summary>
    /// The contact DTO's <c>ToString()</c> is redacted — same hole, one layer up from
    /// <see cref="AdContact"/>: a record's generated <c>ToString()</c> prints every member, so a
    /// plain <c>{Contact}</c> MEL placeholder (no <c>@</c>) dumps name/email/phone past both the
    /// destructuring guard and every token scan (the <c>JobAdImportItem</c> lesson,
    /// <c>JobAdPublicSurfaceGuardTests</c>).
    /// </summary>
    [Fact]
    public void The_contact_dto_does_not_print_its_contents_in_ToString()
    {
        var dto = new JobAdContactDto(
            "Anna Andersson", "Rekryterare", "anna@example.com", "070-123 45 67",
            IsDerived: false);

        var text = dto.ToString();

        text.ShouldNotContain("Anna",
            customMessage: "namnet får aldrig nå en logg via ToString()");
        text.ShouldNotContain("anna@example.com");
        text.ShouldNotContain("070");
        text.ShouldNotContain("Rekryterare");

        // ...and it must still be USEFUL for debugging, or someone will "fix" it by logging the
        // fields individually.
        text.ShouldContain("IsDerived");
    }

    // A synthetic response shape carrying the DOMAIN collection — what a lazy handler returning
    // the aggregate's VO straight out would produce. Lives in the TEST assembly, so it never
    // pollutes the real Application-assembly reflection.
    private sealed record SyntheticContactLeakDto(Guid Id, AdContacts Contacts);

    private static string SourcePath(string repoRelative)
    {
        // Walk up from the test bin directory to the repo root (the directory holding the .sln).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not locate the repo root from the test bin directory");
        return Path.Combine(dir.FullName, repoRelative.Replace('/', Path.DirectorySeparatorChar));
    }
}
