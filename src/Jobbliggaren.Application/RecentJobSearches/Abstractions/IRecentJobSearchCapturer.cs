using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.RecentJobSearches.Abstractions;

/// <summary>
/// Application-port — stores a search in the authenticated user's recent searches. Implemented in Infrastructure
/// (ADR 0060): JobSeekerId lookup, FilterHash, INSERT or a bump through UNIQUE(job_seeker_id, filter_hash). It answers
/// whether the search is in the list afterwards: false when the user has no job-seeker profile, when the list is full of
/// marked rows (ADR 0188), or when a racing write removed the row it was to bump.
/// </summary>
public interface IRecentJobSearchCapturer
{
    Task<bool> CaptureAsync(
        Guid userId,
        SearchCriteria criteria,
        int currentCount,
        CancellationToken cancellationToken);
}
