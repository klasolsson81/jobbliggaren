using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobbliggaren.Api.RateLimiting;

/// <summary>
/// Rate-limiting-konfiguration för Jobbliggaren Api (TD-21). Tre policies:
/// account-deletion (1/60s per user), auth-write (20/min per IP),
/// auth-loose (30/min per IP).
///
/// Defaults är prod-värden; konfigurerbara via <see cref="RateLimitingOptions"/>
/// så test-miljöer kan höja limits för att inte krocka mellan tester.
///
/// Vid 429: <c>Retry-After</c>-header sätts (RFC 6585) och en strukturerad
/// warning emiteras till app-loggen utan PII (endpoint + path, ingen IP/email).
/// </summary>
public static partial class RateLimitingExtensions
{
    public const string AccountDeletionPolicy = "account-deletion";
    public const string AuthWritePolicy = "auth-write";
    public const string AuthLoosePolicy = "auth-loose";
    public const string ListReadPolicy = "list-read";
    public const string SuggestPolicy = "suggest";
    public const string TaxonomyReadPolicy = "taxonomy-read";
    public const string FacetCountsPolicy = "facet-counts";
    public const string MatchCountPreviewPolicy = "match-count-preview";
    public const string LandingPublicReadPolicy = "landing-public-read";
    public const string MeListReadPolicy = "me-list-read";
    public const string JobAdStatusBatchPolicy = "job-ad-status-batch";
    public const string JobAdMatchBatchPolicy = "job-ad-match-batch";
    public const string MeWritePolicy = "me-write";
    public const string FollowSeenMarkPolicy = "follow-seen-mark";
    public const string CompanyLookupPolicy = "company-lookup";
    public const string ResumeImportPolicy = "resume-import";
    public const string ResumeRenderPolicy = "resume-render";
    public const string AdminWritePolicy = "admin-write";

    [LoggerMessage(2001, LogLevel.Warning,
        "Rate limit exceeded. Path={Path} Method={Method}")]
    private static partial void LogRateLimitExceeded(
        ILogger logger, string path, string method);

    public static IServiceCollection AddJobbliggarenRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rateLimitOpts = configuration.GetSection(RateLimitingOptions.SectionName)
            .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // OnRejected — strukturerad warning + Retry-After-header (Sec-Major-3).
            // Loggar inte PII (klient-IP är personuppgift per GDPR Recital 30; email/
            // session är direkt PII). Endpoint + path räcker för incident-respons.
            options.OnRejected = (ctx, _) =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Jobbliggaren.Api.RateLimiting");
                LogRateLimitExceeded(
                    logger,
                    ctx.HttpContext.Request.Path,
                    ctx.HttpContext.Request.Method);

                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            // Partition: UserId (claim "sub"). Skyddar mot kompromettera-session-radera-
            // konto-DoS + power-user resource-DoS. Anonymous → NoLimiter eftersom
            // RequireAuthorization returnerar 401 innan endpoint exekveras (Sec-Minor-1).
            options.AddPolicy(AccountDeletionPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-deletion");

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.AccountDeletion.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.AccountDeletion.WindowSeconds),
                        // QueueLimit=0 ger fail-fast 429 — höj inte (DoS-risk via queue-
                        // memory-exhaustion + latency-spike som döljer attack-signal).
                        QueueLimit = 0,
                    });
            });

            // Partition: IP (Connection.RemoteIpAddress). Bromsar credential-stuffing
            // och registration-spam. Vid prod bakom ALB krävs UseForwardedHeaders så
            // klient-IP plockas från X-Forwarded-For (TD-21 / Sec-Major-1) — annars
            // hamnar alla i samma proxy-IP-bucket och rate-limit blir effektivt no-op.
            options.AddPolicy(AuthWritePolicy, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.AuthWrite.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.AuthWrite.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: IP. Mer permissiv än AuthWrite eftersom logout är idempotent
            // och inte öppnar abuse-vektor på samma sätt som login/register.
            options.AddPolicy(AuthLoosePolicy, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.AuthLoose.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.AuthLoose.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: UserId (claim "sub"). Skyddar list/search-endpoints med
            // wildcard-LIKE-mönster mot multi-query-DoS från komprometterat
            // konto. Auth-gated → anonym fångas av RequireAuthorization
            // (NoLimiter bypass). Per CTO-rond 2026-05-13 F2-P9 + OWASP API4:2023
            // "Unrestricted Resource Consumption". Generisk policy — återanvänds
            // på framtida list/search-endpoints (applications, resumes, etc.)
            // per Martin 2017 §13 REP. 60/min är 6-20x över legit power-user-
            // värde (3-10 req/min vid scroll+filter), revisit-trigger Fas 7+.
            options.AddPolicy(ListReadPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-list-read");

                // TokenBucket (retune 2026-06-24) — REP/CCP-koherens med MeListRead (samma
                // per-user läs-komponent delar failure-ergonomik + Retry-After-kontrakt). Tal oförändrat.
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.ListRead.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.ListRead.PermitLimit / rateLimitOpts.ListRead.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.ListRead.WindowSeconds / (double)rateLimitOpts.ListRead.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Partition: UserId (claim "sub"). Dedikerad typeahead-policy
            // (ej ListRead-återanvändning) — typeahead = 1 req/keystroke,
            // least common mechanism (Saltzer/Schroeder): strypning av
            // typeahead får inte svälta användarens parallella list/detalj-
            // queries. Auth-gated → anonym fångas av RequireAuthorization
            // (NoLimiter bypass). senior-cto-advisor 2026-05-16 (ADR 0042
            // Beslut C, Batch 5). Parametrar IOptions-bundna (§5.1).
            options.AddPolicy(SuggestPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-suggest");

                // TokenBucket (retune 2026-06-24) — per-user läs-komponent. Tal oförändrat.
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.Suggest.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.Suggest.PermitLimit / rateLimitOpts.Suggest.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.Suggest.WindowSeconds / (double)rateLimitOpts.Suggest.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Partition: UserId (claim "sub"). Dedikerad taxonomi-policy
            // (ADR 0043 MAP-3, senior-cto-advisor 2026-05-17) — least common
            // mechanism (Saltzer/Schroeder): statisk referensdata-yta delar
            // inte skyddsbudget med list/suggest. Auth-gated → anonym fångas
            // av RequireAuthorization (NoLimiter bypass). Parametrar
            // IOptions-bundna (§5.1). security-auditor BLOCKING verifierar tal.
            options.AddPolicy(TaxonomyReadPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-taxonomy");

                // TokenBucket (retune 2026-06-24) — per-user läs-komponent. Tal oförändrat.
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.TaxonomyRead.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.TaxonomyRead.PermitLimit / rateLimitOpts.TaxonomyRead.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.TaxonomyRead.WindowSeconds / (double)rateLimitOpts.TaxonomyRead.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Partition: UserId (claim "sub"). Dedikerad facet-counts-policy
            // (ADR 0067 Beslut 4 Fas E2c, senior-cto-advisor VAL 1 2026-06-11) —
            // least common mechanism (Saltzer/Schroeder): facet-ytan är
            // client-side debounce-burst (Ort-popovern ×2 parallella requests)
            // och får inte dela budget med ListRead-RSC-refetcharna — delad
            // budget hade strypt LISTAN av sin egen dekoration (bulkhead,
            // Nygard). Auth-gated → anonym fångas av RequireAuthorization
            // (NoLimiter bypass). Parametrar IOptions-bundna (§5.1).
            // security-auditor BLOCKING verifierar tal (riktvärde 30/10s).
            options.AddPolicy(FacetCountsPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-facet-counts");

                // TokenBucket (retune 2026-06-24) — per-user läs-komponent. Tal oförändrat.
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.FacetCounts.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.FacetCounts.PermitLimit / rateLimitOpts.FacetCounts.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.FacetCounts.WindowSeconds / (double)rateLimitOpts.FacetCounts.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Partition: UserId (claim "sub"). Epik #526 (ADR 0088) — dedikerad bucket för
            // live sök-preview-räknaren i matchnings-setup-modalen. Samma debounce-burst-profil
            // som FacetCounts (~1 req/400 ms klient-debounce) → egen budget (least common
            // mechanism / bulkhead, Nygard): en redigerings-burst i setup-modalen får inte svälta
            // MeListRead som /oversikt redan fläktar ut ~7×. TokenBucket (ej SlidingWindow —
            // populerar inte Retry-After; security-auditor BLOCKING 2026-06-24), QueueLimit=0
            // (kö = memory-DoS). Auth-gated → anonym fångas av RequireAuthorization (NoLimiter
            // bypass). Parametrar IOptions-bundna. security-auditor BLOCKING verifierar tal
            // (riktvärde 30/10s, symmetri med FacetCounts).
            options.AddPolicy(MatchCountPreviewPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-match-count-preview");

                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.MatchCountPreview.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.MatchCountPreview.PermitLimit / rateLimitOpts.MatchCountPreview.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.MatchCountPreview.WindowSeconds / (double)rateLimitOpts.MatchCountPreview.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Partition: IP. Dedikerad policy för publik anonym landing-stats
            // (GET /api/v1/landing/stats) — ADR 0064. Återanvänder INTE
            // ListReadPolicy som NoLimiter:ar anonyma (auth-gated semantik); mixar
            // inte UserId-semantik med IP-semantik (Saltzer/Schroeder least common
            // mechanism — anonym DoS-yta får inte dela skyddsbudget med autentiserad
            // list-yta). senior-cto-advisor 2026-05-23 (agentId a1da26dc2029a5def).
            // 60/min/IP är generöst för aggressiv prefetch + multi-tab, stramt nog
            // för att stoppa hammering. Bakom ALB kräver UseForwardedHeaders satt
            // (Sec-Major-1) annars hamnar alla i samma proxy-IP-bucket.
            options.AddPolicy(LandingPublicReadPolicy, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.LandingPublicRead.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.LandingPublicRead.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: UserId (claim "sub"). Auth-gated GET-ytor under /me/* +
            // /applications/pipeline + /resumes (Pre-4 STEG 5, TD-92). Egen policy
            // (ej ListRead-återanvändning) — /oversikt avfyrar 6 parallella BE-anrop
            // (Promise.all) per sidladdning mot tyngre objekt-grafer → 6× amplifiering
            // får en egen, snävare budget och svälter inte den publika sök-listan
            // (bulkhead, Nygard; OWASP API4:2023). Auth-gated → anonym fångas av
            // RequireAuthorization (NoLimiter bypass). dotnet-architect + senior-cto-
            // advisor 2026-06-14. Parametrar IOptions-bundna (§5.1). security-auditor
            // BLOCKING verifierar tal (riktvärde 40/min).
            options.AddPolicy(MeListReadPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-me-list-read");

                // TokenBucket (retune 2026-06-24): mjuk recovery (tokens återfylls per
                // period → ~Window/Segments väntan i stället för FixedWindows hel-fönster-bann)
                // OCH populerar Retry-After rent — .NET:s SlidingWindow gör INTE det
                // (security-auditor BLOCKING-empiri 2026-06-24; CTO-förauktoriserad fallback).
                // QueueLimit=0 kvar (kö = memory-DoS). Partition/anon-bypass oförändrad.
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.MeListRead.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.MeListRead.PermitLimit / rateLimitOpts.MeListRead.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.MeListRead.WindowSeconds / (double)rateLimitOpts.MeListRead.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // DUAL partition: sub närvarande → user:-bucket, annars → ip:-bucket.
            // POST /me/job-ad-status (ADR 0063 per-user-overlay-batch, Pre-4 STEG 5,
            // TD-87) är anonym-tolerant (INTE RequireAuthorization-gated — handler
            // returnerar tom DTO utan UserId), så den vanliga UserId→NoLimiter-bypassen
            // hade lämnat ytan helt oskyddad mot anonym batch-enumeration/DoS. ip:-
            // fallbacken är därför TD-87:s bärande skyddsegenskap. Prefixen user:/ip:
            // håller de två partition-rummen disjunkta (en sub kan aldrig kollidera
            // med en IP-sträng). Bakom reverse-proxy kräver ip:-grenen
            // UseForwardedHeaders (redan wired, Program.cs) annars hamnar alla i
            // proxy-IP-bucketen. senior-cto-advisor 2026-06-14 Beslut B (60/min,
            // speglar LandingPublicRead). security-auditor BLOCKING verifierar tal.
            options.AddPolicy(JobAdStatusBatchPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var partitionKey = string.IsNullOrEmpty(userId)
                    ? $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}"
                    : $"user:{userId}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.JobAdStatusBatch.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.JobAdStatusBatch.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // DUAL partition (parity JobAdStatusBatch): sub närvarande → user:-bucket,
            // annars → ip:-bucket. POST /me/job-ad-match-tags (F4-13 page-scoped match-tag
            // batch-overlay, ADR 0076 Decision 5) är anonym-tolerant (INTE
            // RequireAuthorization-gated — handler returnerar tom map utan UserId), så den
            // vanliga UserId→NoLimiter-bypassen hade lämnat ytan oskyddad mot anonym
            // batch-enumeration/DoS; ip:-fallbacken är det bärande skyddet. EGEN policy
            // (ej JobAdStatusBatch-återanvändning) — en /jobb-render avfyrar BÅDA overlay-
            // anropen, så en delad bucket hade låtit det ena svälta det andra (bulkhead,
            // Nygard). 60/min speglar JobAdStatusBatch/LandingPublicRead. Bakom
            // reverse-proxy kräver ip:-grenen UseForwardedHeaders (redan wired,
            // Program.cs). security-auditor BLOCKING verifierar talet.
            options.AddPolicy(JobAdMatchBatchPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var partitionKey = string.IsNullOrEmpty(userId)
                    ? $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}"
                    : $"user:{userId}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.JobAdMatchBatch.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.JobAdMatchBatch.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: UserId (claim "sub"). Användarägda /me/*-mutationer
            // (saved-job-ads POST/DELETE, recent-searches DELETE) (Pre-4 STEG 5,
            // TD-87). Egen policy (ej AuthWrite-återanvändning) — AuthWrite är
            // IP-partitionerad för anonym login/register-spam; återanvändning hade
            // läckt IP-axel in på auth-gated användarägd yta och straffat NAT/CGN
            // (Saltzer/Schroeder). Konsistent med AccountDeletion (UserId-partitionerad
            // skriv-policy). Egen budget (ej fold-in i MeListRead) så en läs-burst
            // inte svälter en mutation (bulkhead, Nygard). Auth-gated → anonym fångas
            // av RequireAuthorization (NoLimiter bypass). senior-cto-advisor 2026-06-14
            // Beslut A1. security-auditor BLOCKING verifierar tal (riktvärde 30/min).
            options.AddPolicy(MeWritePolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-me-write");

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.MeWrite.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.MeWrite.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: UserId (claim "sub"). Dedikerad follow-seen-mark-policy (#453) — ej
            // MeWrite-återanvändning: least common mechanism (Saltzer/Schroeder) + bulkhead (Nygard).
            // POST /me/company-watches/ad-hits/{jobAdId}/seen AUTO-avfyras server-side vid VARJE
            // ad-detalj-open (RSC Promise.all, full + modal) — materiellt högre frekvens än någon
            // genuin mutation. En delad MeWrite-bucket hade låtit den auto-avfyrade seen-marken svälta
            // användarens deliberata Spara/Följ på samma yta (falsk paritet med markMatchesSeen som
            // avfyras sällan). Egen bucket → kan varken svälta eller svältas av genuina writes. Auth-
            // gated → anonym fångas av RequireAuthorization (NoLimiter bypass). senior-cto-advisor
            // 2026-07-02 (b), riktvärde 60/min; security-auditor verifierar (BLOCKING). IOptions (§5.1).
            options.AddPolicy(FollowSeenMarkPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-follow-seen-mark");

                // TokenBucket (ej FixedWindow) — en högfrekvent auto-fire passar droppvis återfyllnad
                // (mjuk kontinuerlig recovery + rent Retry-After) bättre än FixedWindows hel-fönster-
                // bann som klustrar 429:or vid en ad-open-burst. QueueLimit=0 kvar (kö = memory-DoS).
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.FollowSeenMark.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.FollowSeenMark.PermitLimit / rateLimitOpts.FollowSeenMark.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.FollowSeenMark.WindowSeconds / (double)rateLimitOpts.FollowSeenMark.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // #454 (ADR 0088 D7) — POST /api/v1/companies/lookup. Egen policy (least common
            // mechanism + bulkhead): varje lookup-miss är en potentiell UPPSTRÖMS-kostnad (SCB-
            // anrop när den riktiga adaptern aktiveras; 10 anrop/10 s per API-Id) och delar
            // därför inte budget med lätta lokala läsningar. TokenBucket per UserId (droppvis
            // återfyllnad + rent Retry-After), QueueLimit=0 (kö = memory-DoS; en människa skriver
            // en handfull uppslag). Anonym → NoLimiter (RequireAuthorization → 401 före endpoint).
            // 12/min = CTO-riktvärdet (10–15/min-spann, 2026-07-02); security-auditor verifierar
            // (BLOCKING). OBS per-user-taket skyddar INTE process-wide-SCB-budgeten — separat
            // process-wide-limiter följer med SCB-aktiverings-PR:en. IOptions-bundna (§5.1).
            options.AddPolicy(CompanyLookupPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-company-lookup");

                return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rateLimitOpts.CompanyLookup.PermitLimit,
                        TokensPerPeriod = Math.Max(1, rateLimitOpts.CompanyLookup.PermitLimit / rateLimitOpts.CompanyLookup.SegmentsPerWindow),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(
                            rateLimitOpts.CompanyLookup.WindowSeconds / (double)rateLimitOpts.CompanyLookup.SegmentsPerWindow),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            // Partition: UserId (claim "sub"). Dedikerad CV-upload-policy (ej MeWrite-
            // återanvändning) — least common mechanism (Saltzer/Schroeder) + bulkhead
            // (Nygard): en 11 MiB-buffrande + parse-tung upload delar inte budget med
            // lättviktiga /me-mutationer (330 MiB/min vid MeWrite-återbruk → svält av
            // spara/ta-bort). Auth-gated → anonym fångas av RequireAuthorization
            // (NoLimiter bypass). senior-cto-advisor 2026-06-16 (B1a), riktvärde 5/min;
            // security-auditor verifierar (BLOCKING). Parametrar IOptions-bundna (§5.1).
            options.AddPolicy(ResumeImportPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-resume-import");

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.ResumeImport.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.ResumeImport.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: UserId (claim "sub"). Dedikerad CV-render-policy (ej MeListRead-
            // återanvändning) — least common mechanism (Saltzer/Schroeder) + bulkhead (Nygard):
            // QuestPDF-generering + dubbel DEK-decrypt är CPU+krypto-tungt och delar inte budget
            // med de lätta in-memory /review + /improvements (40 renders/min hade kunnat svälta
            // MeListRead som gatar /oversikt + /resumes). Auth-gated → anonym fångas av
            // RequireAuthorization (NoLimiter bypass). senior-cto-advisor 2026-06-16 (B2),
            // riktvärde 8/min; security-auditor verifierar (BLOCKING). Parametrar IOptions (§5.1).
            options.AddPolicy(ResumeRenderPolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-resume-render");

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.ResumeRender.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.ResumeRender.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Admin operator mutations under /api/v1/admin/jobs (trigger/retry, #204 /
            // TD-83 PR2; absorbs TD-52/TD-98). Partition: UserId (claim "sub"); anonymous
            // → NoLimiter (admin group is RequireAuthorization-gated → 401 before endpoint).
            // FixedWindow write policy (parity with AccountDeletion/MeWrite), QueueLimit=0.
            // The limit ships WITH the first admin write surface — a compromised admin
            // session could otherwise loop triggers / fan out heavy PII jobs.
            options.AddPolicy(AdminWritePolicy, ctx =>
            {
                var userId = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("anonymous-admin-write");

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.AdminWrite.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.AdminWrite.WindowSeconds),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
