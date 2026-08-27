using Hangfire;
using Hangfire.PostgreSql;
using Jobbliggaren.Api.Authorization;
using Jobbliggaren.Api.Configuration;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Api.HealthChecks;
using Jobbliggaren.Api.Observability;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Infrastructure.Logging;
using Jobbliggaren.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ValidationException = Jobbliggaren.Application.Common.Exceptions.ValidationException;

var builder = WebApplication.CreateBuilder(args);

// #272 SEC-3 — explicit app-wide request-body backstop. Below the framework's implicit
// ~28.6 MiB default and above the /resumes/import per-request override (11 MiB,
// ResumesEndpoints.MaxUploadBytes = the 10 MiB validator floor + 1 MiB), which stays the
// authoritative, tighter gate for CV uploads. Non-resume endpoints carry small JSON
// bodies well under this; it only tightens the unconditional default, never loosens.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16L * 1024 * 1024);

// BEFORE ADDING AddRequestTimeouts/UseRequestTimeouts, read RecruiterErasureMatchQuery's
// CommandTimeoutSeconds. The Art. 17 erasure dry run runs under a reviewed command ceiling of
// several minutes, and it completes today only because nothing here caps request execution. A
// request timeout shorter than that ceiling moves the failure back up the stack, onto the only
// human gate before an irreversible erase. That constant owns the rule and the measurement; this
// is a pointer, not a second copy.

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// #198 / ADR 0050 gate B-1 — secrets arrive as FILES on a RAM-backed mount, never as container
// environment values (Docker persists those to disk in its own container state). LAST source,
// deliberately: on the box the file is the authority. Inert in dev — with no *_FILE variables
// set it contributes zero keys, so appsettings.Local.json keeps working unchanged.
builder.Configuration.AddEnvFileSecrets();

// TD-104 / STEG 6 — persistent strukturerad logg-sink (MEL → Seq, config-gated på
// Seq:ServerUrl). Delad extension med Worker så sink-konfig inte driftar mellan hosts.
builder.Logging.AddJobbliggarenLogging(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(Jobbliggaren.Application.AssemblyMarker)];
});

// Pipeline-behaviors registreras explicit som open-generics per ADR 0008 + ADR 0022.
// Mediator.SourceGenerator 3.0.2 läser inte options.PipelineBehaviors vid compile-time
// från fält-references — explicit DI-registrering krävs för att Mediator runtime ska
// hitta behaviors via GetServices<IPipelineBehavior<...>>(). Delad konstant så Api/Worker
// inte driftar isär (verifieras av WorkerLayerTests).
builder.Services.AddMediatorPipelineBehaviors();

// Scheme-namnet "Bearer" speglar wire-format (Authorization: Bearer <token>), inte token-typ.
// Backend lagrar opaque session-id i Redis sedan Turn 4 (ADR 0017).
// Schemanamnet "Bearer" speglar wire-formatet (RFC 6750), inte token-typen. #827 raderade
// JWT-klasserna; bytet till "Session" återstår och är behavioural (ogiltigförklarar levande
// sessioner), så det är inte en följd av raderingen.
//
// ARKITEKTUR-VARNING: Lägg INTE till AddCookie() på backend. CSRF-modellen (ADR 0018)
// förutsätter att backend är icke-browser-reachable och alltid tar emot Bearer-header.
// Cookie-baserad auth på backend bryter trust-modellen.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Bearer";
        options.DefaultChallengeScheme = "Bearer";
    })
    .AddScheme<SessionAuthenticationSchemeOptions, SessionAuthenticationHandler>("Bearer", _ => { });

// Admin-policy: HTTP-layer gate for admin endpoints (defense-in-depth with the Mediator
// AdminAuthorizationBehavior). AdminRoleRequirement is satisfied by AdminRoleAuthorizationHandler,
// which resolves roles ON DEMAND and attaches ClaimTypes.Role to the principal — so ONLY admin-policy
// requests pay the identity query, not every authenticated request (#746 PR-B; epic #737 d2/d4 —
// non-admin fan-out + 429'd floods now resolve zero roles). RequireAuthenticatedUser makes the
// 401-vs-403 split explicit (anonymous → 401 challenge, no DB call). Immediate-revoke preserved:
// roles resolved fresh per request, no cache (senior-cto-advisor A1 2026-05-11).
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new AdminRoleRequirement());
    });
});
builder.Services.AddScoped<IAuthorizationHandler, AdminRoleAuthorizationHandler>();
builder.Services.AddJobbliggarenRateLimiting(builder.Configuration);

// STEG 6 (2026-05-24) — Hangfire-client (storage-only, INGEN HangfireServer).
// Api enqueue:ar BackfillJobAdSsykWorker via IBackgroundJobClient; körningen
// utförs av Worker-processens HangfireServer som läser från samma storage.
// HTTP-fri-invariant per ADR 0023 bevaras — Hangfire ligger inte i request-vägen,
// bara som klient mot delad postgres-tabell.
//
// Connection-string-resolver speglar Worker.Hosting.HangfireConnectionStringResolver
// fallback-kedjan: HangfireStorage → Postgres. I dev räcker Postgres (samma DB).
// I prod: jobbliggaren_app-roll behöver GRANT på hangfire.* för att kunna enqueue:a;
// alternativt sätt ConnectionStrings:HangfireStorage till jobbliggaren_worker-secret
// via Terraform (TD-X dokumenterar om prod-deploy aktualiseras).
var hangfireConn = builder.Configuration.GetConnectionString("HangfireStorage")
    ?? builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:HangfireStorage eller :Postgres saknas — kan inte enqueue:a Hangfire-jobb.");

builder.Services.AddHangfire(cfg => cfg
    .UseRecommendedSerializerSettings()
    .UseSimpleAssemblyNameTypeSerializer()
    .UsePostgreSqlStorage(
        opts => opts.UseNpgsqlConnection(hangfireConn),
        new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire",
            // Api ska ALDRIG migrera schemat — Worker äger schema-bootstrap.
            PrepareSchemaIfNecessary = false,
            // #688 / #693 — no-ops för en enqueue-only-klient (Api kör ingen HangfireServer, hämtar
            // inga jobb och förvärvar inget DisableConcurrentExecution-lås), men speglas från Workerns
            // HangfireStorageOptionsFactory så de två storage-registreringarna inte driftar isär. En
            // delad factory är arkitektoniskt otillgänglig: Api kan inte referera Worker och
            // Infrastructure är avsiktligt Hangfire-fritt (ADR 0023) → drift-skyddet är denna
            // spegling + kommentar, inte en enda konstruktionspunkt. #693: 12 h lås-expiry så en
            // framtida Api-HangfireServer inte tyst regredierar takeover-skyddet.
            UseSlidingInvisibilityTimeout = true,
            DistributedLockTimeout = TimeSpan.FromHours(12),
        }));

// #204 / TD-83 PR2 — Api-impl av IBackgroundJobController-porten (admin
// operatörsytans trigger/retry-mutationer). Wrappar Hangfire-klienten +
// storage (registrerade av AddHangfire ovan) så Application förblir Hangfire-fri
// (dotnet-architect-bind). Scoped paritet med Mediator-pipeline-livstiden.
builder.Services.AddScoped<
    Jobbliggaren.Application.Admin.BackgroundJobs.IBackgroundJobController,
    Jobbliggaren.Api.BackgroundJobs.HangfireBackgroundJobController>();

// Health checks — TD-29 / F2-P6 strict readiness-probe.
//
// `/api/live`: predicate _ => false → ingen check körs → 200 om processen är upp.
//              Bara liveness, för container-level orchestration (även om Fargate
//              ignorerar Docker HEALTHCHECK så ALB är auktoritativ).
//
// `/api/ready`: predicate c => c.Tags.Contains("ready") → DbContext + Redis-PING.
//               Returnerar 503 under cold-start tills BÅDE Postgres + Redis svarar.
//               ALB target-group pekar på denna (BUILD.md §15.4, modules/alb/variables.tf
//               health_check_path default "/api/ready") → tasks får INGEN trafik förrän
//               DB-pool + Redis-multiplexer är initierade.
//
// Per ADR 0005-amendment-trohet: ECS Fargate ~$30/mån fast kostnad är inte
// kostnadsskydd-relevant, men strict readiness förhindrar 503-spikes vid
// rolling-deploys under Fas 2 trafikvolym (TD-29 ursprungs-motivering från
// dotnet-architect STEG 13b 2026-05-09).
//
// AddDbContextCheck<AppDbContext> är Microsoft-paket (inte Xabaril) — pingar
// via `Database.CanConnectAsync()`. RedisHealthCheck är custom (Api/HealthChecks/)
// — undviker third-party-dep, semantiken är två linjer (IsConnected + PingAsync).
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

// HSTS-config bindas vid service-registrering så ASP.NET Cores AddHsts läser
// rätt värden. UseHsts() i pipelinen nedan gate:as på Environment + HttpsEnabled
// (samma rationale som UseHttpsRedirection). Header sätts bara på HTTPS-svar.
var hstsConfig = builder.Configuration.GetSection(HstsOptions.SectionName).Get<HstsOptions>() ?? new HstsOptions();

// Production-defense per allow-list (paritet med ForwardedHeadersConfig STEG 12).
// Gate:at på reverseProxy.HttpsEnabled — under HTTP-only Fas 0 (ADR 0026) ska
// HSTS-config inte vara obligatorisk; men om HttpsEnabled flippas måste
// MaxAgeDays>=365 + Preload-krav uppfyllas (annars tyst regression).
//
// SINGLE bind, two consumers: this HSTS validation gate, and UseHsts/UseHttpsRedirection
// in the pipeline below. Binding the same section twice would be two normalisers for one
// rule, and the divergence has a security direction — the validation could be skipped
// while UseHsts() still registers, booting Production with MaxAgeDays < 365 and no
// fail-loud (dotnet-architect, PR #1203).
var reverseProxy = builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();
if (reverseProxy.HttpsEnabled)
    hstsConfig.EnsureSafeForEnvironment(builder.Environment.EnvironmentName);

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(hstsConfig.MaxAgeDays);
    o.IncludeSubDomains = hstsConfig.IncludeSubDomains;
    o.Preload = hstsConfig.Preload;
});

// #512: throttled Error log for the session-store-unavailable 503 path (below). Singleton so
// the throttle window is shared across all requests of the host — a Redis outage fans out to
// every authenticated request, so one log per window is enough for the TD-77 alarm.
builder.Services.AddSingleton<SessionStoreUnavailableLog>();

var app = builder.Build();

// ADR 0083 Amendment 2026-08-03 — announce the auth-flow posture once per process. Read through
// IOptions so the values are the ones the handler will actually see (PostConfigure wins over config
// binding, and both resolve the same singleton, so announcement and behaviour cannot diverge).
//
// BOTH flags, deliberately. An OPEN gate with email confirmation OFF is legacy instant-login — an
// account minted with no proof the registrant owns the address — which is the posture #734 exists to
// prevent, and announcing only the gate would reproduce this class of defect one flag over. Measured
// 2026-08-03: the Auth section exists only in appsettings.Development.json, so in the Production
// configuration the handler WOULD take the legacy branch. No Production host has booted yet — that is
// a property of the configuration, not a history.
var authFlags = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
var emailConfirmationState = authFlags.RequireEmailConfirmation ? "REQUIRED" : "NOT REQUIRED";
if (authFlags.RegistrationsOpen && !app.Environment.IsDevelopment())
{
    // Warning, not Information: an open gate outside Development is a security-posture statement and
    // should be alertable rather than one Information line among a boot's dozens.
    RegistrationGateLog.AnnounceOpenOutsideDevelopment(app.Logger, "OPEN", emailConfirmationState);
}
else
{
    RegistrationGateLog.Announce(
        app.Logger, authFlags.RegistrationsOpen ? "OPEN" : "CLOSED", emailConfirmationState);
}

app.Use(async (ctx, next) =>
{
    try
    {
        await next(ctx);
    }
    catch (ValidationException ex)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { errors = ex.Errors });
    }
    catch (UnauthorizedException ex)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ReauthenticationFailedException)
    {
        // Server-enforced re-auth failure (PR2c/C5) — render the SAME ProblemDetails 401 as
        // /auth/verify (AuthProblem is the single source), so wrong-password / locked /
        // soft-deleted are byte-identical on the wire and none leaks which cause applied
        // (GDPR Art. 32 oracle-avoidance). No credential material is logged or echoed.
        await AuthProblem.InvalidCredentials().ExecuteAsync(ctx);
    }
    catch (ForbiddenException ex)
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (NotFoundException ex)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (DomainException ex)
    {
        // Invariant-brott i Domain-lagret — t.ex. EF-rehydrering ger aggregate i
        // inkonsistent state (Resume.MasterVersion saknar/duplicerar Master). 400
        // signalerar att request inte kan fullföljas mot nuvarande domänstate.
        // Per CLAUDE.md §3.4.
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { code = ex.Code, error = ex.Message });
    }
    catch (System.Security.Cryptography.CryptographicException ex)
    {
        // Fas 4b PR-9b (DPIA #659 R-F6, security-auditor Major 1): the Form C read-path opener
        // (BinaryFieldOpener) fails closed on a cold/missing owner DEK or a tampered/wrong-key
        // ciphertext. Map to a BARE 500 with ZERO exception detail — the message can name the DEK
        // context (never plaintext or DEK bytes, but internal) and must never reach the client body.
        // Log the exception TYPE ONLY (never the message, never `ex` itself, never to the response)
        // so a ciphertext-tampering / crypto anomaly keeps an integrity signal even for a failure
        // that never entered the Mediator pipeline — LoggingBehavior already logs the ones that did,
        // but this arm wraps the whole pipeline (security-auditor PR-9b Minor 1).
        Jobbliggaren.Api.Common.CryptographicFailureLog.CryptographicFailure(
            ctx.RequestServices.GetRequiredService<ILogger<Program>>(), ex.GetType().Name);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = "Ett internt fel uppstod." });
    }
    catch (SessionStoreUnavailableException ex)
    {
        // #512: log the outage BEFORE writing 503. Auth runs outside the Mediator pipeline, so
        // LoggingBehavior never sees this — without this line a Redis outage produces zero log
        // signal (the one deliberately-handled infra path was the least observable). Throttled,
        // dedicated event-id. §5/data-minimisation: only the inner exception TYPE is logged, never
        // its message (which can embed the operated Redis key → a userId) — see
        // SessionStoreUnavailableLog.
        ctx.RequestServices.GetRequiredService<SessionStoreUnavailableLog>().Emit(ex.InnerException ?? ex);
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// ForwardedHeaders FÖRE auth + rate-limiting. Required in production behind the reverse
// proxy so Connection.RemoteIpAddress reflects the client IP, not the proxy's
// (TD-21 / Sec-Major-1). I dev körs API:t direkt → headers saknas, ingen verkan.
//
// SECURITY: KnownNetworks MUST carry the reverse proxy's network CIDR before first
// traffic. Konfig är direct-bound från ForwardedHeaders-sektionen (STEG 12) — fail-loud
// vid ogiltigt CIDR/IP-format. I dev (tom array) bevaras ASP.NET-default-beteendet
// (loopback only). Value and stack are owed by #196; the requirement is ADR 0050
// Amendment 2026-08-04, gate M-5b point 3.
//
// That requirement WAS necessary but not sufficient, and #1202 changed which half is missing.
// UseForwardedHeaders only rewrites RemoteIpAddress when an X-Forwarded-For is actually
// present; measured 2026-08-04, no component in the Option B stack sent one, so six policies
// that partition on the client IP (two only for unauthenticated callers) shared a single
// bucket regardless of what this CIDR said. Closed by #1202: Caddy writes the header toward
// web and Next relays it verbatim, so populating the CIDR is now the step that decides
// whether this middleware trusts an arriving header, not one that silences a check and
// changes nothing.
var forwardedCfg = builder.Configuration
    .GetSection(ForwardedHeadersConfig.SectionName)
    .Get<ForwardedHeadersConfig>() ?? new ForwardedHeadersConfig();

// Production-defense per allow-list (security-auditor STEG 12 Sec-Major-1).
forwardedCfg.EnsureSafeForEnvironment(builder.Environment.EnvironmentName);

var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = forwardedCfg.ValidateForwardLimit(),
};
foreach (var network in forwardedCfg.ParseKnownNetworks())
    forwardedOptions.KnownIPNetworks.Add(network);
foreach (var proxy in forwardedCfg.ParseKnownProxies())
    forwardedOptions.KnownProxies.Add(proxy);

app.UseForwardedHeaders(forwardedOptions);

// HttpsRedirection bara om reverse-proxyn faktiskt har en HTTPS-port att redirecta
// TILL. Behind an HTTP-only proxy the redirect targets a closed 443, the health check
// fails and the deploy rolls back (security-auditor STEG 13b Sec-Major-2).
//
// Under Option B this stays FALSE by design, not by omission: Next reaches the API over
// plain internal HTTP, so a true here would answer 307 to every internal call and break
// the app. See ReverseProxyOptions for the full reasoning and for why UseHsts() below is
// inert under the same topology.
//
// Development-miljö behåller redirect (dotnet run använder dev-cert via Kestrel + IIS Express).
// `reverseProxy` is the single bind made above at service-registration time.

// HSTS FÖRE HttpsRedirection så att HSTS-headern sätts på alla HTTPS-svar
// (inklusive 307-redirect-svaret). Skip i Development för att undvika
// browser-HTTPS-lock på localhost (HSTS-policy persistar i `MaxAgeDays`
// dagar även efter dev-cert roterats — bryter `dotnet run` framtida sessioner).
//
// Requires the UseForwardedHeaders registration above — otherwise Request.IsHttps is
// false behind the proxy and the HSTS header is never set on the response
// (dotnet-architect Viktigt-fynd, ASP.NET Core 10 docs).
if (!builder.Environment.IsDevelopment() && reverseProxy.HttpsEnabled)
{
    app.UseHsts();
}

if (builder.Environment.IsDevelopment() || reverseProxy.HttpsEnabled)
{
    app.UseHttpsRedirection();
}

// Fas 4b PR-9b (DPIA #659 M-F2, security-auditor Minor 4): the original-file download path
// (/api/v1/resumes/files/...) must carry `Cache-Control: no-store` + `X-Content-Type-Options:
// nosniff` on EVERY response — the 200, the 404, the 401 auth challenge, and a 405 — not only the
// happy path the endpoint delegate sees. Registered BEFORE UseAuthentication and using OnStarting
// so the headers are stamped even on framework-generated responses (the auth challenge
// short-circuits before the delegate). Path-scoped so no other endpoint is affected.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/v1/resumes/files"))
    {
        ctx.Response.OnStarting(static state =>
        {
            var http = (HttpContext)state;
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Task.CompletedTask;
        }, ctx);
    }

    await next(ctx);
});

app.UseAuthentication();
app.UseAuthorization();
// Rate-limiter efter auth så User-claims är populated för UserId-baserad
// partitionering (account-deletion-policy använder claim "sub").
app.UseRateLimiter();

// Health endpoints — TD-29 stängd 2026-05-12 (F2-P6).
//
// /api/live: liveness (process up). Predicate _ => false = inga registered checks
//             evalueras → alltid 200 så länge ASP.NET-pipelinen kör. Container-
//             orchestration kan peka hit (även om Fargate ignorerar Docker
//             HEALTHCHECK).
//
// /api/ready: strict readiness. DbContext-check + Redis-PING via "ready"-tag.
//             ALB target-group pekar hit (modules/alb/variables.tf
//             health_check_path default "/api/ready"). Returnerar 503 tills
//             BÅDE Postgres + Redis svarar.
//
// Response: default HealthCheckResponseWriter skriver "Healthy" / "Unhealthy"
// som text. ALB kollar bara HTTP-status; manuella smoke-tests får text-body.
//
// #483 Low — both endpoints carry the anonymous, IP-partitioned HealthCheckPolicy: /api/ready
// runs a Postgres CanConnect + Redis PING per hit (an amplification vector for an unauth flood),
// and /api/live, though cheap, is still an anonymous surface. The limit is generous so legitimate
// ALB/orchestrator probes are never throttled (see RateLimitingOptions.HealthCheck).
app.MapHealthChecks("/api/live", new HealthCheckOptions
{
    Predicate = _ => false,
}).RequireRateLimiting(RateLimitingExtensions.HealthCheckPolicy);
app.MapHealthChecks("/api/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).RequireRateLimiting(RateLimitingExtensions.HealthCheckPolicy);

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapApplicationsEndpoints();
app.MapApplicationHistoryEndpoints();
app.MapResumesEndpoints();
app.MapAdminEndpoints();
app.MapAdminJobAdsEndpoints();
app.MapAdminCompanyWatchesEndpoints();
app.MapAdminResumesEndpoints();
app.MapAdminBackgroundJobsEndpoints();
app.MapJobAdsEndpoints();
app.MapSavedSearchesEndpoints();
app.MapRecentSearchesEndpoints();
app.MapSavedJobAdsEndpoints();
app.MapCompanyWatchesEndpoints();
app.MapCompanyWatchCriteriaEndpoints();
app.MapCompaniesEndpoints();
app.MapMeJobAdStatusEndpoints();
app.MapMeJobAdMatchEndpoints();
app.MapMeFollowedCompanyAdsEndpoints();
app.MapMeJobsEndpoints();
app.MapLandingEndpoints();

// DEV-ONLY — remove before launch (Klas), with everything they gate
// (docs/runbooks/release-checklist.md). TWO gates, deliberately not one.
//
// The token-free confirm-email seam is ENVIRONMENT-gated and nothing widens it: it force-
// confirms an address without authentication, so it must be unreachable in every deployed
// environment regardless of configuration.
if (app.Environment.IsDevelopment())
    app.MapDevEnvironmentOnlyEndpoints();

// The owner-scoped reset is CONFIGURATION-gated on top of the environment, because the box runs
// ASPNETCORE_ENVIRONMENT=Production and is the one place the onboarding flow needs re-testing
// (Klas-direktiv 2026-08-27). Fail-closed: DevToolsOptions.EnableResetMyData defaults to false,
// and the handler refuses independently of this gate.
var devTools = app.Services.GetRequiredService<IOptions<DevToolsOptions>>().Value;
if (devTools.EnableResetMyData)
    app.MapDevResetMyDataEndpoint();

// Warning, not Information, and only outside Development: a destructive throwaway tool live in a
// deployed environment is a security-posture statement that should be alertable rather than one
// Information line among a boot's dozens.
if (devTools.EnableResetMyData && !app.Environment.IsDevelopment())
    DevToolsLog.AnnounceResetMyDataEnabledOutsideDevelopment(app.Logger);

app.Run();

public partial class Program;
