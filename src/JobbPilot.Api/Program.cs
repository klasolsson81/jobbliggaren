using JobbPilot.Api.Configuration;
using JobbPilot.Api.Endpoints;
using JobbPilot.Api.RateLimiting;
using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Behaviors;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Infrastructure;
using JobbPilot.Infrastructure.Auth;
using JobbPilot.Infrastructure.Auth.Sessions;
using Mediator;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using ValidationException = JobbPilot.Application.Common.Exceptions.ValidationException;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(JobbPilot.Application.AssemblyMarker)];
});

// Pipeline-behaviors registreras explicit som open-generics per ADR 0008 + ADR 0022.
// Mediator.SourceGenerator 3.0.2 läser inte options.PipelineBehaviors vid compile-time
// från fält-references — explicit DI-registrering krävs för att Mediator runtime ska
// hitta behaviors via GetServices<IPipelineBehavior<...>>(). Delad konstant så Api/Worker
// inte driftar isär (verifieras av WorkerLayerTests).
builder.Services.AddMediatorPipelineBehaviors();

// Scheme-namnet "Bearer" speglar wire-format (Authorization: Bearer <token>), inte token-typ.
// Backend lagrar opaque session-id i Redis sedan Turn 4 (ADR 0017).
// Schemnamnet byter till "Session" när JWT-klasserna raderas i Fas 1.
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

builder.Services.AddAuthorization();
builder.Services.AddJobbPilotRateLimiting(builder.Configuration);

var app = builder.Build();

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
    catch (NotFoundException ex)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (SessionStoreUnavailableException ex)
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// ForwardedHeaders FÖRE auth + rate-limiting. Krävs i prod bakom Next.js-proxy +
// ALB/CloudFront så Connection.RemoteIpAddress reflekterar klient-IP, inte proxy-IP
// (TD-21 / Sec-Major-1). I dev körs API:t direkt → headers saknas, ingen verkan.
//
// SECURITY: KnownNetworks/KnownProxies MÅSTE konfigureras med ALB:s VPC-CIDR i prod
// innan första traffic. Konfig är direct-bound från ForwardedHeaders-sektionen
// (STEG 12) — fail-loud vid ogiltigt CIDR/IP-format. I dev (tom array) bevaras
// ASP.NET-default-beteendet (loopback only). Se docs/runbooks/aws-setup.md §3.3.
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

// HttpsRedirection bara om ALB-listenern faktiskt har en HTTPS-port att redirecta TILL.
// Bakom HTTP-only-ALB skulle redirect → port 443 (stängd) → ALB-health-check failer →
// ECS deployment_circuit_breaker triggar rollback (security-auditor STEG 13b Sec-Major-2).
// Konfig-driven via AlbOptions.HttpsEnabled (env-var Alb__HttpsEnabled från ECS task-def,
// sätts av Terraform när var.alb_https_enabled = true; default false fram till ADR 0026-trigger).
// Development-miljö behåller redirect (dotnet run använder dev-cert via Kestrel + IIS Express).
var albOptions = builder.Configuration.GetSection(AlbOptions.SectionName).Get<AlbOptions>() ?? new AlbOptions();
if (builder.Environment.IsDevelopment() || albOptions.HttpsEnabled)
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
// Rate-limiter efter auth så User-claims är populated för UserId-baserad
// partitionering (account-deletion-policy använder claim "sub").
app.UseRateLimiter();

// /health är legacy-alias; /api/ready är spec'd path per BUILD.md §15.4 (ALB target-group).
//
// TODO TD-29: /api/ready är idag liveness, inte readiness — namnet ljuger. Returnerar 200 OK
// utan DB/Redis-ping → ALB target-group registrerar tasken som "healthy" innan DbContext är
// användbar. För Fas 0/MVP räcker liveness; vid Fas 2 trafikvolym behövs strict readiness via
// AddHealthChecks().AddDbContextCheck<AppDbContext>().AddRedis(...).
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "JobbPilot.Api" }));
app.MapGet("/api/ready", () => Results.Ok(new { status = "ready", service = "JobbPilot.Api" }));

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapApplicationsEndpoints();
app.MapResumesEndpoints();

var jobAds = app.MapGroup("/api/v1/job-ads").WithTags("JobAds");

jobAds.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
    Results.Ok(await mediator.Send(new JobbPilot.Application.JobAds.Queries.ListJobAds.ListJobAdsQuery(), ct)));

jobAds.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new JobbPilot.Application.JobAds.Queries.GetJobAd.GetJobAdQuery(id), ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

jobAds.MapPost("/", async (
    JobbPilot.Application.JobAds.Commands.CreateJobAd.CreateJobAdCommand command,
    IMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(command, ct);
    return result.IsSuccess
        ? Results.Created($"/api/v1/job-ads/{result.Value}", new { id = result.Value })
        : Results.Problem(
            title: result.Error.Code,
            detail: result.Error.Message,
            statusCode: 400);
});

app.Run();

public partial class Program;
