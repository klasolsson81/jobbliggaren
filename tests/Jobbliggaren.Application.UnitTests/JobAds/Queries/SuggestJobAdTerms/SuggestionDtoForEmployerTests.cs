using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// #1546 — the employer suggestion's masking boundary (ADR 0087 D8(c)), pinned on its own.
///
/// <para>
/// <b>Why this exists as a separate class, when the handler already EXCLUDES these rows.</b> The
/// exclusion (CTO bind F3) means no row reaches the wire with <c>IsProtectedIdentity</c> set, so this
/// mapping's masked branch is normally unreachable — the same posture <c>CompanyLookupDto</c> and
/// <c>CompanyBrowseDto</c> already carry, and the reason <c>SuggestionDto</c> is classified in
/// <c>OrganizationNumberSurfacingGuardTests.MaskingOrgNrDtos</c> rather than exempt. That
/// classification is only honest if the branch is actually measured: an unexercised defense-in-depth
/// branch is a claim, not coverage (security-auditor, 2026-08-31, condition 4).
/// </para>
///
/// <para>
/// <b>The two gates are independent, and that is the property under test here.</b> The handler drops a
/// personnummer-shaped employer; this mapping nulls and flags one. Neither is expressed in terms of the
/// other — both call the Domain VO's own detector — so deleting either leaves the other standing.
/// Removing the handler's <c>continue</c> is caught by the handler's own behavioural fact; removing the
/// masking here is caught by this class.
/// </para>
///
/// <para>
/// <b>On the premise (CLAUDE.md §5 <c>Tests:</c>).</b> The arguments are the three fields
/// <c>EmployerAdGroup</c> carries, and the org.nr values are the two shapes the STORED
/// <c>organization_number</c> column really holds: a legal entity's (third digit ≥ 2, Skatteverket's
/// group number for a legal person) and a sole proprietor's, which IS the owner's personnummer. No
/// hand-built state that production cannot produce.
/// </para>
/// </summary>
public class SuggestionDtoForEmployerTests
{
    // Third digit '5' — a legal entity's org.nr; the live-verified JobStream form used across the
    // sibling org.nr guards.
    private const string LegalEntityOrgNr = "5592804784";

    // Third digit '0' — personnummer-shaped, i.e. an enskild firma whose org.nr is the owner's
    // national identity number.
    private const string SolePropOrgNr = "8501012384";

    [Fact]
    public void ALegalEntity_KeepsItsOrganisationNumber_AndIsNotFlagged()
    {
        var dto = SuggestionDto.ForEmployer(LegalEntityOrgNr, "Volvo Group AB", adCount: 136);

        dto.Kind.ShouldBe(SuggestionKind.Employer);
        dto.OrganizationNumber.ShouldBe(LegalEntityOrgNr);
        dto.IsProtectedIdentity.ShouldBeFalse();
        dto.AdCount.ShouldBe(136);
        dto.Label.ShouldBe("Volvo Group AB");

        // The employer axis is not a taxonomy axis; a concept-id here would send the frontend's chip
        // composition down the taxonomy branch.
        dto.ConceptId.ShouldBeNull();
    }

    [Fact]
    public void ASolePropOrgNr_IsNulled_AndFlagged()
    {
        var dto = SuggestionDto.ForEmployer(SolePropOrgNr, "Anna Svensson Konsult", adCount: 1);

        dto.OrganizationNumber.ShouldBeNull(
            "a sole proprietor's org.nr IS the owner's personnummer (ADR 0087 D8(c)); it must never "
            + "reach a wire DTO un-flagged, whether or not the handler also drops the row.");
        dto.IsProtectedIdentity.ShouldBeTrue();
    }

    /// <summary>
    /// The name is NOT masked, and that is deliberate across every masking DTO in this repo — the user
    /// still sees the entity. Pinned so nobody "fixes" it into a claim the product does not make, and
    /// so the sibling rule stays visible: F3 protects the personnummer-shaped subset, never every name
    /// that identifies a natural person.
    /// </summary>
    [Fact]
    public void ASolePropName_IsNotMasked()
    {
        var dto = SuggestionDto.ForEmployer(SolePropOrgNr, "Anna Svensson Konsult", adCount: 1);

        dto.Label.ShouldBe("Anna Svensson Konsult");
        dto.AdCount.ShouldBe(1);
    }

    /// <summary>
    /// The redaction MEL's default <c>{X}</c> rendering depends on (#883). Structurally enforced by
    /// <c>OrgNrRecordLoggingGuardTests</c>; measured here on the branch that actually holds a raw value,
    /// because a record whose org.nr is already null cannot prove its own <c>ToString()</c> redacts.
    /// </summary>
    [Fact]
    public void ToString_DoesNotPrintARawOrganisationNumber()
    {
        var rendered = SuggestionDto.ForEmployer(LegalEntityOrgNr, "Volvo Group AB", 136).ToString();

        rendered.ShouldNotContain(LegalEntityOrgNr);
        rendered.ShouldContain("Volvo Group AB", Case.Sensitive,
            "the override must stay useful for debugging — it redacts the org.nr, not the record.");
    }
}
