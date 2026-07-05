using System.Collections.Frozen;

namespace Jobbliggaren.Application.BackgroundJobs;

/// <summary>
/// Single source of truth for the 16 Hangfire recurring-job ids. Used both by the
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
    }.ToFrozenSet(StringComparer.Ordinal);
}
