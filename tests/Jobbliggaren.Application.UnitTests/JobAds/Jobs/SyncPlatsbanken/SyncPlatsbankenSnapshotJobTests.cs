using System.Runtime.CompilerServices;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Jobs.SyncPlatsbanken;

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
        SanitizedRawPayload: "{\"id\":\"ext-1\"}",
        Facets: TestFacets.FromPayload("{\"id\":\"ext-1\"}"),
        Requirements: [],
        DeclaredContacts: []);

    private static IJobSource StubJobSource(params JobAdImportItem[] items)
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        // ADR 0032-amendment 2026-05-23: FetchSnapshotAsync tar nu en
        // SnapshotOutcomeRecorder som måste sättas innan yield break.
        jobSource.FetchSnapshotAsync(Arg.Any<SnapshotOutcomeRecorder>(), Arg.Any<CancellationToken>())
            .Returns(ci => ToAsyncEnumerable(items, ci.ArgAt<SnapshotOutcomeRecorder>(0)));
        return jobSource;
    }

    private static async IAsyncEnumerable<JobAdImportItem> ToAsyncEnumerable(
        JobAdImportItem[] items,
        SnapshotOutcomeRecorder outcome,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
        // Default stub: rapportera komplett (icke-trunkerad) snapshot.
        outcome.Record(new SnapshotOutcome(
            ParsedTotal: items.Length, Attempts: 1, TruncatedAndExhausted: false));
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
        ISystemEventAuditor? auditor = null,
        IJobAdSnapshotMissTracker? missTracker = null,
        IDateTimeProvider? clock = null,
        IngestionThroughputReporter? throughputReporter = null)
    {
        IJobAdSnapshotMissTracker tracker;
        if (missTracker is null)
        {
            tracker = Substitute.For<IJobAdSnapshotMissTracker>();
            // Default: tracker rapporterar 0 reset/0 incremented + ingen 7d-historik.
            tracker.ApplyAsync(
                    Arg.Any<JobSource>(), Arg.Any<IReadOnlySet<string>>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new SnapshotMissUpdateResult(0, 0));
            tracker.GetMaxObservedSnapshotSizeAsync(
                    Arg.Any<JobSource>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((int?)null);
        }
        else
        {
            // Caller har konfigurerat tracker:n själv — överskriv inte returvärden.
            tracker = missTracker;
        }

        // Default-options: floor som passar små testdata (de 1-5 items vi seedar
        // ska INTE skippas pga floor; sätt absoluteFloor=1).
        var opts = Microsoft.Extensions.Options.Options.Create(new JobSourceRetentionOptions
        {
            SnapshotMissThreshold = 3,
            SnapshotAbsoluteFloor = 1,
            SnapshotRelativeFloorRatio = 0.80,
        });

        return new SyncPlatsbankenSnapshotJob(
            jobSource, scopeFactory, tracker, opts, clock ?? new FakeDateTimeProvider(Now),
            auditor ?? Substitute.For<ISystemEventAuditor>(),
            throughputReporter ?? new IngestionThroughputReporter(
                Options.Create(new IngestionThroughputOptions()),
                NullLogger<IngestionThroughputReporter>.Instance),
            NullLogger<SyncPlatsbankenSnapshotJob>.Instance);
    }

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
    public async Task RunAsync_WhenSnapshotComplete_AppliesMissTrackingWithSeenSet()
    {
        // ADR 0032-amendment 2026-05-23: vid komplett (icke-trunkerad) snapshot
        // ska tracker.ApplyAsync anropas med set:n av sedda ExternalIds.
        var items = new[] { ValidItem("a"), ValidItem("b"), ValidItem("c") };
        var jobSource = StubJobSource(items);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var tracker = Substitute.For<IJobAdSnapshotMissTracker>();
        IReadOnlySet<string>? capturedSeen = null;
        tracker.ApplyAsync(
                Arg.Any<JobSource>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedSeen = ci.ArgAt<IReadOnlySet<string>>(1);
                return new SnapshotMissUpdateResult(3, 0);
            });
        tracker.GetMaxObservedSnapshotSizeAsync(
                Arg.Any<JobSource>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);

        var job = CreateJob(jobSource, new FakeScopeFactory(mediator), missTracker: tracker);
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.MissTrackingApplied.ShouldBeTrue();
        counts.MissResetCount.ShouldBe(3);
        await tracker.Received(1).ApplyAsync(
            JobSource.Platsbanken,
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        capturedSeen.ShouldNotBeNull();
        capturedSeen.ShouldContain("a");
        capturedSeen.ShouldContain("b");
        capturedSeen.ShouldContain("c");
    }

    [Fact]
    public async Task RunAsync_WhenSnapshotTruncated_SkipsMissTracking()
    {
        // ADR 0032-amendment 2026-05-23: vid trunkering kan vi inte särskilja
        // "missing från källan" från "missing från trunkerad prefix" → skip.
        // Skydd mot mass-felaktig arkivering.
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<SnapshotOutcomeRecorder>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                TruncatedEnumerable(
                    new[] { ValidItem("a") },
                    ci.ArgAt<SnapshotOutcomeRecorder>(0)));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var tracker = Substitute.For<IJobAdSnapshotMissTracker>();
        var job = CreateJob(jobSource, new FakeScopeFactory(mediator), missTracker: tracker);

        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.MissTrackingApplied.ShouldBeFalse();
        await tracker.DidNotReceive().ApplyAsync(
            Arg.Any<JobSource>(), Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenParsedTotalBelowAbsoluteFloor_SkipsMissTracking()
    {
        // CTO-rond 2026-05-23 Q5 — absolute floor (30 000 default). Snapshot
        // som ger för få items är "uppenbart trasig" oavsett om bytes hann
        // strömma → skydd mot mass-felaktig arkivering.
        var items = new[] { ValidItem("a"), ValidItem("b") };
        var jobSource = StubJobSource(items);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var tracker = Substitute.For<IJobAdSnapshotMissTracker>();
        tracker.GetMaxObservedSnapshotSizeAsync(
                Arg.Any<JobSource>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);

        // Sätt floor över items.Length så absolute-floor-violationen triggar.
        var opts = Options.Create(new JobSourceRetentionOptions
        {
            SnapshotAbsoluteFloor = 100,  // 2 items < 100 → skip
            SnapshotRelativeFloorRatio = 0.80,
            SnapshotMissThreshold = 3,
        });
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = new SyncPlatsbankenSnapshotJob(
            jobSource, new FakeScopeFactory(mediator), tracker, opts,
            new FakeDateTimeProvider(Now), auditor,
            new IngestionThroughputReporter(
                Options.Create(new IngestionThroughputOptions()), NullLogger<IngestionThroughputReporter>.Instance),
            NullLogger<SyncPlatsbankenSnapshotJob>.Instance);

        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.MissTrackingApplied.ShouldBeFalse();
        await tracker.DidNotReceive().ApplyAsync(
            Arg.Any<JobSource>(), Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenParsedTotalBelowRelativeFloor_SkipsMissTracking()
    {
        // CTO-rond 2026-05-23 Q5 + security-auditor H2 — relative floor
        // (max_7d × 0.80 default). Snapshot som är dramatiskt mindre än
        // 7-dygns-baslinje skippas → ingen falsk arkivering mot trasig
        // snapshot-kontinuitet. Tredje benet av defense-in-depth.
        var items = new[] { ValidItem("a"), ValidItem("b"), ValidItem("c") };
        var jobSource = StubJobSource(items);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var tracker = Substitute.For<IJobAdSnapshotMissTracker>();
        // 7-dygns-baslinje = 50000. Ratio 0.80 → floor = 40000. Snapshot 3 < 40000 → skip.
        tracker.GetMaxObservedSnapshotSizeAsync(
                Arg.Any<JobSource>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((int?)50_000);

        // Sätt absolute floor lågt så bara relative-floor-violation triggar.
        var opts = Options.Create(new JobSourceRetentionOptions
        {
            SnapshotAbsoluteFloor = 1,            // 3 items >= 1 → absolute OK
            SnapshotRelativeFloorRatio = 0.80,     // 3 < 50000 × 0.80 = 40000 → relative violated
            SnapshotMissThreshold = 3,
        });
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = new SyncPlatsbankenSnapshotJob(
            jobSource, new FakeScopeFactory(mediator), tracker, opts,
            new FakeDateTimeProvider(Now), auditor,
            new IngestionThroughputReporter(
                Options.Create(new IngestionThroughputOptions()), NullLogger<IngestionThroughputReporter>.Instance),
            NullLogger<SyncPlatsbankenSnapshotJob>.Instance);

        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.MissTrackingApplied.ShouldBeFalse(
            "relative floor-skydd skippar miss-tracking vid <80% av max_7d");
        await tracker.DidNotReceive().ApplyAsync(
            Arg.Any<JobSource>(), Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<JobAdImportItem> TruncatedEnumerable(
        JobAdImportItem[] items,
        SnapshotOutcomeRecorder outcome,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
        outcome.Record(new SnapshotOutcome(
            ParsedTotal: items.Length, Attempts: 3, TruncatedAndExhausted: true));
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

    // #754 — call-placement guards for IngestionThroughputReporter. The
    // reporter's OWN gating/arithmetic logic (qualifying gate, below-floor,
    // durationSec==0) is tested exhaustively and in isolation in
    // IngestionThroughputReporterTests; these two tests exist ONLY to pin
    // WHERE the call happens: after LogCompleted, a point only reached when
    // RunAsync completes the foreach normally.

    [Fact]
    public async Task RunAsync_OnNormalCompletion_ReportsThroughputAgainstRealDefaults()
    {
        // 205 items >= the REAL MinItemsForVerdict default (200) — qualifies
        // without lowering any threshold. Uses an ADVANCING clock: a frozen
        // FakeDateTimeProvider would make durationSec compute to exactly 0
        // (startedAt == completedAt), which would make this run non-qualifying
        // for the wrong reason and defeat the test (CTO bind #754 Q3(ii)).
        // ALL items resolve to Skipped (added=0) so fetched(205) != added(0) —
        // a call-site numerator mix-up (passing added instead of fetched,
        // ADR 0045/#754 Q3(i)) would show up as "fetched=0" and be caught by
        // the field-anchored assertion below.
        var items = Enumerable.Range(1, 205).Select(i => ValidItem($"ext-{i}")).ToArray();
        var jobSource = StubJobSource(items);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Skipped));
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = new IngestionThroughputReporter(
            Options.Create(new IngestionThroughputOptions()), recorder);
        var job = CreateJob(
            jobSource, new FakeScopeFactory(mediator),
            clock: new AdvancingFakeDateTimeProvider(Now, TimeSpan.FromSeconds(1)),
            throughputReporter: reporter);

        await job.RunAsync(TestContext.Current.CancellationToken);

        // Field-anchored (not bare substring) so a source/jobType swap or a
        // fetched/added numerator mix-up at the call site is actually caught.
        var record = recorder.Records.Single(r => r.EventId.Id == 6201);
        record.Message.ShouldContain("source=platsbanken");
        record.Message.ShouldContain("jobType=snapshot");
        record.Message.ShouldContain("fetched=205");
    }

    [Fact]
    public async Task RunAsync_WhenPerItemOperationCanceledAfterQualifyingFetchCount_DoesNotReportThroughput()
    {
        // Regression guard: fetched reaches 205 (>= the 200 qualifying floor)
        // before the 205th send throws OperationCanceledException and aborts
        // the run BEFORE reaching LogCompleted/the throughput-report call
        // (there is no `finally` here — an exception mid-foreach skips
        // straight out of RunAsync). If the call were ever hoisted earlier
        // (e.g. into a wrapping finally), it would fire here with a bogus
        // rate for a run that never completed — this test would go red.
        var items = Enumerable.Range(1, 250).Select(i => ValidItem($"ext-{i}")).ToArray();
        var jobSource = StubJobSource(items);
        var mediator = Substitute.For<IMediator>();
        var callCount = 0;
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result<UpsertOutcome>>(_ =>
            {
                callCount++;
                if (callCount == 205)
                    throw new OperationCanceledException("simulerad mid-batch cancel");
                return Result.Success(UpsertOutcome.Added);
            });
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = new IngestionThroughputReporter(
            Options.Create(new IngestionThroughputOptions()), recorder);
        var job = CreateJob(
            jobSource, new FakeScopeFactory(mediator),
            clock: new AdvancingFakeDateTimeProvider(Now, TimeSpan.FromSeconds(1)),
            throughputReporter: reporter);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(TestContext.Current.CancellationToken));

        recorder.Records.ShouldBeEmpty();
    }
}
