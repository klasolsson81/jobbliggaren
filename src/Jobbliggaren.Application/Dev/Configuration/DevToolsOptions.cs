namespace Jobbliggaren.Application.Dev.Configuration;

/// <summary>
/// Throwaway dev-tooling toggles the Application layer owns and Infrastructure binds
/// (<c>DevTools</c> section). Application declares the contract so
/// <see cref="Commands.ResetMyData.ResetMyDataCommandHandler"/> can read it via
/// <c>IOptions&lt;DevToolsOptions&gt;</c> without depending on Infrastructure (Clean Architecture
/// dependency rule; template: <c>AuthOptions</c>).
/// <para>
/// <b>REMOVE BEFORE LAUNCH, together with everything it gates.</b> The decommissioning steps are
/// written down in <c>docs/runbooks/release-checklist.md</c> rather than left to memory, because a
/// flag whose removal is nobody's task is a flag that ships.
/// </para>
/// </summary>
public sealed class DevToolsOptions
{
    public const string SectionName = "DevTools";

    /// <summary>
    /// Makes the owner-scoped <c>POST /api/v1/dev/reset-my-data</c> reachable in a deployed
    /// environment so the welcome/matching setup can be re-tested there (Klas-direktiv
    /// 2026-08-27). Until this existed the endpoint was mapped only under
    /// <c>IsDevelopment()</c>, and the box runs <c>ASPNETCORE_ENVIRONMENT=Production</c> — so the
    /// one environment that needed re-testing was the one environment that could not.
    /// <para>
    /// Default <c>false</c> = OFF, and the polarity is the whole point: this gates a
    /// <b>destructive</b> operation, so an unset value must fail CLOSED. The mirror-image name
    /// (<c>DisableResetMyData</c>) would default to enabled and is therefore wrong. The same
    /// reasoning, and the same shape, as <c>AuthOptions.RegistrationsOpen</c>.
    /// </para>
    /// <para>
    /// <b>What this flag deliberately does NOT reach:</b> the rest of the <c>/api/v1/dev/*</c>
    /// group. <c>POST /api/v1/dev/accounts</c> and <c>POST /api/v1/dev/login-code</c> are
    /// UNAUTHENTICATED seams that open an account and hand out a login code for a reserved address,
    /// and they stay gated on <c>IsDevelopment()</c> unconditionally — which is why the
    /// routes are mapped by two different extension methods rather than one call behind one
    /// condition. Turning this flag on must never be one <c>||</c> away from re-arming an auth
    /// bypass, and <c>ProductionStartupSmokeTests</c> measures that in both flag polarities.
    /// </para>
    /// <para>
    /// Read in TWO independent places — the map gate in <c>Program.cs</c> and a refusal inside the
    /// handler — mirroring the two structural gates the other seams have. Deliberately
    /// NOT <c>ValidateOnStart</c>-refused: a validator that rejects the flag outside Development
    /// would make it impossible to use for its only purpose. The boot announcement carries the
    /// posture instead, at Warning.
    /// </para>
    /// <para>
    /// Settable (not init-only) so the integration harness can force the value via
    /// <c>PostConfigure&lt;DevToolsOptions&gt;</c>, exactly as the auth flags are forced.
    /// </para>
    /// </summary>
    public bool EnableResetMyData { get; set; }
}
