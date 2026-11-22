using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.JobAds.JuniorClassificationHarness;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2264 (ADR 0187) — the junior sweep when something goes wrong, on real Postgres: one ad whose write fails, the
/// kill-switch, and the purge of an assessment left on an erased ad, which runs before both. Its own database per test,
/// because the counts and the written tables are the assertion.
/// </summary>
/// <remarks>
/// The failing write is the database server ending the session that carries the ad's INSERT (SQLSTATE 57P01
/// admin_shutdown, what a Postgres restart or failover does to its sessions); the harness's <c>SweepSeams</c> picks the
/// moment and the server produces the failure. The assessment left on an erased ad is produced by the two actors that
/// leave it in production: the erasure command committing between the sweep's read and its write, and the tick then
/// being cancelled (Hangfire's shutdown token) before its closing purge.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class ClassifyJuniorJobAdsJobFailureTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly JuniorClassificationHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    // A spinning sweep never ends on the harness's clock, which stands still: the timeout turns a livelock into a
    // failure instead of a hung run.
    [Fact(Timeout = 120_000)]
    public async Task RunAsync_ShouldClassifyTheRestInTheSameTick_WhenOneAdsWriteFails_AndSelectThatAdOnTheNextTick()
    {
        // The newest ad fails, so it heads every round's selection: the others are reached only by skipping it.
        var failing = _h.Imported("Junior utvecklare", publishedAt: Now.AddHours(-1));
        await _h.AddAsync(failing);
        var trainee = await _h.ImportAsync("Trainee inom IT", publishedAt: Now.AddHours(-2));
        var noSignal = await _h.ImportAsync("Systemutvecklare", publishedAt: Now.AddHours(-3));
        _h.Seams.EndTheSessionOfEveryInsertFor(failing.Id.Value);
        var logger = new RecordingLogger<ClassifyJuniorJobAdsJob>();

        var first = await _h.SweepAsync(
            batchSize: 1, logger: logger, cancellationToken: TestContext.Current.CancellationToken);

        first.ShouldBe(new ClassifyJuniorJobAdsCounts(Junior: 1, NotJunior: 1, Unclear: 0, Refused: 0, Failed: 1));
        _h.Seams.EndedSessions.ShouldBe(1, "a failed ad is skipped for the rest of the tick");
        (await _h.AssessmentAsync(failing.Id.Value)).ShouldBeNull();
        (await _h.AssessmentAsync(trainee)).ShouldNotBeNull();
        (await _h.AssessmentAsync(noSignal)).ShouldNotBeNull();

        var warning = logger.Records.Where(r => r.Level == LogLevel.Warning).ShouldHaveSingleItem();
        var exceptionType = warning.Properties.Where(p => p.Key != "{OriginalFormat}").ShouldHaveSingleItem();
        exceptionType.Key.ShouldBe("ExceptionType");
        var typeName = exceptionType.Value.ShouldBeOfType<string>();
        typeName.ShouldMatch("^[A-Za-z]+Exception$");
        warning.Message.ShouldBe($"Junior classification of one ad failed ({typeName}).");

        _h.Seams.FailureMessages.ShouldNotBeEmpty();
        var logged = logger.Records
            .SelectMany(r => r.Properties.Select(p => p.Value?.ToString() ?? string.Empty).Append(r.Message))
            .ToList();
        foreach (var text in logged)
        {
            foreach (var message in _h.Seams.FailureMessages)
                text.ShouldNotContain(message, Case.Insensitive, "the exception's type is logged, never its message");
            text.ShouldNotContain(failing.Id.Value.ToString(), Case.Insensitive);
            text.ShouldNotContain(failing.External!.ExternalId, Case.Insensitive);
            text.ShouldNotContain(failing.Title, Case.Insensitive);
        }

        // The failure is lifted: the next tick selects that ad, and only that one.
        _h.Seams.LiftTheFailure();
        _h.Clock.UtcNow = Now.AddMinutes(2);

        (await _h.SweepAsync(batchSize: 1)).ShouldBe(new ClassifyJuniorJobAdsCounts(1, 0, 0, 0, 0));
        (await _h.AssessmentAsync(failing.Id.Value)).ShouldNotBeNull().ClassifiedAt.ShouldBe(Now.AddMinutes(2));
    }

    [Fact]
    public async Task RunAsync_ShouldClassifyNothingAndWriteNothing_WhenSwitchedOff_AndNoAssessmentSitsOnAnErasedAd()
    {
        var classified = await _h.ImportAsync("Junior utvecklare");
        await _h.SweepAsync();
        // Stale since: its text changed.
        _h.Clock.UtcNow = Now.AddMinutes(2);
        await _h.RewriteAsync(classified, description: MentorText);
        await _h.ImportAsync("Trainee inom IT");
        _h.Commands.Clear();

        (await _h.SweepAsync(enabled: false)).ShouldBe(ClassifyJuniorJobAdsCounts.None);

        _h.Commands.WrittenTables.ShouldBeEmpty();
        var assessment = (await _h.AssessmentsAsync()).ShouldHaveSingleItem();
        assessment.Id.Value.ShouldBe(classified);
        assessment.ClassifiedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task RunAsync_ShouldPurgeAnAssessmentLeftOnAnErasedAd_EvenWhenSwitchedOff()
    {
        // The erased ad is the newer, so a batch of one selects it first.
        var erased = await _h.ImportAsync(
            "Junior utvecklare", description: NamingTheRecruiter(NeutralText), publishedAt: Now.AddHours(-1));
        var kept = await _h.ImportAsync("Trainee inom IT", publishedAt: Now.AddHours(-2));
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // The erasure commits between the sweep's read and its first write, and the tick is cancelled once that write
        // has committed, before its closing purge.
        var cancelled = false;
        try
        {
            await _h.SweepAsync(
                batchSize: 1,
                beforeTheFirstWrite: async () => (await _h.EraseAsync(RecruiterName)).Erased.JobAds.ShouldBe(1),
                afterTheFirstWrite: shutdown.Cancel,
                cancellationToken: shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        cancelled.ShouldBeTrue();
        (await _h.JuniorInputsAsync(erased)).Status.ShouldBe(JobAdStatus.Erased);
        (await _h.AssessmentAsync(erased)).ShouldNotBeNull("the cancelled tick left its write on the tombstone");
        (await _h.AssessmentAsync(kept)).ShouldBeNull("the tick ended before it reached the other ad");

        // The next tick, with classification switched off.
        _h.Clock.UtcNow = Now.AddMinutes(2);
        _h.Commands.Clear();

        (await _h.SweepAsync(enabled: false)).ShouldBe(ClassifyJuniorJobAdsCounts.None);

        (await _h.AssessmentAsync(erased)).ShouldBeNull();
        _h.Commands.WrittenTables.ShouldBe(["job_ad_junior_assessments"], ignoreOrder: false, "only the purge writes");
        (await _h.AssessmentsAsync()).ShouldBeEmpty("switched off, the sweep classified nothing");
    }
}
