using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;
using Jobbliggaren.Application.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the ingestion run against fakes of its ports. Every state a test starts from is one an
/// earlier run of the job produced through the same fakes; nothing is seeded by hand. The mailbox's answers are the
/// port's own closed set, and the reports are synthetic, with addresses from the documentation ranges only.
/// </summary>
public sealed class DmarcReportIngestionJobTests
{
    // 2026-10-24 10:00Z; the reports cover the UTC day 2026-10-23 (Google's inclusive end, begin + 86399).
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Received = Now.AddHours(-2);
    private const long Begin = 1792713600;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Harness
    {
        public SettableClock Clock { get; } = new(Now);

        public ScriptedDmarcMailbox Mailbox { get; } = new();

        public ScriptedDmarcDecoder Decoder { get; } = new();

        public RecordingDmarcReportStore Store { get; } = new();

        public CapturingLogger<DmarcReportIngestionJob> Log { get; } = new();

        public long Epoch => Mailbox.Epoch;

        public DmarcReportIngestionJob Job(TimeSpan? runDeadline = null) => runDeadline is { } deadline
            ? new DmarcReportIngestionJob(Mailbox, Decoder, Store, Clock, Log, deadline)
            : new DmarcReportIngestionJob(Mailbox, Decoder, Store, Clock, Log);

        public Task RunAsync() => Job().RunAsync(Ct);

        public Task RunAtAsync(DateTimeOffset at)
        {
            Clock.UtcNow = at;
            return RunAsync();
        }

        /// <summary>A message whose one part decodes to <paramref name="candidate"/>.</summary>
        public ScriptedMessage Report(long position, byte key, DmarcReportCandidate? candidate = null, DateTimeOffset? receivedAt = null)
        {
            var message = Mailbox.Add(position, receivedAt ?? Received);
            AnswerWithReport(message, key, candidate);
            return message;
        }

        public void AnswerWithReport(ScriptedMessage message, byte key, DmarcReportCandidate? candidate = null)
        {
            var part = Decoder.Part(key, candidate ?? Candidate($"report-{key}"));
            message.Fetch = Answer(part);
        }

        public ScriptedMessage Answering(long position, DmarcMailboxFetch answer, DateTimeOffset? receivedAt = null)
        {
            var message = Mailbox.Add(position, receivedAt ?? Received);
            message.Fetch = Answer(answer);
            return message;
        }

        public DmarcMessageOutcome OutcomeOf(long position) => Store.Row(Epoch, position).Outcome;

        public IEnumerable<string> Logged(int eventId) =>
            Log.Records.Where(record => record.EventId == eventId).Select(record => record.Message);
    }

    private static Func<CancellationToken, ValueTask<DmarcMailboxFetch>> Answer(DmarcMailboxFetch answer) =>
        _ => ValueTask.FromResult(answer);

    private static DmarcReportCandidate Candidate(
        string reportId, byte digest = 0x42, string policyDomain = "jobbliggaren.se", int records = 1, string source = "192.0.2.1") =>
        new(
            "reporter.example",
            reportId,
            policyDomain,
            Begin,
            Begin + 86399,
            [.. Enumerable.Range(0, records).Select(_ => new DmarcRecordCandidate(source, 3, "jobbliggaren.se", "pass", "fail"))],
            Enumerable.Repeat(digest, 32).ToArray());

    // ---- 0: the sign-in back-off ----

    [Fact]
    public async Task ARunWithinTheBackOffOfARefusedSignIn_OpensNothing_AndWritesNothing()
    {
        var h = new Harness();
        h.Mailbox.OpenOutcome = new DmarcMailboxOpen.Failed(DmarcSessionFault.AuthRefused);
        await h.RunAtAsync(Now.AddHours(-23));
        h.Mailbox.OpenOutcome = null;
        var state = h.Store.State;
        var writes = h.Store.Writes;

        await h.RunAtAsync(Now);

        h.Mailbox.Opens.ShouldBe(1);
        h.Store.Writes.ShouldBe(writes, "a backed-off run writes nothing, or the window would slide for ever");
        h.Store.Calls[^1].ShouldBe("ReadState");
        h.Store.State.ShouldBe(state);
        h.Logged(3121).Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(24 * 60, true)]
    [InlineData(24 * 60 - 1, false)]
    public async Task TheBackOff_LastsTwentyFourHoursFromTheRefusedSignIn(int minutesLater, bool opens)
    {
        var h = new Harness();
        h.Mailbox.OpenOutcome = new DmarcMailboxOpen.Failed(DmarcSessionFault.AuthRefused);
        await h.RunAtAsync(Now);
        h.Mailbox.OpenOutcome = null;

        await h.RunAtAsync(Now.AddMinutes(minutesLater));

        h.Mailbox.Opens.ShouldBe(opens ? 2 : 1);
    }

    [Theory]
    [InlineData(DmarcSessionFault.Unreachable)]
    [InlineData(DmarcSessionFault.TlsFailed)]
    [InlineData(DmarcSessionFault.Timeout)]
    [InlineData(DmarcSessionFault.Protocol)]
    public async Task AFaultThatSentNoRefusedCredential_IsTriedAgainOnTheNextRun(DmarcSessionFault fault)
    {
        var h = new Harness();
        h.Mailbox.OpenOutcome = new DmarcMailboxOpen.Failed(fault);
        await h.RunAtAsync(Now);
        h.Mailbox.OpenOutcome = null;

        await h.RunAtAsync(Now.AddMinutes(1));

        h.Mailbox.Opens.ShouldBe(2);
        h.Store.State.ShouldNotBeNull().LastOutcome.ShouldBe(DmarcRunOutcome.Succeeded);
    }

    // ---- 2: opening the mailbox ----

    public static TheoryData<string, DmarcRunOutcome> OpenOutcomes => new()
    {
        { "NotConfigured", DmarcRunOutcome.NotConfigured },
        { "Misconfigured", DmarcRunOutcome.Misconfigured },
        { nameof(DmarcSessionFault.Unreachable), DmarcRunOutcome.Unreachable },
        { nameof(DmarcSessionFault.TlsFailed), DmarcRunOutcome.TlsFailed },
        { nameof(DmarcSessionFault.AuthRefused), DmarcRunOutcome.AuthRefused },
        { nameof(DmarcSessionFault.Timeout), DmarcRunOutcome.Timeout },
        { nameof(DmarcSessionFault.Protocol), DmarcRunOutcome.Protocol },
    };

    [Theory]
    [MemberData(nameof(OpenOutcomes))]
    public async Task AMailboxThatDoesNotOpen_RecordsWhyAsTheRunsOutcome_AndTouchesNothingElse(
        string open, DmarcRunOutcome expected)
    {
        var h = new Harness();
        h.Mailbox.OpenOutcome = open switch
        {
            "NotConfigured" => new DmarcMailboxOpen.NotConfigured(),
            "Misconfigured" => new DmarcMailboxOpen.Misconfigured(),
            _ => new DmarcMailboxOpen.Failed(Enum.Parse<DmarcSessionFault>(open)),
        };
        h.Report(1, 1);

        await h.RunAsync();

        h.Store.Calls.ShouldBe(["ReadState", "SweepCrashes", "RecordAttempt"]);
        h.Store.Attempts.ShouldBe([(Now, expected)]);
        h.Store.State.ShouldNotBeNull().LastSuccessAt.ShouldBeNull();
        h.Mailbox.Fetches.ShouldBeEmpty();
        h.Logged(3122).ShouldBe([$"[DmarcReportIngestion] Run ended without a success: {expected}"]);
        h.Log.Records.Single(r => r.EventId == 3122).Level
            .ShouldBe(expected == DmarcRunOutcome.NotConfigured ? LogLevel.Information : LogLevel.Warning);
    }

    // ---- 3 to 7: one whole run ----

    [Fact]
    public async Task AFirstRun_WritesItsStateBeforeItClaims_AndEndsWithTheSuccessAndAnalyze()
    {
        var h = new Harness();
        h.Report(5, 1);

        await h.RunAsync();

        h.Store.Calls.ShouldBe(
            ["ReadState", "SweepCrashes", "EnsureState", "EnterEpoch", "Claim", "Store", "ListRetries", "RecordAttempt", "Analyze"]);
        h.OutcomeOf(5).ShouldBe(DmarcMessageOutcome.Stored);
        h.Store.Reports.ShouldHaveSingleItem().ReceivedOn.ShouldBe(new DateOnly(2026, 10, 24));
        h.Store.State.ShouldBe(new DmarcRunState(Now, DmarcRunOutcome.Succeeded, Now, new DmarcCursor(h.Epoch, 5)));
        h.Mailbox.Sessions.ShouldHaveSingleItem().Disposed.ShouldBeTrue();
        h.Logged(3120).ShouldBe([
            "[DmarcReportIngestion] Run ended Succeeded: 1 stored, 0 duplicate, 0 stored conflicting, 0 over the daily cap, "
            + "0 skipped, 0 rejected, 0 handed back for a retry, 0 past retention, 0 retried, 0 abandoned"]);
        h.Logged(3122).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheNextRunInTheSameEpoch_ListsOnlyAfterTheCursor()
    {
        var h = new Harness();
        h.Report(3, 1);
        await h.RunAsync();
        h.Report(5, 2);
        h.Report(7, 3);

        await h.RunAtAsync(Now.AddHours(1));

        h.Mailbox.Listings.ShouldBe([(0, DmarcIngestionLimits.PerRunMessageCap), (3, DmarcIngestionLimits.PerRunMessageCap)]);
        h.Mailbox.Fetches.ShouldBe([3, 5, 7]);
        h.Store.State.ShouldNotBeNull().Cursor.ShouldBe(new DmarcCursor(h.Epoch, 7));
    }

    [Fact]
    public async Task ANewEpoch_ListsFromTheStart_DropsTheOldLedger_AndTheContentKeyStoresNothingTwice()
    {
        var h = new Harness();
        h.Report(3, 1);
        h.Answering(9, new DmarcMailboxFetch.Refused());
        await h.RunAsync();
        var oldEpoch = h.Epoch;
        h.Mailbox.Epoch = oldEpoch + 1;

        await h.RunAtAsync(Now.AddHours(1));

        h.Mailbox.Listings[^1].After.ShouldBe(0);
        h.Store.Ledger.Keys.ShouldAllBe(key => key.Epoch == oldEpoch + 1);
        h.OutcomeOf(3).ShouldBe(DmarcMessageOutcome.Duplicate);
        h.OutcomeOf(9).ShouldBe(DmarcMessageOutcome.RetryPending);
        h.Store.Reports.Count.ShouldBe(1);
        h.Mailbox.Fetches.ShouldBe([3, 9, 3, 9], "each epoch tries a message once per run, by that epoch's position");
    }

    // ---- 4: new mail ----

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public async Task AMessageReceivedBeforeTheRetentionCutoff_MovesTheCursorWithoutALedgerRowOrAFetch(
        int ticksFromCutoff, bool expired)
    {
        var h = new Harness();
        h.Report(1, 1, receivedAt: DmarcRetention.CutoffOf(Now).AddTicks(ticksFromCutoff));
        h.Report(2, 2);

        await h.RunAsync();

        h.Store.Ledger.ContainsKey((h.Epoch, 1)).ShouldBe(!expired);
        h.Mailbox.Fetches.ShouldBe(expired ? [2] : [1, 2]);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored);
        h.Store.State.ShouldNotBeNull().Cursor.ShouldBe(new DmarcCursor(h.Epoch, 2));
        h.Logged(3120).Single().ShouldContain(expired ? "1 past retention" : "0 past retention");
    }

    [Fact]
    public async Task AMessageClaimsItsReceiptAsTheMailboxsTime_NeverLaterThanTheRun()
    {
        var h = new Harness();
        h.Report(1, 1, receivedAt: Now.AddMinutes(-5));
        h.Report(2, 2, receivedAt: Now.AddHours(3));

        await h.RunAsync();

        h.Store.Row(h.Epoch, 1).ReceivedAt.ShouldBe(Now.AddMinutes(-5));
        h.Store.Row(h.Epoch, 2).ReceivedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(DmarcIngestionLimits.MaxMessageBytes + 1, true)]
    [InlineData(DmarcIngestionLimits.MaxMessageBytes, false)]
    public async Task AMessageOverTheSizeCap_IsSkippedWithoutAFetch(long size, bool skipped)
    {
        var h = new Harness();
        var message = h.Mailbox.Add(1, Received, size);
        h.AnswerWithReport(message, 1);

        await h.RunAsync();

        h.OutcomeOf(1).ShouldBe(skipped ? DmarcMessageOutcome.SkippedTooLarge : DmarcMessageOutcome.Stored);
        h.Mailbox.Fetches.Count.ShouldBe(skipped ? 0 : 1);
    }

    public static TheoryData<DmarcSkipReason> SkipReasons => new(Enum.GetValues<DmarcSkipReason>());

    [Theory]
    [MemberData(nameof(SkipReasons))]
    public async Task ASkippedMessage_EndsWithItsSkipOutcome_AndTheRunGoesOn(DmarcSkipReason reason)
    {
        var h = new Harness();
        h.Answering(1, new DmarcMailboxFetch.Skipped(reason));
        h.Report(2, 2);

        await h.RunAsync();

        h.OutcomeOf(1).ShouldBe(DmarcOutcomes.OutcomeOf(reason));
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored);
        h.Decoder.Decodes.ShouldBe(1);
    }

    [Theory]
    [InlineData("an unreadable part", DmarcMessageOutcome.RejectedMalformed)]
    [InlineData("a report about another domain", DmarcMessageOutcome.RejectedPolicyDomain)]
    [InlineData("a report with no record", DmarcMessageOutcome.RejectedContent)]
    [InlineData("a record whose source is no address", DmarcMessageOutcome.RejectedContent)]
    [InlineData("an accepted report", DmarcMessageOutcome.Stored)]
    public async Task AFetchedPart_IsDecodedAcceptedAndStored_OrEndsWithWhyNot(string part, DmarcMessageOutcome expected)
    {
        var h = new Harness();
        var message = h.Mailbox.Add(1, Received);
        message.Fetch = Answer(part switch
        {
            "an unreadable part" => new DmarcMailboxFetch.Fetched([0xEE, 0x00], DmarcReportPartKind.Gzip),
            "a report about another domain" => h.Decoder.Part(1, Candidate("r") with { PolicyDomain = "example.org" }),
            "a report with no record" => h.Decoder.Part(1, Candidate("r", records: 0)),
            "a record whose source is no address" => h.Decoder.Part(1, Candidate("r", source: "198.51.100")),
            _ => h.Decoder.Part(1, Candidate("r")),
        });
        h.Report(2, 2);

        await h.RunAsync();

        h.OutcomeOf(1).ShouldBe(expected);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored);
        h.Store.Reports.Count.ShouldBe(expected == DmarcMessageOutcome.Stored ? 2 : 1);
    }

    [Fact]
    public async Task TheSameReportAgain_IsADuplicate_AndOtherContentUnderTheSameIdIsStoredConflicting()
    {
        var h = new Harness();
        h.Report(1, 1, Candidate("r-1", digest: 0x01));
        h.Report(2, 2, Candidate("r-1", digest: 0x01));
        h.Report(3, 3, Candidate("r-1", digest: 0x02));

        await h.RunAsync();

        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.Stored);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Duplicate);
        h.OutcomeOf(3).ShouldBe(DmarcMessageOutcome.StoredConflicting);
        h.Logged(3120).Single().ShouldContain("1 stored, 1 duplicate, 1 stored conflicting");
    }

    [Fact]
    public async Task OnceTheDaysCapIsReached_AReportIsVolumeCapped_AndTheNextUtcDayStoresAgain()
    {
        var h = new Harness();
        for (byte key = 1; key <= DmarcIngestionLimits.MaxStoredReportsPerUtcDay; key++)
        {
            h.Report(key, key);
        }

        await h.RunAsync();
        h.Report(101, 101);
        await h.RunAtAsync(Now.AddHours(1));
        h.Report(102, 102, receivedAt: new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
        await h.RunAtAsync(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero));

        h.OutcomeOf(100).ShouldBe(DmarcMessageOutcome.Stored);
        h.OutcomeOf(101).ShouldBe(DmarcMessageOutcome.VolumeCapped);
        h.OutcomeOf(102).ShouldBe(DmarcMessageOutcome.Stored);
    }

    [Fact]
    public async Task ARefusedMessage_IsHandedBackForARetry_AndTheRunGoesOn()
    {
        var h = new Harness();
        h.Answering(1, new DmarcMailboxFetch.Refused());
        h.Report(2, 2);

        await h.RunAsync();

        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.RetryPending);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored);
        h.Store.Attempts.ShouldBe([(Now, DmarcRunOutcome.Succeeded)]);
        h.Logged(3120).Single().ShouldContain("1 handed back for a retry");
    }

    [Theory]
    [InlineData(DmarcSessionFault.Unreachable)]
    [InlineData(DmarcSessionFault.TlsFailed)]
    [InlineData(DmarcSessionFault.AuthRefused)]
    [InlineData(DmarcSessionFault.Timeout)]
    [InlineData(DmarcSessionFault.Protocol)]
    public async Task ASessionFault_HandsTheMessageBack_LeavesTheRestUnclaimed_AndEndsTheRunWithTheFault(DmarcSessionFault fault)
    {
        var h = new Harness();
        h.Report(1, 1);
        h.Answering(2, new DmarcMailboxFetch.Failed(fault));
        h.Report(3, 3);

        await h.RunAsync();

        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.Stored);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.RetryPending);
        h.Store.Ledger.ContainsKey((h.Epoch, 3)).ShouldBeFalse();
        h.Store.State.ShouldBe(new DmarcRunState(Now, DmarcOutcomes.RunOutcomeOf(fault), null, new DmarcCursor(h.Epoch, 2)));
        h.Store.Calls.ShouldNotContain("ListRetries");
        h.Store.Analyzes.ShouldBe(0);
        h.Mailbox.Sessions.ShouldHaveSingleItem().Disposed.ShouldBeTrue();
        h.Logged(3122).ShouldBe([$"[DmarcReportIngestion] Run ended without a success: {DmarcOutcomes.RunOutcomeOf(fault)}"]);
    }

    [Fact]
    public async Task AListingFault_EndsTheRunWithTheFault_BeforeAnyClaim()
    {
        var h = new Harness();
        h.Report(1, 1);
        h.Mailbox.ListingFault = DmarcSessionFault.Timeout;

        await h.RunAsync();

        h.Store.Ledger.ShouldBeEmpty();
        h.Store.Attempts.ShouldBe([(Now, DmarcRunOutcome.Timeout)]);
        h.Mailbox.Sessions.ShouldHaveSingleItem().Disposed.ShouldBeTrue();
    }

    // ---- 5: retries ----

    [Fact]
    public async Task Retries_RunAfterTheNewMail_OldestFirst_InWhatTheCapLeaves()
    {
        var h = new Harness();
        foreach (var position in new long[] { 1, 2, 3 })
        {
            h.Answering(position, new DmarcMailboxFetch.Refused());
        }

        await h.RunAsync();
        foreach (var position in new long[] { 1, 2, 3 })
        {
            h.AnswerWithReport(h.Mailbox[position], (byte)position);
        }

        AddSkippedMessages(h, from: 4, count: DmarcIngestionLimits.PerRunMessageCap);
        await h.RunAtAsync(Now.AddHours(1));
        h.Store.RetryListings.ShouldBe(
            [DmarcIngestionLimits.PerRunMessageCap - 3], "a run that claimed the cap in new mail lists no retries");

        var newer = AddSkippedMessages(h, from: 4 + DmarcIngestionLimits.PerRunMessageCap, count: DmarcIngestionLimits.PerRunMessageCap - 2);
        var fetchesBefore = h.Mailbox.Fetches.Count;
        await h.RunAtAsync(Now.AddHours(2));

        h.Store.RetryListings[^1].ShouldBe(2);
        h.Mailbox.Fetches.Skip(fetchesBefore).ShouldBe([.. newer, 1, 2]);
        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.Stored);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored);
        h.Store.Row(h.Epoch, 2).Retries.ShouldBe(1);
        h.OutcomeOf(3).ShouldBe(DmarcMessageOutcome.RetryPending);

        await h.RunAtAsync(Now.AddHours(3));

        h.OutcomeOf(3).ShouldBe(DmarcMessageOutcome.Stored);
        h.Logged(3120).Last().ShouldContain("1 retried");
    }

    // Architect finding 10, reworded by the CTO (r2 §2d): a message that fails on every run is abandoned on the pickup
    // after 168 retries, a newer message is never held behind it, and a crash on the way counts one attempt only.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMessageThatFailsEveryRun_IsAbandonedAfter168Retries_AndNeverHoldsNewerMailBack(bool endsTheRun)
    {
        var h = new Harness();
        h.Answering(1, endsTheRun
            ? new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol)
            : new DmarcMailboxFetch.Refused());
        h.Report(2, 2);
        const int crashRun = 3;
        var run = 0;
        h.Store.FailOn = (call, position) =>
            run == crashRun && call == "SetOutcome" && position == 1 ? new SimulatedDatabaseFailure() : null;

        var analyzesBeforeLast = 0;
        for (run = 1; run <= DmarcIngestionLimits.RetryLimit + 2; run++)
        {
            analyzesBeforeLast = h.Store.Analyzes;
            h.Clock.UtcNow = Now.AddHours(run - 1);
            if (run == crashRun)
            {
                await Should.ThrowAsync<SimulatedDatabaseFailure>(h.RunAsync);
            }
            else
            {
                await h.RunAsync();
            }

            h.Store.Row(h.Epoch, 1).Attempts.ShouldBeLessThanOrEqualTo(DmarcIngestionLimits.MaxCrashAttempts);
            if (run == 1 && !endsTheRun)
            {
                h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored, "a refusal goes on to the newer message");
            }

            if (run == 1 && endsTheRun)
            {
                h.Store.Ledger.ContainsKey((h.Epoch, 2)).ShouldBeFalse("a session fault ends the run");
            }

            if (run == 2)
            {
                h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.Stored, "new mail is read before any retry");
            }

            if (run <= DmarcIngestionLimits.RetryLimit + 1)
            {
                h.OutcomeOf(1).ShouldBe(run == crashRun ? DmarcMessageOutcome.InProgress : DmarcMessageOutcome.RetryPending);
            }
        }

        var row = h.Store.Row(h.Epoch, 1);
        row.Outcome.ShouldBe(DmarcMessageOutcome.Abandoned);
        row.Retries.ShouldBe(DmarcIngestionLimits.RetryLimit);
        row.Attempts.ShouldBe(2, "the crash counted once, the caught failures never");
        h.Mailbox.Fetches.Count(position => position == 1).ShouldBe(DmarcIngestionLimits.RetryLimit + 1);
        h.Store.State.ShouldNotBeNull().LastOutcome.ShouldBe(DmarcRunOutcome.Succeeded);
        h.Store.Analyzes.ShouldBe(analyzesBeforeLast, "the run that only abandoned claimed nothing");
        h.Logged(3120).Last().ShouldContain("1 abandoned");
    }

    [Fact]
    public async Task TwoCrashesOnOneMessage_AbandonIt_AndItIsNeverFetchedAgain()
    {
        var h = new Harness();
        h.Report(1, 1);
        h.Store.FailOn = (call, position) => call == "Store" && position == 1 ? new SimulatedDatabaseFailure() : null;

        await Should.ThrowAsync<SimulatedDatabaseFailure>(h.RunAsync);
        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.InProgress, "a database failure leaves its claim for the next run to count");
        await Should.ThrowAsync<SimulatedDatabaseFailure>(() => h.RunAtAsync(Now.AddHours(1)));
        h.Store.Row(h.Epoch, 1).Attempts.ShouldBe(2);
        await h.RunAtAsync(Now.AddHours(2));

        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.Abandoned);
        h.Mailbox.Fetches.ShouldBe([1, 1]);
        h.Logged(3123).ShouldBe([
            "[DmarcReportIngestion] Earlier runs left messages in progress: 1 handed back, 0 abandoned",
            "[DmarcReportIngestion] Earlier runs left messages in progress: 0 handed back, 1 abandoned"]);
        h.Store.State.ShouldNotBeNull().LastOutcome.ShouldBe(DmarcRunOutcome.Succeeded);
    }

    // A message waits a year when no run reaches it, as when the job stood still; the retention job deletes its row on
    // its next daily run, and until then the retry must not store what retention would delete.
    [Fact]
    public async Task AWaitingMessageReceivedBeforeTheRetentionCutoff_IsNeverRetried()
    {
        var h = new Harness();
        var waiting = h.Answering(1, new DmarcMailboxFetch.Refused());
        await h.RunAsync();
        h.AnswerWithReport(waiting, 1);

        await h.RunAtAsync(Received.AddMonths(12).AddHours(1));

        h.Mailbox.Fetches.ShouldBe([1]);
        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.RetryPending);
        h.Store.Reports.ShouldBeEmpty();
        h.Logged(3120).Last().ShouldContain("1 past retention");
    }

    // ---- the deadline and the host's shutdown ----

    [Fact]
    public async Task TheRunDeadline_HandsTheMessageInHandBack_AndEndsTheRunAsATimeout()
    {
        var h = new Harness();
        h.Report(1, 1);
        h.Mailbox.Add(2, Received).Fetch = async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        };
        h.Report(3, 3);

        await h.Job(TimeSpan.FromSeconds(1)).RunAsync(Ct);

        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.Stored);
        h.OutcomeOf(2).ShouldBe(DmarcMessageOutcome.RetryPending);
        h.Store.Ledger.ContainsKey((h.Epoch, 3)).ShouldBeFalse();
        h.Store.Attempts.ShouldBe([(Now, DmarcRunOutcome.Timeout)]);
        h.Store.Analyzes.ShouldBe(0);
        h.Mailbox.Sessions.ShouldHaveSingleItem().Disposed.ShouldBeTrue();
        h.Logged(3122).ShouldBe(["[DmarcReportIngestion] Run ended without a success: Timeout"]);
    }

    [Fact]
    public async Task TheRunDeadlineWhileOpening_IsATimeout_WithNothingClaimed()
    {
        var h = new Harness();
        h.Report(1, 1);
        h.Mailbox.BeforeOpen = cancellationToken => new ValueTask(Task.Delay(Timeout.Infinite, cancellationToken));

        await h.Job(TimeSpan.FromSeconds(1)).RunAsync(Ct);

        h.Store.Calls.ShouldBe(["ReadState", "SweepCrashes", "RecordAttempt"]);
        h.Store.Attempts.ShouldBe([(Now, DmarcRunOutcome.Timeout)]);
    }

    [Fact]
    public async Task HostShutdown_HandsTheClaimBack_AndPropagates_SoTheNextRunCountsNoCrash()
    {
        var h = new Harness();
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var message = h.Mailbox.Add(1, Received);
        message.Fetch = async cancellationToken =>
        {
            await shutdown.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        };

        await Should.ThrowAsync<OperationCanceledException>(() => h.Job().RunAsync(shutdown.Token));

        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.RetryPending);
        h.Store.Attempts.ShouldBeEmpty();
        h.Store.State.ShouldNotBeNull().LastOutcome.ShouldBe(DmarcRunOutcome.Incomplete);
        h.Store.Analyzes.ShouldBe(0);
        h.Mailbox.Sessions.ShouldHaveSingleItem().Disposed.ShouldBeTrue();

        h.AnswerWithReport(message, 1);
        await h.RunAtAsync(Now.AddHours(1));

        h.Logged(3123).ShouldBeEmpty();
        h.OutcomeOf(1).ShouldBe(DmarcMessageOutcome.Stored);
        h.Store.Row(h.Epoch, 1).Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task HostShutdownWithNothingInHand_WritesNothing_AndPropagates()
    {
        var h = new Harness();
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        h.Mailbox.BeforeOpen = async cancellationToken =>
        {
            await shutdown.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        };

        await Should.ThrowAsync<OperationCanceledException>(() => h.Job().RunAsync(shutdown.Token));

        h.Store.Calls.ShouldBe(["ReadState", "SweepCrashes"]);
    }

    // ---- 7: ANALYZE ----

    [Theory]
    [InlineData("nothing new", 0)]
    [InlineData("a stored report", 1)]
    [InlineData("a skipped message", 1)]
    [InlineData("only mail past retention", 0)]
    [InlineData("a session fault", 0)]
    public async Task Analyze_FollowsACompletedRunThatClaimedAMessage_AndNoOtherRun(string mail, int analyzes)
    {
        var h = new Harness();
        switch (mail)
        {
            case "a stored report":
                h.Report(1, 1);
                break;
            case "a skipped message":
                h.Answering(1, new DmarcMailboxFetch.Skipped(DmarcSkipReason.Forensic));
                break;
            case "only mail past retention":
                h.Report(1, 1, receivedAt: Now.AddMonths(-13));
                break;
            case "a session fault":
                h.Report(1, 1);
                h.Answering(2, new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol));
                break;
        }

        await h.RunAsync();

        h.Store.Analyzes.ShouldBe(analyzes);
        if (analyzes > 0)
        {
            h.Store.Calls[^1].ShouldBe("Analyze");
        }
    }

    // ---- the log ----

    [Fact]
    public async Task TheLog_CarriesEventIdsOutcomesAndCountsOnly()
    {
        const string reporter = "canary-reporter.example";
        const string reportId = "canary-report-id-7731";
        const string source = "203.0.113.77";
        var h = new Harness();
        h.Mailbox.Epoch = 6_150_423;
        var candidate = Candidate(reportId, source: source) with { Reporter = reporter };
        h.Report(918_272, 1, candidate, receivedAt: Now.AddMonths(-13));
        h.Report(918_273, 2, candidate);
        var refused = h.Answering(918_274, new DmarcMailboxFetch.Refused());
        h.Answering(918_275, new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol));

        await h.RunAsync();
        h.AnswerWithReport(refused, 3, candidate with { ContentSha256 = Enumerable.Repeat((byte)0x77, 32).ToArray() });
        h.Store.FailOn = (call, _) => call == "Store" ? new SimulatedDatabaseFailure() : null;
        await Should.ThrowAsync<SimulatedDatabaseFailure>(() => h.RunAtAsync(Now.AddHours(1)));
        h.Store.FailOn = null;
        h.Mailbox.OpenOutcome = new DmarcMailboxOpen.Failed(DmarcSessionFault.AuthRefused);
        await h.RunAtAsync(Now.AddHours(2));
        await h.RunAtAsync(Now.AddHours(3));

        h.Log.Records.Select(r => r.EventId).Distinct().Order().ShouldBe([3120, 3121, 3122, 3123]);
        string[] canaries = [reporter, reportId, source, "918272", "918273", "918274", "918275", "6150423", "jobbliggaren.se"];
        foreach (var (_, _, message) in h.Log.Records)
        {
            foreach (var canary in canaries)
            {
                message.ShouldNotContain(canary, Case.Insensitive);
            }
        }
    }

    private static List<long> AddSkippedMessages(Harness h, long from, int count)
    {
        var positions = Enumerable.Range(0, count).Select(offset => from + offset).ToList();
        foreach (var position in positions)
        {
            h.Mailbox.Add(position, Received);
        }

        return positions;
    }
}
