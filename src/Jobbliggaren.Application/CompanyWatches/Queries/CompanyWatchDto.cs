namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — an owner-facing view of one company follow.
///
/// <para>
/// <b>Personnummer guard (FORK C1 mask+flag, ADR 0087 D8(c)).</b> When the watched org.nr is
/// personnummer-shaped (a potential enskild-firma org.nr that equals the owner's national
/// identity number), <see cref="OrganizationNumber"/> is <c>null</c> (the raw 10-digit value is
/// NEVER surfaced) and <see cref="IsProtectedIdentity"/> is <c>true</c>. The user still identifies
/// the watch by <see cref="CompanyName"/> (resolved at read from public Platsbanken data). For a
/// normal legal-entity org.nr the full number is returned and the flag is <c>false</c>. This is
/// data-minimisation (GDPR Art. 5.1(c)) at the surfacing boundary; the raw value is never logged.
/// </para>
/// </summary>
public sealed record CompanyWatchDto(
    Guid Id,
    string? OrganizationNumber,
    bool IsProtectedIdentity,
    string? CompanyName,
    DateTimeOffset FollowedAt);
