using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// The listed junior IT ads behind <c>/junior</c> (ADR 0186). It returns raw rows with concept ids; the handler names the
/// places and shapes the public answer.
/// </summary>
/// <remarks>
/// Every member reads the ads listed at <c>now</c> (<c>JobAdPublication.IsListed</c>) that <c>JuniorMembership</c>
/// admits. The caller passes one <c>now</c> to all of them, so the count, the page and the floor describe one moment.
/// </remarks>
public interface IJuniorItJobAdQuery
{
    /// <summary>
    /// The number of listed junior IT ads per kommun, over the whole list (no place filter). Ads without a kommun are
    /// one row with a <see langword="null"/> id, so the rows sum to the list's total.
    /// </summary>
    ValueTask<IReadOnlyList<JuniorItMunicipalityCount>> CountByMunicipalityAsync(
        DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// One page of listed junior IT ads, in one kommun when <paramref name="municipalityConceptId"/> is set, ordered by
    /// <paramref name="sortBy"/> with the id as the last key.
    /// </summary>
    ValueTask<IReadOnlyList<JuniorItJobAdRow>> ReadPageAsync(
        DateTimeOffset now,
        string? municipalityConceptId,
        JobAdSortBy sortBy,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the whole list, over every kommun, meets <see cref="Publication.PublicListFloor"/> at
    /// <paramref name="now"/>. Only the outcome leaves the database, never a count per employer.
    /// </summary>
    ValueTask<bool> MeetsFloorAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record JuniorItMunicipalityCount(string? MunicipalityConceptId, int Count);

public sealed record JuniorItJobAdRow(
    Guid Id,
    string Title,
    string CompanyName,
    string? MunicipalityConceptId,
    string? RegionConceptId,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt);
