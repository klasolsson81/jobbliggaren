namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// What lifecycle decision a backend read (or write) of <c>JobAds</c> has made about the ad's
/// <c>Status</c> axis (#887, filed by <c>2026-07-14-864-status-gate-cto.md</c> §D5; ADR 0111 §3 is
/// the seed). Three kinds, mirroring the way the erasure precedent grew to as many dispositions as
/// honesty required rather than forcing reality into two buckets
/// (<c>ErasureCascadeRegistry.ErasureColumnDisposition</c>).
/// </summary>
public enum JobAdSiteKind
{
    /// <summary>
    /// The site restricts to <c>Status == JobAdStatus.Active</c> (directly, or through a helper /
    /// composition whose predicate is Active-only, e.g. <c>JobAdSearchComposition.ApplyFilter</c>).
    /// The safe, conforming baseline — it needs no written reason, because "we only surface live ads"
    /// is the default a jobseeker expects.
    /// </summary>
    ActiveOnly,

    /// <summary>
    /// The site deliberately admits non-Active rows — the detail path that still explains WHY an
    /// archived ad was a match (#805-3), a user's own application/saved record that must show its ad
    /// whatever the ad's lifecycle, the erasure read that must see every carrier of the PII. This is
    /// a load-bearing exception, so it carries a <b>written reason</b>: an unexplained AnyStatus is
    /// exactly the "a read path presents an ad the product may no longer present" defect #887 exists
    /// to surface.
    /// </summary>
    AnyStatus,

    /// <summary>
    /// The site MUTATES or INSERTS <c>JobAds</c> — a bulk <c>ExecuteUpdateAsync</c> writer, or a
    /// <c>.Add</c>. It carries a <b>written reason</b> naming its lifecycle gate (or its deliberate
    /// absence). The IL scan cannot tell a <c>.Where().ExecuteUpdate</c> write from a
    /// <c>.Where().ToList</c> read — both are one <c>get_JobAds</c> call — so a writer must be
    /// classified, never scoped out; scoping it out would dig a fail-open hole exactly where the
    /// lifecycle logic is most irreversible (the erased-tombstone resurrection hazard the two bulk
    /// archival writers guard — #842 / CTO 2026-07-16 B7/B8g).
    /// </summary>
    WritePath,
}

/// <summary>
/// One classified <c>get_JobAds</c> site: its kind, a human <paramref name="Note"/> locating it
/// (which query in the method, and what it does), and — for <see cref="JobAdSiteKind.AnyStatus"/>
/// and <see cref="JobAdSiteKind.WritePath"/> — a written <paramref name="Reason"/>.
/// </summary>
public sealed record JobAdSiteDecision(JobAdSiteKind Kind, string Note, string? Reason = null);

/// <summary>
/// A raw-SQL read of <c>job_ads</c> this IL scan is STRUCTURALLY blind to — its lifecycle predicate
/// lives in a SQL string and emits no <c>get_JobAds</c> instruction.
/// <see cref="JobAdLifecycleReadRegistryTests"/> reflects each one and requires the named method to
/// still exist, so a rename cannot silently drop a raw-SQL lifecycle read out of the disclosure.
/// </summary>
public sealed record RawSqlNonReach(string TypeFullName, string Method, string Why);

/// <summary>
/// The lifecycle read-path registry for <c>JobAds</c> (#887) — every backend <c>get_JobAds</c> site
/// in Application + Infrastructure, classified, with a written reason where the kind demands one.
/// <c>JobAdLifecycleReadRegistryTests</c> pins it against an exhaustive Mono.Cecil IL scan of the two
/// assemblies: a site with no decision, or a method whose declared decision count does not equal the
/// number of <c>get_JobAds</c> calls the scan finds in it, <b>breaks the build</b>, naming the method
/// and the number of decisions owed.
/// </summary>
/// <remarks>
/// <b>Why this type exists, and what it is NOT.</b> #864's D5 verdict is blunt: <i>"there is no
/// chokepoint. <c>JobAd</c> deliberately has no query filter (#821 retired the vacuous one; #805-3
/// needs archived ads visible)."</i> So the same defect — a read path presenting an ad the product
/// may no longer present — kept arriving from new directions and each was found by accident. A
/// source-string scan asserting <c>"MatchScorer contains a Status predicate"</c> is FORBIDDEN
/// (#864 D5/G4, #841): it is a grep wearing a prohibition's clothes, and a class named
/// <c>...StatusGateTests</c> reads as "archived ads can never carry a grade" — a sign saying "no
/// hole here" bolted over the holes it does not reach. This registry is the opposite: it does not
/// assert any site is Active-only; it forces every site to STATE what it decided, and it is
/// fail-closed because the Cecil scan enumerates the sites exhaustively (a new read cannot exist
/// without a new <c>get_JobAds</c> instruction). It generalises the column-granular
/// <c>ErasureCascadeRegistry</c> precedent from persisted columns to read/write SITES.
/// <para>
/// <b>"From the EF model" was infeasible and is reinterpreted (ADR 0113).</b> The precedent sweeps
/// <c>context.Model.GetEntityTypes()</c> → columns; the EF model has no representation of a LINQ call
/// site. The generalised enumerator therefore runs over compiled IL (Mono.Cecil), adapting the
/// shipping <c>ConnectionStringLeakageTests</c> harness. This is the one reinterpretation the CTO
/// ruling (<c>2026-07-17-887-lifecycle-registry-cto.md</c>, Q0) ratified.
/// </para>
/// <para>
/// <b>What this control does NOT reach — stated out loud, because a guard that overstates its reach
/// is the defect it exists to prevent (#841/#842 thesis, mirrored from <c>ErasureCascadeRegistry</c>'s
/// own stated non-reach):</b>
/// <list type="number">
/// <item><b>The frontend.</b> A grade cached and rendered client-side is invisible to a backend IL
/// scan (#864 D5; the #887 issue says so verbatim).</item>
/// <item><b>Raw-SQL reads — named in <see cref="KnownNonReaches"/>.</b> A read whose lifecycle
/// predicate lives in a raw SQL string emits no <c>get_JobAds</c> instruction, so the scan is
/// structurally blind to it.</item>
/// <item><b><c>.FromSql</c>-chained sites: the site IS enumerated (it calls <c>get_JobAds</c>) and
/// classified, but the lifecycle predicate inside the raw fragment is invisible</b> — the same
/// limitation the precedent already admits about port-SQL bodies.</item>
/// <item><b>Read/write is indistinguishable, and a 1-for-1 swap is not detected.</b> The count-pin
/// catches a NET-NEW read; it does not catch swapping one site for another within a method (count
/// unchanged), nor does it prove a site's declared decision matches its actual predicate.</item>
/// <item><b>The control pins SITES, not the TRUTH of each reason.</b> The machine guarantees "no
/// unclassified read exists"; the written reason is prose a reviewer reads.</item>
/// </list>
/// </para>
/// </remarks>
public static class JobAdLifecycleReadRegistry
{
    // Reusable decision factories keep the dictionary readable; the note/reason is what a reviewer
    // reads, so each is specific to its site.
    private static JobAdSiteDecision Active(string note) => new(JobAdSiteKind.ActiveOnly, note);
    private static JobAdSiteDecision Any(string note, string reason) =>
        new(JobAdSiteKind.AnyStatus, note, reason);
    private static JobAdSiteDecision Write(string note, string reason) =>
        new(JobAdSiteKind.WritePath, note, reason);

    private static IReadOnlyList<JobAdSiteDecision> One(JobAdSiteDecision d) => [d];

    /// <summary>
    /// The classified sites, keyed <c>Namespace.Type.Method</c> (the LOGICAL method — async state
    /// machines, iterators, lambdas and local functions are unwrapped back to the method that
    /// declared them by the scan). The value lists one <see cref="JobAdSiteDecision"/> per
    /// <c>get_JobAds</c> call the scan finds in that method; the test requires the list length to
    /// equal the observed count (SITE granularity, never METHOD granularity — the precedent's
    /// "COLUMN granularity, never DbSet" lesson, one level over).
    /// </summary>
    /// <remarks>
    /// <b>Only the list's LENGTH is machine-pinned.</b> The order/position of decisions within a
    /// method's list is human documentation — the machine never binds <c>decisions[i]</c> to IL
    /// site <c>i</c> (that binding would be reorder-fragile, the reason IL-ordinal keys were
    /// rejected). Which decision describes which query is what the Note prose is for.
    /// <para>
    /// <b>Same-name overloads in one type share one key</b> and their counts are SUMMED — the key
    /// carries no signature. Fail-closed regardless: a new overload's read still moves the summed
    /// count and breaks the build; its per-overload attribution is then documentation, like order.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<JobAdSiteDecision>> Sites { get; } =
        new Dictionary<string, IReadOnlyList<JobAdSiteDecision>>(StringComparer.Ordinal)
        {
            // ════════════════════════════ ActiveOnly — the conforming reads ════════════════════════
            ["Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount.GetNewFollowedCompanyAdCountQueryHandler.Handle"] =
                One(Active("join JobAds where j.Status == Active — the follow-rail 'new ads' count (#913 B4g witness).")),
            ["Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan.CompanyWatchScanJob.ScanUserAsync"] =
                One(Active(".Where(j.Status == Active && CreatedAt > since && watchedOrgNrs.Contains(orgNr)) — new-ad scan for followed companies.")),
            ["Jobbliggaren.Application.Companies.Queries.LookupCompany.LookupCompanyQueryHandler.Handle"] =
                One(Active(".CountAsync(orgNr == x && j.Status == Active) — the company's active-ad count (#864 C3 class).")),
            ["Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms.SuggestJobAdTermsQueryHandler.Handle"] =
                One(Active(".Where(j.Status == Active).Where(title LIKE pattern) — the public title autosuggest (#913 B5-equivalent surface).")),
            ["Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount.GetMyNewMatchCountQueryHandler.Handle"] =
                One(Active("join JobAds where j.Status == Active — the Översikt new-match badge (#913 B3 witness).")),
            ["Jobbliggaren.Application.Matching.Queries.GetMyMatches.GetMyMatchesQueryHandler.Handle"] =
                One(Active("join JobAds where j.Status == Active — the /matchningar list (#864 PR-A gate).")),
            ["Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchJob.DispatchUserDigestAsync"] =
                One(Active("join JobAds where j.Status == Active — the Strong-match digest EMAIL rows (#864 PR-A A2).")),
            ["Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchJob.DispatchUserFollowedCompanyDigestAsync"] =
                One(Active("join JobAds where j.Status == Active — the followed-company digest EMAIL rows (#864 PR-A A3).")),
            ["Jobbliggaren.Application.Matching.Jobs.BackgroundMatching.BackgroundMatchingJob.ScanUserAsync"] =
                One(Active(".Where(j.Status == Active && CreatedAt > since) — the matching candidate set (#864 D1/B8, measured subsumed by the port).")),
            ["Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats.RefreshLandingStatsJob.RunAsync"] =
            [
                Active("activeCount: .Where(j.Status == Active).CountAsync — the public landing 'active ads' stat (#913 B6)."),
                Active("newToday: .Where(j.Status == Active && PublishedAt >= todayUtcStart).CountAsync — the public 'new today' stat (#913 B6)."),
            ],
            ["Jobbliggaren.Infrastructure.JobAds.JobAdSearchQuery.SearchAsync"] =
                One(Active("db.JobAds → JobAdSearchComposition.ApplyFilter, whose SPOT predicate is j.Status == Active (#913 B1 — the anonymous /jobb search).")),
            ["Jobbliggaren.Infrastructure.JobAds.JobAdSearchQuery.CountAsync"] =
                One(Active("db.JobAds → ApplyFilter (j.Status == Active SPOT); the /jobb total-count over the filtered Active set.")),
            ["Jobbliggaren.Infrastructure.JobAds.JobAdSearchQuery.FacetCountsAsync"] =
                One(Active("db.JobAds → ApplyFilter (j.Status == Active SPOT); per-option facet counts over the Active set.")),
            ["Jobbliggaren.Infrastructure.JobAds.PerUserJobAdSearchQuery.SearchPerUserAsync"] =
                One(Active("db.JobAds → ApplyFilter (j.Status == Active SPOT) then ApplyStatusFilter (per-user saved/applied); the base set is Active.")),
            ["Jobbliggaren.Infrastructure.JobAds.PerUserJobAdSearchQuery.CountPerUserAsync"] =
                One(Active("db.JobAds → ApplyFilter (j.Status == Active SPOT); the per-user list total-count over the Active set.")),
            ["Jobbliggaren.Infrastructure.JobAds.PerUserJobAdSearchQuery.SearchByStatusAsync"] =
                One(Active("db.JobAds → ApplyFilter (j.Status == Active SPOT) then ApplyStatusFilter; the base set is Active.")),
            ["Jobbliggaren.Infrastructure.JobAds.PerUserJobAdSearchQuery.CountPerUserByEmployerAsync"] =
                One(Active(".Where(orgNrs.Contains(orgNr) && j.Status == Active).GroupBy(orgNr) — per-employer active counts (#864 C1 class).")),
            ["Jobbliggaren.Infrastructure.JobAds.PerUserJobAdSearchQuery.FilterToMatchingAsync"] =
                One(Active(".FromSql(id = ANY(ids)).Where(j.Status == Active) — the claim/drain candidate set (#886 M1; only live ads drain).")),
            ["Jobbliggaren.Infrastructure.Matching.MatchScorer.ScoreBatchAsync"] =
                One(Active(".FromSql(id = ANY(ids)).Where(j.Status == Active) — the batch scorer (#864 PR-B B1).")),
            ["Jobbliggaren.Infrastructure.Matching.MatchScorer.ScoreFullBatchAsync"] =
                One(Active(".FromSql(id = ANY(ids)).Where(j.Status == Active) — the full batch scorer (#864 PR-B B2).")),
            ["Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker.CountActiveJobAdsAsync"] =
                One(Active(".Where(j.Status == Active && External.Source == source).CountAsync — the parsed-total for retention thresholds.")),
            ["Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker.CountArchiveCandidatesAsync"] =
                One(Active(".Where(j.Status == Active && External... && EXISTS miss >= threshold) — mirrors the archival writer's select for count parity.")),

            // ════════════════════════════ AnyStatus — deliberately admits non-Active ════════════════
            ["Jobbliggaren.Application.JobAds.Queries.GetJobAd.GetJobAdQueryHandler.Handle"] =
                One(Any(
                    ".Where(j.Id == id).Select(... j.Status.Value ...) — the ad detail page.",
                    "THE detail page. #805-3: an archived ad is deliberately shown here with its grade and an "
                    + "explanation of why it matched, so it must NOT gate on Active. It projects Status.Value so "
                    + "the surface can label the lifecycle; an Erased ad is turned into a 410 by this same "
                    + "handler AFTER the read (the read itself stays status-agnostic).")),
            ["Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds.ListSavedJobAdsQueryHandler.Handle"] =
                One(Any(
                    ".GroupJoin(db.JobAds).SelectMany(DefaultIfEmpty()) — LEFT join enriching saved (bookmarked) ads.",
                    "A bookmarked ad is the user's own record; its lifecycle must not filter her list. A missing or "
                    + "Erased ad projects as null and reuses the 'Annonsen är borttagen' orphan row (#842). Gating on "
                    + "Active would silently drop bookmarks of archived ads she still wants to see.")),
            ["Jobbliggaren.Application.SavedJobAds.Commands.SaveJobAd.SaveJobAdCommandHandler.Handle"] =
                One(Any(
                    ".AnyAsync(j.Id == id && j.Status != Erased) — existence check before bookmarking.",
                    "A user may bookmark an ad that is Active OR Archived, but not an Erased tombstone: 'Status != "
                    + "Erased' denies only the erased row, by design, and admitting Archived is deliberate (she can "
                    + "save an ad that has since been archived). This is a save action on a real ad, not a surfacing.")),
            ["Jobbliggaren.Application.Applications.Queries.GetPipeline.GetPipelineQueryHandler.Handle"] =
                One(Any(
                    ".GroupJoin(db.JobAds).SelectMany(DefaultIfEmpty()) — LEFT join enriching the pipeline applications.",
                    "An application references an ad that may since have been archived or erased; the pipeline is the "
                    + "user's own record and must show it regardless of the ad's lifecycle (no ad row → JobAdGuid null, "
                    + "handled downstream). #805-3 class — gating on Active would erase the user's own applications. "
                    + "An Erased tombstone keeps its row but the projection swaps summary identity to the applicant's "
                    + "own AdSnapshot — empty identity without one (#892 CTO R1/R5); the join stays status-agnostic.")),
            ["Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory.GetEmployerApplicationHistoryQueryHandler.Handle"] =
                One(Any(
                    ".GroupJoin(db.JobAds).SelectMany(DefaultIfEmpty()) — LEFT join deriving employer/org.nr on applications.",
                    "Enriches the user's application history with the ad's employer; the ad may be archived/erased and "
                    + "the history must still list the application (org.nr is null when the ad has aged out — #824). "
                    + "Gating on Active would drop the user's own record. An Erased ad's org.nr is nulled by Erase(), "
                    + "so it joins the SAME dropped residue: the snapshot carries no org.nr, this is functionally "
                    + "unfixable and documented, never name-guessed (#892 CTO R4; #824 owns the residue bucket).")),
            ["Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch.GetEmployerApplicationCountBatchQueryHandler.Handle"] =
                One(Any(
                    ".GroupJoin(db.JobAds).SelectMany(DefaultIfEmpty()) — LEFT join deriving org.nr per application.",
                    "'Have I applied to this employer?' counts over the user's own applications; the referenced ad may "
                    + "be archived. A NULL org.nr (aged-out ad, #824) drops out, never gated on Active — gating would "
                    + "undercount her real applications. Erased ads join the same NULL-org.nr residue (Erase() nulls "
                    + "the column; the snapshot holds no org.nr) — an undercount is safe, a name-guessed overcount is "
                    + "not, so the drop is documented rather than 'fixed' (#892 CTO R4, identical class to History).")),
            ["Jobbliggaren.Application.Applications.Queries.GetApplications.GetApplicationsQueryHandler.Handle"] =
                One(Any(
                    ".GroupJoin(db.JobAds).SelectMany(DefaultIfEmpty()) — LEFT join enriching the applications list.",
                    "The applications list is the user's own record; an archived/erased referenced ad must still "
                    + "appear (j == null → JobAdGuid null, deliberate). #805-3 class — gating on Active would drop "
                    + "applications whose ad has since been archived. An Erased tombstone keeps its row but the "
                    + "projection swaps summary identity to the applicant's own AdSnapshot — empty identity without "
                    + "one, the '[raderad]' sentinel never crosses the boundary (#892 CTO R1/R5).")),
            ["Jobbliggaren.Application.Applications.Queries.GetActivityReport.GetActivityReportQueryHandler.Handle"] =
                One(Any(
                    ".GroupJoin(db.JobAds).SelectMany(DefaultIfEmpty()) — LEFT join for the Arbetsförmedlingen activity report.",
                    "The activity report lists the user's applications for AF; a referenced ad's lifecycle must not "
                    + "filter it (an archived ad she applied to still counts as activity). No ad row → null, handled "
                    + "in the projection. An Erased ad's employer/title fall back to the applicant's AdSnapshot — the "
                    + "Art. 17(3)(e) retention ground finally READS the column it argued for (#892 CTO §14.3/R1); "
                    + "absent a snapshot the projection emits null, never the tombstone sentinel (R5).")),
            ["Jobbliggaren.Application.Applications.Queries.GetApplicationById.GetApplicationByIdQueryHandler.Handle"] =
                One(Any(
                    ".Where(j.Id == jobAdId).Select(JobAdSummaryDto(... j.Status.Value)) — the application's referenced ad.",
                    "The application detail shows the ad it references; the ad may be archived/erased and must still "
                    + "render (it projects Status.Value so the surface labels the lifecycle). #805-3 class. An Erased "
                    + "summary swaps identity to the aggregate's already-materialised AdSnapshot in plain C# after "
                    + "the query — empty identity without one (#892 CTO R1/R5); the by-id load stays status-agnostic.")),
            ["Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd.CreateApplicationFromJobAdCommandHandler.Handle"] =
                One(Any(
                    ".Where(j.Id == jobAdId).Select(JobAdSnapshotSource) — snapshot the ad's fields at apply time.",
                    "Snapshots the ad's fields into the application at apply time by id; the LOAD is status-agnostic "
                    + "(one projection, Status carried out) and an Archived ad still captures — a frozen legal record "
                    + "(ADR 0086). An ERASED tombstone refuses with 410 Gone BEFORE capture (#892 CTO R3): freezing "
                    + "''/'[raderad]' into a permanent snapshot was the write-path half of the #D3 defect.")),
            ["Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd.ArchiveExternalJobAdCommandHandler.Handle"] =
                One(Any(
                    ".Where(External.Source == source && External.ExternalId == externalId).FirstOrDefault() — load-for-mutate.",
                    "Loads the tracked aggregate by its ingest key to transition it to Archived; the load must be "
                    + "status-agnostic because the Archive() transition guard lives in the aggregate (which refuses an "
                    + "Erased ad — the gate belongs there, not in this lookup).")),
            ["Jobbliggaren.Application.JobAds.Jobs.Common.JobAdRefetchBackfillRunner.RunAsync"] =
                One(Any(
                    ".Where(nullColumnPredicate && External.Source == source).Select(ExternalId) — an id/external-id stream.",
                    "A generic backfill re-fetch runner selects ads missing a column (a null-column predicate) for "
                    + "re-import; status-agnostic — backfill operates on any ad missing the data, Active or Archived. "
                    + "Not a user-facing surface.")),
            ["Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsJob.RunAsync"] =
            [
                Any(".Where(ExtractedLexemes == null).Select(Id) — the id stream of never-extracted ads.",
                    "Selects ads never term-extracted (extracted_lexemes IS NULL) for backfill; status-agnostic — any "
                    + "ad missing extracted terms is re-extracted, Active or Archived. A housekeeping job, no surfacing."),
                Any("scopedDb.JobAds.FirstOrDefault(j.Id == id) — per-item load-for-mutate.",
                    "Loads each ad by id to re-extract and persist its terms; status-agnostic backfill load, one item "
                    + "at a time in its own scope. No lifecycle surfacing decision is made."),
            ],
            ["Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubJob.RunAsync"] =
            [
                Any(".Where(External != null && j.Status != Erased).Select(Id) — the id stream for the Art. 25 scrub.",
                    "Selects external ads for the recruiter-contact scrub backfill, excluding only the Erased tombstone "
                    + "(Status != Erased); admits Active AND Archived deliberately — an archived ad still holds recruiter "
                    + "contact text that must be scrubbed (#842 Tier A / #911)."),
                Any("scopedDb.JobAds.FirstOrDefault(j.Id == id) — per-item load-for-scrub.",
                    "Loads each ad by id to apply the contact scrub; status-agnostic within {Active, Archived} by the "
                    + "same #842 requirement — the scrub runs on any non-Erased ad, one item at a time in its own scope."),
            ],
            ["Jobbliggaren.Infrastructure.JobAds.RecruiterErasureMatchQuery.FindJobAdsAsync"] =
                One(Any(
                    "re-fetch .Where(typedIds.Contains(j.Id)) of the ids the raw match SQL returned.",
                    "Art. 17 erasure must load the matched ads regardless of lifecycle to erase recruiter PII from them; "
                    + "gating on Active would leave archived ads carrying her data unerased. The erased exclusion is "
                    + "applied by the raw match SQL upstream (see KnownNonReaches — the SQL predicate is scan-invisible).")),
            ["Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds.EraseRecruiterAdsCommandHandler.Handle"] =
            [
                Any("var jobAds = db.JobAds ... — the primary erasure read of matched ads.",
                    "Art. 17 erasure loads ads carrying the recruiter's PII regardless of status — an archived ad still "
                    + "holds her data and must be erased. Gating on Active would leave her data behind on archived ads."),
                Any("var stragglers = db.JobAds ... — a second-pass load of any remaining matched ads.",
                    "The straggler pass loads any remaining matched ads to complete the erasure in the same request; "
                    + "status-agnostic by the same Art. 17 requirement (every carrier of her data is reached)."),
            ],
            ["Jobbliggaren.Infrastructure.JobAds.JobAdEmployerReader.GetOrganizationNumbersByJobAdIdsAsync"] =
                One(Any(
                    ".FromSql(id = ANY(ids)).Select(org.nr) — resolve org.nr for a given set of ad ids.",
                    "Resolves org.nr for already-resolved ad ids the caller supplies; status-agnostic — the org.nr is "
                    + "read for whatever ids are passed, regardless of the ad's lifecycle. The FromSql fragment carries "
                    + "no status predicate (id membership only).")),
            ["Jobbliggaren.Infrastructure.JobAds.EmployerDisambiguationQuery.SearchAsync"] =
                One(Any(
                    ".Where(org.nr != null && ILike(Company.Name, pattern)).GroupBy(org.nr) — employer disambiguation.",
                    "Resolves an employer (org.nr) by a company-name search for company-following (#560); status-agnostic "
                    + "by design — an employer exists to be followed whether or not its current ads are Active, so an "
                    + "employer with only archived ads is still offered. Confirmed product decision (Klas, 2026-07-17): "
                    + "following an employer is a bet on its FUTURE ads, so the picker deliberately offers employers "
                    + "whose current ads are all archived.")),
            ["Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches.ListCompanyWatchesQueryHandler.Handle"] =
            [
                Any("pnrShapedAdOrgNrs: .Where(orgNr Length==10 && 3rd digit '0'/'1').Select(orgNr).Distinct() — the bounded pnr-shaped ad set used to resolve an enskild-firma watch HMAC token back to its plaintext org.nr (#544, ADR 0090 D5); only runs when the user holds ≥1 enskild follow.",
                    "Status-agnostic BY DESIGN — parity the name lookup below: a followed company keeps its name whether or not its ads are Active, so the token must resolve for an archived-only enskild firma too. The #447/#452 counts apply their own Active gate downstream."),
                Any("nameByOrgNr: .Where(orgNrs.Contains(orgNr)).Select(orgNr, Company.Name).Distinct() — org.nr → name lookup.",
                    "Builds an org.nr → company-name display lookup across the followed employers; status-agnostic — a "
                    + "company keeps its name whether or not its ads are Active, and the watch list must show the name even "
                    + "for a company that currently has only archived ads."),
                Active("activeAdCountByOrgNr: .Where(orgNrs.Contains(orgNr) && j.Status == Active).GroupBy(orgNr) — active-ad count per followed company."),
            ],
            ["Jobbliggaren.Infrastructure.Matching.MatchScorer.ScoreAsync"] =
                One(Any(
                    ".Where(j.Id == jobAdId && j.Status != Erased).Select(AdFacetRow) — single-ad facet read for the detail path.",
                    "Admits Active AND Archived and denies ONLY the tombstone (#885) — the same deny-list form, for the same "
                    + "requirement, as SaveJobAdCommandHandler. Not Active-gated (#864 D4 / PR-B B3): the detail page must "
                    + "still score and explain an archived ad (#805-3); its batch twin ScoreBatchAsync gates Active. The "
                    + "Erased exclusion MIRRORS GetJobAdQueryHandler's 410 — Erase() keeps the *_concept_id facets this "
                    + "method reads, so an ungated tombstone scores a real grade off live facets. THIS GATE IS BOUND TO "
                    + "THAT ONE: if the detail page's 410 rule changes, this changes with it.")),
            ["Jobbliggaren.Infrastructure.Matching.MatchScorer.ScoreFullAsync"] =
                One(Any(
                    ".Where(j.Id == jobAdId && j.Status != Erased).Select(AdFullRow) — single-ad full read for the detail path.",
                    "Same rule as ScoreAsync — the single family publishes ONE lifecycle contract: Active AND Archived are "
                    + "scored (#805-3, the detail page explains an archived match), the Erased tombstone is not (#885, "
                    + "mirroring GetJobAdQueryHandler's 410). Its batch twin ScoreFullBatchAsync gates Active. Bound to "
                    + "the detail page's 410 rule.")),
            ["Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail.GetJobAdMatchDetailQueryHandler.Handle"] =
                One(Any(
                    ".Where(j.Id == id).Select(j.Status.Value) — the #885 lifecycle pre-check, before the scorer runs.",
                    "Reads the status ALONE to answer an erased ad with 410 Gone (the neutral body /job-ads/{id} emits for "
                    + "the same row) — Infrastructure cannot express Gone, so the response decision lives here (§2.1) while "
                    + "the scorer holds the port invariant. Deny-list (!= Erased): admits Active AND Archived deliberately, "
                    + "because the modal explains an archived match (#805-3). THIS GATE IS BOUND TO GetJobAdQueryHandler's "
                    + "410 rule — this site OWNS the response, so it is where the binding matters most: if that rule "
                    + "changes, this changes with it. A row absent here is NOT decided here — it falls through to the "
                    + "scorer's NotFoundException → 404, the pre-existing missing-ad mechanism.")),

            // ════════════════════════════ WritePath — mutates / inserts ═════════════════════════════
            ["Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds.ExpireJobAdsJob.RunAsync"] =
                One(Write(
                    ".Where(j.Status == Active && ExpiresAt < now).ExecuteUpdateAsync(Status → Archived, Contacts → null).",
                    "Bulk archival writer (Hangfire cron). ALLOW-list (== Active), never != Archived: Erase() does not "
                    + "touch ExpiresAt, so a deny-list would re-stamp an expired Erased tombstone to Archived, bypass the "
                    + "aggregate Archive() guard, and un-key UpdateFromSource's re-import refusal (#842 / CTO 2026-07-16 "
                    + "B7). One of three archival writers that also clear job_ads.contacts (#911).")),
            ["Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads.PurgeStaleRawPayloadsJob.RunAsync"] =
                One(Write(
                    ".Where(RawPayload != null && PublishedAt < cutoff).ExecuteUpdateAsync(RawPayload → null).",
                    "Retention writer nulling raw_payload past the horizon; deliberately status-AGNOSTIC (no Status "
                    + "predicate) — the raw payload is purged on every aged ad, Active or Archived. It mutates a "
                    + "non-lifecycle column, so it never reads or writes Status.")),
            ["Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker.ArchiveJobAdsWithMissCountAtLeastAsync"] =
                One(Write(
                    ".Where(j.Status == Active && External... && EXISTS miss >= threshold).ExecuteUpdateAsync(Status → Archived, Contacts → null).",
                    "The PRIMARY bulk archival writer. ALLOW-list (== Active), never != Archived — the same erased-tombstone "
                    + "resurrection hazard as ExpireJobAdsJob (#842 / CTO 2026-07-16 B8g). Clears job_ads.contacts alongside "
                    + "the status flip (#911).")),
            ["Jobbliggaren.Application.JobAds.Commands.CreateJobAd.CreateJobAdCommandHandler.Handle"] =
                One(Write(
                    "db.JobAds.Add(jobAdResult.Value) — insert a manual ad.",
                    "Inserts a new (manual) JobAd via the aggregate, which starts Active by construction; no read and no "
                    + "query predicate — the lifecycle is set by the aggregate constructor, not by a Status filter.")),
            ["Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd.UpsertExternalJobAdCommandHandler.Handle"] =
            [
                Write("db.JobAds.Add(newJobAd) — insert on upsert-miss.",
                    "Inserts a new external ad when no existing row matches its ingest key; the aggregate starts Active by "
                    + "construction. No Status predicate — the insert sets the lifecycle."),
                Any(".Where(External.Source == source && External.ExternalId == externalId).FirstOrDefault() — the upsert lookup.",
                    "Finds an existing external ad by its ingest key to update it; status-agnostic — an upsert must find and "
                    + "refresh an ad whatever its current status (re-activation logic lives in the aggregate's "
                    + "UpdateFromSource, which refuses an Erased ad). Not a user surface."),
            ],
        };

    /// <summary>
    /// The raw-SQL reads of <c>job_ads</c> this IL scan is STRUCTURALLY blind to — the lifecycle
    /// predicate lives in a SQL string and emits no <c>get_JobAds</c> instruction.
    /// <see cref="JobAdLifecycleReadRegistryTests"/> reflects each named method and requires it to
    /// still exist, so the non-reach is a pinned artifact (a rename breaks the build and forces a
    /// re-examination), not a hopeful comment. Reach honesty, R4 — mirrors
    /// <c>ErasureCascadeRegistry</c>'s stated port-SQL-body non-reach.
    /// </summary>
    public static IReadOnlyList<RawSqlNonReach> KnownNonReaches { get; } =
    [
        new("Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker", "ApplyAsync",
            "The miss-count increment runs a raw NpgsqlCommand (WITH missing AS SELECT j.external_id FROM "
            + "job_ads j WHERE j.status = 'Active' ...) — the lifecycle gate is in the SQL string, not a "
            + "get_JobAds call, so the IL scan cannot see it."),
        new("Jobbliggaren.Infrastructure.JobAds.RecruiterErasureMatchQuery", "FindJobAdsAsync",
            "The Art. 17 match runs db.Database.SqlQuery('... FROM job_ads WHERE status <> {erased} ...'); the "
            + "erased exclusion is in the SQL string. (The subsequent re-fetch on the returned ids DOES call "
            + "get_JobAds and IS classified in Sites above.)"),
    ];
}
