namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;

/// <summary>
/// #560 kriterie-vågen PR-2 — one ACTIVE register company matching the user's criterion, as the
/// browse surface renders it (PR-3).
///
/// <para>
/// <b>Personnummer guard (mask + flag, ADR 0087 D8(c), CLAUDE.md §5 highest-priority).</b> The
/// <c>company_register</c> replica is legal-entities-only by ADR 0091 (SCB's <c>Juridisk form</c>
/// filter plus an <c>IsPersonnummerShaped</c> ingest guard), so this DTO should only ever carry
/// legal-entity org.nrs. But that is an INGEST-time invariant in a DIFFERENT subsystem, and resting a
/// personnummer exposure on it is exactly what the repo declined to do for <c>CompanyLookupDto</c>
/// (#454). This DTO is therefore MASK-CAPABLE (nullable <c>string?</c> org.nr + a <c>bool</c> flag)
/// and the handler routes every value through <c>OrganizationNumber.IsPersonnummerShaped()</c> before
/// surfacing: defense-in-depth, so no future path can surface a raw personnummer. The shape is what
/// lands this DTO in the <c>OrganizationNumberSurfacingGuardTests.MaskingOrgNrDtos</c> fail-closed
/// partition (<c>ExemptOrgNrDtos</c> is empty by policy).
/// </para>
///
/// <para>
/// The org.nr is carried at all (rather than dropped) because PR-3's browse surface needs it as the
/// key for "följ det här företaget" — a browse hit flows into the existing org.nr-keyed
/// <c>CompanyWatch</c> follow.
/// </para>
///
/// <para>
/// <b>No advertising-block member (DPIA C-D3).</b> The reklamspärr flag is never surfaced — and it is
/// not a filter either: a company carrying one IS returned (a jobseeker's spontaneous application is
/// not direct marketing; the E1 reading is ratified and scoped strictly to that). The browse SQL does
/// not SELECT the column at all.
/// </para>
///
/// <para>
/// <b>WARNING for the PR-3 browse surface: the enclosing <c>PagedResult.TotalCount</c> is a PAGINATION
/// QUANTITY, not a magnitude — never render it as "N företag matchar."</b> It SATURATES at
/// <c>CompanyBrowseCriteria.MaxPage × PageSize</c> (senior-cto-advisor 2026-07-13), because an
/// uncapped count would make the pager advertise pages it cannot serve. A criterion matching 108 244
/// companies reports the cap, not 108 244. The honest headline ("10 000+ företag matchar") needs its
/// OWN count with its own product-chosen ceiling — cheap to add (a capped count measured at ~78 ms),
/// but it is a separate query and a PR-3 decision. Reading TotalCount as a magnitude would ship a
/// number nobody measured, which is precisely what #859 already had to be reverted for.
/// </para>
/// </summary>
public sealed record CompanyBrowseDto(
    string? OrganizationNumber,
    bool IsProtectedIdentity,
    string Name,
    // SCB 4-digit "säteskommun" — the company's REGISTERED SEAT, a different concept from an ad's
    // JobTech municipality (RF-4 / ADR 0105; the copy must keep them apart). A string with a
    // load-bearing leading zero ("0180" = Stockholm) — never parsed to int.
    string SeatMunicipalityCode,
    string? SeatMunicipalityName,
    IReadOnlyList<string> SniCodes);
