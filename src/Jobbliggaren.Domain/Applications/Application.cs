using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Domain.Applications;

public sealed class Application : AggregateRoot<ApplicationId>
{
    public JobSeekerId JobSeekerId { get; private set; }
    public JobAdId? JobAdId { get; private set; }
    public ManualPosting? ManualPosting { get; private set; }

    /// <summary>
    /// Frozen copy of the linked JobAd's text, captured at apply-time so the ad
    /// content survives the source ad being archived (issue #315, ADR 0086).
    /// Owned value object; present only on JobAd-linked applications
    /// (snapshot ⇒ <see cref="JobAdId"/>, enforced structurally by
    /// <see cref="CreateFromJobAd"/> — the only writer; <see cref="Create"/>
    /// never sets a snapshot). Minimised (its description dropped) on the user's
    /// terminal transition; see <see cref="TransitionTo"/>.
    /// </summary>
    public AdSnapshot? AdSnapshot { get; private set; }

    public string? CoverLetter { get; private set; }
    public ResumeVersionId? ResumeVersionId { get; private set; }
    public ApplicationStatus Status { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset LastStatusChangeAt { get; private set; }

    /// <summary>
    /// The moment the application was first submitted to the employer (the
    /// "ansökt"/applied date), stamped on the first transition into
    /// <see cref="ApplicationStatus.Submitted"/> and never overwritten by later
    /// transitions or a Ghosted→Submitted reactivation. This is a first-class
    /// domain fact (BUILD.md §5.3 — the canonical aggregate specifies
    /// <c>AppliedAt</c>): unlike <see cref="LastStatusChangeAt"/>, which every
    /// transition overwrites, the apply date is stable. Nullable here (not the
    /// §5.3 non-null) because the shipped lifecycle creates in Draft, so a
    /// not-yet-submitted application has no apply date; the non-null is the
    /// post-Submit invariant. Used by the AF activity report (issue #316).
    /// </summary>
    public DateTimeOffset? AppliedAt { get; private set; }

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
        ManualPosting? manualPosting,
        AdSnapshot? adSnapshot,
        DateTimeOffset now) : base(id)
    {
        JobSeekerId = jobSeekerId;
        JobAdId = jobAdId;
        ManualPosting = manualPosting;
        AdSnapshot = adSnapshot;
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
        ManualPosting? manualPosting,
        IDateTimeProvider clock)
    {
        if (jobSeekerId == default)
            return Result.Failure<Application>(
                DomainError.Validation("Application.JobSeekerIdRequired", "JobSeekerId krävs."));

        if (coverLetter is not null && coverLetter.Length > 10_000)
            return Result.Failure<Application>(
                DomainError.Validation("Application.CoverLetterTooLong", "Personligt brev får vara max 10 000 tecken."));

        // Aggregat-invariant (ADR 0048 Beslut d / datamodell-architect A3):
        // en ansökan är ANTINGEN JobAd-kopplad ELLER manuell, aldrig båda.
        // (null, null) förblir Success — degenererat cover-letter-only-beteende
        // bevaras (regressionsskydd).
        if (jobAdId is not null && manualPosting is not null)
            return Result.Failure<Application>(
                DomainError.Validation(
                    "Application.JobAdAndManualMutuallyExclusive",
                    "En ansökan kan inte vara både kopplad till en annons och manuellt angiven."));

        var now = clock.UtcNow;
        var id = ApplicationId.New();
        var application = new Application(id, jobSeekerId, jobAdId, coverLetter?.Trim(), manualPosting, adSnapshot: null, now);
        application.RaiseDomainEvent(
            new ApplicationCreatedDomainEvent(id, jobSeekerId, jobAdId, now));
        return Result.Success(application);
    }

    /// <summary>
    /// Creates a JobAd-linked application carrying a frozen <see cref="AdSnapshot"/>
    /// of the ad's text (issue #315, ADR 0086). Dedicated factory: this is the
    /// ONLY writer that sets a snapshot, and it always sets a <paramref name="jobAdId"/>,
    /// so the snapshot ⇒ JobAdId invariant holds structurally (symmetry with the
    /// <see cref="ManualPosting"/> XOR; a JobAd-linked application is never manual,
    /// so <see cref="ManualPosting"/> is always null here). Capture (loading the
    /// JobAd fields + resolving the ort name) happens upstream in the handler; the
    /// aggregate receives an already-built, validation-free snapshot value object.
    /// </summary>
    public static Result<Application> CreateFromJobAd(
        JobSeekerId jobSeekerId,
        JobAdId jobAdId,
        AdSnapshot adSnapshot,
        string? coverLetter,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(adSnapshot);

        if (jobSeekerId == default)
            return Result.Failure<Application>(
                DomainError.Validation("Application.JobSeekerIdRequired", "JobSeekerId krävs."));

        if (jobAdId == default)
            return Result.Failure<Application>(
                DomainError.Validation("Application.JobAdIdRequired", "JobAdId krävs."));

        if (coverLetter is not null && coverLetter.Length > 10_000)
            return Result.Failure<Application>(
                DomainError.Validation("Application.CoverLetterTooLong", "Personligt brev får vara max 10 000 tecken."));

        var now = clock.UtcNow;
        var id = ApplicationId.New();
        var application = new Application(
            id, jobSeekerId, jobAdId, coverLetter?.Trim(), manualPosting: null, adSnapshot, now);
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

        // Stamp the apply date on the FIRST submit only (idempotent). A
        // Ghosted→Submitted reactivation (ApplicationStatus.cs:47) finds
        // AppliedAt already set and does not re-stamp — AF reporting wants the
        // month you originally applied, not the month you re-opened a ghosted
        // thread (issue #316; senior-cto-advisor 2026-06-28 D3 amendment).
        if (target == ApplicationStatus.Submitted && AppliedAt is null)
            AppliedAt = clock.UtcNow;

        // Retention / GDPR data-minimisation (issue #315, ADR 0086 D3, GDPR Art.
        // 5(1)(c)): on the user's TERMINAL transition (Accepted/Rejected/
        // Withdrawn) only, drop the bulky preserved ad body, keeping the minimal
        // stats/identity metadata (title/employer/location/dates). NEVER cleared
        // because the source ad disappeared — that is exactly when it is needed.
        // Idempotent, mirroring the AppliedAt stamp above. Ghosted is not
        // terminal (reactivatable), so the body is never minimised there; and
        // terminal statuses have no outgoing transitions, so a re-activation
        // cannot regress an already-minimised snapshot.
        if (IsTerminal(target))
            AdSnapshot = AdSnapshot?.WithoutDescription();

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

    /// <summary>
    /// True unless the application has reached a terminal status
    /// (Accepted/Rejected/Withdrawn). Ghosted is reactivatable, so it remains
    /// attachable. Used by <see cref="AttachResumeVersion"/>; the symmetrical
    /// delete-guard (BUILD §5.6) lists the three terminals explicitly in SQL
    /// because a SmartEnum property does not translate.
    /// </summary>
    public bool CanAttachResumeVersion() => !IsTerminal(Status);

    /// <summary>
    /// A terminal kanban status (Accepted/Rejected/Withdrawn) — no outgoing
    /// transitions (ApplicationStatus.cs). Distinct from
    /// <see cref="IsClosedForActivity"/>, which ALSO counts Ghosted (closed for
    /// follow-ups, but reactivatable, so NOT terminal). Single source for the
    /// three-terminal check reused by <see cref="CanAttachResumeVersion"/> and
    /// the snapshot-retention rule in <see cref="TransitionTo"/>.
    /// </summary>
    private static bool IsTerminal(ApplicationStatus status) =>
        status == ApplicationStatus.Accepted ||
        status == ApplicationStatus.Rejected ||
        status == ApplicationStatus.Withdrawn;

    /// <summary>
    /// Links the exact CV version used for this application (F4-11, BUILD §5.3).
    /// Replaceable while non-terminal (the "version used" is a single current
    /// fact, not an event log). Cross-user ownership is enforced upstream in the
    /// handler — the aggregate references the version by id only (CLAUDE.md §2.2).
    /// </summary>
    public Result AttachResumeVersion(ResumeVersionId versionId, IDateTimeProvider clock)
    {
        if (versionId == default)
            return Result.Failure(DomainError.Validation(
                "Application.ResumeVersionIdRequired", "ResumeVersionId krävs."));

        if (!CanAttachResumeVersion())
            return Result.Failure(DomainError.Validation(
                "Application.ResumeVersionAttachNotAllowed",
                "Det går inte att koppla en CV-version till en avslutad ansökan."));

        ResumeVersionId = versionId;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new ApplicationResumeVersionAttachedDomainEvent(
            Id, JobSeekerId, versionId, clock.UtcNow));
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

    public Result RecordFollowUpOutcome(
        FollowUpId followUpId,
        FollowUpOutcome outcome,
        IDateTimeProvider clock)
    {
        var followUp = _followUps.FirstOrDefault(
            f => f.Id == followUpId && f.DeletedAt is null);

        if (followUp is null)
            return Result.Failure(DomainError.NotFound(
                "Application.FollowUpNotFound", "Uppföljningen hittades inte."));

        var result = followUp.RecordOutcome(outcome, clock);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        RaiseDomainEvent(
            new FollowUpOutcomeRecordedDomainEvent(Id, followUpId, outcome, clock.UtcNow));
        return Result.Success();
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

    // Closed for follow-up activity: the three terminals PLUS Ghosted. Ghosted
    // is deliberately included here (no activity on a ghosted thread) but is NOT
    // terminal (it is reactivatable) — see IsTerminal (architect M3: do not let
    // IsTerminal swallow this intentionally wider scope).
    private bool IsClosedForActivity() =>
        IsTerminal(Status) || Status == ApplicationStatus.Ghosted;
}
