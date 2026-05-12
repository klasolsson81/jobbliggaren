namespace JobbPilot.Api.RateLimiting;

/// <summary>
/// Rate-limiting-konfiguration per policy (TD-21). Defaults är prod-värden
/// per security-auditor STEG 10b Major-2. Test-miljöer höjer limits via
/// <c>RateLimiting__*</c>-env-vars eller <c>appsettings.Test.json</c>-overlay
/// så testerna inte rate-limit:as på varandras gemensamma IP-partition.
///
/// Policy-nycklar finns som konstanter på <see cref="RateLimitingExtensions"/>.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// DELETE /me — partitionerat per UserId (claim "sub"). Skyddar mot
    /// kompromettera-session-radera-konto-DoS + power-user resource-DoS.
    /// </summary>
    public PolicyOptions AccountDeletion { get; init; } = new()
    {
        PermitLimit = 1,
        WindowSeconds = 60,
    };

    /// <summary>
    /// /auth/login + /auth/register — partitionerat per IP. Bromsar credential-
    /// stuffing och registration-spam. 20/min är OWASP-kompatibel default som
    /// rymmer CGN/NAT-användare (skolor, företagsnät, mobiloperatörer) utan att
    /// öppna brute-force-fönster. Revisit-trigger: prod-mätningar i Fas 1+.
    /// </summary>
    public PolicyOptions AuthWrite { get; init; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
    };

    /// <summary>
    /// /auth/logout — partitionerat per IP. Mer permissivt eftersom logout är
    /// idempotent och inte öppnar abuse-vektor på samma sätt som login.
    /// </summary>
    public PolicyOptions AuthLoose { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60,
    };

    /// <summary>
    /// /auth/redeem-invitation — partitionerat per IP. Stoppar brute-force
    /// mot token-hash + enumeration-attacker. Per ADR 0005 amendment
    /// 2026-05-12: 5/timme räcker eftersom legitim användare bara redeems
    /// en gång; angripare som testar massa tokens fångas tidigt.
    /// </summary>
    public PolicyOptions InvitationRedeem { get; init; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 3600,
    };

    /// <summary>
    /// /waitlist (publik anonym signup) — partitionerat per IP. Skyddar mot
    /// spam-signups. 3/24h räcker — legitim användare skriver upp sig en gång.
    /// Per ADR 0005 amendment 2026-05-12.
    /// </summary>
    public PolicyOptions WaitlistSignup { get; init; } = new()
    {
        PermitLimit = 3,
        WindowSeconds = 86400,
    };

    public sealed class PolicyOptions
    {
        public int PermitLimit { get; init; }
        public int WindowSeconds { get; init; }
    }
}
