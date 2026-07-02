namespace Jobbliggaren.Application.Companies.Queries.LookupCompany;

/// <summary>
/// #454 (ADR 0088 D5) — the enriched company-lookup result the /foretag lookup card renders. One
/// wire shape for all three outcomes (<see cref="Status"/> = <c>found</c> / <c>notFound</c> /
/// <c>unavailable</c>, always HTTP 200 — the never-500 civic-degradation doctrine; a refused
/// personnummer-shaped input is a Result-failure/400 and never reaches this DTO).
///
/// <para>
/// <b>Personnummer guard (mask+flag, ADR 0087 D8(c), CLAUDE.md §5 highest-priority).</b> The
/// refuse-posture (ADR 0088 D4) means a personnummer-shaped org.nr is rejected upstream and this
/// DTO only ever carries legal-entity data — but the DTO is still MASK-CAPABLE (nullable
/// <c>string?</c> org.nr + <c>bool</c> flag) and the handler still routes every value through
/// <c>OrganizationNumber.IsPersonnummerShaped()</c> before surfacing: defense-in-depth so a future
/// code path cannot surface a raw personnummer, and the shape lands this DTO in the
/// <c>OrganizationNumberSurfacingGuardTests.MaskingOrgNrDtos</c> fail-closed partition.
/// </para>
///
/// <para>
/// Enrichment (only meaningful when <see cref="Status"/> is <c>found</c>): <see cref="ActiveAdCount"/>
/// = public open-role count in our corpus (#447 idiom; 0 is the honest 0-ad story the feature exists
/// for); <see cref="MatchingAdCount"/> = the current user's ≥ Good matching count, <c>null</c> =
/// not-assessed when no occupation is stated (#452 idiom — never a misleading hard 0);
/// <see cref="CompanyWatchId"/> = the user's existing follow of this org.nr (surrogate id only,
/// parity the follow endpoints — enables an honest "bevakar redan" affordance).
/// </para>
/// </summary>
public sealed record CompanyLookupDto(
    string Status,
    string? OrganizationNumber,
    bool IsProtectedIdentity,
    string? CompanyName,
    int ActiveAdCount,
    int? MatchingAdCount,
    Guid? CompanyWatchId)
{
    /// <summary>Wire-stable status tokens (camelCase, not i18n — the FE switches on them).</summary>
    public const string StatusFound = "found";
    public const string StatusNotFound = "notFound";
    public const string StatusUnavailable = "unavailable";

    /// <summary>The empty (non-found) shape for <paramref name="status"/> — no identity, no counts.</summary>
    public static CompanyLookupDto Empty(string status) => new(
        Status: status,
        OrganizationNumber: null,
        IsProtectedIdentity: false,
        CompanyName: null,
        ActiveAdCount: 0,
        MatchingAdCount: null,
        CompanyWatchId: null);
}
