using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedJobAds.Events;

namespace Jobbliggaren.Domain.SavedJobAds;

/// <summary>
/// Aggregate root — en bokmärkt jobbannons per JobSeeker (F6 P5 Punkt 2 Del A).
/// Refererar JobSeeker + JobAd endast via strongly-typed ID (CLAUDE.md §2.2).
/// Paritet <see cref="Jobbliggaren.Domain.RecentJobSearches.RecentJobSearch"/>:
/// hard-delete (ingen <c>DeletedAt</c>), UNIQUE(JobSeekerId, JobAdId) bär
/// idempotens-invarianten, factory <see cref="Save"/> är enda skapelseväg.
///
/// <para>
/// <b>Invarianter:</b>
/// </para>
/// <list type="number">
/// <item><b>Per-user idempotens:</b> UNIQUE(JobSeekerId, JobAdId) — samma annons
/// får sparas högst en gång av samma seeker. Tonderas av Capturer-impl via
/// ON CONFLICT-pattern (ADR 0032 §5) eller upstream NotFoundException-guard.</item>
/// <item><b>Cross-aggregate-disciplin (ADR 0048 Beslut d):</b> SavedJobAd
/// refererar JobAd via JobAdId-soft-reference (ingen DB-FK enligt ADR 0011
/// strongly-typed-ID-mönstret). Cascade-rensning vid JobSeeker hard-delete
/// sker explicit i <c>AccountHardDeleter</c> (ADR 0024 amend).</item>
/// </list>
/// </summary>
public sealed class SavedJobAd : AggregateRoot<SavedJobAdId>
{
    public JobSeekerId JobSeekerId { get; private set; }
    public JobAdId JobAdId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core constructor
    private SavedJobAd() { }

    private SavedJobAd(
        SavedJobAdId id,
        JobSeekerId jobSeekerId,
        JobAdId jobAdId,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        JobAdId = jobAdId;
        CreatedAt = now;
    }

    /// <summary>
    /// Skapa en bokmärkning av <paramref name="jobAdId"/> för
    /// <paramref name="jobSeekerId"/>. Idempotens enforce:as på persistens-yta
    /// via UNIQUE-index — anroparen ska guarda mot dubbel-spara (ON CONFLICT
    /// eller pre-check) innan denna factory anropas.
    /// </summary>
    public static SavedJobAd Save(
        JobSeekerId jobSeekerId,
        JobAdId jobAdId,
        DateTimeOffset now)
    {
        var id = SavedJobAdId.New();
        var aggregate = new SavedJobAd(id, jobSeekerId, jobAdId, now);
        aggregate.RaiseDomainEvent(new JobAdSavedDomainEvent(id, jobSeekerId, jobAdId, now));
        return aggregate;
    }

    /// <summary>
    /// Raisar unsaved-event innan persistens-DELETE. Kallas i handler innan
    /// <c>db.SavedJobAds.Remove(saved)</c> så audit/domain-event-pipeline kan
    /// observera radering. EF Core skickar inte domain-events vid Remove.
    /// </summary>
    public void Unsave(DateTimeOffset now)
    {
        RaiseDomainEvent(new JobAdUnsavedDomainEvent(Id, JobSeekerId, JobAdId, now));
    }
}
