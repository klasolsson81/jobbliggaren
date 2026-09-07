using System.Collections.Frozen;

namespace Jobbliggaren.Application.BackgroundJobs;

/// <summary>
/// Single source of truth for the 17 Hangfire recurring-job ids. Used both by the
/// Worker's <c>RecurringJobRegistrar</c> (registration) and by the admin operator
/// surface's trigger validator (the closed allowlist).
///
/// <para>
/// SECURITY (#204 / TD-83, security-auditor T7 — fan-out/RCE prevention): the
/// admin "trigger now" surface accepts ONLY a member of <see cref="All"/>. An
/// operator can never trigger an arbitrary job type with arbitrary arguments —
/// only one of these known, parameterless recurring jobs. Keeping the registrar
/// and the allowlist on the same constants closes the drift risk (a registered
/// id missing from the allowlist would be untriggerable; an allowlisted id
/// without registration would validate then no-op). The parity is locked by a
/// test (registrar id-set == <see cref="All"/>).
/// </para>
///
/// Lives in Application (BCL-only) because Worker already depends on Application;
/// Application must not depend on Worker, so the constants cannot live in the
/// Worker registrar. This also retires the magic-string anti-pattern (CLAUDE.md
/// §5) on both the registration and the validation side.
/// </summary>
public static class RecurringJobIds
{
    public const string SyncPlatsbankenStream = "sync-platsbanken-stream";
    public const string SyncPlatsbankenSnapshot = "sync-platsbanken-snapshot";
    public const string AuditLogRetention = "audit-log-retention";
    public const string RetainPlatsbankenJobAds = "retain-platsbanken-job-ads";
    public const string BackgroundMatching = "background-matching";
    public const string CompanyWatchScan = "company-watch-scan";
    public const string ExpireJobAds = "expire-job-ads";
    public const string HardDeleteAccounts = "hard-delete-accounts";
    public const string PurgeStaleRawPayloads = "purge-stale-raw-payloads";
    public const string ReapStrandedMatches = "reap-stranded-matches";
    public const string BackfillFieldEncryption = "backfill-field-encryption";
    public const string ParsedResumeRetention = "parsed-resume-retention";
    public const string DigestDispatchDaily = "digest-dispatch-daily";
    public const string DigestDispatchWeekly = "digest-dispatch-weekly";
    public const string RefreshLandingStats = "refresh-landing-stats";

    /// <summary>#560 (ADR 0091) — full SCB company-register population/refresh (legal-entities-only,
    /// count-then-slice, ~1–3 h). Cron is config-driven (<c>ScbRegister:SyncCadenceCron</c>).</summary>
    public const string SyncScbCompanyRegister = "sync-scb-company-register";

    /// <summary>
    /// #1681 (ADR 0139) — resolve every saved smart watch (a predicate) into the register companies it
    /// matches, out of the request path. Cron is config-driven
    /// (<c>CompanyWatchMaterialisation:CadenceCron</c>), on its OWN options section: tying it to
    /// <c>ScbRegister:*</c> would inherit that section's <c>Enabled=false</c> default and the job would
    /// never run in the default posture (security-auditor Major 3).
    /// </summary>
    public const string MaterialiseCompanyWatchCriteria = "materialise-company-watch-criteria";

    /// <summary>
    /// #1681 clause (ii) — the reconciling sweep: recompute only those criteria whose stored
    /// membership does not describe their current predicate, so a user's OWN edit does not wait for
    /// <see cref="MaterialiseCompanyWatchCriteria"/>. Cron is config-driven
    /// (<c>CompanyWatchMaterialisation:SweepCron</c>), in the same options section because the two
    /// cadences constrain one another.
    ///
    /// <para>
    /// A SECOND id rather than a faster cadence on the first, because the two jobs answer two
    /// different change-reasons — the register moved (weekly, external) versus a predicate moved
    /// (continuous, user) — and because they take opposite retry postures. They share one distributed
    /// lock all the same; see <c>CompanyWatchCriterionMaterialisationWorker</c>.
    /// </para>
    /// </summary>
    public const string SweepChangedCompanyWatchCriteria = "sweep-changed-company-watch-criteria";

    /// <summary>
    /// The closed set of triggerable recurring-job ids. Ordinal comparison — these
    /// are stable internal slugs, not user text.
    /// </summary>
    public static readonly FrozenSet<string> All = new[]
    {
        SyncPlatsbankenStream,
        SyncPlatsbankenSnapshot,
        AuditLogRetention,
        RetainPlatsbankenJobAds,
        BackgroundMatching,
        CompanyWatchScan,
        ExpireJobAds,
        HardDeleteAccounts,
        PurgeStaleRawPayloads,
        ReapStrandedMatches,
        BackfillFieldEncryption,
        ParsedResumeRetention,
        DigestDispatchDaily,
        DigestDispatchWeekly,
        RefreshLandingStats,
        SyncScbCompanyRegister,
        MaterialiseCompanyWatchCriteria,
        SweepChangedCompanyWatchCriteria,
    }.ToFrozenSet(StringComparer.Ordinal);
}
