using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.RecentJobSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// GDPR Art. 17 erasure of recruiter PII (ADR 0106 Tier B, #842).
/// </summary>
/// <remarks>
/// <b>This deletes the carrier, not a detected string</b> — so completeness needs no detector, no
/// recall estimate and no obfuscation argument, and it reaches the recruiter's NAME, which no regex
/// ever will.
/// <para>
/// Durability is bought by <b>placement</b>: <c>JobAd.UpdateFromSource</c> refuses on <c>Erased</c>,
/// so the nightly sync and the 10-minute stream cannot write her back. No suppression ledger — a
/// ledger stores her email in order to keep erasing it.
/// </para>
/// <para>
/// Ads are erased through the aggregate and recent searches removed through the change tracker, so
/// both land in <c>UnitOfWorkBehavior</c>'s single SaveChanges alongside the audit row (ADR 0022). A
/// bulk <c>ExecuteDeleteAsync</c> would have run outside that transaction.
/// </para>
/// </remarks>
public sealed partial class EraseRecruiterAdsCommandHandler(
    IAppDbContext db,
    IRecruiterErasureMatchQuery matchQuery,
    IDateTimeProvider clock,
    ILogger<EraseRecruiterAdsCommandHandler> logger)
    : ICommandHandler<EraseRecruiterAdsCommand, Result<EraseRecruiterAdsResponse>>
{
    public async ValueTask<Result<EraseRecruiterAdsResponse>> Handle(
        EraseRecruiterAdsCommand command, CancellationToken cancellationToken)
    {
        var identifier = command.Identifier.Trim();

        // ---- Match: every surface the cascade registry says we CAN search -------------------
        //
        // What is NOT here is as load-bearing as what is: the DEK-encrypted columns are classified
        // HeldButNotSearchable and are never scanned (a plaintext LIKE would compare her name to
        // base64 and return 0, forever). They are DISCLOSED on every reply via CouldNotSearch, and
        // the structural job_ad_id channel below is what reaches the overlap instead. The full
        // reasoning is the written ground in ErasureCascadeRegistry.
        var jobAdMatches = await matchQuery.FindJobAdsAsync(identifier, cancellationToken);
        var recentMatches = await matchQuery.FindRecentJobSearchesAsync(identifier, cancellationToken);
        var savedSearchCount = await matchQuery.CountSavedSearchesAsync(identifier, cancellationToken);
        var snapshotCount = await matchQuery.CountApplicationSnapshotsAsync(identifier, cancellationToken);
        var manualCount = await matchQuery.CountManualAdEntriesAsync(identifier, cancellationToken);
        var watchCriteriaCount = await matchQuery.CountCompanyWatchCriteriaAsync(identifier, cancellationToken);

        var matchedAdIds = jobAdMatches.Select(m => m.JobAdId).ToList();
        var referencingCount = await matchQuery.CountApplicationsReferencingAsync(
            matchedAdIds, cancellationToken);

        var matched = new ErasureSurfaceCounts(
            JobAds: jobAdMatches.Count,
            RecentJobSearches: recentMatches.Count,
            SavedSearches: savedSearchCount,
            ApplicationSnapshots: snapshotCount,
            ManualAdEntries: manualCount,
            CompanyWatchCriteria: watchCriteriaCount,
            ApplicationsReferencingMatchedAds: referencingCount);

        // The distinct terms, no user ids. These rows are hard-deleted with no per-id confirmation
        // ceremony, so the operator must at least SEE what will go — a count cannot be reviewed.
        var recentTerms = recentMatches
            .Select(m => m.Q)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---- Dry run: report what would go, write nothing -----------------------------------
        if (command.DryRun)
        {
            LogDryRun(logger, command.RequestId, matched.JobAds, matched.RecentJobSearches,
                matched.SavedSearches, matched.ApplicationSnapshots,
                matched.ApplicationsReferencingMatchedAds);

            return Result.Success(new EraseRecruiterAdsResponse(
                RequestId: command.RequestId,
                DryRun: true,
                Matched: matched,
                Erased: ErasureSurfaceCounts.None,
                Matches: jobAdMatches,
                MatchedRecentSearchTerms: recentTerms,
                ErasedExternalIds: [],
                CouldNotSearch: UnsearchableSurfaces.FromRegistry()));
        }

        // ---- Confirmation gate — MUST STAY BEFORE the nothing-held branch ----------------------
        //
        // The ORDER is the control, not the gate's existence. With the nothing-held branch first, a
        // destructive call confirming ads against a corpus that now matches ZERO is answered "we
        // hold no data about you" instead of being refused — the stale-view race the gate exists
        // for, in the one case where the operator's picture and reality are furthest apart.
        var currentIds = matchedAdIds.ToHashSet();
        var confirmedIds = command.ConfirmedJobAdIds ?? [];

        var vanished = confirmedIds.Where(id => !currentIds.Contains(id)).ToList();
        if (vanished.Count > 0)
        {
            LogConfirmationMismatch(logger, command.RequestId, confirmedIds.Count, vanished.Count);

            return Result.Failure<EraseRecruiterAdsResponse>(
                DomainError.Conflict(
                    "EraseRecruiterAds.ConfirmationMismatch",
                    $"{vanished.Count} av de {confirmedIds.Count} annonser du bekräftade matchar "
                    + "inte längre. Annonsbeståndet uppdateras var tionde minut. Kör en ny "
                    + "testkörning, granska på nytt och bekräfta igen."));
        }

        if (matched.Total == 0)
        {
            LogNoMatch(logger, command.RequestId);

            return Result.Success(new EraseRecruiterAdsResponse(
                RequestId: command.RequestId,
                DryRun: false,
                Matched: ErasureSurfaceCounts.None,
                Erased: ErasureSurfaceCounts.None,
                Matches: [],
                MatchedRecentSearchTerms: [],
                ErasedExternalIds: [],
                CouldNotSearch: UnsearchableSurfaces.FromRegistry()));
        }

        // ---- Erase ---------------------------------------------------------------------------
        // Exactly what the operator REVIEWED — never more. An ad that appeared since the dry run is
        // matched but not erased, and the response reports the gap rather than quietly destroying
        // something no human ever looked at.
        // Contains over the strongly-typed VO: EF cannot translate a member access on the value
        // object inside Contains, and falls back to client evaluation (which throws).
        var typedIds = confirmedIds.Select(id => new JobAdId(id)).ToList();

        var jobAds = await db.JobAds
            .Where(j => typedIds.Contains(j.Id))
            .ToListAsync(cancellationToken);

        // The counter is incremented by Erase()'s VERDICT, never re-derived from the ad's status.
        //
        // `Count(j => j.Status == JobAdStatus.Erased)` looks equivalent and is not: Erase() refuses
        // BECAUSE the status is already Erased, so a refused ad satisfies that predicate and gets
        // counted as erased by us. The guard would have been undone by the line below it, and the
        // inflated number goes straight into an Art. 12(3) reply. Nor is erasedExternalIds.Count a
        // substitute — External is nullable, so a manually-created ad would be erased and never
        // counted, trading an over-count for an under-count.
        var erasedJobAdCount = 0;
        var erasedExternalIds = new List<string>(jobAds.Count);

        foreach (var jobAd in jobAds)
        {
            var externalId = jobAd.External?.ExternalId;

            var result = jobAd.Erase(clock);
            if (result.IsFailure)
            {
                // Reachable: FindJobAdsAsync excludes Erased ads, but the tracked re-load above does
                // not, and the corpus moves every ten minutes. If the aggregate moved under us, do
                // NOT count it.
                LogEraseRefused(logger, command.RequestId, result.Error.Code);
                continue;
            }

            erasedJobAdCount++;

            if (externalId is not null)
                erasedExternalIds.Add(externalId);
        }

        // Hard-delete, not a null-out: RecentJobSearch's identity is UNIQUE(JobSeekerId, FilterHash)
        // and `q` is a derivative of that hash which "får aldrig divergera" — a row with q = NULL and
        // a hash computed from that q is a row whose identity contradicts its own content. The
        // aggregate also states the disposal semantics outright (auto-captured cache, no audit-trail
        // dignity, cap 20 with evict-oldest → the list self-rebuilds on her next search).
        var recentIds = recentMatches.Select(m => new RecentJobSearchId(m.Id)).ToList();
        var recentSearches = await db.RecentJobSearches
            .Where(r => recentIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        db.RecentJobSearches.RemoveRange(recentSearches);

        var erased = new ErasureSurfaceCounts(
            JobAds: erasedJobAdCount,
            RecentJobSearches: recentSearches.Count,

            // Zero, and NOT because we forgot. Every one of these is matched, reported, and left
            // standing on a written ground the registry carries (ErasureCascadeRegistry.
            // WrittenGrounds) — saved searches and manual entries because a HUMAN settles them with
            // the affected user in the loop, snapshots because of Art. 17(3)(e), and the referencing
            // applications because the count is a disclosure, not a deletion list.
            //
            // The gap between Matched and these zeroes IS the disclosure the reply template carries.
            SavedSearches: 0,
            ApplicationSnapshots: 0,
            ManualAdEntries: 0,
            CompanyWatchCriteria: 0,
            ApplicationsReferencingMatchedAds: 0);

        LogErased(logger, command.RequestId, erased.JobAds, erased.RecentJobSearches,
            matched.SavedSearches, matched.ApplicationSnapshots,
            matched.ApplicationsReferencingMatchedAds);

        return Result.Success(new EraseRecruiterAdsResponse(
            RequestId: command.RequestId,
            DryRun: false,
            Matched: matched,
            Erased: erased,
            Matches: [],
            MatchedRecentSearchTerms: recentTerms,
            ErasedExternalIds: erasedExternalIds,
            CouldNotSearch: UnsearchableSurfaces.FromRegistry()));
    }

    // Every log line carries the RequestId and counts — NEVER the identifier. An Art. 17 request is
    // itself about a person, and the one thing we must not do while erasing her address is copy it
    // into a log sink (CLAUDE.md §5).

    [LoggerMessage(EventId = 8430, Level = LogLevel.Information,
        Message = "Art. 17 erasure {RequestId}: no match in the searchable surfaces.")]
    private static partial void LogNoMatch(ILogger logger, Guid requestId);

    [LoggerMessage(EventId = 8431, Level = LogLevel.Information,
        Message = "Art. 17 erasure {RequestId} DRY RUN: {JobAds} ads, {RecentSearches} recent "
            + "searches, {SavedSearches} saved searches, {Snapshots} application snapshots, "
            + "{Referencing} applications referencing a matched ad. Nothing written.")]
    private static partial void LogDryRun(ILogger logger, Guid requestId, int jobAds,
        int recentSearches, int savedSearches, int snapshots, int referencing);

    [LoggerMessage(EventId = 8432, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId} REFUSED: {Vanished} of {Confirmed} confirmed ads no "
            + "longer match. Nothing erased.")]
    private static partial void LogConfirmationMismatch(
        ILogger logger, Guid requestId, int confirmed, int vanished);

    [LoggerMessage(EventId = 8433, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId}: JobAd.Erase refused with {ErrorCode} — NOT counted "
            + "as erased.")]
    private static partial void LogEraseRefused(ILogger logger, Guid requestId, string errorCode);

    [LoggerMessage(EventId = 8434, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId} EXECUTED: {JobAds} ads erased, {RecentSearches} "
            + "recent searches deleted. {SavedSearches} saved searches, {Snapshots} application "
            + "snapshots and {Referencing} referencing applications matched and were NOT erased — a "
            + "human handles those, and the reply discloses them.")]
    private static partial void LogErased(ILogger logger, Guid requestId, int jobAds,
        int recentSearches, int savedSearches, int snapshots, int referencing);
}
