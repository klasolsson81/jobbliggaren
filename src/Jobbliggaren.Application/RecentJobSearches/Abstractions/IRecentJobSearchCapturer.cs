using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.RecentJobSearches.Abstractions;

/// <summary>
/// Application-port — auto-capture av en sökning för en authenticated user.
/// Implementeras i Infrastructure (ADR 0060): JobSeekerId-lookup, FilterHash-
/// beräkning, INSERT eller UPDATE-bump via UNIQUE(job_seeker_id, filter_hash),
/// evict äldsta rad om <c>RecentJobSearch.MaxPerSeeker</c> överskrids.
///
/// <para>Capture är best-effort — implementationen får inte kasta vid normala
/// fel. Behavior wrappar anropet i try/catch för defensive logging, men
/// idiomatic implementations sväljer transient-fel internt.</para>
/// </summary>
public interface IRecentJobSearchCapturer
{
    Task CaptureAsync(
        Guid userId,
        SearchCriteria criteria,
        int currentCount,
        CancellationToken cancellationToken);
}
