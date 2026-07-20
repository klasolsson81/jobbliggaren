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
    /// </summary>
    public PolicyOptions MeListRead { get; init; } = new()
    {
        PermitLimit = 120,
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
    /// GET /api/v1/me/company-watch-criteria/{id}/companies (#560 PR-3, CTO Fork G4) — the
    /// criteria browse over the 1.17M-row company register: by measurement the HEAVIEST read in
    /// the house (25–163 ms typical per call, items + capped count + magnitude). Dedicated policy,
    /// never folded into MeListRead — a browse scan-burst must not consume the budget /oversikt's
    /// ~7-call fan-out lives on, and a limit tuned for light /me-reads would let the heavy browse
    /// through too freely (least common mechanism / bulkhead — the doctrine every policy in this
    /// file applies). Partitionerad per UserId, anonym → NoLimiter (RequireAuthorization-gated).
    /// TokenBucket (#875 gate condition 3 — populates Retry-After; SlidingWindow does not),
    /// QueueLimit=0. 15/min = CTO riktvärde 2026-07-16 (a human pages a result list; only a
    /// scraper needs more) — <b>security-auditor BLOCKING verifierar talet</b>.
    /// </summary>
    public PolicyOptions CompanyBrowse { get; init; } = new()
    {
        PermitLimit = 15,
        WindowSeconds = 60,
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
    /// Postgres CanConnect + Redis PING per hit, so an unthrottled flood is an amplification vector;
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

    public sealed class PolicyOptions
    {
        public int PermitLimit { get; init; }
        public int WindowSeconds { get; init; }

        /// <summary>
        /// Antal replenishment-slices per fönster för TokenBucket-policies (rate-limit-
        /// retune 2026-06-24, senior-cto-advisor). Endast meningsfullt för de per-user
        /// policies som använder TokenBucket (MeListRead/ListRead/FacetCounts/Suggest/
        /// TaxonomyRead/FollowSeenMark/CompanyLookup); ignoreras av FixedWindow-policies
        /// (IP-säkerhet + write). Styr <c>ReplenishmentPeriod = Window/Segments</c> +
        /// <c>TokensPerPeriod = PermitLimit/Segments</c> → tokens återfylls var ~Window/
        /// Segments-sekund (mjuk väntan i stället för hel-fönster-bann; Klas UX-rapport).
        /// TokenBucket (ej SlidingWindow) eftersom .NET:s SlidingWindow inte populerar
        /// Retry-After (security-auditor + code-reviewer empiri 2026-06-24). Default 6.
        /// </summary>
        public int SegmentsPerWindow { get; init; } = 6;
    }
}
