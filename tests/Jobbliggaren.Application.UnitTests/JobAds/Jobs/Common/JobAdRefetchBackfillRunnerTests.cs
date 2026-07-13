using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Jobs.Common;

/// <summary>
/// Delad re-ingest-kärna (senior-cto-advisor Variant H 2026-06-08, extraherad ur
/// BackfillJobAdSsykJob). Iterar JobAds som matchar ett NULL-kolumn-predikat,
/// re-fetchar mot JobTech per ID, och kör UpsertExternalJobAd-pipelinen för att
/// re-skriva raw_payload — och sedan #841 skriver ingest-funneln OM de sju facett-
/// kolumnerna i C# (JobAd.SetSourcePayload). Det gör runnern till #841:s DEPLOY-
/// REPARATIONSVERKTYG: annonser vars facetter purgen redan nollat matchar NULL-
/// predikatet, re-fetchas, och får alla sju skrivna på nytt. Testerna använder
/// ssyk-predikatet (<c>SsykConceptId IS NULL</c>) som representativ stand-in — de
/// seedade raderna skrivs utan facetter, så predikatet matchar dem; samma täckning
/// som gällde när loopen bodde i ssyk-jobbet. Klass2-/ssyk-wrappers är tunna
/// delegationer (predikat + auditJobType) och verifieras via runnern +
/// JobAdFacetsSurvivePurgeTests (riktig Postgres).
///
/// Verifierar:
/// <list type="bullet">
/// <item>Empty DB → counts alla 0, audit-rad skrivs ändå</item>
/// <item>Happy path: 3 NULL-rader → 3 RefetchAttempted → 3 Updated, scope per item</item>
/// <item>404 från källan (RefetchByExternalIdAsync → null) → NotFoundOnSource++</item>
/// <item>UpsertOutcome.Skipped klassas separat från Updated</item>
/// <item>Per-item handler-failure isolerad → Errors++, batchen fortsätter</item>
/// <item>OperationCanceledException propagerar</item>
/// <item>MaxItemsPerRun bryter gracefully</item>
/// </list>
/// </summary>
public class JobAdRefetchBackfillRunnerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 8, 14, 0, 0, TimeSpan.Zero);

    // Representativt NULL-kolumn-predikat (ssyk). I InMemory är shadow-kolumnen
    // alltid NULL → matchar alla seedade rader.
    private static System.Linq.Expressions.Expression<Func<JobAd, bool>> SsykNullPredicate =>
        j => EF.Property<string?>(j, "SsykConceptId") == null;

    private static JobAd CreateImportedJobAd(string externalId, FakeDateTimeProvider clock) =>
        JobAd.Import(
            title: $"Title-{externalId}",
            company: Company.Create("Acme").Value,
            description: "Beskrivning",
            url: $"https://example.com/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: "{\"id\":\"" + externalId + "\"}",
            facets: TestFacets.FromPayload("{\"id\":\"" + externalId + "\"}"),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(30),
            clock: clock).Value;

    private static JobAdImportItem RefetchedItem(string externalId)
    {
        // #841 — the refetched item now carries its facets, exactly as the real ACL would supply them.
        // This is what makes the refetch backfill the repair tool for ads whose facets were nulled by
        // the purge: re-ingesting through UpsertExternalJobAd writes the seven columns in C#.
        var payload = "{\"id\":\"" + externalId + "\",\"occupation\":{\"concept_id\":\"fg7B_yov_smw\"}}";
        return new JobAdImportItem(
            ExternalId: externalId,
            Title: $"Refetched-{externalId}",
            CompanyName: "Acme",
            Description: "Beskrivning",
            Url: $"https://example.com/{externalId}",
            PublishedAt: Now.AddDays(-1),
            ExpiresAt: Now.AddDays(30),
            SanitizedRawPayload: payload,
            Facets: TestFacets.FromPayload(payload),
            Requirements: []);
    }

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

    private static JobAdRefetchBackfillRunner CreateRunner(
        IJobSource jobSource,
        IServiceScopeFactory scopeFactory,
        AppDbContext db,
        ISystemEventAuditor? auditor = null)
    {
        var clock = new FakeDateTimeProvider(Now);
        return new JobAdRefetchBackfillRunner(
            jobSource: jobSource,
            scopeFactory: scopeFactory,
            db: db,
            clock: clock,
            auditor: auditor ?? Substitute.For<ISystemEventAuditor>(),
            logger: NullLogger<JobAdRefetchBackfillRunner>.Instance);
    }

    private static BackfillRunnerOptions Opts(int maxItems = 100_000) =>
        new(PerItemDelayMs: 0, MaxItemsPerRun: maxItems, ProgressLogEvery: 100);

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

        var runner = CreateRunner(jobSource, scopeFactory, db, auditor);
        var counts = await runner.RunAsync(SsykNullPredicate, Opts(), "backfill", ct);

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
        var runner = CreateRunner(jobSource, scopeFactory, db);

        var counts = await runner.RunAsync(SsykNullPredicate, Opts(), "backfill", ct);

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
        var runner = CreateRunner(jobSource, scopeFactory, db);

        var counts = await runner.RunAsync(SsykNullPredicate, Opts(), "backfill", ct);

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
        var runner = CreateRunner(jobSource, scopeFactory, db);

        var counts = await runner.RunAsync(SsykNullPredicate, Opts(), "backfill", ct);

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
        var runner = CreateRunner(jobSource, scopeFactory, db);

        var counts = await runner.RunAsync(SsykNullPredicate, Opts(), "backfill", ct);

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
        var runner = CreateRunner(jobSource, scopeFactory, db);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => runner.RunAsync(SsykNullPredicate, Opts(), "backfill", cts.Token));
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
        var runner = CreateRunner(jobSource, scopeFactory, db);

        var counts = await runner.RunAsync(SsykNullPredicate, Opts(maxItems: 2), "backfill", ct);

        counts.Fetched.ShouldBe(3);  // 2 processade + 1 trigger-check innan break
        counts.Updated.ShouldBe(2);
    }
}
