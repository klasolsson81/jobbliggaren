namespace JobbPilot.Application.Common.Abstractions;

/// <summary>
/// Runtime feature flags — togglebara utan deploy via Secrets Manager-overlay
/// per BUILD.md §13.2. Per ADR 0005 amendment 2026-05-12: <c>registrations_open</c>
/// fungerar som emergency kill-switch som blockerar både invitation-redemption
/// och waitlist-signup. Befintliga users opåverkade.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// När false: nya registreringar via invitation-redemption och
    /// waitlist-signup returnerar 503 Service Unavailable. Default false
    /// (stängd-by-default per Alternativ C).
    /// </summary>
    bool RegistrationsOpen { get; }
}
