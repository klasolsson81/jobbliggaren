using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches.Events;

namespace Jobbliggaren.Domain.SavedSearches;

/// <summary>
/// Aggregate root — en sparad jobbsökning som tillhör en JobSeeker.
/// Refererar JobSeeker endast via strongly-typed ID (CLAUDE.md §2.2).
/// run-semantik är query, last_run_at-skrivlogik tillhör Fas 5 (ADR 0039
/// Beslut 2) — därav ingen MarkRun-metod i Fas 2.
/// </summary>
public sealed class SavedSearch : AggregateRoot<SavedSearchId>
{
    public const int NameMaxLength = 120;

    public JobSeekerId JobSeekerId { get; private set; }
    public string Name { get; private set; } = null!;
    public SearchCriteria Criteria { get; private set; } = null!;
    public bool NotificationEnabled { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
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
