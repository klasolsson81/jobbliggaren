using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.RecentJobSearches.Events;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Domain.RecentJobSearches;

/// <summary>
/// Aggregate root — en auto-fångad jobbsökning per JobSeeker, fångad vid
/// /jobb-sök (CTO 2026-05-20: pipeline-behavior post-handler). Refererar
/// JobSeeker endast via strongly-typed ID (CLAUDE.md §2.2). Skild från
/// <see cref="SavedSearch"/> (manuell spara) — auto-capture-semantik
/// (ADR 0060). Soft-delete används EJ; DELETE = hard-delete (auto-capture-
/// rader har ingen audit-trail-värdighet vs. användar-skapade SavedSearches).
///
/// <para>
/// <b>Invarianter:</b>
/// </para>
/// <list type="number">
/// <item><b>Identitet via FilterHash:</b> UNIQUE(JobSeekerId, FilterHash) på persistens-yta.
/// <see cref="FilterHashCalculator"/> är canonical-källan; Q/OccupationGroup/Municipality/
/// Region/EmploymentType/WorktimeExtent/SortBy är derivat av hash och får aldrig divergera
/// (Bump muterar dem ej).</item>
/// <item><b>Cap per seeker:</b> <see cref="MaxPerSeeker"/> — affärsregel, enforce:as i
/// <c>IRecentJobSearchCapturer</c>-implementationen (evict äldsta LastViewedAt vid
/// overflow). Konstanten deklareras i Domain (CLAUDE.md §5.1 — ingen magic number).</item>
/// <item><b>Kriterier:</b> <see cref="SearchCriteria"/>-invarianter (ADR 0042 Beslut B,
/// MaxConceptIds=400, regex, Q 2-100, tom-invariant, Relevance kräver Q) bärs av VO:t
/// — Capture tar redan-validerad criteria som parameter (DRY, Evans 2003 kap. 5).</item>
/// </list>
///
/// <para><b>Fas C2 (ADR 0067, CTO-dom (d) 2026-06-09):</b> occupation-name-
/// dimensionen (Ssyk) UTGICK — ersatt av OccupationGroup (ssyk-level-4) +
/// Municipality. Befintliga rader raderades i C2-migrationen (cache-data utan
/// audit-trail-värdighet, cap-20-eviction självåterbygger) i stället för
/// hash-versionering.</para>
/// </summary>
public sealed class RecentJobSearch : AggregateRoot<RecentJobSearchId>
{
    /// <summary>Max antal RecentJobSearches per JobSeeker (CTO 2026-05-20 Q3 villkor).
    /// Capturer evictar äldsta vid overflow innan ny rad skapas. Bär YAGNI-domen
    /// för Variant A (re-query per row) — utan cap växer N+1 obegränsat.</summary>
    public const int MaxPerSeeker = 20;

    public JobSeekerId JobSeekerId { get; private set; }
    public string FilterHash { get; private set; } = null!;
    public string? Q { get; private set; }

    private readonly List<string> _occupationGroup = [];
    public IReadOnlyList<string> OccupationGroup => _occupationGroup.AsReadOnly();

    private readonly List<string> _municipality = [];
    public IReadOnlyList<string> Municipality => _municipality.AsReadOnly();

    private readonly List<string> _region = [];
    public IReadOnlyList<string> Region => _region.AsReadOnly();

    // ADR 0067 Beslut 6 (Fas B2, 2026-06-12) — Klass 2-dimensioner. Persisteras
    // som text[]-kolumner (employment_type_list/worktime_extent_list) i samma
    // shadow-backing-field-mönster som de tre ovan; ingår i FilterHash → en
    // recent-rad reproducerar exakt sökningen inkl. Klass 2.
    private readonly List<string> _employmentType = [];
    public IReadOnlyList<string> EmploymentType => _employmentType.AsReadOnly();

    private readonly List<string> _worktimeExtent = [];
    public IReadOnlyList<string> WorktimeExtent => _worktimeExtent.AsReadOnly();

    public JobAdSortBy SortBy { get; private set; }
    public DateTimeOffset LastViewedAt { get; private set; }
    public int LastSeenCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core constructor
    private RecentJobSearch() { }

    private RecentJobSearch(
        RecentJobSearchId id,
        JobSeekerId jobSeekerId,
        string filterHash,
        SearchCriteria criteria,
        int currentCount,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        FilterHash = filterHash;
        Q = criteria.Q;
        _occupationGroup.AddRange(criteria.OccupationGroup);
        _municipality.AddRange(criteria.Municipality);
        _region.AddRange(criteria.Region);
        _employmentType.AddRange(criteria.EmploymentType);
        _worktimeExtent.AddRange(criteria.WorktimeExtent);
        SortBy = criteria.SortBy;
        LastViewedAt = now;
        LastSeenCount = currentCount;
        CreatedAt = now;
    }

    /// <summary>
    /// Skapar en ny RecentJobSearch för en specifik <paramref name="criteria"/>.
    /// <paramref name="criteria"/> ska vara redan-validerad via <see cref="SearchCriteria.Create"/>
    /// — Capture muterar inte criteria, bara projicerar fält + beräknar FilterHash.
    /// </summary>
    public static RecentJobSearch Capture(
        JobSeekerId jobSeekerId,
        SearchCriteria criteria,
        int currentCount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (currentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(currentCount),
                "currentCount får inte vara negativt.");

        var hash = FilterHashCalculator.Compute(criteria);
        var id = RecentJobSearchId.New();
        var aggregate = new RecentJobSearch(
            id, jobSeekerId, hash, criteria, currentCount, now);
        aggregate.RaiseDomainEvent(
            new RecentJobSearchCapturedDomainEvent(id, jobSeekerId, hash, now));
        return aggregate;
    }

    /// <summary>
    /// Bump existerande rad vid återbesök på samma filter (samma FilterHash).
    /// Uppdaterar bara <see cref="LastViewedAt"/> + <see cref="LastSeenCount"/> —
    /// criteria-fält + hash bevaras (de definierar radens identitet).
    /// </summary>
    public void Bump(int currentCount, DateTimeOffset now)
    {
        if (currentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(currentCount),
                "currentCount får inte vara negativt.");

        LastViewedAt = now;
        LastSeenCount = currentCount;
        RaiseDomainEvent(new RecentJobSearchBumpedDomainEvent(Id, JobSeekerId, now));
    }
}
