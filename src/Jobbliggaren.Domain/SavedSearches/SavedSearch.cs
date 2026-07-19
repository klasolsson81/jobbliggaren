using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches.Events;

namespace Jobbliggaren.Domain.SavedSearches;

/// <summary>
/// Aggregate root — en sparad jobbsökning som tillhör en JobSeeker.
/// Refererar JobSeeker endast via strongly-typed ID (CLAUDE.md §2.2).
/// run-semantik är query; last_run_at (email-fasens SCAN-mark) tillhör Fas 5
/// (ADR 0039 Beslut 2) — därav fortfarande ingen MarkRun-metod. #312 lägger till
/// ResultsSeenAt (USER-läs-mark) + MarkResultsSeen för in-app "nya träffar"-räkningen:
/// en DISTINKT axel (ADR 0115), aldrig sammanslagen med last_run_at.
/// </summary>
public sealed class SavedSearch : AggregateRoot<SavedSearchId>
{
    public const int NameMaxLength = 120;

    public JobSeekerId JobSeekerId { get; private set; }
    public string Name { get; private set; } = null!;
    public SearchCriteria Criteria { get; private set; } = null!;
    public bool NotificationEnabled { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }

    // #312 — per-search USER-read watermark: how far the user has SEEN this saved
    // search's results (drives the in-app "N nya träffar sedan senast" count via
    // JobAd.CreatedAt > this). The exact sibling of JobSeeker.LastSeenMatchesAt /
    // LastSeenFollowedAdsAt. DELIBERATELY DISTINCT from LastRunAt: that is the
    // deferred email-phase SCAN high-water-mark (ADR 0039 Beslut 2, still unwritten
    // in v1); this is the USER-read mark. Merging the two would break either the
    // future scan's idempotency or the unread count (same NEVER-MERGE rationale as
    // JobSeeker.LastMatchScanAt vs LastSeenMatchesAt). null = never seen (a fresh
    // search inits it to its creation time in the ctor; the #312 migration backfills
    // existing rows to now() so no historical backlog lights up as "new").
    public DateTimeOffset? ResultsSeenAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private SavedSearch() { }

    private SavedSearch(
        SavedSearchId id,
        JobSeekerId jobSeekerId,
        string name,
        SearchCriteria criteria,
        bool notificationEnabled,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        Name = name;
        Criteria = criteria;
        NotificationEnabled = notificationEnabled;
        CreatedAt = now;
        UpdatedAt = now;
        // #312 — a fresh search baselines its seen-watermark at creation: only ads
        // ingested AFTER it exists count as "new" (never the historical backlog).
        ResultsSeenAt = now;
    }

    public static Result<SavedSearch> Create(
        JobSeekerId jobSeekerId,
        string? name,
        SearchCriteria criteria,
        bool notificationEnabled,
        IDateTimeProvider clock)
    {
        if (jobSeekerId == default)
            return Result.Failure<SavedSearch>(DomainError.Validation(
                "SavedSearch.JobSeekerIdRequired", "JobSeekerId krävs."));

        if (criteria is null)
            return Result.Failure<SavedSearch>(DomainError.Validation(
                "SavedSearch.CriteriaRequired", "Sökkriterier krävs."));

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
            return Result.Failure<SavedSearch>(nameResult.Error);

        var now = clock.UtcNow;
        var id = SavedSearchId.New();
        var savedSearch = new SavedSearch(
            id, jobSeekerId, name!.Trim(), criteria, notificationEnabled, now);
        savedSearch.RaiseDomainEvent(
            new SavedSearchCreatedDomainEvent(id, jobSeekerId, name.Trim(), now));
        return Result.Success(savedSearch);
    }

    /// <summary>
    /// Creates a SavedSearch from the user's CONFIRMED CV-derived occupation selection (ADR 0040
    /// Beslut 4 — the user confirms; the engine never auto-creates). Identical to <see cref="Create"/>
    /// plus a provenance event recording the source CV. There is NO stored DerivedFromResumeId:
    /// ADR 0040 Beslut 3 defers that column to a future supersession-ADR, so the provenance rides
    /// on the event only (no migration — parity with <c>Resume.CreateFromParsed</c>, STEG A). The
    /// confirmed ssyk-4 ids are plain client input on <paramref name="criteria"/>, never the
    /// deriver's result — so the bearing invariant (no auto-create from derivation output) holds.
    /// </summary>
    public static Result<SavedSearch> CreateFromResume(
        JobSeekerId jobSeekerId,
        string? name,
        SearchCriteria criteria,
        bool notificationEnabled,
        Guid? sourceParsedResumeId,
        IDateTimeProvider clock)
    {
        var result = Create(jobSeekerId, name, criteria, notificationEnabled, clock);
        if (result.IsFailure)
            return result;

        result.Value.RaiseDomainEvent(new SavedSearchDerivedFromResumeDomainEvent(
            result.Value.Id, jobSeekerId, sourceParsedResumeId, clock.UtcNow));
        return result;
    }

    public Result Rename(string? name, IDateTimeProvider clock)
    {
        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
            return nameResult;

        Name = name!.Trim();
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new SavedSearchRenamedDomainEvent(Id, Name, clock.UtcNow));
        return Result.Success();
    }

    public Result UpdateCriteria(SearchCriteria criteria, IDateTimeProvider clock)
    {
        if (criteria is null)
            return Result.Failure(DomainError.Validation(
                "SavedSearch.CriteriaRequired", "Sökkriterier krävs."));

        Criteria = criteria;
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    public void SetNotification(bool enabled, IDateTimeProvider clock)
    {
        if (NotificationEnabled == enabled) return;

        NotificationEnabled = enabled;
        UpdatedAt = clock.UtcNow;
    }

    /// <summary>
    /// #312 — advances the per-search USER-read watermark to <paramref name="seenThrough"/>:
    /// the max <c>JobAd.CreatedAt</c> the user actually saw when they viewed this saved
    /// search's results. The exact sibling of <see cref="JobSeeker.SetLastSeenMatches"/> /
    /// <see cref="JobSeeker.SetLastSeenFollowedAds"/>: NOT clock-now — an ad ingested between
    /// the user's fetch and this call has <c>CreatedAt &gt; seenThrough</c>, so it stays above
    /// the watermark and is still flagged "ny" next visit (clock-now would swallow it silently,
    /// #477/#759). Monotonic (a stale or out-of-order call never rewinds it); a future-dated
    /// <paramref name="seenThrough"/> is CLAMPED to now so a bad client clock can never run the
    /// watermark past reality (the aggregate guards its own invariant, CLAUDE.md §2.2). Raises no
    /// domain event — a read-watermark advance has no reactive consumer (parity the JobSeeker
    /// seen-marks). DISTINCT from the deferred <c>LastRunAt</c> scan mark (ADR 0039 Beslut 2).
    /// </summary>
    public void MarkResultsSeen(DateTimeOffset seenThrough, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;

        if (seenThrough > now)
            seenThrough = now;

        if (ResultsSeenAt is { } current && seenThrough <= current)
            return;

        ResultsSeenAt = seenThrough;
        UpdatedAt = now;
    }

    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;

        DeletedAt = clock.UtcNow;
        RaiseDomainEvent(new SavedSearchDeletedDomainEvent(Id, JobSeekerId, clock.UtcNow));
    }

    private static Result ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(DomainError.Validation(
                "SavedSearch.NameRequired", "Namn är obligatoriskt."));

        if (name.Trim().Length > NameMaxLength)
            return Result.Failure(DomainError.Validation(
                "SavedSearch.NameTooLong", $"Namn får vara max {NameMaxLength} tecken."));

        return Result.Success();
    }
}
