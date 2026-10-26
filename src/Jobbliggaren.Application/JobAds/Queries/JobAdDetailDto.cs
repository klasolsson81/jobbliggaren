namespace Jobbliggaren.Application.JobAds.Queries;

/// <summary>
/// The single-ad DETAIL projection (#842 Tier A, CTO re-bind R2/ISP) — deliberately a distinct
/// type from the LIST projection <see cref="JobAdDto"/>. The two now diverge on TWO axes:
/// <c>Contacts</c> (detail-only — the bulk-harvest guard below) and, since #745, <c>Description</c>
/// (detail-only too). This record declares its own <c>Description</c>; the list DTO deliberately
/// omits it (perf — epic #737 finding <c>d1-list-dto-ships-full-description</c>: no list surface
/// renders the ad text, so shipping the untruncated body per row was dead weight). Do NOT "re-unify"
/// the types by reading a missing <c>Description</c> off the list DTO as a bug — the divergence is
/// intentional and pinned by <c>JobAdListDtoShapeTests</c> (counterfactual: list omits, detail keeps).
/// </summary>
/// <remarks>
/// <b>Why a twin type instead of one shared DTO.</b> PR4 lands the recruiter contact block on the
/// ad detail. <c>JobAdDto</c> is shared by every search/list path (~20 ads per page over the whole
/// corpus), so a <c>Contacts</c> member there would put ~37 000 recruiters' structured contacts on
/// the search wire — a machine-readable bulk-harvest surface reachable in a few thousand
/// authenticated requests, WORSE for the recruiter than today's free text because it arrives
/// pre-parsed. The split makes that structurally impossible instead of convention-dependent: the
/// list DTO cannot carry a contact, because the type has no such member and the architecture test
/// (<c>RecruiterContactFtsLockTests</c>, FTS lock L4) breaks the build if one appears.
/// <para>
/// <b><see cref="Contacts"/> landed WITH its reader (PR4, ADR 0108 §3)</b> — the contact block on
/// the two detail surfaces (R2: ad detail + application detail, never list cards). Never null;
/// <c>[]</c> when the ad holds none or retention cleared them. Each entry carries the R1(b)
/// truth claim (<see cref="JobAdContactDto.IsDerived"/>) the UI must render.
/// </para>
/// <para>
/// <b><see cref="ApplicationUrl"/> (#2191)</b> is the employer's own application page, rendered as the
/// modal's "Ansök" link. No list surface links to it, and the public DTO carries it only as the public
/// gate admits it (<c>PublicApplicationLink</c>, #2214 PR 1b), whereas this one carries every link
/// <c>ApplicationUrl.FromSource</c> stores. Null when the ad has none, which includes every non-Active
/// ad, since the aggregate clears it at archival.
/// </para>
/// <para>
/// <b><see cref="IsListed"/> (#2214 PR 3, ADR 0183 D6)</b> is whether this ad is publicly listed,
/// computed by the one <see cref="Jobbliggaren.Application.JobAds.Publication.JobAdPublication"/>
/// predicate from the import provenance (<c>External?.Source</c>), NEVER the user-settable
/// <see cref="Source"/>. The signed-in "Dela annonsen" row renders only when this is true, so a
/// share never hands on a link that answers 404/410 on the public surface. Carried on the read so
/// the client never re-derives listedness from status/source/expiry (a second copy of the gate
/// drifts, SA-2 M-2). Always <see langword="false"/> for a non-Active or never-imported ad.
/// </para>
/// </remarks>
public sealed record JobAdDetailDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    string? ApplicationUrl,
    string Source,
    string Status,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<JobAdContactDto> Contacts,
    bool IsListed);
