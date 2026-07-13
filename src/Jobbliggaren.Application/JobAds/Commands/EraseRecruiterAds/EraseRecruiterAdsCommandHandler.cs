using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// GDPR Art. 17 erasure of recruiter PII (ADR 0106 Tier B, #842) — the path that replaces the
/// vacuous purger.
/// </summary>
/// <remarks>
/// <b>What is different, in one line:</b> the old mechanism deleted a <i>string</i> it could never
/// find; this one deletes the <i>carrier</i>. Completeness therefore needs no detector, no recall
/// estimate, no obfuscation argument and no image-embedded caveat — and it reaches the recruiter's
/// NAME, which no regex ever will.
/// <para>
/// <b>Durability is bought by placement, not machinery.</b> The erasure survives the nightly
/// snapshot sync and the 10-minute stream because <c>JobAd.UpdateFromSource</c> refuses on
/// <c>Erased</c> — the re-import tombstone, keyed by the existing <c>(source, external_id)</c>
/// UNIQUE tuple. There is <b>no suppression ledger</b>, and there never will be: a ledger stores
/// the recruiter's email in order to keep erasing it, which is the one design in the space that
/// leaves us holding more of her data after her request than before it.
/// </para>
/// <para>
/// <b>Atomicity.</b> Ads are erased through the aggregate and recent searches removed through the
/// change tracker, so both land in <c>UnitOfWorkBehavior</c>'s single SaveChanges alongside the
/// audit row (ADR 0022). A bulk <c>ExecuteDeleteAsync</c> would have run outside that transaction
/// and could have left us with an erasure that happened and an audit row that did not.
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

        // ---- Match: two channels, every surface in the cascade registry ----------------------
        var jobAdIds = await matchQuery.FindJobAdIdsAsync(identifier, cancellationToken);
        var savedSearchCount = await matchQuery.CountSavedSearchesAsync(identifier, cancellationToken);

        // RecentJobSearch is matched here rather than behind the port because `q` is a plain
        // varchar (so a case-insensitive Contains translates without any Npgsql-specific cast —
        // unlike the jsonb columns, where lower() does not even exist), and because the rows must
        // be REMOVED through the change tracker to stay inside the UnitOfWork transaction.
        var needle = identifier.ToLowerInvariant();

        // CA1304/CA1311/CA1862: this is a LINQ→SQL translation (LOWER(q) ... LIKE), not a runtime
        // string op — culture and StringComparison are meaningless here, and an OrdinalIgnoreCase
        // overload would simply fail to translate. Same suppression, same reason, as
        // JobAdSearchComposition's title-LIKE branch.
#pragma warning disable CA1304, CA1311, CA1862
        var recentSearches = await db.RecentJobSearches
            .Where(r => r.Q != null && r.Q.ToLower().Contains(needle))
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        var matched = new ErasureSurfaceCounts(
            JobAds: jobAdIds.Count,
            RecentJobSearches: recentSearches.Count,
            SavedSearches: savedSearchCount);

        // ---- Nothing held ------------------------------------------------------------------
        // The one sentence the old mechanism said on every request and could never honestly say.
        // It is true now, and it is true BECAUSE both channels ran over every free-text surface.
        if (matched.Total == 0)
        {
            LogNoMatch(logger, command.RequestId, command.DryRun);
            return Result.Success(new EraseRecruiterAdsResponse(
                RequestId: command.RequestId,
                Outcome: ErasureOutcome.NoMatchingDataHeld,
                DryRun: command.DryRun,
                Matched: ErasureSurfaceCounts.None,
                Erased: ErasureSurfaceCounts.None,
                ErasedExternalIds: []));
        }

        // ---- Dry run: report, write nothing -------------------------------------------------
        if (command.DryRun)
        {
            LogDryRun(logger, command.RequestId, matched.JobAds, matched.RecentJobSearches,
                matched.SavedSearches);
            return Result.Success(new EraseRecruiterAdsResponse(
                RequestId: command.RequestId,
                Outcome: ErasureOutcome.DryRun,
                DryRun: true,
                Matched: matched,
                Erased: ErasureSurfaceCounts.None,
                ErasedExternalIds: []));
        }

        // ---- Confirmation gate ---------------------------------------------------------------
        // The validator guarantees ConfirmedJobAdCount is present on a destructive call. Here we
        // check it still describes reality: ingest runs every 10 minutes, so the match set can
        // genuinely move between the dry run and the confirmation. Refusing is the only safe
        // answer — the operator re-reads and re-confirms, and nothing is destroyed on a stale view.
        if (command.ConfirmedJobAdCount != matched.JobAds)
        {
            LogConfirmationMismatch(
                logger, command.RequestId, command.ConfirmedJobAdCount ?? -1, matched.JobAds);

            return Result.Failure<EraseRecruiterAdsResponse>(
                DomainError.Conflict(
                    "EraseRecruiterAds.ConfirmationMismatch",
                    $"Testkörningen visade {command.ConfirmedJobAdCount} annonser, men just nu "
                    + $"matchar {matched.JobAds}. Annonsbeståndet uppdateras var tionde minut. "
                    + "Kör en ny testkörning och bekräfta det nya antalet."));
        }

        // ---- Erase ---------------------------------------------------------------------------
        // Tracked (no AsNoTracking): the ads are erased THROUGH the aggregate, so the invariant
        // lives in one place and the bulk-update shortcut that bypassed it cannot be reintroduced
        // (CLAUDE.md §2.2).
        var ids = jobAdIds.Select(id => new JobAdId(id)).ToList();
        var jobAds = await db.JobAds
            .Where(j => ids.Contains(j.Id))
            .ToListAsync(cancellationToken);

        var erasedExternalIds = new List<string>(jobAds.Count);
        foreach (var jobAd in jobAds)
        {
            var externalId = jobAd.External?.ExternalId;

            var result = jobAd.Erase(clock);
            if (result.IsFailure)
            {
                // The only reachable failure is AlreadyErased, which the match query excludes. If
                // it happens anyway the aggregate has changed under us — do not silently count it
                // as erased, because that number goes into an Art. 12(3) reply.
                LogEraseRefused(logger, command.RequestId, result.Error.Code);
                continue;
            }

            if (externalId is not null)
                erasedExternalIds.Add(externalId);
        }

        db.RecentJobSearches.RemoveRange(recentSearches);

        var erased = new ErasureSurfaceCounts(
            JobAds: jobAds.Count(j => j.Status == JobAdStatus.Erased),
            RecentJobSearches: recentSearches.Count,

            // Zero, always, and NOT because we forgot. A saved search is the USER's artefact,
            // processed under Art. 6(1)(b), which Art. 21(1) does not reach — see
            // ErasureCascadeRegistry.MatchedButNotErased. The gap between Matched.SavedSearches
            // and this zero is the disclosure the reply template is required to carry.
            SavedSearches: 0);

        LogErased(logger, command.RequestId, erased.JobAds, erased.RecentJobSearches,
            matched.SavedSearches);

        return Result.Success(new EraseRecruiterAdsResponse(
            RequestId: command.RequestId,
            Outcome: ErasureOutcome.AdsErased,
            DryRun: false,
            Matched: matched,
            Erased: erased,
            ErasedExternalIds: erasedExternalIds));
    }

    // Every log line below carries the RequestId and counts — NEVER the identifier. An Art. 17
    // request is itself about a person, and the one thing we must not do while erasing her address
    // is copy it into a log sink (CLAUDE.md §5).

    [LoggerMessage(EventId = 8430, Level = LogLevel.Information,
        Message = "Art. 17 erasure {RequestId}: no matching data held (dryRun={DryRun}).")]
    private static partial void LogNoMatch(ILogger logger, Guid requestId, bool dryRun);

    [LoggerMessage(EventId = 8431, Level = LogLevel.Information,
        Message = "Art. 17 erasure {RequestId} DRY RUN: {JobAds} ads, {RecentSearches} recent "
            + "searches, {SavedSearches} saved searches matched. Nothing written.")]
    private static partial void LogDryRun(
        ILogger logger, Guid requestId, int jobAds, int recentSearches, int savedSearches);

    [LoggerMessage(EventId = 8432, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId} REFUSED: operator confirmed {Confirmed} ads but "
            + "{Actual} match now. Nothing erased.")]
    private static partial void LogConfirmationMismatch(
        ILogger logger, Guid requestId, int confirmed, int actual);

    [LoggerMessage(EventId = 8433, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId}: JobAd.Erase refused with {ErrorCode} — NOT counted "
            + "as erased.")]
    private static partial void LogEraseRefused(ILogger logger, Guid requestId, string errorCode);

    [LoggerMessage(EventId = 8434, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId} EXECUTED: {JobAds} ads erased, {RecentSearches} "
            + "recent searches deleted. {SavedSearches} saved searches matched and were NOT erased "
            + "(Art. 6(1)(b) — outside Art. 21(1); disclosed to the data subject).")]
    private static partial void LogErased(
        ILogger logger, Guid requestId, int jobAds, int recentSearches, int savedSearches);
}
