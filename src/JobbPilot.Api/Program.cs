using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Behaviors;
using JobbPilot.Application.JobAds.Commands.CreateJobAd;
using JobbPilot.Application.JobAds.Queries.GetJobAd;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Infrastructure;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "JobbPilot.Api" }));

var jobAds = app.MapGroup("/api/v1/job-ads").WithTags("JobAds");

jobAds.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
    Results.Ok(await mediator.Send(new ListJobAdsQuery(), ct)));

jobAds.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new GetJobAdQuery(id), ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

jobAds.MapPost("/", async (
    CreateJobAdCommand command,
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
