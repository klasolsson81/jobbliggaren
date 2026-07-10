using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers.Events;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Domain.JobSeekers;

public sealed class JobSeeker : AggregateRoot<JobSeekerId>
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public Preferences Preferences { get; private set; } = null!;

    // F4-12 (ADR 0076) — the user's STATED job-search preferences (desired
    // occupation-groups/regions/employment-types) that feed the deterministic
    // match score. Distinct concern from notification/locale Preferences (SRP).
    // Defaults to Empty so a freshly-registered seeker has a valid (empty) value.
    public MatchPreferences MatchPreferences { get; private set; } = MatchPreferences.Empty;

    public ResumeId? PrimaryResumeId { get; private set; }

    // ADR 0080 Vag 4 — two DISTINCT per-user watermarks (first-class columns, not jsonb —
    // both are hot-path-updated). LastMatchScanAt = how far the Worker has SCANNED (system,
    // advances atomically with each background-match scan, drives idempotency, Beslut 2).
    // LastSeenMatchesAt = how far the USER has READ (advances when the user views the
    // matches surface, drives the "nya sedan senaste besök" count, Beslut 6). They are NEVER
    // merged — merging breaks either idempotency or the unread-count. Both nullable
    // (null = never scanned / never seen).
    public DateTimeOffset? LastMatchScanAt { get; private set; }
    public DateTimeOffset? LastSeenMatchesAt { get; private set; }

    // #293 (ADR 0042 Beslut E amendment) — the user-read watermark for the /jobb surface:
    // how far the USER has SEEN the job list (advances on each /jobb page load). Drives the
    // per-user "Ny = arrived since your last visit" tag (NY = JobAd.CreatedAt > this). The
    // exact sibling of LastSeenMatchesAt for the matches surface — a separate per-user read
    // concern kept OFF the public JobAdDto projection (ADR 0063 Beslut b). null = never
    // visited (FE shows no NY on the first visit; that load establishes the baseline).
    public DateTimeOffset? LastSeenJobsAt { get; private set; }

    // ADR 0087 D5 (#311 PR-4) — the company-follow scan high-water-mark (a first-class column,
    // parity LastMatchScanAt; ADR 0087 D4). How far the CompanyWatchScanJob has scanned this user's
    // followed employers (advances atomically with each scan's FollowedCompanyAdHit inserts, drives
    // idempotency). A DISTINCT concern from LastMatchScanAt — the two scans (skill-match vs.
    // watched-org.nr) advance independently (never merged). null = never scanned.
    public DateTimeOffset? LastCompanyWatchScanAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private JobSeeker() { }

    private JobSeeker(
        JobSeekerId id,
        Guid userId,
        string displayName,
        Preferences preferences,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        DisplayName = displayName;
        Preferences = preferences;
        CreatedAt = createdAt;
    }

    public static Result<JobSeeker> Register(
        Guid userId,
        string? displayName,
        IDateTimeProvider clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<JobSeeker>(
                DomainError.Validation("JobSeeker.UserIdRequired", "UserId krävs."));

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<JobSeeker>(
                DomainError.Validation("JobSeeker.DisplayNameRequired", "Visningsnamn är obligatoriskt."));

        if (displayName.Length > 200)
            return Result.Failure<JobSeeker>(
                DomainError.Validation("JobSeeker.DisplayNameTooLong", "Visningsnamn får vara max 200 tecken."));

        var now = clock.UtcNow;
        var id = JobSeekerId.New();
        var jobSeeker = new JobSeeker(id, userId, displayName.Trim(), new Preferences(), now);
        jobSeeker.RaiseDomainEvent(
            new JobSeekerRegisteredDomainEvent(id, userId, displayName.Trim(), now));

        return Result.Success(jobSeeker);
    }

    public Result UpdateDisplayName(string? displayName, IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure(
                DomainError.Validation("JobSeeker.DisplayNameRequired", "Visningsnamn är obligatoriskt."));

        if (displayName.Length > 200)
            return Result.Failure(
                DomainError.Validation("JobSeeker.DisplayNameTooLong", "Visningsnamn får vara max 200 tecken."));

        DisplayName = displayName.Trim();
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    public void UpdatePreferences(Preferences preferences, IDateTimeProvider clock)
    {
        Preferences = preferences;
        UpdatedAt = clock.UtcNow;
    }

    /// <summary>
    /// ADR 0080 Vag 4 — sets the background-match notification consent (opt-in, GDPR Art.
    /// 6/7). Invariants: enabling stamps <see cref="Preferences.NotificationConsentAt"/>
    /// ONCE (immutable Art. 7(1) evidence) and clears the withdrawal; disabling (from
    /// enabled) stamps <see cref="Preferences.NotificationConsentWithdrawnAt"/> (Art. 7(3)
    /// revocation proof). Withdrawal stops dispatch immediately (the Worker filters on
    /// enabled AND withdrawn-null). Audit-logged via the pipeline.
    /// </summary>
    public void UpdateNotificationConsent(
        bool enabled, DigestCadence cadence, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        var consentAt = Preferences.NotificationConsentAt;
        var withdrawnAt = Preferences.NotificationConsentWithdrawnAt;

        if (enabled)
        {
            // Stamp the first-ever opt-in once (immutable); re-consent clears the withdrawal.
            consentAt ??= now;
            withdrawnAt = null;
        }
        else if (Preferences.BackgroundMatchNotificationsEnabled)
        {
            // Opt-out from an enabled state — record the revocation time (Art. 7(3)).
            withdrawnAt = now;
        }

        Preferences = Preferences with
        {
            BackgroundMatchNotificationsEnabled = enabled,
            DigestCadence = cadence,
            NotificationConsentAt = consentAt,
            NotificationConsentWithdrawnAt = withdrawnAt,
        };
        UpdatedAt = now;
    }

    /// <summary>
    /// ADR 0087 D5 (#311 PR-4) — sets the SEPARATE company-follow notification consent (opt-in,
    /// GDPR Art. 6/7). A DISTINCT processing purpose from background-match notifications, so it
    /// stamps its OWN Art. 7(1)/7(3) evidence pair independently: enabling stamps
    /// <see cref="Preferences.FollowedCompanyNotificationConsentAt"/> ONCE (immutable) and clears the
    /// withdrawal; disabling (from enabled) stamps
    /// <see cref="Preferences.FollowedCompanyNotificationConsentWithdrawnAt"/> (Art. 7(3) revocation).
    /// Withdrawal stops company-follow dispatch immediately (the digest filters on enabled AND
    /// withdrawn-null). The digest CADENCE is shared with background-match notifications (ADR 0087
    /// D2) — this method deliberately does NOT touch <see cref="Preferences.DigestCadence"/>.
    /// <para>
    /// <b>Write API deferred (ADR 0087 D5, senior-cto-advisor 2026-07-01):</b> PR-4 ships this domain
    /// method + persistence so the scan/dispatch can READ the flag; the Application command + Api
    /// endpoint to TOGGLE it lands with the #408-blocked FE PR (mirrors PR-2b's endpoint deferral),
    /// where the mandatory security-auditor consent gate has a real surface. Default OFF ⇒ a
    /// not-yet-settable flag is inert (nobody silently opted in), not dishonest.
    /// </para>
    /// </summary>
    public void UpdateFollowedCompanyNotificationConsent(bool enabled, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        var consentAt = Preferences.FollowedCompanyNotificationConsentAt;
        var withdrawnAt = Preferences.FollowedCompanyNotificationConsentWithdrawnAt;

        if (enabled)
        {
            // Stamp the first-ever opt-in once (immutable Art. 7(1)); re-consent clears the withdrawal.
            consentAt ??= now;
            withdrawnAt = null;
        }
        else if (Preferences.FollowedCompanyNotificationsEnabled)
        {
            // Opt-out from an enabled state — record the revocation time (Art. 7(3)).
            withdrawnAt = now;
        }

        Preferences = Preferences with
        {
            FollowedCompanyNotificationsEnabled = enabled,
            FollowedCompanyNotificationConsentAt = consentAt,
            FollowedCompanyNotificationConsentWithdrawnAt = withdrawnAt,
        };
        UpdatedAt = now;
    }

    /// <summary>
    /// ADR 0080 Vag 4 (Beslut 2) — advances the Worker's scan high-water-mark. Set ATOMICALLY
    /// with the match upsert in the SAME unit of work (hard invariant — else a crash mid-scan
    /// either re-notifies or drops matches). Monotonic: never moves backwards.
    /// </summary>
    public void AdvanceMatchScan(DateTimeOffset scannedThrough, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;

        // Defense-in-depth: a future-dated scannedThrough (e.g. a Worker window-calc bug)
        // would permanently skip all ads up to that bad value (matches silently dropped, not
        // re-notified). The aggregate protects its own invariant (CLAUDE §2.2) — clamp to now
        // so the watermark can never run ahead of reality.
        if (scannedThrough > now)
            scannedThrough = now;

        if (LastMatchScanAt is { } current && scannedThrough <= current)
            return;

        LastMatchScanAt = scannedThrough;
        UpdatedAt = now;
    }

    /// <summary>
    /// ADR 0087 D5 (#311 PR-4) — advances the company-follow scan high-water-mark. Set ATOMICALLY
    /// with the <c>FollowedCompanyAdHit</c> inserts in the SAME unit of work (hard invariant —
    /// parity <see cref="AdvanceMatchScan"/>: else a crash mid-scan either re-notifies or drops
    /// hits). A DISTINCT watermark from <see cref="LastMatchScanAt"/> (the two scans advance
    /// independently). Monotonic; clamped to now so a bad window-calc can never run the watermark
    /// ahead of reality (CLAUDE §2.2 — the aggregate protects its own invariant).
    /// </summary>
    public void AdvanceCompanyWatchScan(DateTimeOffset scannedThrough, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;

        if (scannedThrough > now)
            scannedThrough = now;

        if (LastCompanyWatchScanAt is { } current && scannedThrough <= current)
            return;

        LastCompanyWatchScanAt = scannedThrough;
        UpdatedAt = now;
    }

    /// <summary>
    /// ADR 0080 Vag 4 (Beslut 6) — advances the user-read watermark to
    /// <paramref name="seenThrough"/>: the max <c>CreatedAt</c> of the matches the user actually
    /// viewed. #477 Low — NOT clock-now: a match inserted between the user's fetch and this call
    /// has <c>CreatedAt &gt; seenThrough</c>, so it stays ABOVE the watermark and is correctly
    /// still flagged "nya" (clock-now would swallow it silently). Drives the "nya sedan senaste
    /// besök" count. Monotonic; clamped to now so a future-dated <paramref name="seenThrough"/>
    /// can never run the watermark ahead of reality (mirrors <see cref="AdvanceMatchScan"/>;
    /// CLAUDE §2.2 — the aggregate guards its own invariant). Called when the user views the
    /// matches surface, NOT on every page load.
    /// </summary>
    public void SetLastSeenMatches(DateTimeOffset seenThrough, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;

        if (seenThrough > now)
            seenThrough = now;

        if (LastSeenMatchesAt is { } current && seenThrough <= current)
            return;

        LastSeenMatchesAt = seenThrough;
        UpdatedAt = now;
    }

    /// <summary>
    /// Convenience overload — advances the watermark to <b>now</b>. Use only when there is no
    /// specific seen-window (e.g. an empty match list, or test setup); the production mark-seen
    /// path passes an explicit <paramref name="clock"/>-independent <c>seenThrough</c> (#477 Low —
    /// see the overload above) so a match created between the user's fetch and mark-seen is not
    /// silently swallowed.
    /// </summary>
    public void SetLastSeenMatches(IDateTimeProvider clock) => SetLastSeenMatches(clock.UtcNow, clock);

    /// <summary>
    /// #293 (ADR 0042 Beslut E amendment) — marks the /jobb job list as seen up to now
    /// (advances the user-read watermark). The next visit's "Ny" tag then flags only ads
    /// ingested after this moment. Called on each /jobb page load (the sibling of
    /// <see cref="SetLastSeenMatches"/>). Monotonic — a stale/duplicate call never rewinds
    /// the watermark.
    /// </summary>
    public void SetLastSeenJobs(IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        if (LastSeenJobsAt is { } current && now <= current)
            return;

        LastSeenJobsAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Sets the job-seeker's STATED job-search preferences (F4-12, ADR 0076).
    /// Mirrors <see cref="UpdatePreferences"/>: replaces the value object + bumps
    /// <see cref="UpdatedAt"/>. Raises NO domain event — there is no reactive
    /// consumer (matching is compute-on-demand; CTO-bound). An empty
    /// <see cref="MatchPreferences"/> is valid (clears stated preferences).
    /// </summary>
    public void UpdateMatchPreferences(MatchPreferences matchPreferences, IDateTimeProvider clock)
    {
        MatchPreferences = matchPreferences;
        UpdatedAt = clock.UtcNow;
    }

    /// <summary>
    /// Sätter primary Resume för denna JobSeeker. Atomic swap — invarianten
    /// "exakt 1 primary per JobSeeker" hålls trivialt eftersom state ägs här
    /// (ADR 0058 + senior-cto-advisor 2026-05-20 Alt A2). Handler ansvarar
    /// för cross-aggregat-validering att <paramref name="resumeId"/> tillhör
    /// denna JobSeeker — Resume har ingen knowledge om sin egen primary-status.
    /// Idempotent vid samma ID.
    /// </summary>
    public Result SetPrimaryResume(ResumeId resumeId, IDateTimeProvider clock)
    {
        if (resumeId == default)
            return Result.Failure(DomainError.Validation(
                "JobSeeker.PrimaryResumeIdRequired", "Resume-id krävs."));

        if (PrimaryResumeId == resumeId)
            return Result.Success();

        PrimaryResumeId = resumeId;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new PrimaryResumeSetDomainEvent(Id, resumeId, clock.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// Nullar primary Resume. Anropas av <c>DeleteResumeCommandHandler</c> som
    /// del av cascade när den primary-markerade Resume soft-raderas. Idempotent.
    /// </summary>
    public Result UnsetPrimaryResume(IDateTimeProvider clock)
    {
        if (PrimaryResumeId is null)
            return Result.Success();

        PrimaryResumeId = null;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new PrimaryResumeSetDomainEvent(Id, null, clock.UtcNow));
        return Result.Success();
    }

    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;

        DeletedAt = clock.UtcNow;
        RaiseDomainEvent(new JobSeekerDeletedDomainEvent(Id, clock.UtcNow));
    }
}
