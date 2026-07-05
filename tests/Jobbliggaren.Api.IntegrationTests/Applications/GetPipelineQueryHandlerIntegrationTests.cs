using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

// Alias matchar Application.UnitTests GlobalUsings.cs (Application-typen
// krockar med Jobbliggaren.Application-namespacet); integrationsprojektet har
// ingen global alias, så den deklareras per fil.
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// Flyttad från Jobbliggaren.Application.UnitTests (EF InMemory) till Npgsql/
// Testcontainers per senior-cto-advisor rev2 (B). Pipeline-handlern joinar
// db.JobAds FÖRE materialisering (ADR 0048 EN LEFT JOIN) — relationell
// query-translation, ej en ren unit. Scenarier + assertions bevarade 1:1;
// testnamn bevarade för spårbar täckning (ADR 0044). Mönster kopierat från
// ManualPostingPersistenceTests.cs. User-scoping (ADR 0031) bevaras via
// unik seedad user per test.
[Collection("Api")]
public class GetPipelineQueryHandlerIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // #343 / #630 PR 4: the read handlers inject IOptions<ApplicationAttentionOptions>
    // to stamp AttentionSignal. The defaults (NoResponseNudgeDays=14, GhostSuggestDays=30,
    // SilentAfterInterviewDays=7, DraftDeadlineDays=7) are what these tests assert against.
    private static readonly IOptions<ApplicationAttentionOptions> AttentionOptions =
        Options.Create(new ApplicationAttentionOptions());

    public GetPipelineQueryHandlerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        AppDbContext db,
        IDateTimeProvider clock,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    [Fact]
    public async Task Handle_WhenNoApplications_ReturnsEmptyList()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedSeekerAsync(db, clock, _userId);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyList()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var handler = new GetPipelineQueryHandler(db, currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithApplicationsOfDifferentStatuses_GroupsByStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var draft1 = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        var draft2 = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        var submitted = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        submitted.TransitionTo(ApplicationStatus.Submitted, clock);

        db.Applications.Add(draft1);
        db.Applications.Add(draft2);
        db.Applications.Add(submitted);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        var draftGroup = result.First(g => g.Status == "Draft");
        draftGroup.Count.ShouldBe(2);
        draftGroup.Applications.Count.ShouldBe(2);

        var submittedGroup = result.First(g => g.Status == "Submitted");
        submittedGroup.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WithSingleApplication_ReturnsSingleGroup()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Status.ShouldBe("Draft");
        result[0].Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ProjectsAppliedAt_SetForSubmitted_NullForDraft()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var draft = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        var submitted = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        submitted.TransitionTo(ApplicationStatus.Submitted, clock);

        db.Applications.Add(draft);
        db.Applications.Add(submitted);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        // #336: AppliedAt is projected into the read DTO. Draft never submitted →
        // null; Submitted → the idempotent first-submit stamp. UpdatedAt is NOT a
        // valid proxy (it moves on every status change), hence the dedicated field.
        var draftDto = result.First(g => g.Status == "Draft").Applications.Single();
        draftDto.AppliedAt.ShouldBeNull();

        var submittedDto = result.First(g => g.Status == "Submitted").Applications.Single();
        submittedDto.AppliedAt.ShouldNotBeNull();
        // Postgres timestamptz truncates .NET ticks to microseconds, so compare
        // with a tolerance rather than exact equality on the round-tripped value.
        submittedDto.AppliedAt!.Value.ShouldBe(submitted.AppliedAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ProjectsAppliedAt_SurvivesTransitionToTerminalStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        // Draft → Submitted (stamps AppliedAt) → Rejected (terminal). The domain
        // stamps AppliedAt once on first Submit and never overwrites it
        // (Application.cs), so it must survive into a terminal state. Klas Q1:
        // the row keeps "Skickad för X sedan" for terminal states — that copy is
        // anchored on AppliedAt, so the projection must still carry it here.
        var rejected = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        rejected.TransitionTo(ApplicationStatus.Submitted, clock);
        var stampedAt = rejected.AppliedAt;
        rejected.TransitionTo(ApplicationStatus.Rejected, clock);

        db.Applications.Add(rejected);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var rejectedDto = result.First(g => g.Status == "Rejected").Applications.Single();
        rejectedDto.AppliedAt.ShouldNotBeNull();
        // Same idempotent first-submit stamp — NOT re-stamped by the Rejected
        // transition. Postgres timestamptz → microsecond tolerance.
        rejectedDto.AppliedAt!.Value.ShouldBe(stampedAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        db.Applications.Add(app);

        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, clock, otherUserId);
        for (var i = 0; i < 5; i++)
        {
            db.Applications.Add(
                DomainApplication.Create(otherSeeker.Id, null, null, null, clock).Value);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Sum(g => g.Count).ShouldBe(1);
    }

    // #342 (ADR 0085 §3) — HasOverdueFollowUp is projected via a correlated EXISTS
    // (a Pending follow-up whose ScheduledAt has passed). These run on Npgsql so
    // they prove the subquery actually translates (an unit/InMemory test would not).

    [Fact]
    public async Task Handle_ProjectsHasOverdueFollowUp_TrueForPendingPastScheduledFollowUp()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddDays(-1), null, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        dto.HasOverdueFollowUp.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ProjectsHasOverdueFollowUp_FalseForFutureScheduledFollowUp()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddDays(1), null, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        dto.HasOverdueFollowUp.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ProjectsHasOverdueFollowUp_FalseForRespondedFollowUp()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        // Overdue by schedule, but the outcome is recorded → no longer Pending.
        var followUpId = app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddDays(-1), null, clock).Value;
        app.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        dto.HasOverdueFollowUp.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ProjectsHasOverdueFollowUp_FalseForSoftDeletedFollowUp()
    {
        // Proves the ADR 0048 (c) point: the FollowUp global query filter
        // (DeletedAt == null) already excludes soft-deleted rows from the
        // correlated EXISTS, so a soft-deleted overdue follow-up does NOT trip the flag.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddDays(-1), null, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        // Soft-delete just the follow-up (the aggregate exposes no single-follow-up
        // delete; set the mapped column directly, mirroring the JobAd soft-delete
        // pattern in ReadHandlerManualPostingFallbackIntegrationTests).
        var followUp = app.FollowUps.Single();
        db.Entry(followUp).Property(f => f.DeletedAt).CurrentValue = clock.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        dto.HasOverdueFollowUp.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ProjectsHasOverdueFollowUp_FalseForNoResponseOutcome()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        // Overdue by schedule, but the outcome is recorded as NoResponse → no
        // longer Pending, so it must not trip the flag (only Pending counts).
        var followUpId = app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddDays(-1), null, clock).Value;
        app.RecordFollowUpOutcome(followUpId, FollowUpOutcome.NoResponse, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        dto.HasOverdueFollowUp.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ProjectsHasOverdueFollowUp_TrueForJustPastScheduledFollowUp()
    {
        // Pins the strict `<` time boundary: a follow-up scheduled barely in the
        // past (sub-second) is overdue. AddDays(-1) would not catch a `<` → `<=`
        // or `<` → `>` regression near the captured `now`.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddSeconds(-1), null, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        dto.HasOverdueFollowUp.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ProjectsLastStatusChangeAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        // Postgres timestamptz truncates .NET ticks to microseconds → tolerance.
        dto.LastStatusChangeAt.ShouldBe(app.LastStatusChangeAt, TimeSpan.FromSeconds(1));
        dto.HasOverdueFollowUp.ShouldBeFalse();
    }

    // #343 (ADR 0085 §3, CTO Option a) — the handler stamps AttentionSignal in-memory
    // via ApplicationAttentionEvaluator (the SSOT). The full rule matrix is unit-tested
    // in ApplicationAttentionEvaluatorTests; these prove the WIRING (signal flows from
    // Evaluate onto the projected DTO) for cases reachable with the real clock.

    [Fact]
    public async Task Handle_StampsAttentionSignal_OfferReceived_AsOfferAwaitingReply()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.TransitionTo(ApplicationStatus.Acknowledged, clock);
        app.TransitionTo(ApplicationStatus.InterviewScheduled, clock);
        app.TransitionTo(ApplicationStatus.Interviewing, clock);
        app.TransitionTo(ApplicationStatus.OfferReceived, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "OfferReceived").Applications.Single();
        dto.AttentionSignal.ShouldBe(ApplicationAttentionSignal.OfferAwaitingReply);
    }

    [Fact]
    public async Task Handle_StampsAttentionSignal_OverdueFollowUp_ForSubmittedWithPastPendingFollowUp()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.AddFollowUp(FollowUpChannel.Email, clock.UtcNow.AddDays(-1), null, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        // Signal 2 (overdue follow-up) outranks the proactive nudge — Evaluate returns
        // the single highest-priority signal.
        dto.AttentionSignal.ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    [Fact]
    public async Task Handle_StampsAttentionSignal_None_ForRecentlySubmittedWithoutFollowUp()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Submitted").Applications.Single();
        // Submitted just now: within the 14-day nudge and 30-day ghost-suggest windows,
        // no follow-up → None. Proves the "Kräver åtgärd" feed is not a dumping ground —
        // a calm application surfaces no signal.
        dto.AttentionSignal.ShouldBe(ApplicationAttentionSignal.None);
    }
}
