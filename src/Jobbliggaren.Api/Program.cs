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
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
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

// An app-wide request-body ceiling, below the framework's implicit default.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16L * 1024 * 1024);

// BEFORE ADDING AddRequestTimeouts/UseRequestTimeouts, read RecruiterErasureMatchQuery's
// CommandTimeoutSeconds. The Art. 17 erasure dry run runs under a reviewed command ceiling of
// several minutes, and it completes today only because nothing here caps request execution. A
// request timeout shorter than that ceiling moves the failure back up the stack, onto the only
// human gate before an irreversible erase. That constant owns the rule and the measurement; this
// is a pointer, not a second copy.

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Secrets named by *_FILE variables arrive as files on a RAM-backed mount instead of as
// container environment values, which Docker persists to disk (ADR 0050 gate B-1). This is
// deliberately the last source, so on the server the file wins. With no *_FILE variables set
// it adds no keys, so local appsettings.Local.json works unchanged.
builder.Configuration.AddEnvFileSecrets();

// Structured logging to the console and, when Seq:ServerUrl is set, to Seq. Shared with
// the Worker so the two hosts' sinks cannot drift apart.
builder.Logging.AddJobbliggarenLogging(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddApplication();

// Scoped, so a criterion's ad count and its matching set are measured at most once per request
// however many handlers ask: a count and a list from two different instants could disagree.
//
// Registered here, not in AddApplication(), which both hosts call. The memo is keyed on the
// criterion id but its value is per user, so it is safe only where a scope is a request. A
// Worker scope is not (DigestDispatchJob iterates users inside one scope), so the Worker must
// not be able to resolve it at all.
builder.Services.AddScoped<CriterionMatchingAdSetResolver>();

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(Jobbliggaren.Application.AssemblyMarker)];
});

// Pipeline behaviors are registered explicitly as open generics (ADR 0008, ADR 0022): the
// Mediator source generator does not read options.PipelineBehaviors from a field reference,
// so the runtime finds them only through DI. Both hosts register the same ordered list,
// which WorkerLayerTests verifies.
builder.Services.AddMediatorPipelineBehaviors();

// The scheme is named "Bearer" after the wire format (RFC 6750), not the token: the value is
// an opaque session id looked up in Redis (ADR 0017). Renaming it to "Session" would
// invalidate live sessions, so it is a behavioural change of its own.
//
// Do NOT add AddCookie() here. The CSRF model (ADR 0018) assumes the API is unreachable from
// a browser and always receives a Bearer header; cookie authentication on the API would
// break that trust model.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Bearer";
        options.DefaultChallengeScheme = "Bearer";
    })
    .AddScheme<SessionAuthenticationSchemeOptions, SessionAuthenticationHandler>("Bearer", _ => { });

// The Admin policy gates admin endpoints at the HTTP layer, in addition to the Mediator
// AdminAuthorizationBehavior. AdminRoleAuthorizationHandler resolves roles on demand, so only
// requests under this policy pay the identity query. RequireAuthenticatedUser keeps 401 and
// 403 apart: an anonymous caller is challenged without a database call. Roles are read fresh
// on every request, with no cache, so revoking the role takes effect at once.
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

// A Hangfire client only, with no server: the API enqueues jobs that the Worker's Hangfire
// server runs from the same storage, so Hangfire never sits in the request path (ADR 0023).
// The connection string falls back from HangfireStorage to Postgres, mirroring the Worker's
// HangfireConnectionStringResolver.
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
            // The API never creates the schema; the Worker owns it.
            PrepareSchemaIfNecessary = false,
            // No-ops for an enqueue-only client, but mirrored from the Worker's
            // HangfireStorageOptionsFactory. A shared factory is not possible: the API cannot
            // reference the Worker, and Infrastructure is deliberately free of Hangfire (ADR 0023).
            UseSlidingInvisibilityTimeout = true,
            DistributedLockTimeout = TimeSpan.FromHours(12),
        }));

// The admin job-trigger and retry port, implemented over the Hangfire client registered
// above, so Application stays free of Hangfire.
builder.Services.AddScoped<
    Jobbliggaren.Application.Admin.BackgroundJobs.IBackgroundJobController,
    Jobbliggaren.Api.BackgroundJobs.HangfireBackgroundJobController>();

// Readiness checks, tagged "ready": Postgres (Database.CanConnectAsync), the persistent Redis,
// and the Redis without persistence that holds sign-in challenges. The last one is registered
// through Infrastructure because its connection type is internal there. The endpoints are
// mapped further down.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddVolatileRedisCheck();

// HSTS settings are bound at registration so AddHsts reads them; UseHsts() below is gated on
// the environment and HttpsEnabled, like UseHttpsRedirection.
var hstsConfig = builder.Configuration.GetSection(HstsOptions.SectionName).Get<HstsOptions>() ?? new HstsOptions();

// The HSTS settings are validated only when HttpsEnabled is true.
//
// SINGLE bind, two consumers: this HSTS validation gate, and UseHsts/UseHttpsRedirection
// in the pipeline below. Binding the same section twice would be two normalisers for one
// rule, and the divergence has a security direction — the validation could be skipped
// while UseHsts() still registers, booting Production with MaxAgeDays < 365 and no
// fail-loud.
var reverseProxy = builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();
if (reverseProxy.HttpsEnabled)
    hstsConfig.EnsureSafeForEnvironment(builder.Environment.EnvironmentName);

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(hstsConfig.MaxAgeDays);
    o.IncludeSubDomains = hstsConfig.IncludeSubDomains;
    o.Preload = hstsConfig.Preload;
});

// A throttled Error log for the store-unavailable 503 path below. Singleton so the
// throttle windows are shared across all requests of the host — a Redis outage fans out to
// every request on that store, so one log per window and store is enough for the #1172 alarm.
builder.Services.AddSingleton<StoreUnavailableLog>();

await using var app = builder.Build();
await app.Services.RequireApiRedisReadyAsync();

// ADR 0083 Amendment 2026-08-03 — announce the registration gate once per process. Read through IOptions so
// the value is the one the handlers will actually see (PostConfigure wins over config binding, and both resolve
// the same singleton, so announcement and behaviour cannot diverge).
RegistrationGateLog.AnnounceGate(
    app.Logger,
    app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.RegistrationsOpen,
    app.Environment.IsDevelopment());

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
        // Server-enforced re-auth failure (PR2c/C5) — render the ProblemDetails 401
        // (AuthProblem is the single source), so an unusable grant and a soft-deleted
        // account are byte-identical on the wire and neither leaks which cause applied
        // (GDPR Art. 32 oracle-avoidance). No credential material is logged or echoed.
        await AuthProblem.InvalidCredentials().ExecuteAsync(ctx);
    }
    catch (AccountAccessCommitUncertainException)
    {
        ctx.Response.Headers.CacheControl = "private, no-store";
        await Results.Problem(
            title: "Admin.AccountAccessOutcomeUnknown",
            detail: "Det gick inte att bekräfta resultatet. Läs kontots uppgifter innan du gör en ny åtgärd.",
            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(ctx);
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
    catch (ConcurrencyConflictException)
    {
        // The precommit concurrency refusal is fixed and carries no entity or exception text.
        await Results.Problem(
            detail: "Uppgifterna ändrades samtidigt. Försök igen.",
            title: "Concurrency.Conflict",
            statusCode: StatusCodes.Status409Conflict).ExecuteAsync(ctx);
    }
    catch (DomainException ex)
    {
        // A domain invariant was broken, for example an aggregate rehydrated in an
        // inconsistent state. 400: the request cannot complete against the current state.
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { code = ex.Code, error = ex.Message });
    }
    catch (System.Security.Cryptography.CryptographicException ex)
    {
        // The encrypted-file reader (BinaryFieldOpener) fails closed on a missing owner key or a
        // tampered or wrong-key ciphertext. Answer a bare 500 with no exception detail: the
        // message can name internal key context and must never reach the client. Log the
        // exception type only, never the message or `ex`, so a tampering anomaly still leaves an
        // integrity signal for a failure that never entered the Mediator pipeline.
        Jobbliggaren.Api.Common.CryptographicFailureLog.CryptographicFailure(
            ctx.RequestServices.GetRequiredService<ILogger<Program>>(), ex.GetType().Name);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = "Ett internt fel uppstod." });
    }
    catch (StoreUnavailableException ex)
    {
        // Log the outage before answering 503. The session store fails inside authentication,
        // outside the Mediator pipeline, so this is the only log line it gets. Throttled, with a
        // dedicated event id. Only the failure's type is logged, never its message, which can
        // contain the Redis key (a user id or an address fingerprint); see StoreUnavailableLog.
        // Every subtype answers the same body.
        ctx.RequestServices.GetRequiredService<StoreUnavailableLog>().Emit(ex.Store, ex.InnerType);
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { error = StoreUnavailableException.ClientMessage });
    }
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Forwarded headers come before authentication and rate limiting, so behind the reverse
// proxy Connection.RemoteIpAddress is the client's address, not the proxy's. In local
// development the API is reached directly and no header arrives.
//
// SECURITY: KnownNetworks must hold the proxy network's CIDR before the first traffic (ADR
// 0050 Amendment 2026-08-04, gate M-5b point 3). The section is bound directly and fails
// loudly on an invalid CIDR or address.
// Caddy writes X-Forwarded-For towards Next, which relays it unchanged (#1202), so this list
// is what decides whether an arriving header is trusted. The IP-partitioned rate-limit
// policies depend on it.
var forwardedCfg = builder.Configuration
    .GetSection(ForwardedHeadersConfig.SectionName)
    .Get<ForwardedHeadersConfig>() ?? new ForwardedHeadersConfig();

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

// HTTPS redirection only when the reverse proxy actually has an HTTPS port to redirect to;
// otherwise the redirect targets a closed port and health checks fail.
//
// Under Option B HttpsEnabled stays FALSE by design, not by omission: Next reaches the API over
// plain internal HTTP, so true would answer 307 to every internal call and break the app. See
// ReverseProxyOptions for the full reasoning and for why UseHsts() below is inert under the
// same topology. Development keeps the redirect, using the development certificate.
// `reverseProxy` is the single bind made above at service-registration time.

// HSTS before HTTPS redirection. Skipped in Development: the browser would remember the
// policy for localhost long after the development certificate changes.
//
// Requires the UseForwardedHeaders registration above — otherwise Request.IsHttps is
// false behind the proxy and the HSTS header is never set on the response.
if (!builder.Environment.IsDevelopment() && reverseProxy.HttpsEnabled)
{
    app.UseHsts();
}

if (builder.Environment.IsDevelopment() || reverseProxy.HttpsEnabled)
{
    app.UseHttpsRedirection();
}

// Private binary reads need these headers on framework refusals as well as successful responses.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/v1/admin/overview"))
    {
        ctx.Response.OnStarting(static state =>
        {
            ((HttpContext)state).Response.Headers.CacheControl = "private, no-store";
            return Task.CompletedTask;
        }, ctx);
    }
    if (ctx.Request.Path.Value is { } imagePath
        && ((ctx.Request.Path.StartsWithSegments("/api/v1/resumes")
                && imagePath.TrimEnd('/').EndsWith("/original", StringComparison.OrdinalIgnoreCase))
            || (ctx.Request.Path.StartsWithSegments("/api/v1/admin/feedback")
                && imagePath.TrimEnd('/').EndsWith("/screenshot", StringComparison.OrdinalIgnoreCase))))
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
// Rate limiting comes after authentication, so the user's claims are available to the
// policies that partition per user ("sub").
app.UseRateLimiter();

// /api/live is liveness: it runs no checks and answers 200 while the process serves requests.
// /api/ready runs the "ready" checks and answers 503 until Postgres and both Redis instances
// respond. Both answer plain "Healthy" or "Unhealthy".
//
// Both carry the anonymous, IP-partitioned HealthCheckPolicy: /api/ready runs a Postgres
// connect and a PING on each Redis per hit, an amplification vector for an anonymous flood.
// The limit is generous so legitimate probes are never throttled (RateLimitingOptions.HealthCheck).
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
app.MapAdminAccountsEndpoints();
app.MapAdminJobAdsEndpoints();
app.MapAdminCompanyWatchesEndpoints();
app.MapAdminResumesEndpoints();
app.MapAdminBackgroundJobsEndpoints();
app.MapAdminFeedbackEndpoints();
app.MapMeFeedbackEndpoints();
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
// The seed and login-code seams are ENVIRONMENT-gated and nothing widens them: unauthenticated, they
// open an account and hand out a login code for a reserved address, so they must be unreachable in every
// deployed environment regardless of configuration.
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

await app.RunAsync();

public partial class Program;
