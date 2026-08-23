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
        var snapshotContactIds = await matchQuery.FindApplicationSnapshotContactsAsync(identifier, cancellationToken);
        var manualCount = await matchQuery.CountManualAdEntriesAsync(identifier, cancellationToken);
        var watchCriteriaCount = await matchQuery.CountCompanyWatchCriteriaAsync(identifier, cancellationToken);
        var watchFollowCount = await matchQuery.CountCompanyWatchFollowsAsync(identifier, cancellationToken);
        var jobSeekerProfileCount = await matchQuery.CountJobSeekerProfilesAsync(identifier, cancellationToken);
        var resumeMetadataCount = await matchQuery.CountResumeMetadataAsync(identifier, cancellationToken);

        var matchedAdIds = jobAdMatches.Select(m => m.JobAdId).ToList();
        var referencingCount = await matchQuery.CountApplicationsReferencingAsync(
            matchedAdIds, cancellationToken);

        var matched = new ErasureSurfaceCounts(
            JobAds: jobAdMatches.Count,
            RecentJobSearches: recentMatches.Count,
            SavedSearches: savedSearchCount,
            ApplicationSnapshots: snapshotCount,
            ApplicationSnapshotContacts: snapshotContactIds.Count,
            ManualAdEntries: manualCount,
            CompanyWatchCriteria: watchCriteriaCount,
            CompanyWatchFollows: watchFollowCount,
            JobSeekerProfiles: jobSeekerProfileCount,
            ResumeMetadata: resumeMetadataCount,
            ApplicationsReferencingMatchedAds: referencingCount);

        // The distinct match evidence, no user ids. These rows are hard-deleted with no per-id
        // confirmation ceremony, so the operator must at least SEE what will go — a count cannot
        // be reviewed. A q-matched row shows the term; an employer-only row (q = NULL) shows the
        // matched org.nr; a concept-id axis hit shows the matched axis VALUE (#1425). The org.nr
        // and axis lines are flagged when personnummer-shaped (ADR 0087 D8(c) — never surfaced
        // un-flagged, even to the operator, even when the subject herself supplied it); the q line
        // is not, and that gap is repo state this change did not create.
        //
        // EVERY arm the row matched on is emitted, not the first non-null one. `??` was honest
        // with two slots (the shown hit justified the deletion by itself) but becomes a MASKING
        // rule with three, and the mask would land on the arm #1425 just added: a row with
        // q = "Karlsson jobb" and occupationGroup = ["5509281234"] would show the q term and never
        // tell the operator a personnummer-shaped value sat in a concept-id axis. This surface's
        // review is its ONLY gate. Distinct() downstream bounds the cost by distinct REASONS.
        var recentTerms = recentMatches
            .SelectMany(RecentSearchEvidence)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var couldNotSearch = UnsearchableSurfaces.FromRegistry();

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
                CouldNotSearch: couldNotSearch));
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
                CouldNotSearch: couldNotSearch));
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

        // #842 Tier A — the SURGICAL arm (b1 §4.4, T2 CTO 2026-07-16): remove the recruiter's
        // frozen contact block from every matched application snapshot, leaving the applicant's
        // own record intact. Durable by construction (the funnel never writes a snapshot). Through
        // the aggregate and the change tracker, so it lands in the same UnitOfWork SaveChanges as
        // the ad erasure and the audit row. No per-id confirmation ceremony: unlike the ad erase,
        // what this removes is the requester's own data — a frozen block ABOUT her, inside another
        // user's application record. That ground holds only while the match is sound, which is why
        // this channel's predicate is the tightest of the set (#1448): it excludes jsonb key names
        // and the closed provenance vocabulary, because an over-match here destroys an applicant's
        // record with no human looking at it first.
        var snapshotAppIds = snapshotContactIds
            .Select(id => new Domain.Applications.ApplicationId(id))
            .ToList();
        // IgnoreQueryFilters IS the fix for code-review B1 (2026-07-16): the SEARCH is raw SQL and
        // sees soft-deleted applications; a filtered load here would find her contacts, REPORT
        // them matched, and never erase them — "found by SQL, dropped by the filter, never
        // erased", the defect class this issue exists to end. SoftDelete() hides the row from the
        // product; it does not erase her data from it, and an Art. 17 erasure must reach the same
        // physical set the search reported.
        var applications = await db.Applications
            .IgnoreQueryFilters()
            .Where(a => snapshotAppIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        var erasedSnapshotContactsCount = 0;
        foreach (var application in applications)
        {
            if (application.EraseAdSnapshotContacts())
                erasedSnapshotContactsCount++;
        }

        // Belt to retention's braces (b1 §4.4): a matched NON-Active ad the operator did not
        // confirm for whole-record erasure should hold no contacts (retention cleared them at
        // archival) — sweep any straggler. Active ads are refused by the aggregate: a surgical
        // clear the nightly sync rewrites within ten minutes is the B1 defect, and their remedy
        // is the confirmed whole-record erase above.
        var confirmedSet = confirmedIds.ToHashSet();
        var unconfirmedIds = matchedAdIds
            .Where(id => !confirmedSet.Contains(id))
            .Select(id => new JobAdId(id))
            .ToList();
        var backstopSweptCount = 0;
        if (unconfirmedIds.Count > 0)
        {
            var stragglers = await db.JobAds
                .Where(j => unconfirmedIds.Contains(j.Id)
                            && j.Status != JobAdStatus.Active
                            && j.Contacts != null)
                .ToListAsync(cancellationToken);

            foreach (var straggler in stragglers)
            {
                if (straggler.ClearContactsRetentionBackstop().IsSuccess)
                    backstopSweptCount++;
            }
        }

        if (backstopSweptCount > 0)
            LogContactsBackstopSwept(logger, command.RequestId, backstopSweptCount);

        var erased = new ErasureSurfaceCounts(
            JobAds: erasedJobAdCount,
            RecentJobSearches: recentSearches.Count,
            ApplicationSnapshotContacts: erasedSnapshotContactsCount,

            // Zero, and NOT because we forgot. Every zero below is matched, reported, and left
            // standing on a written ground the registry carries (ErasureCascadeRegistry.
            // WrittenGrounds): a HUMAN settles the user-authored surfaces with the affected user in
            // the loop, snapshots are retained under Art. 17(3)(e), and the referencing-application
            // count is a disclosure rather than a deletion list.
            //
            // The gap between Matched and these zeroes IS the disclosure the reply template carries.
            SavedSearches: 0,
            ApplicationSnapshots: 0,
            ManualAdEntries: 0,
            CompanyWatchCriteria: 0,
            CompanyWatchFollows: 0,
            JobSeekerProfiles: 0,
            ResumeMetadata: 0,
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
            CouldNotSearch: couldNotSearch));
    }

    /// <summary>
    /// EVERY arm the row matched on, one line each. A row can match on two or three arms at once,
    /// and a first-non-null rule would hide the others from the one review this surface gets
    /// before an irreversible hard-delete. Pattern narrowing, not <c>!</c>: the constructor's
    /// invariant is that at least one arm is present, and this method needs no stronger claim.
    /// </summary>
    private static IEnumerable<string> RecentSearchEvidence(ErasureRecentSearchMatch match)
    {
        if (match.Q is not null)
            yield return match.Q;

        if (match.MatchedEmployerOrgNr is not null)
            yield return $"arbetsgivarfilter: {PersonnummerFlagged(match.MatchedEmployerOrgNr)}";

        if (match.MatchedTaxonomyValue is not null)
            yield return $"sökfilter: {PersonnummerFlagged(match.MatchedTaxonomyValue)}";
    }

    /// <summary>
    /// The value as the operator may see it: suffixed "(personnummer-format)" when it is a
    /// personnummer-shaped org.nr (ADR 0087 D8(c) — never surfaced un-flagged, even to the admin
    /// operator, even when the subject herself supplied it). Review payload only; never logged.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The recogniser gate is LOAD-BEARING, and it is not defensive noise.</b>
    /// <c>OrganizationNumber.IsPersonnummerShaped()</c> fails SAFE — it returns <c>true</c> for
    /// anything that is not ten ASCII digits. On the employer arm that never mattered, because the
    /// value is a validated org.nr by construction. A concept-id axis value is ARBITRARY TEXT, so
    /// <c>FromTrusted("Karlsson").IsPersonnummerShaped()</c> is <c>true</c> and every ordinary name
    /// would be surfaced as "(personnummer-format)". A flag that fires on everything flags nothing,
    /// and the control would degrade into decoration on exactly the request it exists for.
    /// <para>
    /// <c>TryFromWrittenForm</c> and not <c>Create</c>: the axes store what was typed, so the value
    /// here can be <c>550928-1234</c> or <c>19550928-1234</c>. <c>Create</c> demands ten ASCII
    /// digits and would leave those UNFLAGGED. It gates just as tightly against ordinary text --
    /// <c>TryFromWrittenForm</c> runs <c>Create</c> internally, so <c>Karlsson</c> and
    /// <c>DJh5_yyF_hEM</c> both return null.
    /// </para>
    /// </remarks>
    private static string PersonnummerFlagged(string value)
    {
        var orgNr = Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(value);
        return orgNr?.IsPersonnummerShaped() == true
            ? $"{value} (personnummer-format)"
            : value;
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

    [LoggerMessage(EventId = 8435, Level = LogLevel.Warning,
        Message = "Art. 17 erasure {RequestId}: retention backstop cleared contacts on "
            + "{Stragglers} non-Active ads — retention should have left ZERO here; investigate "
            + "which archival writer missed the clear (#842 Tier A fitness rule).")]
    private static partial void LogContactsBackstopSwept(
        ILogger logger, Guid requestId, int stragglers);
}
