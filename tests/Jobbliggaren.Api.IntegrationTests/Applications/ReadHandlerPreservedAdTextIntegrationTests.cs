using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;
using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// #1827: ApplicationDto.HasPreservedAdText in the two list read handlers. Every state is
// produced by the actor production uses: the apply command's handler (the capture), a
// manual application (no snapshot), TransitionTo a terminal status on the saved aggregate
// (the minimisation, written as an UPDATE) and TransitionTo back out of it (Undo, which
// never restores the text). Each test reads both handlers, so a projection dropped from one
// of them fails here.
[Collection("Api")]
public class ReadHandlerPreservedAdTextIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly IOptions<ApplicationAttentionOptions> AttentionOptions =
        Options.Create(new ApplicationAttentionOptions());

    private static readonly DateTimeOffset JobAdPublishedAt =
        new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    public ReadHandlerPreservedAdTextIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    [Fact]
    public async Task ListHandlers_WithCapturedSnapshot_ProjectHasPreservedAdTextTrue()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedSeekerAsync(db, clock, _userId);
        await ApplyToSeededJobAdAsync(db, clock);

        var (list, pipeline) = await ReadBothAsync(db, clock);

        list.HasPreservedAdText.ShouldBeTrue();
        pipeline.HasPreservedAdText.ShouldBeTrue();
    }

    [Fact]
    public async Task ListHandlers_WithoutSnapshot_ProjectHasPreservedAdTextFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var manual = ManualPosting.Create(
            "Manuell titel", "Manuellt företag", "https://example.com/manuell",
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).Value;
        var app = DomainApplication.Create(seeker.Id, null, null, manual, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var (list, pipeline) = await ReadBothAsync(db, clock);

        list.HasPreservedAdText.ShouldBeFalse();
        pipeline.HasPreservedAdText.ShouldBeFalse();
    }

    [Fact]
    public async Task ListHandlers_AfterTerminalMove_ProjectHasPreservedAdTextFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedSeekerAsync(db, clock, _userId);
        var app = await ApplyToSeededJobAdAsync(db, clock);
        app.TransitionTo(ApplicationStatus.Rejected, clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var (list, pipeline) = await ReadBothAsync(db, clock);

        list.HasPreservedAdText.ShouldBeFalse();
        pipeline.HasPreservedAdText.ShouldBeFalse();
    }

    [Fact]
    public async Task ListHandlers_AfterTerminalMoveAndUndo_ProjectHasPreservedAdTextFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedSeekerAsync(db, clock, _userId);
        var app = await ApplyToSeededJobAdAsync(db, clock);
        app.TransitionTo(ApplicationStatus.Rejected, clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        app.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var (list, pipeline) = await ReadBothAsync(db, clock);

        list.Status.ShouldBe(ApplicationStatus.Submitted.Name);
        list.HasPreservedAdText.ShouldBeFalse();
        pipeline.HasPreservedAdText.ShouldBeFalse();
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    // Applies through the command handler production runs, then returns the saved
    // aggregate, still tracked, for the transitions that follow.
    private async Task<DomainApplication> ApplyToSeededJobAdAsync(
        AppDbContext db, IDateTimeProvider clock)
    {
        var jobAd = JobAd.Create(
            "Backend-utvecklare",
            Company.Create("Klarna").Value,
            "En beskrivning",
            "https://example.com/jobb/1",
            JobSource.Platsbanken,
            JobAdPublishedAt,
            null,
            clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var created = await new CreateApplicationFromJobAdCommandHandler(db, _currentUser, clock)
            .Handle(new CreateApplicationFromJobAdCommand(jobAd.Id.Value), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var id = new Jobbliggaren.Domain.Applications.ApplicationId(created.Value);
        return await db.Applications.FirstAsync(a => a.Id == id, CancellationToken.None);
    }

    private async Task<(ApplicationDto List, ApplicationDto Pipeline)> ReadBothAsync(
        AppDbContext db, IDateTimeProvider clock)
    {
        var listHandler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var listResult = await listHandler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        var pipelineHandler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var pipelineResult = await pipelineHandler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var list = listResult.Items.ShouldHaveSingleItem();
        var pipeline = pipelineResult.ShouldHaveSingleItem().Applications.ShouldHaveSingleItem();
        return (list, pipeline);
    }
}
