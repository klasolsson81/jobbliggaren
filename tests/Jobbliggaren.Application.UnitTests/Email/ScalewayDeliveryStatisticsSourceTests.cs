using System.Globalization;
using System.Net;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.Admin.Email;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Email.ScalewayReadComposition;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The delivery totals read against Scaleway's <c>statistics</c> (#1981 PR 2), through the real composition with only
/// the transport replaced: exactly what is sent, that the window's start is the server's own, that all six counts must
/// be there, and that nothing about the project, the key or the body reaches any log.
/// </summary>
public sealed class ScalewayDeliveryStatisticsSourceTests : IDisposable
{
    private const string BodyMarker = "BODY-MARKER-4711";
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 19, 0, 0, 750, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ScriptedTransport _transport = new();
    private readonly CapturingLogSink _logs = new();
    private readonly ServiceProvider _provider;

    public ScalewayDeliveryStatisticsSourceTests() =>
        _provider = Build(_transport, _logs, new FakeDateTimeProvider(Now));

    public void Dispose() => _provider.Dispose();

    private ValueTask<ProviderAttempt<EmailDeliveryStatisticsRead>> AttemptAsync(
        StatisticsWindow window = StatisticsWindow.Last7Days, CancellationToken? token = null) =>
        _provider.GetRequiredService<IDeliveryStatisticsAttemptSource>().AttemptAsync(window, token ?? Ct);

    private static string Counts(
        string total = "12", string created = "0", string sending = "0", string sent = "10", string failed = "2", string canceled = "0") =>
        $$"""
        {"total_count":{{total}},"new_count":{{created}},"sending_count":{{sending}},"sent_count":{{sent}},
         "failed_count":{{failed}},"canceled_count":{{canceled}},"note":"{{BodyMarker}}","project_id":"{{ProjectId}}"}
        """;

    // ---- the request ----

    [Theory]
    [InlineData(nameof(StatisticsWindow.Last24Hours), "2026-10-18T19:00:00Z")]
    [InlineData(nameof(StatisticsWindow.Last3Days), "2026-10-16T19:00:00Z")]
    [InlineData(nameof(StatisticsWindow.Last7Days), "2026-10-12T19:00:00Z")]
    [InlineData(nameof(StatisticsWindow.Sentinel), "2026-09-17T19:00:00Z")]
    public async Task Attempt_ShouldSendOneGet_WithTheProjectAndTheServersOwnSince_AndNothingElse(string window, string since)
    {
        _transport.Respond(HttpStatusCode.OK, Counts());

        await AttemptAsync(Enum.Parse<StatisticsWindow>(window));

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri!.GetLeftPart(UriPartial.Path).ShouldBe(BaseAddress + "statistics");
        QueryOf(request.Uri).ShouldBe([("project_id", ProjectId), ("since", since)]);
    }

    [Fact]
    public async Task Attempt_ShouldSendTheReadKey_NeverTheSendKey()
    {
        _transport.Respond(HttpStatusCode.OK, Counts());
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.Unauthorized, "{}");
        await AttemptAsync(StatisticsWindow.Sentinel);

        _transport.Requests.Count.ShouldBe(2);
        _transport.Requests.ShouldAllBe(request => request.AuthToken == ReadKey);
    }

    // ---- an answer ----

    [Fact]
    public async Task Attempt_ShouldCarryTheTotalSentAndFailed_StampedWithTheClock()
    {
        _transport.Respond(HttpStatusCode.OK, Counts(total: "163", created: "1", sending: "2", sent: "150", failed: "7", canceled: "3"));

        var observed = (await AttemptAsync()).Read.ShouldBeOfType<EmailDeliveryStatisticsRead.Observed>();

        observed.TotalCount.ShouldBe(163);
        observed.SentCount.ShouldBe(150);
        observed.FailedCount.ShouldBe(7);
        observed.ObservedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Attempt_ShouldReadZeros_AndTheLargestCountTheWireCarries()
    {
        _transport.Respond(HttpStatusCode.OK, Counts("0", "0", "0", "0", "0", "0"));
        (await AttemptAsync()).Read.ShouldBeOfType<EmailDeliveryStatisticsRead.Observed>().TotalCount.ShouldBe(0);

        var max = uint.MaxValue.ToString(CultureInfo.InvariantCulture);
        _transport.Respond(HttpStatusCode.OK, Counts(total: max, sent: "0", failed: "0"));
        (await AttemptAsync()).Read.ShouldBeOfType<EmailDeliveryStatisticsRead.Observed>().TotalCount.ShouldBe(uint.MaxValue);
    }

    [Theory]
    [InlineData("total_count")]
    [InlineData("new_count")]
    [InlineData("sending_count")]
    [InlineData("sent_count")]
    [InlineData("failed_count")]
    [InlineData("canceled_count")]
    public async Task Attempt_ShouldBeMalformed_NeverZero_WhenAnyOfTheSixCountsIsMissing(string missing)
    {
        var body = Counts().Replace($"\"{missing}\":", "\"renamed\":", StringComparison.Ordinal);
        _transport.Respond(HttpStatusCode.OK, body);

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"3\"")]
    [InlineData("null")]
    [InlineData("4294967296")]
    [InlineData("{\"n\":1}")]
    public async Task Attempt_ShouldBeMalformed_WhenACountIsNotAUInt32(string canceled)
    {
        _transport.Respond(HttpStatusCode.OK, Counts(canceled: canceled));

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenSentAndFailedExceedTheTotal()
    {
        _transport.Respond(HttpStatusCode.OK, Counts(total: "5", sent: "4", failed: "2"));

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Theory]
    [InlineData("""{"nested":{"total_count":1,"new_count":0,"sending_count":0,"sent_count":1,"failed_count":0,"canceled_count":0}}""")]
    [InlineData("""[{"total_count":1,"new_count":0,"sending_count":0,"sent_count":1,"failed_count":0,"canceled_count":0}]""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Attempt_ShouldBeMalformed_WhenTheCountsAreNotTopLevelFields(string body)
    {
        _transport.Respond(HttpStatusCode.OK, body);

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenTheBodyIsLargerThanTheCap()
    {
        _transport.Respond(HttpStatusCode.OK, Counts() + new string(' ', ScalewayDeliveryStatisticsSource.MaxBodyBytes), omitContentLength: true);

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Malformed));
    }

    // ---- a refusal and no answer ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, EmailProviderReadReason.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, EmailProviderReadReason.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailProviderReadReason.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, EmailProviderReadReason.ProviderError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailProviderReadReason.ProviderError)]
    public async Task Attempt_ShouldMapEachRefusal_WithoutReadingItsBody(HttpStatusCode status, EmailProviderReadReason reason)
    {
        _transport.RespondWithUnreadableBody(status);

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(reason));
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, EmailProviderReadReason.Unreachable)]
    [InlineData(HttpRequestError.ResponseEnded, EmailProviderReadReason.ProviderError)]
    public async Task Attempt_ShouldMapATransportFailure(HttpRequestError error, EmailProviderReadReason reason)
    {
        _transport.Throw(new HttpRequestException(error, "transport said " + BodyMarker));

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(reason));
    }

    [Fact]
    public async Task Attempt_ShouldBeATimeout_WhenTheClientGivesUp()
    {
        _transport.Throw(new TaskCanceledException("client timeout", new TimeoutException()));

        (await AttemptAsync()).Read.ShouldBe(new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Timeout));
    }

    [Fact]
    public async Task Attempt_ShouldPropagateTheCallersOwnCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await AttemptAsync(token: cancelled.Token));
    }

    // ---- what is never logged ----

    [Fact]
    public async Task NoCategory_ShouldEverLogTheProjectId_EitherKey_OrTheBody()
    {
        _transport.Respond(HttpStatusCode.OK, Counts());
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.OK, "{\"echo\":\"" + BodyMarker + "\"}");
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.Forbidden, "{\"echo\":\"" + BodyMarker + "\"}");
        await AttemptAsync();
        _transport.Throw(new HttpRequestException(HttpRequestError.ConnectionError, "transport said " + BodyMarker));
        await AttemptAsync();

        foreach (var forbidden in new[] { ProjectId, SendKey, ReadKey, BodyMarker, "project_id", "since" })
        {
            _logs.Lines.ShouldAllBe(line => !line.Contains(forbidden, StringComparison.Ordinal));
        }

        _logs.Records.ShouldNotContain(r => r.Category.StartsWith("System.Net.Http", StringComparison.Ordinal));
        _logs.Records.Where(r => r.EventId == 3020).ShouldAllBe(r => r.Message.Contains("statistics", StringComparison.Ordinal));
    }
}
