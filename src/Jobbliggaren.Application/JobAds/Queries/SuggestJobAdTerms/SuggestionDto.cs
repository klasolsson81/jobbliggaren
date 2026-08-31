using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// Ett typeahead-förslag (ADR 0067 Beslut 5a — utökad suggest-union). Union av
/// taxonomi-snapshot-labels (Län/Kommun/Yrkesområde/Yrkesgrupp) och job_ads-
/// titel-prefix. Ren Application-DTO (CLAUDE.md §3.3 record class).
/// <para>
/// <see cref="ConceptId"/> är <c>null</c> för <see cref="SuggestionKind.Title"/>
/// (fri titel-text har ingen concept-id); satt för alla taxonomi-träffar. FE
/// (Fas E) bär ett strukturerat chip vidare som <c>{kind, conceptId}</c> mot
/// rätt filter-dimension — eller, för titel-träffen, som ren q-fritext.
/// </para>
/// <para>
/// <b>#1546 — the employer axis.</b> <see cref="OrganizationNumber"/> and
/// <see cref="AdCount"/> are set ONLY for <see cref="SuggestionKind.Employer"/> and are
/// <c>null</c> for every other kind. The org.nr is what makes an employer suggestion
/// actionable: selecting one navigates to <c>?employer=&lt;org.nr&gt;</c>, an exact
/// entity filter, rather than to a fuzzy <c>?q=&lt;name&gt;</c> that would re-open the
/// "Volvo×20" trap ADR 0087 made org.nr the canonical key to close.
/// </para>
/// <para>
/// <b>The org.nr does NOT ride <see cref="ConceptId"/>, and that is a security decision.</b>
/// A member named <c>ConceptId</c> holding an org.nr is invisible to
/// <c>OrgNrSurfaceScan.HasOrgNrMember</c> — it is precisely the <c>EmployerKey</c> example that
/// guard's own docblock names as the hole no name detector can close. The member is named
/// <c>OrganizationNumber</c> so the guard DOES see it and this DTO enters the fail-closed
/// partition deliberately.
/// </para>
/// <para>
/// <b><see cref="IsProtectedIdentity"/> is defense-in-depth, and its branch is normally
/// unreachable.</b> A sole proprietorship's org.nr can equal the owner's personnummer (ADR 0087
/// D8(c)), and the handler EXCLUDES such an employer from the union outright (CTO bind F3) — so
/// in practice no row reaches the wire with this flag set. It is computed per row anyway, and
/// the value nulled with it, for the same reason <c>CompanyLookupDto</c> and
/// <c>CompanyBrowseDto</c> carry the same normally-unreachable branch: a personnummer exposure
/// must not rest on one <c>continue</c> in one handler staying correct. Two independent gates,
/// so removing either leaves the other standing.
/// </para>
/// </summary>
public sealed record SuggestionDto(
    SuggestionKind Kind,
    string? ConceptId,
    string Label,
    string? OrganizationNumber = null,
    int? AdCount = null,
    bool IsProtectedIdentity = false)
{
    /// <summary>
    /// REDACTED (#883), for the reason <c>EmployerAdGroup</c> and <c>EmployerDisambiguationDto</c>
    /// already carry: this record can hold an org.nr, and the compiler-generated <c>ToString()</c>
    /// would print it for a plain <c>{X}</c> MEL placeholder. Pinned by
    /// <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() =>
        $"SuggestionDto({Kind}, {Label}, AdCount={AdCount}, org.nr redacted)";

    /// <summary>
    /// #1546 — the employer projection's masking boundary, as a named mapping rather than four lines
    /// inline in the handler, so it can be pinned on its own (security-auditor condition 4: a
    /// defense-in-depth branch that no test exercises is a claim, not coverage).
    /// <para>
    /// Uses the Domain VO's canonical detector — the SAME predicate the handler's exclusion uses and
    /// the same one <c>DisambiguateEmployersQueryHandler</c> and <c>CompanyWatchFollowExecutor</c> use.
    /// Never a second format test: a rule with two normalisers is two rules (#844), and a drift between
    /// them would let this mask what the exclusion passes, or the reverse.
    /// </para>
    /// <para>
    /// <c>FromTrusted</c>: the value came from the validated STORED <c>organization_number</c> column,
    /// not from user input.
    /// </para>
    /// </summary>
    public static SuggestionDto ForEmployer(string organizationNumber, string companyName, int adCount)
    {
        var isProtected = Domain.CompanyWatches.OrganizationNumber
            .FromTrusted(organizationNumber)
            .IsPersonnummerShaped();

        return new SuggestionDto(
            Kind: SuggestionKind.Employer,
            ConceptId: null,
            Label: companyName,
            OrganizationNumber: isProtected ? null : organizationNumber,
            AdCount: adCount,
            IsProtectedIdentity: isProtected);
    }
}
