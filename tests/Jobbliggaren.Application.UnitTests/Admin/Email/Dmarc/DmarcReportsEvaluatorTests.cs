using System.Net;
using System.Text.Json;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — what the DMARC region may show. Every state row below is one the ingestion job writes: no
/// row before its first run, <see cref="DmarcRunOutcome.Incomplete"/> while a first run has not finished, a run outcome
/// with the latest success kept. The totals and sources are the shapes the SQL source reads back, its addresses through
/// <see cref="ReportedSourceAddress.FromStored"/>, from the documentation ranges only. A read no source can produce is
/// declared as such and asserted only to be refused.
/// </summary>
public sealed class DmarcReportsEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastAttempt = Now.AddMinutes(-20);
    private static readonly DateTimeOffset EarlierSuccess = Now.AddDays(-2);

    private static readonly DmarcReportsTotals NoReports = new(0, 0, 0, 0, 0, 0, null, null);

    private static readonly DmarcReportsTotals SomeReports = new(
        Reports: 3,
        Reporters: 2,
        SourceCount: 3,
        Messages: 1_200,
        FailingMessages: 1,
        ConflictingReports: 1,
        CoveredFrom: new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero),
        CoveredTo: new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));

    private static readonly IReadOnlyList<DmarcSourceRow> SomeSources =
    [
        new(Address("198.51.100.20"), "jobbliggaren.se", 100, 99, 0, 1),
        new(Address("::ffff:192.0.2.9"), "mail.jobbliggaren.se", 900, 900, 0, 0),
        new(Address("2001:db8::25"), "jobbliggaren.se", 200, 197, 200, 0),
    ];

    private static readonly IReadOnlyList<DmarcSourceRow> CappedSources =
    [
        .. Enumerable.Range(1, DmarcReportsEvaluator.MaxSources)
            .Select(n => new DmarcSourceRow(Address($"192.0.2.{n}"), "jobbliggaren.se", 10, 10, 10, 0)),
    ];

    private static ReportedSourceAddress Address(string address) => ReportedSourceAddress.FromStored(IPAddress.Parse(address));

    // A run that never opened the mailbox leaves no cursor; the evaluator reads none either way.
    private static DmarcRunState State(
        DmarcRunOutcome latest, DateTimeOffset? lastSuccessAt, DateTimeOffset? lastAttemptAt = null) =>
        new(lastAttemptAt ?? LastAttempt, latest, lastSuccessAt, lastSuccessAt is null ? null : new DmarcCursor(4_200_017, 12));

    private static DmarcReportsRead Read(
        DmarcRunState? state,
        DmarcReportsTotals? totals = null,
        IReadOnlyList<DmarcSourceRow>? sources = null,
        IReadOnlyList<DmarcOutcomeCount>? counts = null,
        long retryPending = 0) =>
        new(state, sources ?? (totals is { Reports: > 0 } ? SomeSources : []), totals ?? NoReports, counts ?? [], retryPending);

    private static EmailDmarcDto Evaluate(DmarcReportsRead read) => DmarcReportsEvaluator.Evaluate(read, Now);

    // ---- the states and their precedence ----

    public static TheoryData<DmarcRunOutcome?, bool, bool, EmailDmarcStatus> States => new()
    {
        { null, false, false, EmailDmarcStatus.NotObservable },
        { DmarcRunOutcome.NotConfigured, false, false, EmailDmarcStatus.NotConfigured },
        { DmarcRunOutcome.NotConfigured, true, true, EmailDmarcStatus.NotConfigured },
        { DmarcRunOutcome.Misconfigured, false, false, EmailDmarcStatus.Misconfigured },
        { DmarcRunOutcome.Misconfigured, true, true, EmailDmarcStatus.Misconfigured },
        { DmarcRunOutcome.Incomplete, false, true, EmailDmarcStatus.Failed },
        { DmarcRunOutcome.Unreachable, false, true, EmailDmarcStatus.Failed },
        { DmarcRunOutcome.TlsFailed, false, false, EmailDmarcStatus.Failed },
        { DmarcRunOutcome.AuthRefused, false, true, EmailDmarcStatus.Failed },
        { DmarcRunOutcome.Timeout, false, false, EmailDmarcStatus.Failed },
        { DmarcRunOutcome.Protocol, false, true, EmailDmarcStatus.Failed },
        { DmarcRunOutcome.Succeeded, true, true, EmailDmarcStatus.Observed },
        { DmarcRunOutcome.Succeeded, true, false, EmailDmarcStatus.NoReports },
        { DmarcRunOutcome.Timeout, true, true, EmailDmarcStatus.Observed },
        { DmarcRunOutcome.AuthRefused, true, false, EmailDmarcStatus.NoReports },
    };

    // A first run that stored a report and then hit a fault leaves reports without a success, and a password removed
    // after a success leaves both; neither is shown as figures.
    [Theory]
    [MemberData(nameof(States))]
    public void Evaluate_DecidesTheState_InTheOnePrecedenceOrder(
        DmarcRunOutcome? latest, bool succeededBefore, bool reportsInWindow, EmailDmarcStatus expected)
    {
        var success = latest == DmarcRunOutcome.Succeeded ? LastAttempt : succeededBefore ? EarlierSuccess : (DateTimeOffset?)null;
        var state = latest is { } outcome ? State(outcome, success) : null;

        var dto = Evaluate(Read(state, reportsInWindow ? SomeReports : NoReports));

        dto.Status.ShouldBe(expected);
        (dto.Reports is not null).ShouldBe(expected == EmailDmarcStatus.Observed, "figures exactly when observed");
        (dto.Mailbox is not null).ShouldBe(expected is EmailDmarcStatus.Observed or EmailDmarcStatus.NoReports);
        (dto.Ingestion is not null).ShouldBe(
            expected is EmailDmarcStatus.Observed or EmailDmarcStatus.NoReports or EmailDmarcStatus.Failed);
    }

    [Theory]
    [InlineData(EmailDmarcStatus.NotObservable)]
    [InlineData(EmailDmarcStatus.NotConfigured)]
    [InlineData(EmailDmarcStatus.Misconfigured)]
    public void AMailboxNotRead_CarriesItsStatusAndNothingElse(EmailDmarcStatus status)
    {
        var state = status switch
        {
            EmailDmarcStatus.NotConfigured => State(DmarcRunOutcome.NotConfigured, EarlierSuccess),
            EmailDmarcStatus.Misconfigured => State(DmarcRunOutcome.Misconfigured, EarlierSuccess),
            _ => null,
        };

        var dto = Evaluate(state is null
            ? Read(null)
            : Read(state, SomeReports, counts: [new(DmarcMessageOutcome.SkippedForensic, 2)], retryPending: 3));

        dto.ShouldBe(status switch
        {
            EmailDmarcStatus.NotConfigured => EmailDmarcDto.NotConfigured(),
            EmailDmarcStatus.Misconfigured => EmailDmarcDto.Misconfigured(),
            _ => EmailDmarcDto.NotObservable(),
        });
        dto.Ingestion.ShouldBeNull();
        dto.From.ShouldBeNull();
        dto.Mailbox.ShouldBeNull();
        dto.Sources.ShouldBeNull();
    }

    [Theory]
    [InlineData(DmarcRunOutcome.Incomplete)]
    [InlineData(DmarcRunOutcome.Unreachable)]
    [InlineData(DmarcRunOutcome.TlsFailed)]
    [InlineData(DmarcRunOutcome.AuthRefused)]
    [InlineData(DmarcRunOutcome.AuthUnconfirmed)]
    [InlineData(DmarcRunOutcome.Timeout)]
    [InlineData(DmarcRunOutcome.Protocol)]
    public void AMailboxNeverReadSuccessfully_IsFailed_WithTheLatestOutcome_AndNoFigure(DmarcRunOutcome latest)
    {
        var dto = Evaluate(Read(State(latest, null), SomeReports));

        dto.Status.ShouldBe(EmailDmarcStatus.Failed);
        dto.Ingestion.ShouldBe(new DmarcIngestionDto(LastAttempt, null, latest, stale: true));
        dto.From.ShouldBeNull();
        dto.ConflictingReports.ShouldBeNull();
        dto.Mailbox.ShouldBeNull();
        dto.Reports.ShouldBeNull();
        dto.Sources.ShouldBeNull();
    }

    [Fact]
    public void AWindowWithReports_IsObserved_WithEveryFigureAndTheSourcesInTheSourcesOrder()
    {
        var dto = Evaluate(Read(State(DmarcRunOutcome.Succeeded, LastAttempt), SomeReports, retryPending: 2));

        dto.Status.ShouldBe(EmailDmarcStatus.Observed);
        dto.Ingestion.ShouldBe(new DmarcIngestionDto(LastAttempt, LastAttempt, null, stale: false));
        dto.From.ShouldBe(Now.AddDays(-7));
        dto.To.ShouldBe(Now);
        dto.CoveredFrom.ShouldBe(SomeReports.CoveredFrom);
        dto.CoveredTo.ShouldBe(SomeReports.CoveredTo);
        dto.ConflictingReports.ShouldBe(1);
        dto.Reports.ShouldBe(3);
        dto.Reporters.ShouldBe(2);
        dto.Messages.ShouldBe(1_200);
        dto.FailingMessages.ShouldBe(1);
        dto.SourceCount.ShouldBe(3);
        dto.Mailbox.ShouldBe(new DmarcMailboxCountsDto(0, 0, 0, 0, 2));
        var sources = dto.Sources.ShouldNotBeNull();
        sources.Select(s => s.Address).ShouldBe(["198.51.100.20", "192.0.2.9", "2001:db8::25"]);
        sources.Select(s => s.Flagged).ShouldBe([true, false, false]);
        sources[0].ShouldBe(new DmarcSourceDto(SomeSources[0]));
        sources[0].HeaderFrom.ShouldBe("jobbliggaren.se");
        sources[0].Messages.ShouldBe(100);
        sources[0].DkimAligned.ShouldBe(99);
        sources[0].SpfAligned.ShouldBe(0);
        sources[0].FailingDmarc.ShouldBe(1, "one failing message of a hundred flags the source");
    }

    [Fact]
    public void MoreSourcesThanTheCap_ShowTheCap_AndCountAndSumEverySource()
    {
        var totals = SomeReports with { SourceCount = 30, Messages = 255, FailingMessages = 0 };

        var dto = Evaluate(Read(State(DmarcRunOutcome.Succeeded, LastAttempt), totals, CappedSources));

        dto.Sources.ShouldNotBeNull().Count.ShouldBe(DmarcReportsEvaluator.MaxSources);
        dto.SourceCount.ShouldBe(30, "the cap note's total is every source, not the rows shown");
        dto.Messages.ShouldBe(255, "the figures count every source, the five not shown included");
        dto.FailingMessages.ShouldBe(0, "a source with a failing message sorts into the shown rows");
    }

    // The actor: one report at Accept's record and count caps, every record from one source, which a single UTC day's
    // storage cap admits.
    [Fact]
    public void TheFigures_AreSixtyFourBit_UpToOneReportAtItsCaps()
    {
        const long messages = (long)DmarcIngestionLimits.MaxRecordsPerReport * DmarcIngestionLimits.MaxRecordMessageCount;
        var totals = SomeReports with { Reports = 1, Reporters = 1, SourceCount = 1, Messages = messages, FailingMessages = messages };
        IReadOnlyList<DmarcSourceRow> sources = [new(Address("203.0.113.5"), "jobbliggaren.se", messages, 0, 0, messages)];

        var dto = Evaluate(Read(State(DmarcRunOutcome.Succeeded, LastAttempt), totals, sources));

        dto.Messages.ShouldBe(messages);
        dto.Sources.ShouldNotBeNull().ShouldHaveSingleItem().FailingDmarc.ShouldBe(messages);
        Serialised(dto).GetProperty("failingMessages").GetInt64().ShouldBe(messages);
    }

    [Fact]
    public void AWindowWithoutReports_ShowsNoFigure_NeverAZero_ButKeepsTheWindowAndTheMailboxCounts()
    {
        var dto = Evaluate(Read(
            State(DmarcRunOutcome.Succeeded, LastAttempt),
            NoReports with { ConflictingReports = 2 },
            counts: [new(DmarcMessageOutcome.RejectedMalformed, 1)]));

        dto.Status.ShouldBe(EmailDmarcStatus.NoReports);
        dto.From.ShouldBe(Now.AddDays(-7));
        dto.To.ShouldBe(Now);
        dto.ConflictingReports.ShouldBe(2, "every version of a conflicting report is out of the figures, and it is counted");
        dto.Mailbox.ShouldBe(new DmarcMailboxCountsDto(0, 1, 0, 0, 0));
        dto.CoveredFrom.ShouldBeNull();
        dto.CoveredTo.ShouldBeNull();
        dto.Reports.ShouldBeNull();
        dto.Reporters.ShouldBeNull();
        dto.Messages.ShouldBeNull();
        dto.FailingMessages.ShouldBeNull();
        dto.SourceCount.ShouldBeNull();
        dto.Sources.ShouldBeNull();
    }

    [Fact]
    public void ARunThatFailedAfterAnEarlierSuccess_KeepsTheFigures_StaleWithItsOutcome()
    {
        var dto = Evaluate(Read(State(DmarcRunOutcome.Protocol, EarlierSuccess), SomeReports));

        dto.Status.ShouldBe(EmailDmarcStatus.Observed);
        dto.Ingestion.ShouldBe(new DmarcIngestionDto(LastAttempt, EarlierSuccess, DmarcRunOutcome.Protocol, stale: true));
        dto.Messages.ShouldBe(1_200);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ASuccess_IsStaleOnceOlderThanThreeHours(int secondsPastThreshold, bool stale)
    {
        var at = Now - DmarcReportsEvaluator.StaleAfter - TimeSpan.FromSeconds(secondsPastThreshold);

        var dto = Evaluate(Read(State(DmarcRunOutcome.Succeeded, at, lastAttemptAt: at), SomeReports));

        dto.Ingestion.ShouldNotBeNull().Stale.ShouldBe(stale);
        dto.Ingestion.LatestFailure.ShouldBeNull();
    }

    [Theory]
    [InlineData(DmarcRunOutcome.AuthRefused, false)]
    [InlineData(DmarcRunOutcome.AuthRefused, true)]
    [InlineData(DmarcRunOutcome.AuthUnconfirmed, false)]
    [InlineData(DmarcRunOutcome.AuthUnconfirmed, true)]
    public void ASignInThatBacksOff_SaysWhenSignInResumes_FailedAndAfterASuccess(DmarcRunOutcome latest, bool succeededBefore)
    {
        var dto = Evaluate(Read(State(latest, succeededBefore ? EarlierSuccess : null), SomeReports));

        dto.Status.ShouldBe(succeededBefore ? EmailDmarcStatus.Observed : EmailDmarcStatus.Failed);
        dto.Ingestion.ShouldNotBeNull().SignInResumesAt.ShouldBe(LastAttempt + DmarcIngestionLimits.SignInBackoff);
        Serialised(dto).GetProperty("ingestion").GetProperty("signInResumesAt").GetDateTimeOffset()
            .ShouldBe(LastAttempt + DmarcIngestionLimits.SignInBackoff);
    }

    // Only the pairs the job writes: Incomplete before any success, Succeeded only as one.
    [Theory]
    [InlineData(DmarcRunOutcome.Succeeded, true)]
    [InlineData(DmarcRunOutcome.Incomplete, false)]
    [InlineData(DmarcRunOutcome.Unreachable, false)]
    [InlineData(DmarcRunOutcome.Unreachable, true)]
    [InlineData(DmarcRunOutcome.TlsFailed, false)]
    [InlineData(DmarcRunOutcome.TlsFailed, true)]
    [InlineData(DmarcRunOutcome.Timeout, false)]
    [InlineData(DmarcRunOutcome.Timeout, true)]
    [InlineData(DmarcRunOutcome.Protocol, false)]
    [InlineData(DmarcRunOutcome.Protocol, true)]
    public void EveryOtherOutcome_SaysNothingAboutWhenSignInResumes(DmarcRunOutcome latest, bool succeededBefore)
    {
        var dto = Evaluate(Read(State(latest, succeededBefore ? EarlierSuccess : null), SomeReports));

        dto.Ingestion.ShouldNotBeNull().SignInResumesAt.ShouldBeNull();
    }

    [Fact]
    public void TheMailboxCounts_GroupEveryClassThatStoredNothing_AndLeaveTheRestOut()
    {
        IReadOnlyList<DmarcOutcomeCount> counts =
        [
            new(DmarcMessageOutcome.InProgress, 1_000_000),
            new(DmarcMessageOutcome.RetryPending, 1_000_000),
            new(DmarcMessageOutcome.Abandoned, 1),
            new(DmarcMessageOutcome.Stored, 1_000_000),
            new(DmarcMessageOutcome.Duplicate, 1_000_000),
            new(DmarcMessageOutcome.StoredConflicting, 1_000_000),
            new(DmarcMessageOutcome.VolumeCapped, 2),
            new(DmarcMessageOutcome.SkippedTooLarge, 10),
            new(DmarcMessageOutcome.SkippedNoReportPart, 20),
            new(DmarcMessageOutcome.SkippedMultipleReportParts, 40),
            new(DmarcMessageOutcome.SkippedForensic, 80),
            new(DmarcMessageOutcome.SkippedUnsupportedEncoding, 160),
            new(DmarcMessageOutcome.SkippedHeaderTooLarge, 320),
            new(DmarcMessageOutcome.SkippedNotInMailbox, 640),
            new(DmarcMessageOutcome.RejectedMalformed, 1_000),
            new(DmarcMessageOutcome.RejectedPolicyDomain, 2_000),
            new(DmarcMessageOutcome.RejectedContent, 4_000),
            new(DmarcMessageOutcome.RejectedByStore, 8_000),
        ];
        counts.Select(c => c.Outcome).ShouldBe(Enum.GetValues<DmarcMessageOutcome>(), ignoreOrder: true);

        var dto = Evaluate(Read(State(DmarcRunOutcome.Succeeded, LastAttempt), counts: counts, retryPending: 5));

        dto.Mailbox.ShouldBe(new DmarcMailboxCountsDto(
            skipped: 1_270, rejected: 15_000, volumeCapped: 2, abandoned: 1, retryPending: 5));
    }

    [Fact]
    public void WindowOf_IsSevenTimesTwentyFourHoursEndingAtTheRead_InUtc()
    {
        var (from, to) = DmarcReportsEvaluator.WindowOf(new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.FromHours(2)));

        to.ShouldBe(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero));
        to.Offset.ShouldBe(TimeSpan.Zero);
        (to - from).ShouldBe(TimeSpan.FromHours(7 * 24), "across the DST change the window is hours, not civil days");
    }

    // ---- input no source can produce ----

    // SqlDmarcReportsSource.SourcesSql counts dkim and spf over the records that passed, and failing over the records
    // that passed neither, so no failing message is an aligned one.
    [Fact]
    public void EverySourceRowHere_IsOneTheSqlSourceCanReturn()
    {
        IReadOnlyList<DmarcSourceRow> rows = [.. SomeSources, .. CappedSources];

        rows.ShouldAllBe(row => row.FailingDmarc + Math.Max(row.DkimAligned, row.SpfAligned) <= row.Messages);
    }

    [Fact]
    public void AReadNoSourceCanProduce_IsRefused()
    {
        var succeeded = State(DmarcRunOutcome.Succeeded, LastAttempt);

        Should.Throw<ArgumentOutOfRangeException>(() => Evaluate(Read(State((DmarcRunOutcome)99, null))));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Evaluate(Read(succeeded, counts: [new((DmarcMessageOutcome)99, 1)])));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Evaluate(Read(succeeded, counts: [new(DmarcMessageOutcome.SkippedForensic, -1)])));
        Should.Throw<ArgumentException>(() => Evaluate(Read(State(DmarcRunOutcome.Succeeded, null))));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, NoReports, sources: SomeSources)));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, NoReports with { Messages = 4 })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, SomeReports with { FailingMessages = 1_201 })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, SomeReports with { CoveredTo = null })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, SomeReports with { Reporters = 4 })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, SomeReports with { SourceCount = 30 })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(succeeded, SomeReports with { Messages = 1_300 })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(
            succeeded, SomeReports with { CoveredFrom = Now.AddDays(-10), CoveredTo = Now.AddDays(-7) })));
        Should.Throw<ArgumentException>(() => Evaluate(Read(State(DmarcRunOutcome.Incomplete, EarlierSuccess), SomeReports)));
        Should.Throw<ArgumentException>(() => Evaluate(Read(
            succeeded,
            SomeReports with { SourceCount = 100 },
            sources: [.. Enumerable.Repeat(SomeSources[0], DmarcReportsEvaluator.MaxSources + 1)])));
    }

    [Fact]
    public void TheShapes_RefuseWhatCannotBeTrue()
    {
        Should.Throw<ArgumentException>(() => new DmarcIngestionDto(Now, null, DmarcRunOutcome.Timeout, stale: false));
        Should.Throw<ArgumentException>(() => new DmarcIngestionDto(Now, Now, DmarcRunOutcome.NotConfigured, stale: true));
        Should.Throw<ArgumentException>(() => new DmarcIngestionDto(Now, Now, DmarcRunOutcome.Succeeded, stale: true));
        Should.Throw<ArgumentException>(() => new DmarcIngestionDto(Now, null, null, stale: false));
        Should.Throw<ArgumentException>(() => new DmarcIngestionDto(Now, Now, DmarcRunOutcome.Incomplete, stale: true));
        Should.Throw<ArgumentException>(() => EmailDmarcDto.Failed(new DmarcIngestionDto(Now, Now, DmarcRunOutcome.Timeout, true)));
        Should.Throw<ArgumentException>(() =>
            new DmarcSourceDto(new DmarcSourceRow(Address("192.0.2.1"), "jobbliggaren.se", 3, 4, 0, 0)));
        Should.Throw<ArgumentException>(() =>
            new DmarcSourceDto(new DmarcSourceRow(Address("192.0.2.1"), "jobbliggaren.se", 0, 0, 0, 0)));
        Should.Throw<ArgumentOutOfRangeException>(() => new DmarcMailboxCountsDto(0, -1, 0, 0, 0));
    }

    // ---- the wire ----

    [Fact]
    public void TheBrowser_GetsExactlyTheKeysItWasPromised_InEveryState()
    {
        var observed = Serialised(Evaluate(Read(State(DmarcRunOutcome.Timeout, EarlierSuccess), SomeReports)));
        var notConfigured = Serialised(EmailDmarcDto.NotConfigured());
        string[] keys =
        [
            "conflictingReports", "coveredFrom", "coveredTo", "failingMessages", "from", "ingestion", "mailbox",
            "messages", "reporters", "reports", "sourceCount", "sources", "status", "to",
        ];

        Keys(observed).ShouldBe(keys);
        Keys(notConfigured).ShouldBe(keys);
        Keys(observed.GetProperty("ingestion"))
            .ShouldBe(["lastAttemptAt", "lastSuccessAt", "latestFailure", "signInResumesAt", "stale"]);
        observed.GetProperty("ingestion").GetProperty("signInResumesAt").ValueKind.ShouldBe(JsonValueKind.Null);
        Keys(observed.GetProperty("mailbox")).ShouldBe(["abandoned", "rejected", "retryPending", "skipped", "volumeCapped"]);
        Keys(observed.GetProperty("sources")[0])
            .ShouldBe(["address", "dkimAligned", "failingDmarc", "flagged", "headerFrom", "messages", "spfAligned"]);
        observed.GetProperty("status").GetString().ShouldBe("Observed");
        observed.GetProperty("ingestion").GetProperty("latestFailure").GetString().ShouldBe("Timeout");
        notConfigured.GetProperty("status").GetString().ShouldBe("NotConfigured");
        notConfigured.GetProperty("reports").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static JsonElement Serialised(EmailDmarcDto dto) =>
        JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web)).RootElement;

    private static string[] Keys(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];
}
