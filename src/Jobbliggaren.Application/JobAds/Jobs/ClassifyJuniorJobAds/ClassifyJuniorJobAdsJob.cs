using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;

/// <summary>Per-tick counts. Never an ad id, title or employer.</summary>
public sealed record ClassifyJuniorJobAdsCounts(int Junior, int NotJunior, int Unclear, int Refused, int Failed)
{
    public static ClassifyJuniorJobAdsCounts None { get; } = new(0, 0, 0, 0, 0);

    public int Total => Junior + NotJunior + Unclear + Refused + Failed;
}

/// <summary>
/// #2264 (ADR 0187) — the one writer of the junior verdict. Level-triggered, like the company-watch sweep: the committed
/// state is the queue. An ad is selected while it is listed and in an enabled field's groups and has no assessment whose
/// stamped fingerprint equals the ad's input fingerprint under the current rules. It reads a narrow projection of the
/// ad, never the entity (no raw payload, contacts or organisation number), classifies it in Application, and creates or
/// re-classifies the ad's <see cref="JobAdJuniorAssessment"/>, stamping the fingerprint it read with the text. It never
/// writes <c>job_ads</c> and never touches an admin's decision.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounded.</b> Rounds of <see cref="ClassifyJuniorJobAdsOptions.BatchSize"/> re-select the newest stale ads (no
/// offset: the predicate shrinks by its own work) until nothing is stale or the time budget is spent; the next tick
/// continues. An ad that fails is skipped for the rest of the tick and retried on the next, so one bad ad can neither
/// stall the sweep nor spin it.
/// </para>
/// <para>
/// <b>An erased ad never keeps an assessment.</b> The erasure command deletes the ad's assessment in its own unit of
/// work, but a sweep that read the ad before the erasure committed can write one after it. So every tick begins and
/// ends by deleting the assessments of erased ads: such a row lives at most until the end of the tick that wrote it,
/// or of the next one, and never longer.
/// </para>
/// <para>
/// <b>Logs counts only</b>, and only for a tick that did something; never an id, a title, an employer or a matched span.
/// </para>
/// </remarks>
public sealed partial class ClassifyJuniorJobAdsJob(
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    IJuniorFieldCatalog catalog,
    IDateTimeProvider clock,
    IOptions<ClassifyJuniorJobAdsOptions> options,
    ILogger<ClassifyJuniorJobAdsJob> logger)
{
    public async Task<ClassifyJuniorJobAdsCounts> RunAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (!o.Enabled)
            return ClassifyJuniorJobAdsCounts.None;

        var fields = await catalog.ResolveEnabledAsync(cancellationToken);
        if (fields.Count == 0)
            return ClassifyJuniorJobAdsCounts.None;

        var purged = await PurgeErasedAsync(cancellationToken);

        var groups = JuniorMembership.GroupsOf(fields).ToList();
        var versions = fields.Select(f => f.Rules.Version).ToList();
        var classifier = new JuniorFieldClassifier(fields);
        var deadline = clock.UtcNow.AddSeconds(o.TimeBudgetSeconds);
        var skippedThisTick = new HashSet<JobAdId>();
        var assessments = db.JuniorAssessments;

        int junior = 0, notJunior = 0, unclear = 0, refused = 0, failed = 0;

        while (clock.UtcNow < deadline)
        {
            var now = clock.UtcNow;
            var candidates = await db.JobAds
                .AsNoTracking()
                .Where(JobAdPublication.IsListed(now))
                .Where(j => j.OccupationGroupConceptId != null && groups.Contains(j.OccupationGroupConceptId))
                .Where(j => !assessments.Any(a => a.Id == j.Id
                                                  && a.ClassifiedFingerprint == j.JuniorInputFingerprint
                                                  && versions.Contains(a.RuleSetVersion)))
                .OrderByDescending(j => j.PublishedAt)
                .ThenBy(j => j.Id)
                .Select(j => new AdToClassify(
                    j.Id, j.External!.ExternalId, j.Title, j.Description, j.OccupationGroupConceptId,
                    j.ExperienceRequired, j.JuniorInputFingerprint))
                .Take(o.BatchSize + skippedThisTick.Count)
                .ToListAsync(cancellationToken);

            var batch = candidates.Where(ad => !skippedThisTick.Contains(ad.Id)).Take(o.BatchSize).ToList();
            if (batch.Count == 0)
                break;

            foreach (var ad in batch)
            {
                try
                {
                    var classification = classifier.Classify(
                        ad.OccupationGroupConceptId,
                        new JuniorClassificationInput(ad.ExternalId, ad.Title, ad.Description, ad.ExperienceRequired));
                    if (classification is null)
                    {
                        refused++;
                        skippedThisTick.Add(ad.Id);
                        continue;
                    }

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var scopedDb = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                    var assessment = await scopedDb.JuniorAssessments.FirstOrDefaultAsync(a => a.Id == ad.Id, cancellationToken);
                    if (assessment is null)
                        scopedDb.JuniorAssessments.Add(
                            JobAdJuniorAssessment.Classify(ad.Id, classification, ad.Fingerprint, clock));
                    else
                        assessment.Reclassify(classification, ad.Fingerprint, clock);

                    await scopedDb.SaveChangesAsync(cancellationToken);

                    switch (classification.Verdict)
                    {
                        case JuniorVerdict.Junior: junior++; break;
                        case JuniorVerdict.NotJunior: notJunior++; break;
                        default: unclear++; break;
                    }

                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    skippedThisTick.Add(ad.Id);
                    LogItemFailed(logger, ex.GetType().Name);
                }
            }
        }

        purged += await PurgeErasedAsync(cancellationToken);

        var counts = new ClassifyJuniorJobAdsCounts(junior, notJunior, unclear, refused, failed);
        if (counts.Total > 0 || purged > 0)
            LogTick(logger, junior, notJunior, unclear, refused, failed, purged);
        return counts;
    }

    // The assessments of erased ads: rows a sweep wrote after the erasure committed (see the remarks). Loaded and removed
    // through the change tracker, so a tick with nothing to purge writes nothing. A handful of rows at most.
    private async Task<int> PurgeErasedAsync(CancellationToken cancellationToken)
    {
        var onErasedAds = await db.JuniorAssessments
            .Where(a => db.JobAds.Any(j => j.Id == a.Id && j.Status == JobAdStatus.Erased))
            .ToListAsync(cancellationToken);
        if (onErasedAds.Count == 0)
            return 0;

        db.JuniorAssessments.RemoveRange(onErasedAds);
        await db.SaveChangesAsync(cancellationToken);
        return onErasedAds.Count;
    }

    private sealed record AdToClassify(
        JobAdId Id,
        string ExternalId,
        string Title,
        string Description,
        string? OccupationGroupConceptId,
        bool? ExperienceRequired,
        string? Fingerprint);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Junior classification: {Junior} junior, {NotJunior} not junior, {Unclear} unclear, {Refused} refused, {Failed} failed, {Purged} purged from erased ads.")]
    private static partial void LogTick(
        ILogger logger, int junior, int notJunior, int unclear, int refused, int failed, int purged);

    // The exception's type only: a message or a stack could carry a fragment of the ad's text.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Junior classification of one ad failed ({ExceptionType}).")]
    private static partial void LogItemFailed(ILogger logger, string exceptionType);
}
