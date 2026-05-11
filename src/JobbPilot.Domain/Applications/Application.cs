using JobbPilot.Domain.Applications.Events;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Domain.Applications;

public sealed class Application : AggregateRoot<ApplicationId>
{
    public JobSeekerId JobSeekerId { get; private set; }
    public JobAdId? JobAdId { get; private set; }
    public string? CoverLetter { get; private set; }
    public ApplicationStatus Status { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset LastStatusChangeAt { get; private set; }
    public int GhostedThresholdDays { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private readonly List<FollowUp> _followUps = [];
    private readonly List<ApplicationNote> _notes = [];

    public IReadOnlyList<FollowUp> FollowUps => _followUps.AsReadOnly();
    public IReadOnlyList<ApplicationNote> Notes => _notes.AsReadOnly();

    // EF Core constructor
    private Application() { }

    private Application(
        ApplicationId id,
        JobSeekerId jobSeekerId,
        JobAdId? jobAdId,
        string? coverLetter,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        JobAdId = jobAdId;
        CoverLetter = coverLetter;
        Status = ApplicationStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
        LastStatusChangeAt = now;
        GhostedThresholdDays = 21;
    }

    public static Result<Application> Create(
        JobSeekerId jobSeekerId,
        JobAdId? jobAdId,
        string? coverLetter,
        IDateTimeProvider clock)
    {
        if (jobSeekerId == default)
            return Result.Failure<Application>(
                DomainError.Validation("Application.JobSeekerIdRequired", "JobSeekerId krävs."));

        if (coverLetter is not null && coverLetter.Length > 10_000)
            return Result.Failure<Application>(
                DomainError.Validation("Application.CoverLetterTooLong", "Personligt brev får vara max 10 000 tecken."));

        var now = clock.UtcNow;
        var id = ApplicationId.New();
        var application = new Application(id, jobSeekerId, jobAdId, coverLetter?.Trim(), now);
        application.RaiseDomainEvent(
            new ApplicationCreatedDomainEvent(id, jobSeekerId, jobAdId, now));
        return Result.Success(application);
    }

    public Result TransitionTo(ApplicationStatus target, IDateTimeProvider clock)
    {
        if (!Status.AllowedTransitions.Contains(target))
            return Result.Failure(DomainError.Validation(
                "Application.InvalidTransition",
                $"Övergång från {Status.Name} till {target.Name} är inte tillåten."));

        var previous = Status;
        Status = target;
        UpdatedAt = clock.UtcNow;
        LastStatusChangeAt = clock.UtcNow;
        RaiseDomainEvent(
            new ApplicationStatusTransitionedDomainEvent(Id, JobSeekerId, previous, target, clock.UtcNow));
        return Result.Success();
    }

    public Result MarkGhosted(IDateTimeProvider clock)
    {
        if (Status != ApplicationStatus.Submitted && Status != ApplicationStatus.Acknowledged)
            return Result.Success(); // idempotent — inget att göra

        var previous = Status;
        Status = ApplicationStatus.Ghosted;
        UpdatedAt = clock.UtcNow;
        LastStatusChangeAt = clock.UtcNow;
        RaiseDomainEvent(
            new ApplicationGhostedDomainEvent(Id, JobSeekerId, previous, clock.UtcNow));
        return Result.Success();
    }

    public Result<FollowUpId> AddFollowUp(
        FollowUpChannel channel,
        DateTimeOffset scheduledAt,
        string? note,
        IDateTimeProvider clock)
    {
        if (IsClosedForActivity())
            return Result.Failure<FollowUpId>(DomainError.Validation(
                "Application.FollowUpNotAllowed",
                "Det går inte att lägga till uppföljning på en avslutad ansökan."));

        var result = FollowUp.Create(channel, scheduledAt, note, clock);
        if (result.IsFailure)
            return Result.Failure<FollowUpId>(result.Error);

        _followUps.Add(result.Value);
        RaiseDomainEvent(new FollowUpAddedDomainEvent(Id, result.Value.Id, clock.UtcNow));
        return Result.Success(result.Value.Id);
    }

    public Result AddNote(string? content, IDateTimeProvider clock)
    {
        var result = ApplicationNote.Create(content, clock);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        _notes.Add(result.Value);
        RaiseDomainEvent(new ApplicationNotedDomainEvent(Id, result.Value.Id, clock.UtcNow));
        return Result.Success();
    }

    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;

        DeletedAt = clock.UtcNow;
        foreach (var followUp in _followUps) followUp.SoftDelete(clock);
        foreach (var note in _notes) note.SoftDelete(clock);
        RaiseDomainEvent(new ApplicationDeletedDomainEvent(Id, JobSeekerId, clock.UtcNow));
    }

    private bool IsClosedForActivity() =>
        Status == ApplicationStatus.Accepted ||
        Status == ApplicationStatus.Rejected ||
        Status == ApplicationStatus.Withdrawn ||
        Status == ApplicationStatus.Ghosted;
}
