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
    /// POST /me/delete — partitionerat per UserId (claim "sub"). Skyddar mot
    /// kompromettera-session-radera-konto-DoS + power-user resource-DoS.
    /// </summary>
    public PolicyOptions AccountDeletion { get; init; } = new()
    {
        PermitLimit = 1,
        WindowSeconds = 60,
    };

    /// <summary>
    /// Partitionerat per IP. Bromsar registration-spam. 20/min är OWASP-kompatibel default som
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
    /// GET /saved-searches/derive (typeahead-format) — partitionerat per UserId (claim
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
    /// GET /job-ads/suggest specifically, since #1546 (security-auditor Major 1, 2026-08-31).
    /// <para>
    /// <b>Why this endpoint left the shared <see cref="Suggest"/> budget.</b> The employer branch runs
    /// a <c>%contains%</c> + <c>GROUP BY</c> over <c>job_ads</c> — the same query FORM that
    /// <c>/job-ads/employers</c> deliberately sits on the heavier <see cref="ListRead"/> budget for,
    /// its endpoint comment saying so in as many words: <i>"the ILIKE + GROUP BY is a heavier scan than
    /// the typeahead suggest"</i>. Leaving it on 30/10s would have moved that scan onto a lighter
    /// budget AND from per-submit to per-keystroke at the same time, which is exactly the budget
    /// sharing least common mechanism forbids.
    /// </para>
    /// <para>
    /// <b>20/10s</b> — a burst of 20 and, with <c>SegmentsPerWindow</c> = 6, a sustained
    /// 3 tokens per 1.67 s ≈ <b>1.8 req/s</b> — carries a 300 ms-debounced typeahead and cuts the
    /// sustained script-flood budget by a third. <see cref="Suggest"/> itself is UNCHANGED at 30/10s —
    /// <c>SavedSearchesEndpoints</c> is typeahead-shaped but carries none of this weight, and tightening
    /// it would be collateral, not calibration.
    /// </para>
    /// <para>
    /// ⚠ <b>The number is deliberately conservative and is revised UP after the latency measurement,
    /// never down after shipping.</b> Raising a limit once it is measured is free; lowering one that
    /// users already have is a visible regression. <c>security-auditor</c> owns this calibration (this
    /// file's own convention: <i>"riktvärde, security-auditor verifierar/justerar (BLOCKING)"</i>).
    /// </para>
    /// </summary>
    public PolicyOptions JobAdSuggest { get; init; } = new()
    {
        PermitLimit = 20,
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
    /// POST /me/match-count-preview (live sök-preview-räknaren i matchnings-setup-modalen,
    /// epik #526, ADR 0089) — partitionerat per UserId (claim "sub"). Egen policy (bulkhead,
    /// Nygard) — samma debounce-burst-profil som FacetCounts (~1 req/400 ms klient-debounce
    /// medan användaren ändrar yrke/ort/form) och får inte dela budget med MeListRead som
    /// /oversikt redan fläktar ut ~7×. 30/10s ≈ 3 req/s ger rikligt headroom över den
    /// debouncade profilen och kapar script-flod inom sekunder (symmetri med FacetCounts/
    /// Suggest). senior-cto-advisor 2026-07-02 (D5) — riktvärde, security-auditor verifierar/
    /// justerar (BLOCKING). IOptions-bundet (§5.1).
    /// </summary>
    public PolicyOptions MatchCountPreview { get; init; } = new()
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
    /// publika sök-listan vid kompromissat konto (bulkhead, Nygard).
    /// <para>
    /// <b>Retune 2026-06-24 (senior-cto-advisor, Klas UX-rapport):</b> 40→120/min +
    /// FixedWindow→TokenBucket (<see cref="PolicyOptions.SegmentsPerWindow"/>=6 styr
    /// replenishment). STEG 6 + Vag 4 PR-5 la till match-count + new-match-count → /oversikt
    /// avfyrar nu ~7 MeListRead-anrop/laddning (inte 6); 40/min ÷ 7 ≈ 5,7 laddningar/min
    /// trippade normal bläddring. 120 = ~17 laddningar/min headroom (~3× originalet),
    /// UserId-partition intakt = kvar under scrape-DoS-signatur. TokenBucket ger ~10s mjuk
    /// väntan i stället för FixedWindows 60s-bann OCH populerar Retry-After rent —
    /// SlidingWindow gör INTE det (security-auditor + code-reviewer empiri 2026-06-24, CTO-
    /// förauktoriserad fallback). QueueLimit=0 kvar (kö = memory-DoS).
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Omprissättning 2026-09-07 (#1681 del 2, security-auditor Major 1) — härledningen ovan är
    /// REN REQUEST-AMPLIFIERING och saknar en per-request-BACKENDKOSTNADSTERM.</b> Det spelade ingen
    /// roll så länge varje rutt i hinken var en bunden objektgraf-läsning. Sedan #1681 del 2 är
    /// <c>GET /me/company-watch-criteria</c> det inte längre: den kör <b>upp till ~40 bundna satser
    /// plus ett graderingsanrop per request</b> (två satser per kriterium × <c>MaxPerUser</c> = 20,
    /// plus en batchad <c>FilterToMatchingAsync</c>). Vid taket är det ~4 800 satser/min/användare
    /// mot <c>job_ads</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Mätta kostnadsklasser</b>
    /// (<c>docs/reviews/2026-09-07-1681-part2-fanin-and-corpus-measurement.md</c>): tvillingen
    /// <c>ListCompanyWatchesQueryHandler</c> läser på <b>0,166 ms p95</b>; den nya rutten på
    /// <b>47,3 ms</b> i vardagsfallet och <b>240–381 ms</b> vid annonstaket — alltså ~285× respektive
    /// ~1 400–2 300× tvillingen. ADR 0139:s alternativ 3 lyfte sin invändning på TVÅ grunder; den
    /// första (<i>ingen registerjoin kvar i läsvägen</i>) håller och är pinnad, men den andra — att
    /// läsvägen blir <i>"samma bundna GROUP BY som företagsblocket redan kör"</i> — är <b>falsk som
    /// levererad</b>: tvillingen kör 2 satser, den här rutten upp till 40.
    /// </para>
    ///
    /// <para>
    /// <b>Vad som gjordes åt det, och vad som inte gjordes.</b> Marginalen köptes tillbaka genom att
    /// <b>ta bort ett anrop</b>, inte genom att höja taket: båda detaljsidorna slutade anropa den här
    /// rutten (#1681 del 2 gav dem <c>GetCriterionIdentityQuery</c> på rutter de redan anropar), så av
    /// tre konsumenter är en kvar — den lista som faktiskt renderar talen. <b>Höjd
    /// <see cref="PolicyOptions.PermitLimit"/> är uttryckligen INTE en tillgänglig åtgärd</b>
    /// (security-auditor 2026-09-07), och det är samma doktrin <c>CompanyBrowse</c> redan skriver ut:
    /// <i>"Buy the margin back by removing a call, not by raising this."</i>
    /// </para>
    ///
    /// <para>
    /// <b>Residualet är stängt 2026-09-07 — och INTE här.</b> Borttagningen av anropet fixade det
    /// OAVSIKTLIGA fallet (vanlig navigering), aldrig vad en avsiktlig aktör når: ett konto kunde
    /// fortfarande träffa listrutten 120 ggr/min, vilket vid annonstaket är ~45,8 s databastid per
    /// minut från en enda rutt. <c>security-auditor</c> vägrade signera hinken på den grunden, och
    /// Klas valde hennes rekommendation: <b>rutten fick en EGEN policy</b>,
    /// <see cref="CompanyWatchCriteriaList"/> (5 burst / 3 per minut uthålligt), där härledningen —
    /// båda halvorna, inklusive den per-request-backendkostnadsterm som saknas i stycket ovan —
    /// står i sin helhet. <b>Ingen ratchet gjordes på den här policyn</b>: 120/min står orört för
    /// <c>/oversikt</c>s ~7-anrops-fan, och en nedskruvning här hade varit kollateral på ytor som
    /// inte bar kostnaden. Styckena ovan står kvar som protokoll över VARFÖR rutten lämnade hinken;
    /// de beskriver inte längre ett öppet läge.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Läxan är generell och gäller den här policyn, inte bara den rutt som lämnade.</b>
    /// Härledningen ovan är ren request-amplifiering. Den håller så länge varje kvarvarande rutt i
    /// hinken är en bunden objektgraf-läsning — och den säger ingenting alls den dag en av dem inte
    /// är det. <b>En ny rutt hör hemma här bara om dess per-request-kostnad ligger i samma klass som
    /// de befintliga; annars är svaret en egen policy, precis som här.</b>
    /// </para>
    /// </summary>
    public PolicyOptions MeListRead { get; init; } = new()
    {
        PermitLimit = 120,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /api/v1/me/notification-feed — the notification bell's poll (#1666), partitioned per UserId
    /// (claim "sub"), anonymous → NoLimiter (RequireAuthorization answers 401 first). Its own bucket,
    /// not <see cref="MeListRead"/>: the bell fires on every page of the signed-in shell, in the
    /// background, and must neither starve /oversikt's fan-out nor be starved by it (bulkhead,
    /// Nygard), and its per-request cost is two sources — the statement count without an "endast
    /// matchade" watch is pinned by <c>NotificationFeedEndpointTests</c> — outside MeListRead's class.
    /// <para>
    /// The numbers (security-auditor, #1666 form round): a visible tab polls every 5 minutes and on
    /// open or visibility at most every 30 s, so one tab needs 0.2 calls a minute at rest and 2 at
    /// most. 12 a minute (burst 12, 2 tokens per 10 s) covers six visible windows at the throttle
    /// limit, or twelve tabs reloading at once. TokenBucket for a clean Retry-After, QueueLimit 0
    /// (a queue is a memory-DoS). Re-derive when a source is added (#1663).
    /// </para>
    /// </summary>
    public PolicyOptions NotificationFeedRead { get; init; } = new()
    {
        PermitLimit = 12,
        WindowSeconds = 60,
    };

    /// <summary>
    /// POST /api/v1/me/notification-feed/{source}/{jobAdId}/read, /api/v1/me/notification-feed/{source}/read and
    /// /api/v1/me/notification-feed/{source}/{jobAdId}/dismiss - the notification bell's read marks (#2130) and its X
    /// (#2151), one bucket for all three, partitioned per UserId (claim "sub"), anonymous ->
    /// NoLimiter (RequireAuthorization answers 401 first). Its own bucket, never <see cref="MeWrite"/> and never
    /// <see cref="FollowSeenMark"/>: a withdrawal of a consent must not meet a 429 that clicking through notifications
    /// caused (GDPR Art. 7(3); the reason <see cref="FollowSeenMark"/> was split out of <see cref="MeWrite"/>), and the
    /// ad-open auto-mark must not drain the budget of a deliberate gesture (bulkhead, Nygard).
    /// <para>
    /// The numbers (security-auditor, #2130 PR 2): the demand is one POST per click on a row that is unread locally
    /// (the client sends nothing for a row it holds as read) plus two POSTs per "Markera alla". The panel holds at
    /// most 20 rows, so ctrl- or middle-clicking every one and then pressing "Markera alla" is 22 calls within
    /// seconds; for the read marks alone 60 covers about three panels, i.e. two tabs plus slack. Sustained, a tab can demand at most about
    /// 20 marks per 30 s (the open-throttle), so 60 a minute covers it. A false 429 puts a dot back, which is the
    /// defect this PR removes, while over-provisioning costs only owner-scoped, index-bounded updates of the user's own
    /// rows; every bulk update excludes rows already read, so a looped "Markera alla" writes no tuple. Burst 60,
    /// 10 tokens per 10 s. TokenBucket for a clean Retry-After, QueueLimit 0 (a queue is a memory-DoS).
    /// </para>
    /// <para>
    /// The X (security-auditor, #2151 form round): one POST per X, whether the row is read or not, and a dismissed unread
    /// row costs the one call because a dismissal also reads. A row costs at most two calls (open, then X). A panel's
    /// burst is 20 reads + 20 X + 2 "Markera alla" = 42, under 60; clearing with the X alone is 20. Sustained, new rows
    /// reach the panel only through a read, at most one per 30 s, so X alone is at most 40 a minute, under the refill
    /// of 60; open-then-remove on every row is about 80 a minute, which the burst carries for about three minutes per
    /// tab. Derived, not measured. A 429 costs a row that comes back, no data and no consent.
    /// </para>
    /// </summary>
    public PolicyOptions NotificationFeedMark { get; init; } = new()
    {
        PermitLimit = 60,
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
    /// POST /api/v1/me/job-ad-match-tags (F4-13 page-scoped match-tag batch-overlay,
    /// ADR 0076 Decision 5) — <strong>dual-partition</strong>: sub närvarande →
    /// user:-bucket, annars → ip:-bucket. Anonym-tolerant (INTE RequireAuthorization-
    /// gated — handler returnerar tom map utan UserId), så ip:-fallbacken är det bärande
    /// skyddet mot anonym batch-enumeration/DoS. Batch är taklagd (validator-cap = 100
    /// IDs → en query, ej N+1). EGEN budget (ej fold-in i JobAdStatusBatch) eftersom en
    /// /jobb-render avfyrar BÅDA overlay-anropen — delad bucket hade låtit det ena svälta
    /// det andra (bulkhead, Nygard). 60/min speglar JobAdStatusBatch/LandingPublicRead
    /// (generöst för en list-render-dekoration + scroll/multi-tab, stramt nog mot
    /// hammering). Bakom reverse-proxy kräver ip:-bucketen UseForwardedHeaders (redan
    /// wired, Program.cs). security-auditor kan ratcha ned (BLOCKING).
    /// </summary>
    public PolicyOptions JobAdMatchBatch { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /me/company-watch-criteria/{id}/companies, /{id}/ads, /{id}/ad-count (#560 PR-3, #1559)
    /// and POST /companies/search (#560 search wave, CTO F1) — FOUR routes, ONE bucket, over the same
    /// register join: the heaviest read in the house (#875 measured it). Never folded into
    /// MeListRead — a browse scan-burst must not consume the budget /oversikt's ~7-call fan-out lives
    /// on. Partitioned per UserId; anonymous -> NoLimiter, which is safe ONLY because UseRateLimiter is
    /// registered AFTER UseAuthorization (Program.cs) and all four routes are RequireAuthorization-
    /// gated. TokenBucket (#875 condition 3 — populates Retry-After; SlidingWindow does not),
    /// QueueLimit=0 (a queue is memory-DoS).
    ///
    /// <para><b>One bucket, deliberately — and this file's other policies argue the opposite way, so
    /// read the CONDITION, not the conclusion.</b> Least common mechanism splits budgets between
    /// surfaces with DIFFERENT legitimate-frequency profiles (typeahead vs list read). These four share
    /// one profile (a human paging a register result list) AND one backing resource. A bulkhead
    /// separates failure domains; there is one here. What splitting the bucket would actually cost is
    /// worked out in CompanyWatchCriteriaRateLimitWiringTests — do not restate the multiplier here, it
    /// depends on which routes you split off.</para>
    ///
    /// <para><b>The number, and how to recompute it (security-auditor 2026-09-04, #1654 — BLOCKING).</b>
    /// A token is ONE HTTP request to any of the four routes. Cost is per REQUEST, never per mediator
    /// send: /companies, /ads and /search each compose two sends and still cost one token.
    /// 15 is the BURST. The SUSTAINED rate is 12/min: TokensPerPeriod = max(1, 15/6) = 2 per
    /// ReplenishmentPeriod = 60/6 = 10 s. 15 does not divide by SegmentsPerWindow = 6 — JobAdSuggest
    /// (20/10s) has the same property and states its sustained rate explicitly; this one now does too.
    /// Measured 2026-09-04 against the dev stack, bucket full, 75 s idle between readings, calibrated
    /// against a fresh account's 15-then-429:
    ///   /foretag/branschbevakningar/{id}           2 tokens (browse + ad-count) — PER PAGE TURN
    ///   /foretag/branschbevakningar/{id}/annonser  1 token
    ///   /foretag/sok, no search term                0 tokens
    ///   /foretag/sok, search or page turn          1 token
    /// Sustained headroom: 6 detail views/min, 12 for the other two.
    /// The verified criterion is UNCHANGED — "a human pages a result list; only a scraper needs more".
    /// 6 page turns/min clears a human READING 20 rows and no longer clears one SKIMMING them. That
    /// margin is spent knowingly: 15 vs 30 does not separate a human from a scraper (MaxPage = 100 and
    /// CompanyBrowseDto's org.nr mask do that work), while doubling the cap doubles the register load
    /// one compromised account can impose. Buy the margin back by removing a call, not by raising this.
    /// The cap counts REQUESTS but the cost is in ROWS: MaxPageSize = 100 while the FE sends 20, so a
    /// script gets 5x the row throughput this derivation assumes. Known, not priced in here.
    /// An ADDITIONAL call on any of these pages spends a token off the SAME 12/min: divide 12 by the new
    /// per-view total BEFORE adding it, and re-run the measurement above. Raising PermitLimit is a
    /// security-auditor decision (BLOCKING), never a fix for a page that got chattier.</para>
    ///
    /// <para><b>#1656 (b) — the per-view token counts above are UNCHANGED; what one token buys on the
    /// two AD routes is not.</b> The personal match count was composed into the EXISTING sends rather
    /// than given a fifth route, so the detail page stays at 2 tokens and /annonser at 1 whether or not
    /// its matching axis is set. On the routes that resolve it, one token additionally buys the
    /// criterion's ad-id set and a grade pass over it. Each of those is read ONCE per request however
    /// many sends ask, because the resolver is scoped — and a criterion too broad to grade is refused
    /// without reading the set at all. Whether 15 still holds against the new per-token row cost is
    /// security-auditor's call, not this file's.</para>
    /// </summary>
    public PolicyOptions CompanyBrowse { get; init; } = new()
    {
        PermitLimit = 15,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /me/company-watch-criteria — the smart-watch LIST (#1681 part 2, ADR 0139). Its own
    /// policy since 2026-09-07, on <c>security-auditor</c>'s recommendation and Klas's decision;
    /// it left <see cref="MeListRead"/> because its per-request backend cost stopped resembling
    /// anything else in that bucket. Partitioned per UserId; anonymous → NoLimiter (safe only
    /// because <c>UseRateLimiter</c> is registered AFTER <c>UseAuthorization</c> and the route is
    /// <c>RequireAuthorization</c>-gated). TokenBucket (populates <c>Retry-After</c>;
    /// SlidingWindow does not), <c>QueueLimit = 0</c> (a queue is memory-DoS).
    ///
    /// <para>
    /// <b>A separate policy LOWERS NOTHING anyone already holds.</b> <see cref="MeListRead"/> keeps
    /// 120/min for its own routes, and a ratchet there would have been collateral on
    /// <c>/oversikt</c>'s ~7-call fan — which this route is not part of. This is the same ground on
    /// which <see cref="CompanyBrowse"/> got its own budget for a divergent cost profile (least
    /// common mechanism, Saltzer/Schroeder; bulkhead, Nygard). <b>Raising
    /// <see cref="PolicyOptions.PermitLimit"/> is expressly NOT an available remedy here</b>
    /// (security-auditor 2026-09-07) — buy margin back by removing a call.
    /// </para>
    ///
    /// <para>
    /// <b>THE DERIVATION — two halves, because the half this file kept omitting is the second one.</b>
    /// <see cref="MeListRead"/>'s own derivation is pure request AMPLIFICATION with no per-request
    /// BACKEND-COST term; that omission is how #1681 part 2 put a ~380 ms read on a 120/min budget
    /// without anything surfacing. Both halves are written out here so the next reader can redo it.
    /// </para>
    ///
    /// <para>
    /// <b>Half 1 — legitimate frequency (amplification).</b> TWO consumer pages,
    /// <c>/foretag/branschbevakningar</c> and, since 2026-09-07 (below), <c>/oversikt</c>, each at
    /// <b>~1 call per load</b>. It used to be three pages before <c>/oversikt</c> came;
    /// both detail pages left when <c>GetCriterionIdentityQuery</c> composed their heading into
    /// routes they already call, and that departure is pinned FE-side
    /// (<c>expect(getCompanyWatchCriteria).not.toHaveBeenCalled()</c> in both page tests). Against
    /// <see cref="MeListRead"/>'s ~7 calls per <c>/oversikt</c> load this is a seventh of the
    /// amplification — and it is not a debounce-burst surface: no typeahead, no popover, no poll.
    /// </para>
    ///
    /// <para>
    /// <b>The Översikt ⇄ <c>/nya</c> round trip (ADR 0175 D7, #1663 PR 2).</b> A card on
    /// <c>/oversikt</c> can carry an "N nya" pill to <c>/foretag/branschbevakningar/{id}/nya</c>, and
    /// the way back is another <c>/oversikt</c> load, which calls this route again. That is not a new
    /// consumer page — <c>/nya</c> never calls this route, pinned FE-side as on the two detail pages —
    /// but one of the two loaded more often: five fast round trips spend the burst, and after that one
    /// return per 20 s is accepted knowingly. The pills' count is not this route's; it is
    /// <see cref="CriterionNewAdCounts"/>, so half 2 below is unchanged.
    /// </para>
    ///
    /// <para>
    /// <b>Half 2 — measured per-request backend cost</b>
    /// (<c>docs/reviews/2026-09-07-1681-part2-fanin-and-corpus-measurement.md</c>): everyday
    /// (20 criteria × 202 ads, realistic profile) <b>47,3 ms</b>; every criterion refused 25,0 ms;
    /// at the ad ceiling <b>240,1 / 316,7 / 381,5 ms</b> for a realistic / rich / ceiling profile.
    /// <b>The bucket is derived against 381,5 ms, not against 47,3 ms — a bucket does not exist for
    /// the everyday case.</b>
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Each Σ errs in BOTH directions, and neither is a clean number.</b> It OVERSTATES as a
    /// sum of p95s (a sum of 95th percentiles is not the 95th percentile of the composed read — it
    /// prices every component as simultaneously unlucky). It UNDERSTATES twice, independently: the
    /// statement halves were measured on a fixture <b>25x narrower per row</b> than dev, and the
    /// same report measures row width flipping the plan and costing 2–5x at equal n; and nothing
    /// else in the handler is counted at all — the criteria load, the four Mediator behaviours, DTO
    /// mapping, serialising 20 rows. <b>So 381,5 ms is treated as a FLOOR on the worst measured
    /// state, never as its ceiling</b>, and the limit below is chosen to stay defensible if the true
    /// figure is a small multiple of it.
    /// </para>
    ///
    /// <para>
    /// <b>The anchor: ~1 second of database time per minute per account, from one route.</b> That
    /// is not invented here — it is what the house already grants for a measured-heavy authenticated
    /// read and <c>security-auditor</c> already signed: <see cref="CompanyBrowse"/> runs 12/min
    /// sustained over a capped register count measured at ~78 ms worst case ≈ <b>0,94 s/min</b>.
    /// ⚠ That 78 ms is the COUNT half only (<see cref="CompanyBrowseCriteria.MaxServableRows"/>
    /// carries the measurement); <c>/companies</c> also runs the items query, so the anchor
    /// UNDERSTATES what the house already tolerates — which biases the limit below toward the strict
    /// side, and is the direction to be wrong in. The
    /// twin (<c>ListCompanyWatchesQueryHandler</c>, 0,166 ms p95 on <see cref="MeListRead"/>) is
    /// deliberately NOT the anchor: at 120/min it places ~20 ms/min, and anchoring a heavy read on a
    /// route with no heavy state would derive a limit below one request per minute. An order-of-
    /// magnitude anchor is stated as such rather than dressed up as a precise budget.
    /// </para>
    ///
    /// <para>
    /// <b>The arithmetic.</b> 1 000 ms ÷ 381,5 ms ≈ <b>2,6 requests/min</b> → the sustained rate is
    /// <b>3/min</b>. Worst-case load at that rate is 3 × 381,5 ms = <b>1,14 s/min</b>, in the same
    /// class as the anchor; the everyday cost is 3 × 47,3 ms = <b>142 ms/min</b>. Compare what the
    /// finding priced: 120/min × 381,5 ms = <b>45,8 s of database time per minute</b> from one
    /// account, with <c>QueueLimit = 0</c> making the bucket the entire protection.
    /// </para>
    ///
    /// <para>
    /// <b>The burst is 5, and the formula pins it rather than taste.</b> Sustained rate is
    /// <c>TokensPerPeriod / ReplenishmentPeriod</c> = <c>max(1, PermitLimit / SegmentsPerWindow)</c>
    /// per <c>WindowSeconds / SegmentsPerWindow</c>. With <c>SegmentsPerWindow = 3</c> the period is
    /// 20 s, and <c>PermitLimit</c> 4 or 5 both truncate to 1 token per period = 3/min, while 6
    /// divides evenly to 2 = 6/min. <b>5 is therefore the largest burst compatible with a 3/min sustained
    /// rate</b> — the same integer-truncation shape that gives <see cref="CompanyBrowse"/> a burst of
    /// 15 over a sustained 12, and <see cref="JobAdSuggest"/> a burst of 20 over ~1,8/s.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b><see cref="PolicyOptions.SegmentsPerWindow"/> is 3 here, not the house default 6, and
    /// that is forced.</b> At Segments = 6 the replenishment period is 10 s and the minimum
    /// expressible sustained rate is 1 token / 10 s = 6/min — twice the derived figure. The cost is
    /// paid in recovery latency: an exhausted burst waits <b>20 s</b> for its next token instead of
    /// 10 s. Still far softer than a FixedWindow's 60 s ban, which is why the 2026-06-24 retune chose
    /// TokenBucket at all; named here rather than left for someone to discover.
    /// </para>
    ///
    /// <para>
    /// <b>Is 5/3 enough?</b> The tightest legitimate flow is SETUP: each created
    /// criterion redirects to the list, so creating five watches back to back spends the whole burst
    /// and the sixth waits 20 s. That is accepted knowingly and it has a cheaper fix than a bigger
    /// number — a create that revalidated without a full list re-fetch would cost no token at all.
    /// </para>
    ///
    /// <para>
    /// <b>The trigger below FIRED on 2026-09-07</b> (#1681 part 3, PR #1702). A second consumer
    /// page appeared: <c>/oversikt</c> calls this route beside <c>/foretag/branschbevakningar</c> —
    /// measured as 1 consumer on <c>c9599517</c>, 2 on <c>99e033da</c>.
    /// <c>security-auditor</c> re-derived the limit against that trigger and <b>declined to ratchet
    /// it</b>; her grounds are in
    /// <c>docs/reviews/2026-09-07-1681-part3-security-auditor.md</c>. Residual filed as #1705.
    /// </para>
    ///
    /// <para>
    /// <b>It FIRED again on 2026-09-08</b> (#1706 part 2): <c>CompanyWatchCriterionMember.MaxPerCriterion</c>
    /// moved from 1 000 to 2 500, which is one of the triggers named below by name.
    /// <c>security-auditor</c> verified the limit against it and <b>again declined to ratchet</b>.
    /// Her discharge rests on three properties rather than on a re-measured tail: this route's OUTPUT
    /// is capped by <c>CriterionMatchingAdSetResolver.MaxSetSize</c> and the callers' own ceilings, in
    /// which the member bound does not appear at all; the member set enters as an UNCORRELATED
    /// <c>ARRAY(subselect)</c> evaluated once per statement, so a higher bound adds member-scan cost
    /// and not per-ad cost; and the worst case was already corpus-saturated at the old bound. The
    /// figures are in <c>docs/reviews/2026-09-08-1706-bound-rederivation.md</c> and deliberately not
    /// restated here.
    /// </para>
    ///
    /// <para>
    /// <b>Recompute trigger.</b> Any of these invalidates the number and none of them is subtle: a
    /// second consumer page (half 1), a change to <c>CompanyWatchCriterion.MaxPerUser</c> or
    /// <c>CriterionMatchingAdSetResolver.MaxSetSize</c> (half 2 — both are multipliers on the fan),
    /// a change to <c>CompanyWatchCriterionMember.MaxPerCriterion</c> — it is the ceiling on the
    /// member set half 2's statements run over, and this bucket and that bound are ONE decision
    /// (<c>ListCompanyWatchCriteriaQueryHandler</c> says the same from its side),
    /// or a re-measurement that moves the 381,5 ms figure. Re-run the report's own protocol; do not
    /// scale one of its points, its series is non-monotone. <b>The limit is
    /// <c>security-auditor</c>'s to verify and hers to ratchet (BLOCKING); revising it UP after a
    /// production latency measurement is the direction this file permits.</b>
    /// </para>
    /// </summary>
    public PolicyOptions CompanyWatchCriteriaList { get; init; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 60,
        SegmentsPerWindow = 3,
    };

    /// <summary>
    /// POST /api/v1/me/company-watch-criteria/preview-count (#560 PR-3, CTO Fork G3) — the
    /// criterion picker's live magnitude preview ("ditt urval matchar N företag"), the
    /// FacetCounts/MatchCountPreview FAMILY: same client-debounced burst profile (~1 req/400 ms),
    /// same numbers (30/10s symmetry). EGEN bucket, inte återanvänd MatchCountPreview — det är en
    /// annan dialog på en annan sida, och delad budget hade låtit den ena preview-ytan svälta den
    /// andra (bulkhead; samma skäl MatchCountPreview inte återanvände FacetCounts). Partitionerad
    /// per UserId, anonym → NoLimiter. TokenBucket, QueueLimit=0.
    /// security-auditor BLOCKING verifierar talet.
    /// </summary>
    public PolicyOptions CriterionCountPreview { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 10,
    };

    /// <summary>
    /// GET /me/company-watch-criteria/occupation-divisions (#1682 — the bransch picker's occupation
    /// block: a typed word resolved to occupation groups and their measured huvudgrupp shares) —
    /// partitionerat per UserId (claim "sub"). Same debounce-burst profile as
    /// <see cref="CriterionCountPreview"/> (~1 req/400 ms client debounce while the user types) and
    /// its OWN bucket for the same reason: the picker now has two live reads, and a shared budget
    /// would let a typing burst starve the magnitude preview or the reverse (bulkhead, Nygard). The
    /// read is a PK lookup over ~5 000 profile rows plus an in-memory deriver call with no register
    /// join, so it is not a <c>CompanyBrowse</c> cost class. 30/10 s riktvärde, symmetri med
    /// FacetCounts/MatchCountPreview/CriterionCountPreview; security-auditor verifierar/justerar
    /// (BLOCKING). IOptions-bundet (§5.1).
    /// </summary>
    public PolicyOptions OccupationDivisions { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 10,
    };

    /// <summary>
    /// GET /job-ads/taxonomy/occupations/search (#2198, ADR 0180 — the occupation pickers'
    /// free-text search) — partitionerat per UserId (claim "sub"). One request per settled 300 ms
    /// debounce caps continuous typing at about 3.3 requests per second, and a 13-letter word typed
    /// with a pause after every key costs at most 12, inside the burst of 30. The handler is an
    /// in-memory scan with no database read, so the bucket contains floods rather than protecting
    /// cost. Its OWN bucket (bulkhead, Nygard; ADR 0043 Beslut D): never <c>taxonomy-read</c>,
    /// whose 20/60 s would starve the tree read, and never <c>occupation-divisions</c>, another
    /// surface's.
    /// 30/10 s per security-auditor's plan read 2026-10-23; raise it after the ADR 0180 timing, never
    /// lower it after shipping. IOptions-bundet (§5.1).
    /// </summary>
    public PolicyOptions OccupationSearch { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 10,
    };

    /// <summary>
    /// Användarägda /me/*-mutationer (saved-job-ads POST/DELETE, recent-searches
    /// DELETE) (Pre-4 STEG 5, TD-87) — partitionerat per UserId (claim "sub"),
    /// anonym → NoLimiter (alla RequireAuthorization-gated). Egen policy (ej
    /// AuthWrite-återanvändning) — AuthWrite är IP-partitionerad för anonym
    /// login/register-spam; att återanvända den hade läckt IP-axel-semantik in
    /// på en auth-gated användarägd yta och straffat NAT/CGN-delade IP:n
    /// (Saltzer/Schroeder least common mechanism). Konsistent med AccountDeletion
    /// (POST /me/delete) som redan är en UserId-partitionerad skriv-policy. Egen
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

    /// <summary>
    /// POST /api/v1/resumes/import (CV-upload + deterministisk parse, Fas 4 STEG B)
    /// — partitionerat per UserId (claim "sub"), anonym → NoLimiter
    /// (RequireAuthorization-gated → 401 före endpoint). Egen policy (ej MeWrite-
    /// återanvändning) — least common mechanism (Saltzer/Schroeder) + bulkhead
    /// (Nygard): en 11 MiB-buffrande-plus-extraherande upload har en helt annan
    /// resursprofil än MeWrites lättviktiga bokmärknings-/rensnings-mutationer; delad
    /// budget hade gett 30/min × 11 MiB = 330 MiB/min/användare och låtit en
    /// upload-flod svälta spara/ta-bort-mutationerna (och vice versa). 5/min ger
    /// ~1 import var 12:e sekund — täcker iterativ om-uppladdning vid en dålig parse
    /// med marginal, kapar script-flod inom en minut, och håller buffer-taket till
    /// 55 MiB/min/användare (en storleksordning under MeWrite-ekvivalenten).
    /// 5/fönster speglar AccountDeletion-disciplinen för en dyr/känslig skriv-yta.
    /// OWASP API4:2023 "Unrestricted Resource Consumption"; ADR 0045 Worker-512-MiB.
    /// senior-cto-advisor 2026-06-16 (B1a) — riktvärde, security-auditor verifierar/
    /// justerar (BLOCKING). IOptions-bundet (§5.1).
    /// </summary>
    public PolicyOptions ResumeImport { get; init; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 60,
    };

    /// <summary>
    /// POST /api/v1/me/feedback (#1979), per UserId. Ten in ten minutes covers a user who rates
    /// several pages and adds a comment or two in one sitting, while each accepted submission queues
    /// one mail to the operator, so a flood is capped before it reaches his inbox. A starting value;
    /// security-auditor verifies it as a blocking check.
    /// </summary>
    public PolicyOptions FeedbackSubmit { get; init; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 600,
    };

    /// <summary>
    /// GET /api/v1/me/feedback/prompt-state (#1979), per UserId: read once per product page render,
    /// so it carries MeListRead's numbers in a bucket of its own.
    /// </summary>
    public PolicyOptions FeedbackPromptState { get; init; } = new()
    {
        PermitLimit = 120,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /api/v1/resumes/parsed/{id}/render (deterministic QuestPDF CV-render, Fas 4 STEG B)
    /// — partitionerat per UserId (claim "sub"), anonym → NoLimiter (RequireAuthorization-gated).
    /// Egen policy (ej MeListRead-återanvändning) — least common mechanism (Saltzer/Schroeder) +
    /// bulkhead (Nygard): render kör synkron PDF-generering + dubbel DEK-decrypt (Form A raw_text
    /// + Form B parsed_content_enc) per anrop — en CPU+krypto-tung resursprofil en storleksordning
    /// över den lätta in-memory-läsningen /review (som korrekt stannar på MeListRead;
    /// /improvements retirerades med åtgärda-lagrets deferral, ADR 0112). Delad budget hade
    /// låtit 40 PDF-genereringar/min/användare svälta samma
    /// MeListRead-budget som gatar /oversikt + /resumes. 8/min täcker iterativ förhandsgranska-
    /// justera-cykel med marginal och kapar script-flod; sitter medvetet mellan ResumeImport
    /// (5/min, tyngre sällan-op) och MeWrite (30/min, lätt mutation). senior-cto-advisor
    /// 2026-06-16 (B2) — riktvärde, security-auditor verifierar/justerar (BLOCKING). IOptions (§5.1).
    /// </summary>
    public PolicyOptions ResumeRender { get; init; } = new()
    {
        PermitLimit = 8,
        WindowSeconds = 60,
    };

    /// <summary>
    /// Admin operator mutations under /api/v1/admin/jobs (trigger/retry, #204 /
    /// TD-83 PR2; absorbs TD-52/TD-98) — partitioned per UserId (claim "sub"),
    /// anonymous → NoLimiter (the admin group is RequireAuthorization-gated → 401
    /// before the endpoint). These mutations create the first admin write/DoS
    /// surface, so the limit ships WITH them (a compromised admin session could
    /// otherwise loop triggers / fan out heavy PII-processing jobs — security-
    /// auditor T5 + hangfire-schema.md §5 p.2). 60/min/UserId is generous for an
    /// operator (manual clicks) yet caps trigger-spam. FixedWindow (write policy,
    /// parity with AccountDeletion/MeWrite), QueueLimit=0 (queue = memory-DoS).
    /// </summary>
    public PolicyOptions AdminWrite { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// The admin account directory's reads (#1974, ADR 0151): the search with its counts, and the account
    /// detail. One bucket per UserId, apart from <see cref="AdminWrite"/> so typing a search never starves an
    /// admin command. The numbers are a starting value, derived from both halves of the cost:
    /// <list type="bullet">
    /// <item>Request frequency: a search debounced at 300 ms or more settles about 3.3 times a second, and each
    /// settle is one call, since the counts ride in the search response. A burst of 30 absorbs a fast typist,
    /// and the refill of 5 tokens per 1.67 s holds about 3 requests a second.</item>
    /// <item>Backend cost per request: the directory scans the account table sequentially, because a substring
    /// match cannot use the address index. Re-derive the numbers at ADR 0151 D8's trigger.</item>
    /// </list>
    /// security-auditor verifies the numbers as a blocking check, the file's convention.
    /// </summary>
    public PolicyOptions AdminRead { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 10,
    };

    /// <summary>
    /// POST /api/v1/me/company-watches/ad-hits/{jobAdId}/seen (#453 cross-channel follow-dedup) —
    /// partitionerat per UserId (claim "sub"), anonym -> NoLimiter (RequireAuthorization-gated ->
    /// 401 fore endpoint). Egen policy (ej MeWrite-atervanvandning) — least common mechanism
    /// (Saltzer/Schroeder) + bulkhead (Nygard): denna mark-seen AUTO-avfyras server-side vid VARJE
    /// ad-detalj-open (RSC Promise.all, full + modal), en materiellt hogre frekvens an nagon genuin
    /// mutation. Delad MeWrite-budget hade latit den auto-avfyrade seen-marken svalta anvandarens
    /// DELIBERATA Spara/Folj pa samma yta (bada annars MeWrite). TokenBucket (ej FixedWindow, parity
    /// per-user-lasretunen 2026-06-24) — en hogfrekvent auto-fire passar droppvis aterfyllnad battre
    /// an FixedWindows hel-fonster-bann (som klustrar 429:or vid en ad-open-burst) och populerar
    /// Retry-After rent. 60/min ar generost for rask ad-click-through sa dedupen faktiskt fungerar
    /// under normal bladdring, stramt nog mot patologisk loop; stampeln ar en billig indexerad
    /// no-op-write (budgeten skyddar ingen tung resurs, bara isolerar bucketen). senior-cto-advisor
    /// 2026-07-02 (b) — riktvarde, security-auditor verifierar/justerar (BLOCKING). IOptions (§5.1).
    /// </summary>
    public PolicyOptions FollowSeenMark { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// GET /api/v1/me/company-watch-criteria/new-ad-counts (ADR 0175 D7, #1663 PR 2) — "N nya" per
    /// Branschbevakning, one grouped statement over the owner's unseen hits. Its own bucket and not
    /// <see cref="CompanyWatchCriteriaList"/>'s: that bucket is priced on the list's gate statements, and
    /// the catalogue page that also calls the list shows no count (ADR 0175 D7's decision change).
    ///
    /// <para>
    /// <b>Half 1 — legitimate frequency.</b> ONE consumer page, <c>/oversikt</c>, at one call per load,
    /// beside <see cref="CompanyWatchCriteriaList"/> on the same load. The Översikt ⇄ <c>/nya</c> round
    /// trip is what loads it most: one call per return.
    /// </para>
    ///
    /// <para>
    /// <b>Half 2 — measured per-request cost.</b> p95 <b>33,2 ms</b> (p50 25,9): 20
    /// Scanned criteria × 2 500 unseen hits on Active ads, plus 500 hits each on Archived ads still
    /// waiting for the purge, over 43 000 ads; a reverse-order control pass gave 26,5 ms
    /// (<c>docs/reviews/2026-10-18-1663-pr2-latency.md</c>, regenerated by
    /// <c>CriterionNewAdsLatencyMeasurement</c>). Inside ADR 0045 class (a)'s 300 ms. It times the
    /// handler alone, called directly: not the Mediator behaviours, serialisation, HTTP or the network.
    /// So it is a floor on the real figure, never its ceiling.
    /// </para>
    ///
    /// <para>
    /// <b>The arithmetic.</b> Against the house anchor of ~1 s of database time per minute per account
    /// from one route (<see cref="CompanyWatchCriteriaList"/> derives it), ⌊1 000 ÷ 33,2⌋ = 30/min is
    /// the ceiling for the sustained rate. <b>20/min</b> sits below it: 20 × 33,2 ms ≈ <b>0,66 s/min</b>,
    /// which stays inside the anchor if the true figure is up to 1,50 times the measured one.
    /// 20 per minute is at least as many as the list route lets <c>/oversikt</c> load, so this bucket is
    /// never the first to stop a legitimate load. When it does stop one, the page shows the cards
    /// without pills, never a 0.
    /// </para>
    ///
    /// <para>
    /// <b>The burst is 20, at S-5's cap of 20.</b> Sustained rate is <c>max(1, PermitLimit /
    /// SegmentsPerWindow)</c> tokens per <c>WindowSeconds / SegmentsPerWindow</c>; with
    /// <c>SegmentsPerWindow = 20</c> that is exactly 1 token per 3 s = 20/min, with no truncation. The
    /// house default 6 would truncate 20 to 3 tokens per 10 s (18/min) and make an exhausted burst wait
    /// 10 s instead of 3 s; nothing here needs that wait.
    /// </para>
    ///
    /// <para>
    /// <b>Mechanics.</b> TokenBucket partitioned by <c>UserId</c> (claim <c>"sub"</c>),
    /// <c>QueueLimit = 0</c>; anonymous → NoLimiter, which only the group's
    /// <c>RequireAuthorization</c> makes safe (401 before the endpoint). <b>Never partition it by
    /// client IP</b> — see <c>RateLimitingExtensions.MeWritePolicy</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Recompute trigger.</b> A second consumer page; a change to
    /// <c>CompanyWatchCriterion.MaxPerUser</c>; anything that lets the hit stock grow past what half 2
    /// measured; the count's statement in <c>GetCriterionNewAdCountsQueryHandler</c>, or the
    /// <c>NewCriterionAdSet.NewHits</c> it composes; or a re-measurement.
    /// security-auditor verifies the numbers (BLOCKING), the file's convention.
    /// </para>
    /// </summary>
    public PolicyOptions CriterionNewAdCounts { get; init; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
        SegmentsPerWindow = 20,
    };

    /// <summary>
    /// GET /api/v1/me/company-watch-criteria/{id}/new-ads (ADR 0175 D7, #1663 PR 2) — one
    /// Branschbevakning's new ads, capped at <c>ListNewCriterionAdsQueryHandler.MaxRows</c>, each graded
    /// at read time. Its own bucket: <see cref="MeListRead"/>'s derivation never priced a grading term,
    /// and that bucket admits a new route only at the same per-request cost (security-auditor S-5).
    ///
    /// <para>
    /// <b>Half 1 — legitimate frequency.</b> ONE consumer page,
    /// <c>/foretag/branschbevakningar/{id}/nya</c>, at one call per load, entered from at most
    /// <c>CompanyWatchCriterion.MaxPerUser</c> (20) pills on <c>/oversikt</c>. Opening every pill in a
    /// row is the tightest legitimate flow, and the burst covers it.
    /// </para>
    ///
    /// <para>
    /// <b>Half 2 — measured per-request cost.</b> p95 <b>9,2 ms</b> (p50 8,3): the
    /// owner load, the capped page over hits ⋈ ads with the full <c>JobAdDto</c> row, and the grading
    /// of all of them against security-auditor S-5's ceiling profile; 3,9 ms with no stated occupation,
    /// and 9,5 ms on the reverse-order control pass. Same run, report and class as
    /// <see cref="CriterionNewAdCounts"/>, and the same floor caveat.
    /// </para>
    ///
    /// <para>
    /// <b>The arithmetic.</b> ⌊1 000 ÷ 9,2⌋ = 108/min is the ceiling for the sustained rate.
    /// <b>20/min</b> sits far below it: 20 × 9,2 ms ≈ <b>0,18 s/min</b>, inside the
    /// anchor if the true figure is five times the measured one.
    /// </para>
    ///
    /// <para>
    /// <b>The burst is 20, and S-5 caps it there.</b> Sustained rate is <c>max(1, PermitLimit /
    /// SegmentsPerWindow)</c> tokens per <c>WindowSeconds / SegmentsPerWindow</c>; with
    /// <c>SegmentsPerWindow = 20</c> that is exactly 1 token per 3 s = 20/min, with no truncation. The
    /// house default 6 would truncate 20 to 3 tokens per 10 s (18/min) and make an exhausted burst wait
    /// 10 s instead of 3 s; nothing here needs that wait.
    /// </para>
    ///
    /// <para>
    /// <b>Mechanics.</b> TokenBucket partitioned by <c>UserId</c> (claim <c>"sub"</c>),
    /// <c>QueueLimit = 0</c>; anonymous → NoLimiter, which only the group's
    /// <c>RequireAuthorization</c> makes safe (401 before the endpoint). <b>Never partition it by
    /// client IP</b> — see <c>RateLimitingExtensions.MeWritePolicy</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Recompute trigger.</b> A second consumer page; a change to <c>MaxRows</c>, to the grading's
    /// cost, or to <c>CompanyWatchCriterion.MaxPerUser</c>; or a re-measurement. Any change here is
    /// made to <see cref="CriterionNewAdsSeen"/> in the same edit. security-auditor verifies the numbers
    /// (BLOCKING).
    /// </para>
    /// </summary>
    public PolicyOptions CriterionNewAds { get; init; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
        SegmentsPerWindow = 20,
    };

    /// <summary>
    /// POST /api/v1/me/company-watch-criteria/{id}/new-ads/seen (ADR 0175 D7, #1663 PR 2) — the write
    /// that acknowledges the window <c>/nya</c> showed, sent at most once per successful render.
    /// <b>Its numbers equal <see cref="CriterionNewAds"/>'s, and that equality is the derivation</b>
    /// (security-auditor S-5): the page sends this from a Next <c>after()</c> callback, where a 429
    /// fails silently, so a smaller bucket would leave seen ads unacknowledged and show them again.
    /// Its own bucket, not <see cref="MeWrite"/>, so an automatic write cannot use up the budget for
    /// writes the user makes on purpose; and not <see cref="FollowSeenMark"/>, which is the
    /// <c>/jobb/{id}</c> stamp. The write is one conditional indexed update, so the bucket isolates
    /// rather than protects a heavy resource.
    ///
    /// <para>
    /// <b>Mechanics.</b> TokenBucket partitioned by <c>UserId</c> (claim <c>"sub"</c>),
    /// <c>QueueLimit = 0</c>; anonymous → NoLimiter, which only the group's
    /// <c>RequireAuthorization</c> makes safe (401 before the endpoint). <b>Never partition it by
    /// client IP</b> — see <c>RateLimitingExtensions.MeWritePolicy</c>.
    /// </para>
    /// </summary>
    public PolicyOptions CriterionNewAdsSeen { get; init; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
        SegmentsPerWindow = 20,
    };

    /// <summary>
    /// POST /api/v1/companies/lookup (#454, ADR 0089 D7) — partitionerat per UserId (claim "sub"),
    /// anonym -> NoLimiter (RequireAuthorization-gated -> 401 fore endpoint). Egen policy (ej
    /// MeListRead-atervanvandning) — least common mechanism (Saltzer/Schroeder) + bulkhead
    /// (Nygard): varje lookup-miss ar en potentiell UPPSTROMS-kostnad (SCB-anrop nar den riktiga
    /// adaptern aktiveras; 10 anrop/10 s per API-Id) och far inte dela budget med latta lokala
    /// lasningar. TokenBucket (droppvis aterfyllnad + rent Retry-After), QueueLimit=0 (ko = memory-
    /// DoS; en manniska skriver en handfull uppslag). 12/min ar CTO-riktvardet (10-15/min-spann,
    /// 2026-07-02) — manskligt tempo for medvetna uppslag, stramt mot enumeration/harvest via var
    /// budget; security-auditor verifierar/justerar (BLOCKING). OBS: per-user-taket skyddar INTE
    /// process-wide-SCB-budgeten — den separata process-wide-limitern följer med
    /// SCB-aktiverings-PR:en (ADR 0089 forward-note). IOptions (§5.1).
    /// </summary>
    public PolicyOptions CompanyLookup { get; init; } = new()
    {
        PermitLimit = 12,
        WindowSeconds = 60,
    };

    /// <summary>
    /// #483 Low — anonymous health endpoints GET /api/live + GET /api/ready — partitioned per IP,
    /// FixedWindow. Own policy (least common mechanism, Saltzer/Schroeder): an anonymous, unauth
    /// DoS surface must not share a protection budget with LandingPublicRead. /api/ready runs a
    /// Postgres CanConnect + two Redis PINGs per hit, so an unthrottled flood is an amplification vector;
    /// /api/live is predicate-free (cheap) but still an anonymous surface. The two SHARE this one
    /// policy (one budget per IP across both).
    /// <para>
    /// <b>Load-bearing:</b> legitimate probes (ALB target-group / container runtime, a handful of
    /// source IPs at a low cadence) must NEVER be throttled, so the limit is generous — 120/min/IP
    /// covers any orchestrator cadence (even 5s liveness+readiness = 24/min) plus manual smoke tests
    /// with wide headroom, while a flood (thousands/sec from one IP) is capped hard. Behind ALB
    /// requires UseForwardedHeaders (else all probes bucket under one proxy IP). security-auditor
    /// verifies the number (BLOCKING). Klas-lockable (ops/cost dimension). IOptions (§5.1).
    /// </para>
    /// </summary>
    public PolicyOptions HealthCheck { get; init; } = new()
    {
        PermitLimit = 120,
        WindowSeconds = 60,
    };

    /// <summary>
    /// The anonymous job-ad detail <c>GET /api/v1/public/job-ads/{id}</c> (ADR 0160) — partitioned
    /// per client IP, TokenBucket. Its own budget (least common mechanism): crawler traffic on one
    /// surface must not drain the landing page's or the health probes'. Conservative on purpose,
    /// raised from measured crawl rates rather than guessed; security-auditor approved the numbers
    /// (2026-10-10).
    /// </summary>
    public PolicyOptions PublicJobAdRead { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    /// <summary>
    /// The job sitemap's parts <c>GET /api/v1/public/job-ads/listed-ids</c> (ADR 0174) — partitioned per client IP,
    /// TokenBucket. A burst of six is three complete reads of the two parts, then one call per ten seconds.
    /// security-auditor approved the numbers (2026-10-18).
    /// </summary>
    public PolicyOptions PublicJobAdListedIds { get; init; } = new()
    {
        PermitLimit = 6,
        WindowSeconds = 60,
    };

    /// <summary>
    /// The public catalogue's pages <c>GET /api/v1/public/job-ads/catalogue</c> (ADR 0176) — partitioned per client IP,
    /// TokenBucket, the same starting numbers as the detail read. A starting number for a new endpoint, not a raise:
    /// ADR 0174 D5's readings measure it. security-auditor approved the numbers (2026-10-18).
    /// </summary>
    public PolicyOptions PublicJobAdCatalogue { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60,
    };

    public sealed class PolicyOptions
    {
        public int PermitLimit { get; init; }
        public int WindowSeconds { get; init; }

        /// <summary>
        /// Antal replenishment-slices per fönster för TokenBucket-policies (rate-limit-
        /// retune 2026-06-24, senior-cto-advisor). Endast meningsfullt för TokenBucket-policies; ignoreras av
        /// FixedWindow-policies (IP-säkerhet + write). Styr <c>ReplenishmentPeriod = Window/Segments</c> +
        /// <c>TokensPerPeriod = PermitLimit/Segments</c> → tokens återfylls var ~Window/
        /// Segments-sekund (mjuk väntan i stället för hel-fönster-bann; Klas UX-rapport).
        /// TokenBucket (ej SlidingWindow) eftersom .NET:s SlidingWindow inte populerar
        /// Retry-After (security-auditor + code-reviewer empiri 2026-06-24). Default 6.
        /// </summary>
        public int SegmentsPerWindow { get; init; } = 6;
    }
}
