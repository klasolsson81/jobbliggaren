using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — configuration for the SCB company-register population channel. Bound from the
/// <c>ScbRegister</c> section; DataAnnotations validated + validated on start (parity
/// <c>JobTechOptions</c>/<c>CompanyRegistryOptions</c>). Ships <see cref="Enabled"/>=false by default
/// so CI and any dev environment WITHOUT the SCB client certificate stay dark — the recurring job
/// then no-ops. The certificate itself is loaded from the Windows cert-store by
/// <see cref="CertThumbprint"/>; the thumbprint (not a secret, but kept out of the repo) lives in
/// gitignored <c>appsettings.Local.json</c> or a managed store / runtime env override, never
/// committed. The certificate is a personal credential (signed SCB terms) — never committed, never
/// shared.
/// </summary>
public sealed class ScbRegisterOptions
{
    public const string SectionName = "ScbRegister";

    /// <summary>Master switch. When false the refresh job runs but does nothing (no SCB call, no
    /// cert load). Default false — the real population is a deliberate, cert-gated, DPIA-cleared
    /// action (ADR 0091), not an implicit dev/CI behaviour.</summary>
    public bool Enabled { get; set; }

    /// <summary>SHA-1 thumbprint of the SCB client certificate in the Windows cert-store
    /// (<c>CurrentUser\My</c> by default). Required when <see cref="Enabled"/> is true. Spaces/casing
    /// are normalized on load.</summary>
    public string? CertThumbprint { get; set; }

    /// <summary>Cert-store location holding the client cert: <c>CurrentUser</c> (default) or
    /// <c>LocalMachine</c>.</summary>
    public string CertStoreLocation { get; set; } = "CurrentUser";

    /// <summary>SCB API base URL (JE / legal-entity endpoints hang off this).</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://privateapi.scb.se/nv0101/v1/sokpavar/";

    /// <summary>Cron for the recurring refresh job (UTC). Default weekly Monday 03:00 — matches SCB's
    /// own weekly register update cadence (senior-cto-advisor 2026-07-04, Fork 3; Klas may override
    /// toward monthly <c>"0 3 1 * *"</c> — it is one config value).</summary>
    [Required]
    public string SyncCadenceCron { get; set; } = "0 3 * * 1";

    /// <summary>Max rows per <c>hamtaforetag</c> fetch — the hard SCB cap (2000). Also the planner's
    /// slice target. (The 10-calls/10-s upstream budget is NOT config — it is a hard SCB invariant
    /// enforced by a static process-wide rate limiter in <c>AddScbCompanyRegister</c>, senior-cto-advisor
    /// Fork 7.)</summary>
    [Range(1, 2000)]
    public int BatchSize { get; set; } = 2000;

    /// <summary>Absolute floor: the deregister sweep is SKIPPED unless at least this many legal-entity
    /// rows were fetched this run. Guards against a partial run (SCB 503 mid-run) flipping the
    /// untouched majority to Deregistered. Default 500 000 (~half the ~1M register).</summary>
    [Range(0, int.MaxValue)]
    public int FloorAbsolute { get; set; } = 500_000;

    /// <summary>Relative floor: the sweep is SKIPPED unless this run's fetched total is at least this
    /// ratio of the max total observed in prior <c>CompanyRegisterSynced</c> audit rows. Parity the
    /// JobTech snapshot floor-guard.</summary>
    [Range(0.0, 1.0)]
    public double FloorRelativeRatio { get; set; } = 0.80;

    /// <summary>Per-call HTTP timeout in minutes.</summary>
    [Range(1, 30)]
    public int HttpTimeoutMinutes { get; set; } = 5;
}
