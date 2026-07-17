namespace Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;

/// <summary>
/// ADR 0087 D6/D8(c) (#311 PR-2b C2) — one row in the employer-disambiguation list: a distinct
/// legal entity the user can pick to follow/filter. org.nr is the canonical follow key (the
/// "Volvo×20" trap is why); <see cref="AdCount"/> is the disambiguation hint (how many ads this
/// entity has in the corpus).
///
/// <para>
/// <b>Personnummer guard (mask+flag, ADR 0087 D8(c), CLAUDE.md §5 highest-priority).</b> A Swedish
/// enskild-firma (sole-proprietorship) org.nr CAN EQUAL the owner's personnummer. When the entity's
/// org.nr is personnummer-shaped, <see cref="OrganizationNumber"/> is <c>null</c> (the raw 10-digit
/// value is NEVER surfaced) and <see cref="IsProtectedIdentity"/> is <c>true</c>; the user still sees
/// the entity via <see cref="CompanyName"/> + <see cref="AdCount"/>. For a normal legal-entity org.nr
/// the full number is returned and the flag is <c>false</c>. This structure makes the DTO land in the
/// <c>OrganizationNumberSurfacingGuardTests.MaskingOrgNrDtos</c> fail-closed partition (a nullable
/// <c>string?</c> org.nr it can null + a <c>bool</c> protection flag). Data-minimisation (GDPR Art.
/// 5.1(c)) at the surfacing boundary; the raw value is never logged.
/// </para>
///
/// <para>
/// <b>Municipality omitted in v1 (ADR 0087 D2 literal divergence, senior-cto-advisor 2026-07-01).</b>
/// D2's projection lists a municipality column, but <c>ITaxonomyReadModel.ResolveLabelsAsync</c>
/// deliberately excludes kommun reverse-resolution (ADR 0043 Variant A) and the ACL forbids surfacing
/// a raw concept-id — so kommun can be neither resolved-to-name nor surfaced-as-id without re-opening
/// the ADR 0043 scope decision (a distinct change-reason). org.nr + name + count fully delivers D2's
/// disambiguation value; municipality enrichment is deferred to a post-#408 batch (Option B).
/// </para>
/// </summary>
public sealed record EmployerDisambiguationDto(
    string? OrganizationNumber,
    bool IsProtectedIdentity,
    string? CompanyName,
    int AdCount)
{
    /// <summary>
    /// REDACTED (#883). The DTO masks its org.nr at the SURFACING boundary, but a record's
    /// compiler-generated <c>ToString()</c> prints every member — a plain <c>{X}</c> MEL placeholder
    /// would still write <see cref="OrganizationNumber"/> into a log. Defense-in-depth at the log
    /// boundary too (a sole prop's org.nr IS a personnummer, ADR 0087 D8(c); CLAUDE.md §5). Keeps
    /// <see cref="CompanyName"/> + <see cref="AdCount"/>; pinned by <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() =>
        $"EmployerDisambiguationDto({CompanyName}, AdCount={AdCount}, org.nr redacted)";
}
