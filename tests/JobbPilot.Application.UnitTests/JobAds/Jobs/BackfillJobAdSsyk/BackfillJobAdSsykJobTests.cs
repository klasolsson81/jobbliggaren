using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Application.JobAds.Jobs.BackfillJobAdSsyk;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Jobs.BackfillJobAdSsyk;

/// <summary>
/// STEG 6 (2026-05-24) — BackfillJobAdSsykJob iterar JobAds med NULL
/// ssyk_concept_id, re-fetchar mot JobTech per ID, och kör UpsertExternalJobAd-
/// pipelinen för att uppdatera raw_payload (STORED computed column re-evaluerar).
///
/// Verifierar:
/// <list type="bullet">
/// <item>Empty DB → counts alla 0, audit-rad skrivs ändå</item>
/// <item>Happy path: 3 NULL-rader → 3 RefetchAttempted → 3 Updated</item>
/// <item>404 från källan (RefetchByExternalIdAsync → null) → NotFoundOnSource++</item>
/// <item>OperationCanceledException propagerar</item>
/// <item>Per-item exception isolerad → Errors++, batchen fortsätter</item>
/// </list>
/// </summary>
public class BackfillJobAdSsykJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 24, 14, 0, 0, TimeSpan.Zero);

    private static JobAd CreateImportedJobAd(string externalId, FakeDateTimeProvider clock) =>
        JobAd.Import(
            title: $"Title-{externalId}",
            company: Company.Create("Acme").Value,
            description: "Beskrivning",
            url: $"https://example.com/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: "{\"id\":\"" + externalId + "\"}",
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(30),
            clock: clock).Value;

    private static JobAdImportItem RefetchedItem(string externalId) => new(
        ExternalId: externalId,
        Title: $"Refetched-{externalId}",
        CompanyName: "Acme",
        Description: "Beskrivning",
        Url: $"https://example.com/{externalId}",
        PublishedAt: Now.AddDays(-1),
        ExpiresAt: Now.AddDays(30),
        SanitizedRawPayload: "{\"id\":\"" + externalId + "\",\"occupation\":{\"concept_id\":\"fg7B_yov_smw\"}}");

    private sealed class FakeScopeFactory(IMediator mediator)
        : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public int ScopesCreated { get; private set; }

        public IServiceScope CreateScope()
        {
            ScopesCreated++;
            return this;
        }

        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IMediator) ? mediator : null;

        public void Dispose() { }
    }

    private static BackfillJobAdSsykJob CreateJob(
        IJobSource jobSource,
        IServiceScopeFactory scopeFactory,
        AppDbContext db,
        BackfillJobAdSsykOptions? options = null,
        ISystemEventAuditor? auditor = null)
    {
        var clock = new FakeDateTimeProvider(Now);
        return new BackfillJobAdSsykJob(
            jobSource: jobSource,
            scopeFactory: scopeFactory,
            db: db,
            options: Options.Create(options ?? new BackfillJobAdSsykOptions { PerItemDelayMs = 0 }),
            clock: clock,
            auditor: auditor ?? Substitute.For<ISystemEventAuditor>(),
            logger: NullLogger<BackfillJobAdSsykJob>.Instance);
    }

    [Fact]
    public async Task RunAsync_EmptyDatabase_AllCountsZero_AuditRecorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        var auditor = Substitute.For<ISystemEventAuditor>();
        var mediator = Substitute.For<IMediator>();
        var scopeFactory = new FakeScopeFactory(mediator);

        var job = CreateJob(jobSource, scopeFactory, db, auditor: auditor);
        var counts = await job.RunAsync(ct);

        counts.Fetched.ShouldBe(0);
        counts.RefetchAttempted.ShouldBe(0);
        counts.Updated.ShouldBe(0);
        counts.NotFoundOnSource.ShouldBe(0);
        counts.Errors.ShouldBe(0);

        // Audit-rad ska skrivas även vid 0 rader (GDPR Art. 30 — "behandlingsaktivitet har körts").
        await auditor.Received(1).RecordAsync(
            Arg.Is<JobAdsSynced>(e => e.JobType == "backfill"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HappyPath_RefetchAndUpsertAll()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(Now);
        var db = TestAppDbContextFactory.Create();

        // 3 imported JobAds med External satt — InMemory ger NULL shadow-SsykConceptId
        // (computed column körs inte i InMemory), så alla matchar filtret.
        db.JobAds.Add(CreateImportedJobAd("ext-1", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-2", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-3", clock));
        await db.SaveChangesAsync(ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<JobAdImportItem?>(RefetchedItem(ci.Arg<string>())));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Updated));

        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory, db);

        var counts = await job.RunAsync(ct);

        counts.Fetched.ShouldBe(3);
        counts.RefetchAttempted.ShouldBe(3);
        counts.Updated.ShouldBe(3);
        counts.NotFoundOnSource.ShouldBe(0);
        counts.Errors.ShouldBe(0);

        // En egen scope per item (ADR 0032 §5 single-command-scope-paritet)
        scopeFactory.ScopesCreated.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_NotFoundOnSource_IncrementsNotFoundCounterSkipsUpsert()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(Now);
        var db = TestAppDbContextFactory.Create();

        db.JobAds.Add(CreateImportedJobAd("ext-found", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-gone", clock));
        await db.SaveChangesAsync(ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync("ext-found", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobAdImportItem?>(RefetchedItem("ext-found")));
        jobSource.RefetchByExternalIdAsync("ext-gone", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobAdImportItem?>(null));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Updated));

        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory, db);

        var counts = await job.RunAsync(ct);

        counts.Fetched.ShouldBe(2);
        counts.RefetchAttempted.ShouldBe(2);
        counts.Updated.ShouldBe(1);
        counts.NotFoundOnSource.ShouldBe(1);

        // Mediator anropas BARA för found-raden (gone skipas innan upsert)
        await mediator.Received(1).Send(
            Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HandlerSkipped_IncrementsSkippedByHandlerNotUpdated()
    {
        // code-reviewer M-1 (2026-05-24): verifierar att UpsertOutcome.Skipped
        // klassas separat från Updated. T.ex. archived-handler-branch returnerar
        // Success(Skipped) — vi vill att backfill-counts skiljer.
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(Now);
        var db = TestAppDbContextFactory.Create();

        db.JobAds.Add(CreateImportedJobAd("ext-skip", clock));
        await db.SaveChangesAsync(ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<JobAdImportItem?>(RefetchedItem(ci.Arg<string>())));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Skipped));

        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory, db);

        var counts = await job.RunAsync(ct);

        counts.Updated.ShouldBe(0);
        counts.SkippedByHandler.ShouldBe(1);
        counts.Errors.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_HandlerFailure_IncrementsErrorsContinuesBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(Now);
        var db = TestAppDbContextFactory.Create();

        db.JobAds.Add(CreateImportedJobAd("ext-ok", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-fail", clock));
        await db.SaveChangesAsync(ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<JobAdImportItem?>(RefetchedItem(ci.Arg<string>())));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Is<UpsertExternalJobAdCommand>(c => c.ExternalId == "ext-ok"),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Updated));
        mediator.Send(Arg.Is<UpsertExternalJobAdCommand>(c => c.ExternalId == "ext-fail"),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UpsertOutcome>(
                DomainError.Validation("Test.Failure", "expected handler failure")));

        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory, db);

        var counts = await job.RunAsync(ct);

        counts.Updated.ShouldBe(1);
        counts.Errors.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_Cancellation_Propagates()
    {
        var clock = new FakeDateTimeProvider(Now);
        var db = TestAppDbContextFactory.Create();
        db.JobAds.Add(CreateImportedJobAd("ext-1", clock));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobAdImportItem?>(RefetchedItem("ext-1")));

        var mediator = Substitute.For<IMediator>();
        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory, db);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(cts.Token));
    }

    [Fact]
    public async Task RunAsync_MaxItemsPerRunReached_BreaksGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(Now);
        var db = TestAppDbContextFactory.Create();

        // 5 rader, men MaxItemsPerRun=2 → bara 2 processade.
        db.JobAds.Add(CreateImportedJobAd("ext-1", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-2", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-3", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-4", clock));
        db.JobAds.Add(CreateImportedJobAd("ext-5", clock));
        await db.SaveChangesAsync(ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<JobAdImportItem?>(RefetchedItem(ci.Arg<string>())));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Updated));

        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory, db,
            options: new BackfillJobAdSsykOptions { PerItemDelayMs = 0, MaxItemsPerRun = 2 });

        var counts = await job.RunAsync(ct);

        counts.Fetched.ShouldBe(3);  // 2 processade + 1 trigger-check innan break
        counts.Updated.ShouldBe(2);
    }
}
