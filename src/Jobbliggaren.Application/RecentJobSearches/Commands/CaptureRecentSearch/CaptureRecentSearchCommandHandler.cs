using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Application.SavedSearches;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.CaptureRecentSearch;

/// <summary>
/// #2285 — the gate first, so a refused search costs no count. Then the count of what the gate admitted, which is what a
/// replay of the stored row counts (#1471), so the row's "nya" starts at 0. Then the store.
/// <para>
/// Best-effort, as capture always was: an unexpected failure in the count or the store answers <c>stored: false</c>,
/// never a 500, and is logged by type alone, since Npgsql can carry parameter values, q among them, in an exception's
/// detail (security-auditor F6 P4a High-1). The change tracker is cleared first, so the unit of work's save after this
/// handler cannot send the failed write again and store a row this answer says was not stored.
/// </para>
/// </summary>
public sealed partial class CaptureRecentSearchCommandHandler(
    ICurrentUser currentUser,
    RecentSearchCaptureGate gate,
    IJobAdSearchQuery search,
    IRecentJobSearchCapturer capturer,
    IAppDbContext db,
    ILogger<CaptureRecentSearchCommandHandler> logger)
    : ICommandHandler<CaptureRecentSearchCommand, Result<CaptureRecentSearchResult>>
{
    public async ValueTask<Result<CaptureRecentSearchResult>> Handle(
        CaptureRecentSearchCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<CaptureRecentSearchResult>(
                DomainError.Validation("RecentJobSearch.Unauthorized", "Användaren är inte autentiserad."));

        if (gate.Admit(command) is not { } criteria)
            return Result.Success(new CaptureRecentSearchResult(Stored: false));

        try
        {
            var count = await search.CountAsync(SearchCriteriaMapping.ToFilterCriteria(criteria), cancellationToken);
            var stored = await capturer.CaptureAsync(userId, criteria, count, cancellationToken);
            return Result.Success(new CaptureRecentSearchResult(stored));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ClearTracking();
            LogCaptureFailed(logger, ex.GetType().FullName ?? "Unknown");
            return Result.Success(new CaptureRecentSearchResult(Stored: false));
        }
    }

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "RecentJobSearch capture failed (best-effort, nothing stored). ExceptionType={ExceptionType}")]
    private static partial void LogCaptureFailed(ILogger logger, string exceptionType);
}
