using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// The listed junior IT ads behind <c>/junior</c> (ADR 0186). A port because the predicate is translated by the database
/// provider (a regex over the title), which Application may not reference (AGENTS.md §2.1). It returns raw rows with
/// concept ids; the handler names the places and shapes the public answer.
/// </summary>
/// <remarks>
/// Both members read the ads listed at <c>now</c> (<c>JobAdPublication.IsListed</c>) that the junior IT predicate
/// admits. The caller passes the same <c>now</c> to both, so the count and the page describe one moment.
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
