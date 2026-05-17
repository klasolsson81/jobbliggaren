using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace JobbPilot.Api.RateLimiting;

/// <summary>
/// Rate-limiting-konfiguration för JobbPilot Api (TD-21). Tre policies:
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
    public const string InvitationRedeemPolicy = "invitation-redeem";
    public const string WaitlistSignupPolicy = "waitlist-signup";
    public const string ListReadPolicy = "list-read";
    public const string SuggestPolicy = "suggest";
    public const string TaxonomyReadPolicy = "taxonomy-read";

    [LoggerMessage(2001, LogLevel.Warning,
        "Rate limit exceeded. Path={Path} Method={Method}")]
    private static partial void LogRateLimitExceeded(
        ILogger logger, string path, string method);

    public static IServiceCollection AddJobbPilotRateLimiting(
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
                    .CreateLogger("JobbPilot.Api.RateLimiting");
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

            // Partition: IP. Stoppar brute-force mot invitation-token-hash +
            // enumeration. 5/timme räcker eftersom legitim användare bara löser
            // in en gång. Per ADR 0005 amendment 2026-05-12.
            options.AddPolicy(InvitationRedeemPolicy, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.InvitationRedeem.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.InvitationRedeem.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            // Partition: IP. Anonym waitlist-signup-endpoint kräver spam-skydd.
            // 3/24h räcker — legitim användare skriver upp sig en gång. Per
            // ADR 0005 amendment 2026-05-12.
            options.AddPolicy(WaitlistSignupPolicy, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.WaitlistSignup.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.WaitlistSignup.WindowSeconds),
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

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.ListRead.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.ListRead.WindowSeconds),
                        QueueLimit = 0,
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

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.Suggest.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.Suggest.WindowSeconds),
                        QueueLimit = 0,
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

                return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOpts.TaxonomyRead.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOpts.TaxonomyRead.WindowSeconds),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
