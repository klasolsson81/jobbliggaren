using JobbPilot.Api.Endpoints;
using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Behaviors;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using ValidationException = JobbPilot.Application.Common.Exceptions.ValidationException;
using JobbPilot.Infrastructure;
using JobbPilot.Infrastructure.Auth;
using JobbPilot.Infrastructure.Auth.Sessions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(JobbPilot.Application.AssemblyMarker)];
    options.PipelineBehaviors =
    [
        typeof(LoggingBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(UnitOfWorkBehavior<,>),
    ];
});

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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "JobbPilot.Api" }));

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapApplicationsEndpoints();

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
