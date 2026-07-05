using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

// Alias matchar GetApplicationsQueryHandlerIntegrationTests (Application-typen
// krockar med Jobbliggaren.Application-namespacet); ingen global alias i
// integrationsprojektet, så den deklareras per fil.
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// ADR 0092 D5: the denormalised <c>LastFollowUpAt</c> scalar is projected into the
/// shared <c>ApplicationDto</c> on BOTH read paths (GetApplications + GetPipeline),
/// and drives <c>effectiveWaitDays</c> in the attention evaluator so a logged
/// follow-up resets the "no response" wait. Query-handler-direct against Npgsql/
/// Testcontainers (mirrors GetApplicationsQueryHandlerIntegrationTests): the
/// LastFollowUpAt projection + evaluator interaction need a relational provider and
/// full clock control, so they live here, not in the EF-InMemory unit suite.
///
/// Follow-ups are seeded with a NULL note by design: FollowUp.Note is an encrypted
/// PII field whose owner DEK is warmed by FieldEncryptionKeyPrefetchBehavior in the
/// Mediator pipeline (ADR 0049). Direct-seeding via SaveChangesAsync bypasses that
/// pipeline, so a non-null note would have no cached DEK — the same reason the
/// existing GetApplications/GetPipeline follow-up tests seed null notes. The
/// note-round-trip through the real pipeline is covered by LogFollowUpEndpointTests.
/// </summary>
[Collection("Api")]
public class LastFollowUpWaitResetIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly IOptions<ApplicationAttentionOptions> AttentionOptions =
        Options.Create(new ApplicationAttentionOptions());

    public LastFollowUpWaitResetIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    [Fact]
    public async Task GetApplications_ProjectsLastFollowUpAt_SetAfterLoggedFollowUp_NullWithout()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        var withFollowUp = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        withFollowUp.TransitionTo(ApplicationStatus.Submitted, clock);
        withFollowUp.LogFollowUp(null, clock);
        db.Applications.Add(withFollowUp);

        var without = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        db.Applications.Add(without);

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        var withDto = result.Items.Single(a => a.Id == withFollowUp.Id.Value);
        withDto.LastFollowUpAt.ShouldNotBeNull();
        // Postgres timestamptz truncates .NET ticks to microseconds → tolerance.
        withDto.LastFollowUpAt!.Value.ShouldBe(withFollowUp.LastFollowUpAt!.Value, TimeSpan.FromSeconds(1));

        var withoutDto = result.Items.Single(a => a.Id == without.Id.Value);
        withoutDto.LastFollowUpAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetPipeline_ProjectsLastFollowUpAt_SetAfterLoggedFollowUp_NullWithout()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        var withFollowUp = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        withFollowUp.TransitionTo(ApplicationStatus.Submitted, clock);
        withFollowUp.LogFollowUp(null, clock);
        db.Applications.Add(withFollowUp);

        var without = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        without.TransitionTo(ApplicationStatus.Submitted, clock);
        db.Applications.Add(without);

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var submitted = result.Single(g => g.Status == "Submitted").Applications;
        var withDto = submitted.Single(a => a.Id == withFollowUp.Id.Value);
        withDto.LastFollowUpAt.ShouldNotBeNull();
        withDto.LastFollowUpAt!.Value.ShouldBe(withFollowUp.LastFollowUpAt!.Value, TimeSpan.FromSeconds(1));

        var withoutDto = submitted.Single(a => a.Id == without.Id.Value);
        withoutDto.LastFollowUpAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetApplications_AfterLoggedFollowUp_NoResponseLongSignalResetsToNone()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed a Submitted application whose last status change is far in the past,
        // using a fixed clock so the wait is deterministic.
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var seedClock = new FakeDateTimeProvider(t0);

        var seeker = JobSeeker.Register(_userId, "Test User", seedClock).Value;
        db.JobSeekers.Add(seeker);
        var app = DomainApplication.Create(seeker.Id, null, null, null, seedClock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, seedClock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        // "now" is 30 days later — past the 21-day ghosted threshold → NoResponseLong.
        var now = t0.AddDays(30);
        var nowClock = new FakeDateTimeProvider(now);
        var handler = new GetApplicationsQueryHandler(db, _currentUser, nowClock, AttentionOptions);

        var before = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);
        before.Items.Single().AttentionSignal.ShouldBe(ApplicationAttentionSignal.NoResponseLong);

        // Log a follow-up "today" (at now) → LastFollowUpAt = now, resetting the wait.
        app.LogFollowUp(null, nowClock);
        await db.SaveChangesAsync(CancellationToken.None);

        var after = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);
        var dto = after.Items.Single();
        dto.LastFollowUpAt.ShouldNotBeNull();
        dto.AttentionSignal.ShouldBe(ApplicationAttentionSignal.None);
    }
}
