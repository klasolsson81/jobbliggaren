namespace Jobbliggaren.Api.RateLimiting;

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

    /// <summary>
    /// List/search-endpoints (GET /api/v1/job-ads med
    /// ?occupationGroup/?municipality/?region/?q) —
    /// partitionerat per UserId (claim "sub"). Skyddar mot multi-query-DoS
    /// från komprometterat konto via wildcard-LIKE-pattern (CWE-400, OWASP
    /// API4:2023 "Unrestricted Resource Consumption"). 60/min ger 6-20x
    /// headroom över normal scroll/filter-användning (3-10 req/min) utan
    /// att öppna sequential-scan-attack-fönster. Per CTO-rond 2026-05-13
    /// F2-P9. Kalibrering utan prod-mätdata — revisit-trigger Fas 7+.
    /// </summary>
    public PolicyOptions ListRead { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /job-ads/suggest (typeahead) — partitionerat per UserId (claim
    /// "sub"). Egen policy (ej ListRead-återanvändning) eftersom typeahead
    /// är strukturellt högre frekvens (1 req/keystroke) — least common
    /// mechanism (Saltzer/Schroeder): dela inte skyddsbudget mellan ytor
    /// med olika legitim-frekvensprofil. 30/10s ≈ 3 req/s headroom för
    /// debouncad (≥300ms) typeahead, kapar odebouncad/script-flod inom 1s.
    /// senior-cto-advisor 2026-05-16 (ADR 0042 Beslut C, Batch 5) —
    /// riktvärde, security-auditor verifierar/justerar (BLOCKING).
    /// </summary>
    public PolicyOptions Suggest { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 10,
    };

    /// <summary>
    /// GET /job-ads/taxonomy(+/labels) (ADR 0043 picker-träd + reverse-
    /// lookup) — partitionerat per UserId (claim "sub"). Egen policy
    /// (least common mechanism, Saltzer/Schroeder): statisk referensdata
    /// med ETag + private cache → frontend hämtar sällan; en låg egen
    /// budget får inte svälta list/suggest-ytan och vice versa. 20/60s
    /// täcker initial-load + ev. reverse-lookup per sökvy med marginal,
    /// kapar script-flod. senior-cto-advisor MAP-3 2026-05-17 — riktvärde,
    /// security-auditor verifierar/justerar (BLOCKING). IOptions-bundet (§5.1).
    /// </summary>
    public PolicyOptions TaxonomyRead { get; init; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /job-ads/facet-counts (per-option facet-counts, ADR 0067 Beslut 4,
    /// Fas E2c) — partitionerat per UserId (claim "sub"). Egen policy (ej
    /// ListRead-återanvändning) — least common mechanism (Saltzer/Schroeder):
    /// facet-profilen är client-side debounce-burst (Ort-popovern gör 2
    /// parallella requests, 20-40 req/min under aktiv filtrering) medan
    /// ListRead bär RSC-list-refetcharna (live-commit gör varje toggle till
    /// en router.push) — delad budget hade svält LISTAN av sin egen
    /// dekoration (bulkhead, Nygard). 30/10s ≈ 3 req/s ger ×4-9 headroom
    /// över profilen och kapar script-flod inom sekunder (symmetri med
    /// Suggest — samma debouncade ≥300ms klientprofil). senior-cto-advisor
    /// VAL 1 2026-06-11 (E2c) — riktvärde, security-auditor verifierar/
    /// justerar (BLOCKING).
    /// </summary>
    public PolicyOptions FacetCounts { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 10,
    };

    /// <summary>
    /// GET /api/v1/landing/stats (publik anonym landing-stats, ADR 0064) —
    /// partitionerat per IP. Egen policy (least common mechanism,
    /// Saltzer/Schroeder): publik anonym DoS-yta får inte dela skyddsbudget
    /// med autentiserad list-yta (ListRead) eller statisk taxonomi (TaxonomyRead).
    /// 60/min/IP per senior-cto-advisor-dom 2026-05-23 (agentId a1da26dc2029a5def):
    /// generöst för aggressiv prefetch + multi-tab, stramt nog för hammering-skydd.
    /// Klas-låsbart (produkt-/kostnadsdimension).
    /// </summary>
    public PolicyOptions LandingPublicRead { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// Auth-gated GET-ytor under /me/* + /applications/pipeline + /resumes
    /// (Pre-4 STEG 5, TD-92) — partitionerat per UserId (claim "sub"), anonym
    /// → NoLimiter (alla är RequireAuthorization-gated → 401 före endpoint).
    /// Egen policy (ej ListRead-återanvändning) — least common mechanism
    /// (Saltzer/Schroeder): /oversikt avfyrar 6 parallella BE-anrop (Promise.all)
    /// per sidladdning = 6× request-amplifiering mot tyngre objekt-grafer
    /// (pipeline/resumes/profile) än publika job-ads-listan, så denna yta får
    /// en egen, snävare budget än ListRead (60/min) och svälter inte den
    /// publika sök-listan vid kompromissat konto (bulkhead, Nygard). 40/min ger
    /// headroom över en /oversikt-render (6 anrop) + normal navigation, kapar
    /// OWASP API4:2023-style multi-query-DoS. dotnet-architect + senior-cto-
    /// advisor 2026-06-14 — riktvärde, security-auditor verifierar/justerar
    /// (BLOCKING).
    /// </summary>
    public PolicyOptions MeListRead { get; init; } = new()
    {
        PermitLimit = 40,
        WindowSeconds = 60,
    };

    /// <summary>
    /// POST /api/v1/me/job-ad-status (per-user-overlay-status batch, ADR 0063,
    /// Pre-4 STEG 5, TD-87) — <strong>dual-partition</strong>: sub närvarande →
    /// user:-bucket, annars → ip:-bucket. Endpointen är anonym-tolerant (INTE
    /// RequireAuthorization-gated — handler returnerar tom DTO utan UserId), så
    /// den vanliga UserId→NoLimiter-bypassen skulle lämna ytan helt oskyddad
    /// mot anonym batch-enumeration/DoS; ip:-fallbacken är därför TD-87:s
    /// bärande skyddsegenskap. Batch är taklagd (validator-cap = 100 IDs → en
    /// query, ej N+1). 60/min speglar LandingPublicRead (enda jämförbara
    /// öppna-internet-IP-prejudikat): generöst för en list-render-dekoration
    /// (FE anropar en gång per job-ads-render) + scroll/multi-tab, stramt nog
    /// mot hammering. Bakom reverse-proxy kräver ip:-bucketen UseForwardedHeaders
    /// (redan wired, Program.cs) annars hamnar alla i proxy-IP-bucketen.
    /// senior-cto-advisor 2026-06-14 (Beslut B) — Klas-låsbart (kostnads-/
    /// exponeringsdimension); security-auditor kan ratcha ned (BLOCKING).
    /// </summary>
    public PolicyOptions JobAdStatusBatch { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// Användarägda /me/*-mutationer (saved-job-ads POST/DELETE, recent-searches
    /// DELETE) (Pre-4 STEG 5, TD-87) — partitionerat per UserId (claim "sub"),
    /// anonym → NoLimiter (alla RequireAuthorization-gated). Egen policy (ej
    /// AuthWrite-återanvändning) — AuthWrite är IP-partitionerad för anonym
    /// login/register-spam; att återanvända den hade läckt IP-axel-semantik in
    /// på en auth-gated användarägd yta och straffat NAT/CGN-delade IP:n
    /// (Saltzer/Schroeder least common mechanism). Konsistent med AccountDeletion
    /// (DELETE /me) som redan är en UserId-partitionerad skriv-policy. Egen
    /// budget (ej fold-in i MeListRead) så en /oversikt-läs-burst inte svälter
    /// en spara/ta-bort-mutation (bulkhead, Nygard). 30/min är gott om utrymme
    /// för bokmärknings-/rensnings-interaktion. dotnet-architect + senior-cto-
    /// advisor 2026-06-14 (Beslut A1) — riktvärde, security-auditor verifierar/
    /// justerar (BLOCKING).
    /// </summary>
    public PolicyOptions MeWrite { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60,
    };

    public sealed class PolicyOptions
    {
        public int PermitLimit { get; init; }
        public int WindowSeconds { get; init; }
    }
}
