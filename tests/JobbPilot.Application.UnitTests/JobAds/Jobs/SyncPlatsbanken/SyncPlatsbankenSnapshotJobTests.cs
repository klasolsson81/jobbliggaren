using System.Runtime.CompilerServices;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Jobs.SyncPlatsbanken;

/// <summary>
/// F2-P8c — SyncPlatsbankenSnapshotJob strömmar hela snapshot (IAsyncEnumerable,
/// root-cause-fix 2026-05-16) och delegerar per item till mediator i en EGEN
/// DI-child-scope per item. Per-item-fel räknas men avbryter inte batchen.
/// Verifierar:
/// <list type="bullet">
/// <item>Empty snapshot → SyncCounts alla 0</item>
/// <item>Added/Updated/Skipped räknas i rätt bucket</item>
/// <item>Per-item exception isolerad → Errors++, batchen fortsätter</item>
/// <item>Handler-Failure → Errors++ utan att kasta</item>
/// <item>OperationCanceledException propagerar</item>
/// <item>Regression (root-cause): dubblett-tunga snapshots med per-item-fel
/// slutförs utan uncaught exception OCH varje item får en egen child-scope</item>
/// </list>
/// </summary>
public class SyncPlatsbankenSnapshotJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static JobAdImportItem ValidItem(string externalId = "ext-1") => new(
        ExternalId: externalId,
        Title: "Backend-utvecklare",
        CompanyName: "Klarna",
        Description: "Beskrivning",
        Url: "https://example.com/jobs/1",
        PublishedAt: Now.AddDays(-1),
        ExpiresAt: Now.AddDays(30),
        SanitizedRawPayload: "{\"id\":\"ext-1\"}");

    private static IJobSource StubJobSource(params JobAdImportItem[] items)
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(items));
        return jobSource;
    }

    private static async IAsyncEnumerable<JobAdImportItem> ToAsyncEnumerable(
        JobAdImportItem[] items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Konkret fake för DI-scope-kedjan. <c>scopeFactory.CreateAsyncScope()</c>
    /// är en extension-metod som internt anropar
    /// <see cref="IServiceScopeFactory.CreateScope"/> och wrappar resultatet i
    /// <c>AsyncServiceScope</c>. En liten konkret fake är mer läsbar än en djup
    /// NSubstitute-kedja (CLAUDE.md §2.4) och låter testet räkna scope-skapande
    /// via <see cref="ScopesCreated"/> (regressions-assertion).
    /// </summary>
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

    private static SyncPlatsbankenSnapshotJob CreateJob(
        IJobSource jobSource,
        IServiceScopeFactory scopeFactory,
        ISystemEventAuditor? auditor = null) =>
        new(
            jobSource, scopeFactory, new FakeDateTimeProvider(Now),
            auditor ?? Substitute.For<ISystemEventAuditor>(),
            NullLogger<SyncPlatsbankenSnapshotJob>.Instance);

    [Fact]
    public async Task RunAsync_WithEmptySnapshot_ReturnsZeroCounts()
    {
        var jobSource = StubJobSource();
        var mediator = Substitute.For<IMediator>();
        var job = CreateJob(jobSource, new FakeScopeFactory(mediator));

        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(0);
        counts.Added.ShouldBe(0);
        counts.Updated.ShouldBe(0);
        counts.Skipped.ShouldBe(0);
        counts.Errors.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenAllItemsAdded_IncrementsAddedCount()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"), ValidItem("c"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var job = CreateJob(jobSource, new FakeScopeFactory(mediator));
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(3);
        counts.Added.ShouldBe(3);
        counts.Updated.ShouldBe(0);
        counts.Skipped.ShouldBe(0);
        counts.Errors.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenItemsMappedToSkipped_IncrementsSkippedCount()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Skipped));

        var job = CreateJob(jobSource, new FakeScopeFactory(mediator));
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(2);
        counts.Skipped.ShouldBe(2);
        counts.Added.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenPerItemHandlerThrows_IncrementsErrorCountAndContinues()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"), ValidItem("c"));
        var mediator = Substitute.For<IMediator>();
        var callCount = 0;
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 2)
                    throw new InvalidOperationException("simulerad item-fail");
                return Result.Success(UpsertOutcome.Added);
            });

        var job = CreateJob(jobSource, new FakeScopeFactory(mediator));
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(3);
        counts.Added.ShouldBe(2);
        counts.Errors.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_WhenHandlerReturnsFailure_IncrementsErrorCountWithoutThrowing()
    {
        var jobSource = StubJobSource(ValidItem("a"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UpsertOutcome>(
                DomainError.Validation("Test.Failure", "simulerad")));

        var job = CreateJob(jobSource, new FakeScopeFactory(mediator));
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(1);
        counts.Errors.ShouldBe(1);
        counts.Added.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationRequestedDuringBatch_PropagatesOperationCanceledException()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"));
        var mediator = Substitute.For<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = CreateJob(jobSource, new FakeScopeFactory(mediator));

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(cts.Token));
    }

    [Fact]
    public async Task RunAsync_WithEmptySnapshot_StillRecordsAuditOnce()
    {
        // Branch (e) — empty snapshot ska ändå skriva EXAKT en audit-rad
        // (GDPR Art. 30 "behandlingsaktivitet har körts" — relevant även
        // när 0 items processades).
        var jobSource = StubJobSource();
        var mediator = Substitute.For<IMediator>();
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = CreateJob(jobSource, new FakeScopeFactory(mediator), auditor);

        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(0);
        await auditor.Received(1).RecordAsync(
            Arg.Any<JobAdsSynced>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenMediatorThrowsOperationCanceled_RethrowsWithoutSwallowing()
    {
        // Branch (c) — det dedikerade catch (OperationCanceledException) { throw; }
        // i try-blocket. Skiljer sig från det existerande
        // PropagatesOperationCanceledException-testet som triggar
        // ThrowIfCancellationRequested FÖRE try-blocket. Här kastas OCE
        // mitt i mediator.Send → måste propagera, INTE räknas som Errors
        // (generic catch får inte svälja den).
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result<UpsertOutcome>>(_ =>
                throw new OperationCanceledException("simulerad mid-send-cancel"));
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = CreateJob(jobSource, new FakeScopeFactory(mediator), auditor);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(TestContext.Current.CancellationToken));

        // OCE-rethrow kortsluter RunAsync → ingen audit-rad, OCE inte
        // omklassad till Errors av generic catch.
        await auditor.DidNotReceive().RecordAsync(
            Arg.Any<JobAdsSynced>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenSnapshotContainsDuplicates_IsolatesPerItemScope_AndCompletes()
    {
        // Root-cause-regression (2026-05-16): tidigare körde hela ~47k-loopen i
        // EN DI-scope → ett scoped IAppDbContext vars change-tracker
        // ackumulerade. När snapshot ⊇ redan infogade ads (dubbletter) bröts
        // UpsertExternalJobAdCommandHandler:s per-command 23505-isolering →
        // uncaught DbUpdateException → Hangfire-loop (60 starts / 0 completes).
        // Fixen är child-scope per item. Detta test simulerar 23505-isolering
        // via per-item-fel (Failure + thrown) på dubbletter och verifierar att
        // (1) jobbet slutför utan uncaught exception, (2) counts är korrekta,
        // (3) en NY scope skapas per item (scope-isoleringen som återställer
        // ADR 0032 §5:s single-command-scope-antagande).
        var items = new[]
        {
            ValidItem("dup-1"), ValidItem("dup-2"), ValidItem("dup-3"),
            ValidItem("dup-4"), ValidItem("dup-5"),
        };
        var jobSource = StubJobSource(items);

        var mediator = Substitute.For<IMediator>();
        var callCount = 0;
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount switch
                {
                    // item 2: simulerar uncaught 23505 från handlern (gamla buggen).
                    2 => throw new InvalidOperationException(
                        "23505: duplicate key value violates unique constraint"),
                    // item 4: handlern fångar 23505 internt och returnerar Failure.
                    4 => Result.Failure<UpsertOutcome>(
                        DomainError.Validation("JobAd.Duplicate", "redan infogad")),
                    _ => Result.Success(UpsertOutcome.Added),
                };
            });

        var scopeFactory = new FakeScopeFactory(mediator);
        var job = CreateJob(jobSource, scopeFactory);

        // Får INTE kasta — per-item-scope-isolering förhindrar att en dubblett
        // bryter hela batchen (root-cause-fix).
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(5);
        counts.Added.ShouldBe(3);
        counts.Errors.ShouldBe(2);
        counts.Updated.ShouldBe(0);
        counts.Skipped.ShouldBe(0);

        // Kärn-assertionen: EN child-scope per item (5 items → 5 scopes).
        // CreateAsyncScope()-extensionen anropar internt CreateScope().
        scopeFactory.ScopesCreated.ShouldBe(5);
    }
}
