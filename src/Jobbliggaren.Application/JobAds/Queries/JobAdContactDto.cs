using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Queries;

/// <summary>
/// One recruiter contact on a DETAIL surface (#842 PR4) — the query projection of
/// <see cref="AdContact"/>, and the only type through which a contact crosses the Application
/// boundary (§2.3; the reachability lock in <c>RecruiterContactFtsLockTests</c> enforces that no
/// Mediator response ever reaches the domain types). Shared by BOTH detail readers — the live ad
/// (<see cref="JobAdDetailDto"/>) and the apply-time frozen copy (<c>AdSnapshotDto</c>) — because
/// both project the same domain VO (CTO verdict 2026-07-17: a DTO that is a 1:1 projection of ONE
/// shared VO is one type; the fail-closed rule below lives in ONE mapper).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="IsDerived"/> is FAIL-CLOSED</b> (R1(b); dotnet-architect Major 2026-07-17): true
/// for every origin except <see cref="AdContactOrigin.Declared"/>, so a future third origin value
/// renders as "our inference" — never silently as the advertiser's declaration. Presenting a
/// regex hit as her declaration is the untruth class CLAUDE.md §5 bans; a
/// <c>== ExtractedFromBody</c> mapping would commit it by default the day the enum grows.
/// </para>
/// <para>
/// <b><see cref="ToString"/> is REDACTED</b> — a record's generated <c>ToString()</c> prints
/// every member, so a plain <c>{Contact}</c> log placeholder (no <c>@</c> anywhere) would dump
/// the recruiter's name/email/phone through MEL past both the destructuring guard and every token
/// scan (the <c>JobAdImportItem</c>/<c>JobAdFacets</c> lesson). The wire payload goes through
/// System.Text.Json, which never calls <c>ToString</c> — redaction costs nothing.
/// </para>
/// </remarks>
public sealed record JobAdContactDto(
    string? Name,
    string? Role,
    string? Email,
    string? Phone,
    bool IsDerived)
{
    public static JobAdContactDto FromDomain(AdContact contact) => new(
        contact.Name,
        contact.Role,
        contact.Email,
        contact.Phone,
        IsDerived: contact.Origin != AdContactOrigin.Declared);

    /// <summary>
    /// Projects the nullable domain collection to the wire list. Null (never populated /
    /// retention cleared) and <see cref="AdContacts.Empty"/> (the funnel found nothing) BOTH
    /// become <c>[]</c> — that distinction is a retention-fitness concern, not a display one; the
    /// UI shows no block either way.
    /// </summary>
    public static IReadOnlyList<JobAdContactDto> ListFrom(AdContacts? contacts) =>
        contacts is null || contacts.IsEmpty
            ? []
            : [.. contacts.Contacts.Select(FromDomain)];

    /// <summary>Redacted — see the type remarks. Only the non-PII discriminator survives.</summary>
    public override string ToString() => $"JobAdContactDto(IsDerived={IsDerived}, redacted)";
}
