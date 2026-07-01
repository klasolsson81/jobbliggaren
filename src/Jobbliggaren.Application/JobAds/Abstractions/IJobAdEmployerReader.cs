namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// #311 #455 (ADR 0087 D2/D8(c); senior-cto-advisor 2026-07-01) — resolves the STORED
/// <c>organization_number</c> shadow column for a set of job ads, server-side. The follow-from-card
/// command (single id) and the follow-state batch (a page of ids) both read employer identity through
/// this one port so the raw SQL stays in Infrastructure (ADR 0062, parity <see cref="IJobAdSearchQuery"/>
/// / <c>IMatchScorer</c>) and both handlers stay unit-testable behind a fake.
///
/// <para>
/// <b>ADR 0087 D8(c) — highest-priority PII boundary:</b> a Swedish sole-proprietorship (enskild firma)
/// org.nr CAN EQUAL the owner's personnummer (CLAUDE.md §5). The raw org.nr returned here is a
/// SERVER-SIDE-ONLY detail: it keys a <c>CompanyWatch</c> or computes a <c>Followable</c> flag, and MUST
/// NEVER be placed in a response DTO, a URL, or a log. That is exactly why #455 keys the card-action by
/// JobAdId (non-PII) and resolves org.nr here rather than surfacing it to the client.
/// </para>
/// </summary>
public interface IJobAdEmployerReader
{
    /// <summary>
    /// Returns a map <c>jobAdId → organization_number</c> for the given ads. An ad present with a
    /// <c>null</c> value carries no employer org.nr (the B2 not-yet-re-ingested case); an ad ABSENT from
    /// the map does not exist or is soft-deleted (the global soft-delete filter applies). The value is
    /// the raw 10-digit org.nr — see the type remarks: it is server-side only and must not be surfaced.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetOrganizationNumbersByJobAdIdsAsync(
        IReadOnlyList<Guid> jobAdIds, CancellationToken cancellationToken);
}
