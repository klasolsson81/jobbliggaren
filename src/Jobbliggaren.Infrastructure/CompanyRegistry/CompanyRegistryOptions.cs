using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.CompanyRegistry;

/// <summary>
/// #454 (ADR 0088 D3) — config for the company-registry module. v1 providers are <c>Fake</c>
/// (dev/test-gated deterministic table) and <c>Off</c> (→ <see cref="NullCompanyRegistry"/>, the
/// prod default until the real SCB adapter lands). <c>ScbApiKey</c> is RESERVED for the follow-up
/// adapter against SCB's NEW API (September 2026, API-key auth; the September auth switch is why no
/// cert-based provider is ever built — ADR 0088 D3). Unknown values fail-stop at startup (parity
/// <c>Email:Provider</c>). No secrets live here in v1 (Fake/Null need none); the future SCB API key
/// goes in gitignored <c>appsettings.Local.json</c> / a managed secret store, never committed.
/// </summary>
public sealed class CompanyRegistryOptions
{
    public const string SectionName = "CompanyRegistry";

    public const string ProviderOff = "Off";
    public const string ProviderFake = "Fake";

    /// <summary>Provider selector: <c>Off</c> (default) or <c>Fake</c> (dev/test only) in v1.</summary>
    public string Provider { get; set; } = ProviderOff;

    /// <summary>
    /// Positive read-through cache TTL in days for org.nr→name (ADR 0088 D6 — org.nr→name is stable
    /// per legal person; 30 d accepted-staleness window recorded in the ADR). Negative caching is
    /// DELIBERATELY absent in v1 (deferred to the SCB-activation PR where the real upstream budget
    /// exists to tune against).
    /// </summary>
    [Range(1, 365)]
    public int PositiveCacheTtlDays { get; set; } = 30;
}
