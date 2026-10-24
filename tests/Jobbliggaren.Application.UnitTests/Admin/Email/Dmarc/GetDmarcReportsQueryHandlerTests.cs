using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the DMARC query: an admin request with no period, one read of the fixed window ending now,
/// at most the source cap, and the evaluator's verdict.
/// </summary>
public sealed class GetDmarcReportsQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingSource(DmarcReportsRead read) : IDmarcReportsSource
    {
        public List<(DateTimeOffset Start, DateTimeOffset End, int MaxSources)> Reads { get; } = [];

        public ValueTask<DmarcReportsRead> ReadAsync(
            DateTimeOffset windowStart, DateTimeOffset windowEnd, int maxSources, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add((windowStart, windowEnd, maxSources));
            return ValueTask.FromResult(read);
        }
    }

    /// <summary>The SQL source fails its read on a database error.</summary>
    private sealed class FailingSource : IDmarcReportsSource
    {
        public ValueTask<DmarcReportsRead> ReadAsync(
            DateTimeOffset windowStart, DateTimeOffset windowEnd, int maxSources, CancellationToken cancellationToken) =>
            ValueTask.FromException<DmarcReportsRead>(new InvalidOperationException("the read failed"));
    }

    private static readonly DmarcReportsRead NeverRead = new(null, [], new DmarcReportsTotals(0, 0, 0, 0, 0, 0, null, null), [], 0);

    private static GetDmarcReportsQueryHandler Handler(IDmarcReportsSource source) => new(source, new SettableClock(Now));

    [Fact]
    public void TheQuery_IsAnAdminRequest_AndTakesNoPeriod()
    {
        typeof(IAdminRequest).IsAssignableFrom(typeof(GetDmarcReportsQuery)).ShouldBeTrue();
        typeof(GetDmarcReportsQuery).GetProperties().Where(p => p.DeclaringType == typeof(GetDmarcReportsQuery))
            .ShouldBeEmpty("the region's window is fixed; no request can widen or move it");
    }

    [Fact]
    public async Task Handle_ReadsTheSevenTimesTwentyFourHoursEndingNow_AtMostTheSourceCap()
    {
        var source = new RecordingSource(NeverRead);

        await Handler(source).Handle(new GetDmarcReportsQuery(), Ct);

        source.Reads.ShouldBe([(Now.AddHours(-7 * 24), Now, 25)]);
    }

    [Fact]
    public async Task Handle_AnswersWithTheEvaluatorsVerdictOnTheRead()
    {
        var success = Now.AddMinutes(-30);
        var read = NeverRead with
        {
            State = new DmarcRunState(success, DmarcRunOutcome.Succeeded, success, new DmarcCursor(4_200_017, 3)),
        };

        var dto = await Handler(new RecordingSource(read)).Handle(new GetDmarcReportsQuery(), Ct);

        dto.ShouldBe(DmarcReportsEvaluator.Evaluate(read, Now));
        dto.Status.ShouldBe(EmailDmarcStatus.NoReports);
    }

    [Fact]
    public async Task Handle_WhenTheReadFails_Fails()
        => await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Handler(new FailingSource()).Handle(new GetDmarcReportsQuery(), Ct));

    [Fact]
    public async Task Handle_LetsTheCallersCancellationThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Handler(new RecordingSource(NeverRead)).Handle(new GetDmarcReportsQuery(), cts.Token));
    }
}
