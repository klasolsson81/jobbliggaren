namespace Jobbliggaren.Application.JobAds.Queries;

/// <summary>
/// The single-ad DETAIL projection (#842 Tier A, CTO re-bind R2/ISP) — deliberately a distinct
/// type from the LIST projection <see cref="JobAdDto"/>, even though the fields are identical
/// today.
/// </summary>
/// <remarks>
/// <b>Why a twin type instead of one shared DTO.</b> PR4 lands the recruiter contact block on the
/// ad detail. <c>JobAdDto</c> is shared by every search/list path (~20 ads per page over the whole
/// corpus), so a <c>Contacts</c> member there would put ~37 000 recruiters' structured contacts on
/// the search wire — a machine-readable bulk-harvest surface reachable in a few thousand
/// authenticated requests, WORSE for the recruiter than today's free text because it arrives
/// pre-parsed. The split makes that structurally impossible instead of convention-dependent: the
/// list DTO cannot carry a contact, because the type has no such member and the architecture test
/// (<c>JobAdDtoSplitTests</c>, FTS lock L4) breaks the build if one appears.
/// <para>
/// <b><see cref="Contacts"/> landed WITH its reader (PR4, ADR 0108 §3)</b> — the contact block on
/// the two detail surfaces (R2: ad detail + application detail, never list cards). Never null;
/// <c>[]</c> when the ad holds none or retention cleared them. Each entry carries the R1(b)
/// truth claim (<see cref="JobAdContactDto.IsDerived"/>) the UI must render.
/// </para>
/// </remarks>
public sealed record JobAdDetailDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    string Source,
    string Status,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<JobAdContactDto> Contacts);
