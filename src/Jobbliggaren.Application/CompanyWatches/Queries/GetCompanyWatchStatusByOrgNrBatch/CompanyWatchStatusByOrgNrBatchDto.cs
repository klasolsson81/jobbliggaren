namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusByOrgNrBatch;

/// <summary>
/// #560 company-search wave PR-C (CTO F3, ADR 0087 D8(c)) — follow-state overlay for a page of register
/// -search rows. <see cref="Statuses"/> is POSITIONAL: element <c>i</c> is the follow-state of request
/// org.nr <c>i</c>, in the SAME order, with NO dedup — the caller (RSC edge) zips by index. That is what
/// lets this DTO carry NO org.nr: the caller supplied the org.nrs, so echoing them back is redundant AND
/// forbidden (a sole-prop org.nr can be a personnummer; a raw org.nr on a Mediator-response-reachable DTO
/// would trip <c>OrganizationNumberSurfacingGuardTests</c>). No member here is org.nr-shaped, so that
/// guard stays green by construction — the same property the jobAdId-keyed
/// <c>CompanyWatchStatusBatchDto</c> relies on.
/// </summary>
public sealed record CompanyWatchStatusByOrgNrBatchDto(IReadOnlyList<OrgNrFollowStatusDto> Statuses);

/// <summary>
/// One company's follow-state. <paramref name="CompanyWatchId"/> is <c>null</c> when the current user does
/// not follow this company; non-null it is the opaque watch id the FE uses to unfollow via the existing
/// DELETE-by-id. There is no <c>Followable</c> flag (unlike the jobAdId-keyed sibling): the FE only ever
/// sends unmasked org.nrs from non-protected rows, so every requested company is followable by
/// construction.
/// </summary>
public sealed record OrgNrFollowStatusDto(Guid? CompanyWatchId);
