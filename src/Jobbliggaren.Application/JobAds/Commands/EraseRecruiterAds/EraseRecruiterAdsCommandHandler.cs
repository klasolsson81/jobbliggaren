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
/// <b>The old mechanism deleted a string it could never find; this one deletes the carrier.</b>
/// Completeness therefore needs no detector, no recall estimate and no obfuscation argument — and
/// it reaches the recruiter's NAME, which no regex ever will.
/// <para>
/// Durability is bought by <b>placement</b>: <c>JobAd.UpdateFromSource</c> refuses on <c>Erased</c>,
/// so the nightly sync and the 10-minute stream cannot write her back. No suppression ledger — a
/// ledger stores her email in order to keep erasing it.
/// </para>
/// <para>
/// Ads are erased through the aggregate and recent searches removed through the change tracker, so
/// both land in <c>UnitOfWorkBehavior</c>'s single SaveChanges alongside the audit row (ADR 0022).
/// A bulk <c>ExecuteDeleteAsync</c> would have run outside that transaction.
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

        // ---- Match: every channel, every surface in the cascade registry ---------------------
        var jobAdMatches = await matchQuery.FindJobAdsAsync(identifier, cancellationToken);
        var savedSearchCount = await matchQuery.CountSavedSearchesAsync(identifier, cancellationToken);
        var snapshotCount = await matchQuery.CountApplicationSnapshotsAsync(identifier, cancellationToken);
        var userTextCount = await matchQuery.CountUserAuthoredTextAsync(identifier, cancellationToken);

        // RecentJobSearch is matched here rather than behind the port because `q` is a plain varchar
        // (so a case-insensitive Contains translates without an Npgsql-specific cast, unlike the
        // jsonb columns where lower() does not even exist), and because the rows must be REMOVED
        // through the change tracker to stay inside the UnitOfWork transaction.
        var needle = identifier.ToLowerInvariant();

        // CA1304/CA1311/CA1862: a LINQ→SQL translation (LOWER(q) … LIKE), not a runtime string op —
        // culture and StringComparison are meaningless here and would not translate. Same
        // suppression, same reason, as JobAdSearchComposition's title-LIKE branch.
#pragma warning disable CA1304, CA1311, CA1862
        var recentSearches = await db.RecentJobSearches
            .Where(r => r.Q != null && r.Q.ToLower().Contains(needle))
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        var matched = new ErasureSurfaceCounts(
            JobAds: jobAdMatches.Count,
            RecentJobSearches: recentSearches.Count,
            SavedSearches: savedSearchCount,
            ApplicationSnapshots: snapshotCount,
            UserAuthoredText: userTextCount);

        // ---- Dry run: report what would go, write nothing -----------------------------------
        if (command.DryRun)
        {
            LogDryRun(logger, command.RequestId, matched.JobAds, matched.RecentJobSearches,
                matched.SavedSearches, matched.ApplicationSnapshots);

            return Result.Success(new EraseRecruiterAdsResponse(
                RequestId: command.RequestId,
                Outcome: matched.Total == 0 ? ErasureOutcome.NoMatchingDataHeld : ErasureOutcome.DryRun,
                DryRun: true,
                Matched: matched,
                Erased: ErasureSurfaceCounts.None,
                Matches: jobAdMatches,
                ErasedExternalIds: []));
        }

        // ---- Confirmation gate — BEFORE the nothing-held branch -------------------------------
        //
        // This ordering is load-bearing, and getting it wrong was a real defect: the gate used to sit
        // AFTER an early `matched.Total == 0 → NoMatchingDataHeld` return. So a destructive call that
        // confirmed three ads against a corpus now matching ZERO was answered "we hold no data about
        // you" — 200 OK — instead of being refused. That is exactly the stale-view race the gate
        // exists for, and it is the case where the operator's picture and reality are furthest apart.
        // He would then have relayed "we hold nothing about you" to a named person, on the strength
        // of a discrepancy the system swallowed.
        var currentIds = jobAdMatches.Select(m => m.JobAdId).ToHashSet();
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

        // Nothing held at all: the honest answer, and it is TRUE when we say it — every channel ran
        // over every free-text surface we hold. It comes AFTER the gate, deliberately (above).
        if (matched.Total == 0)
        {
            LogNoMatch(logger, command.RequestId);

            return Result.Success(new EraseRecruiterAdsResponse(
                RequestId: command.RequestId,
                Outcome: ErasureOutcome.NoMatchingDataHeld,
                DryRun: false,
                Matched: ErasureSurfaceCounts.None,
                Erased: ErasureSurfaceCounts.None,
                Matches: [],
                ErasedExternalIds: []));
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

        var erasedExternalIds = new List<string>(jobAds.Count);
        foreach (var jobAd in jobAds)
        {
            var externalId = jobAd.External?.ExternalId;

            var result = jobAd.Erase(clock);
            if (result.IsFailure)
            {
                // The only reachable failure is AlreadyErased, which the match query excludes. If it
                // happens anyway the aggregate moved under us — do NOT count it as erased, because
                // that number goes into an Art. 12(3) reply.
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

            // Zero on both — and NOT because we forgot.
            //
            // savedSearches: her right DOES apply here (the processing of HER name inside a user's
            // saved search rests on Art. 6(1)(f), which Art. 21(1) reaches — it is NOT covered by the
            // 6(1)(b) contract we have with the USER, to which she is not a party). We honour it in
            // full — but a HUMAN does, not this job: SoftDelete() would leave `criteria` in the row
            // (it hides, it does not erase), and stripping the term is not always constructible. This
            // is a mechanism choice, never a refusal, and the reply must never tell her otherwise.
            //
            // applicationSnapshots: Art. 17(3)(e). A Swedish jobseeker must file an aktivitetsrapport
            // naming the employer, so the company name is the SPINE of her own record, not its colour.
            // STOPP-3 — Klas affirms.
            //
            // The gap between Matched and this zero IS the disclosure the reply template carries.
            SavedSearches: 0,
            ApplicationSnapshots: 0,

            // Also zero, and also disclosed: a user's own note or cover letter may name the
            // recruiter. Her right reaches it — but a job does not silently rewrite a person's
            // private notes about her own job hunt. A human does, with that user in the loop.
            UserAuthoredText: 0);

        LogErased(logger, command.RequestId, erased.JobAds, erased.RecentJobSearches,
            matched.SavedSearches, matched.ApplicationSnapshots);

        return Result.Success(new EraseRecruiterAdsResponse(
            RequestId: command.RequestId,

            // We DO hold matching data here (matched.Total > 0, checked above). If nothing was
            // erased — the operator reviewed the matches and confirmed none, or everything we found
            // lives on a surface a human must settle — then the honest word is NothingErased.
            // Collapsing that into NoMatchingDataHeld would rebuild the old `rowsAffected: 0`
            // ambiguity in a new vocabulary: "we found nothing" and "we found things and removed
            // none of them" are different sentences to send a data subject.
            Outcome: erased.Total > 0 ? ErasureOutcome.AdsErased : ErasureOutcome.NothingErased,
            DryRun: false,
            Matched: matched,
            Erased: erased,
            Matches: [],
            ErasedExternalIds: erasedExternalIds));
    }

    // Every log line carries the RequestId and counts — NEVER the identifier. An Art. 17 request is
    // itself about a person, and the one thing we must not do while erasing her address is copy it
    // into a log sink (CLAUDE.md §5).

    [LoggerMessage(EventId = 8430, Level = LogLevel.Information,
        Message = "Art. 17 erasure {RequestId}: no matching data held.")]
    private static partial void LogNoMatch(ILogger logger, Guid requestId);

    [LoggerMessage(EventId = 8431, Level = LogLevel.Information,
        Message = "Art. 17 erasure {RequestId} DRY RUN: {JobAds} ads, {RecentSearches} recent "
            + "searches, {SavedSearches} saved searches, {Snapshots} application snapshots matched. "
            + "Nothing written.")]
    private static partial void LogDryRun(ILogger logger, Guid requestId, int jobAds,
        int recentSearches, int savedSearches, int snapshots);

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
            + "recent searches deleted. {SavedSearches} saved searches and {Snapshots} application "
            + "snapshots matched and were NOT erased — a human handles those, and the reply "
            + "discloses them.")]
    private static partial void LogErased(ILogger logger, Guid requestId, int jobAds,
        int recentSearches, int savedSearches, int snapshots);
}
