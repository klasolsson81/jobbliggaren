using System.Net;
using System.Text;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.Admin.Email;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Email.ScalewayReadComposition;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The recent-failures read against Scaleway's <c>emails</c> (#1981 PR 2; security-auditor dom 4 and 8d, dotnet-architect
/// V4 and N2), through the real composition with only the transport replaced: exactly what is sent; that the answer is
/// checked against the query, so a provider that ignored a parameter yields no list rather than a wrong one; that only
/// the masked recipient leaves the adapter; and that no address, full or masked, reaches any log. Every address is on a
/// domain RFC 2606/6761 reserves.
/// </summary>
public sealed class ScalewayRecentFailuresSourceTests : IDisposable
{
    private const string MarkerLocalPart = "zz-marker-4711";
    private const string MarkerAddress = MarkerLocalPart + "@example.com";
    private const string MaskedMarker = "z•••@example.com";
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 19, 0, 0, 750, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ScriptedTransport _transport = new();
    private readonly CapturingLogSink _logs = new();
    private readonly ServiceProvider _provider;

    public ScalewayRecentFailuresSourceTests() =>
        _provider = Build(_transport, _logs, new FakeDateTimeProvider(Now));

    public void Dispose() => _provider.Dispose();

    private ValueTask<ProviderAttempt<EmailRecentFailuresRead>> AttemptAsync(
        EmailDeliveryPeriod period = EmailDeliveryPeriod.Last7Days, CancellationToken? token = null) =>
        _provider.GetRequiredService<IRecentFailuresAttemptSource>().AttemptAsync(period, token ?? Ct);

    /// <summary>One item in the provider's shape, every field present; the fragments are raw JSON values.</summary>
    private static string Item(
        string createdAt = "2026-10-19T18:39:12.123456Z",
        string status = "\"failed\"",
        string flags = """["hard_bounce"]""",
        string recipient = "\"" + MarkerAddress + "\"") =>
        $$"""
        {"id":"6f1c2a52-6a3e-4a8b-9c4e-0d6f7b8a9c01","message_id":"<{{MarkerLocalPart}}.msg@example.com>",
         "project_id":"{{ProjectId}}","mail_from":"noreply@example.com","mail_rcpt":{{recipient}},
         "rcpt_to":"{{MarkerAddress}}","rcpt_type":"to","subject":"Till {{MarkerAddress}}",
         "created_at":"{{createdAt}}","updated_at":"2026-10-19T18:45:00Z","status":{{status}},
         "status_details":"550 5.1.1 <{{MarkerAddress}}>: user unknown","try_count":3,
         "last_tries":[{"rank":1,"tried_at":"2026-10-19T18:40:00Z","code":550,"message":"550 <{{MarkerAddress}}> unknown"}],
         "flags":{{flags}}}
        """;

    private static string Page(params string[] items) =>
        $$"""{"total_count":{{items.Length}},"emails":[{{string.Join(",", items)}}]}""";

    private static string CreatedAt(int minutesAgo) =>
        Now.AddMinutes(-minutesAgo).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<EmailRecentFailuresRead.Observed> ObservedAsync(string body)
    {
        _transport.Respond(HttpStatusCode.OK, body);
        return (await AttemptAsync()).Read.ShouldBeOfType<EmailRecentFailuresRead.Observed>();
    }

    private async Task ShouldBeMalformedAsync(string body)
    {
        _transport.Respond(HttpStatusCode.OK, body);
        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Malformed));
    }

    // ---- the request ----

    [Theory]
    [InlineData(EmailDeliveryPeriod.Last24Hours, "2026-10-18T19:00:00Z")]
    [InlineData(EmailDeliveryPeriod.Last3Days, "2026-10-16T19:00:00Z")]
    [InlineData(EmailDeliveryPeriod.Last7Days, "2026-10-12T19:00:00Z")]
    public async Task Attempt_ShouldSendExactlyTheFailedFilter_ThePeriodsOwnSince_OnePageOfTen_NewestFirst(
        EmailDeliveryPeriod period, string since)
    {
        _transport.Respond(HttpStatusCode.OK, Page());

        await AttemptAsync(period);

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri!.GetLeftPart(UriPartial.Path).ShouldBe(BaseAddress + "emails");
        QueryOf(request.Uri).ShouldBe(
        [
            ("project_id", ProjectId),
            ("statuses", "failed"),
            ("since", since),
            ("page_size", "10"),
            ("order_by", "created_at_desc"),
        ]);
    }

    [Fact]
    public async Task Attempt_ShouldNeverSendAParameterThatLooksUpOnePerson()
    {
        _transport.Respond(HttpStatusCode.OK, Page());

        await AttemptAsync();

        var names = QueryOf(_transport.Requests.ShouldHaveSingleItem().Uri).Select(pair => pair.Name).ToList();
        foreach (var lookup in new[] { "mail_rcpt", "search", "mail_from", "message_id" })
        {
            names.ShouldNotContain(lookup);
        }
    }

    [Fact]
    public async Task Attempt_ShouldSendTheReadKey_NeverTheSendKey()
    {
        _transport.Respond(HttpStatusCode.OK, Page());
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.Forbidden, "{}");
        await AttemptAsync();

        _transport.Requests.ShouldAllBe(request => request.AuthToken == ReadKey);
    }

    // ---- an answer ----

    [Fact]
    public async Task Attempt_ShouldCarryEachFailure_WithItsTimeTheMaskedRecipientAndTheReason_NewestFirst()
    {
        var observed = await ObservedAsync(Page(
            Item(createdAt: "2026-10-19T18:39:12.123456Z", flags: """["mailbox_full"]"""),
            Item(createdAt: "2026-10-19T12:00:00Z", recipient: "\"adam@example.org\"", flags: """["soft_bounce"]""")));

        observed.ObservedAt.ShouldBe(Now);
        observed.Failures.Count.ShouldBe(2);
        observed.Failures[0].CreatedAt.ShouldBe(new DateTimeOffset(2026, 10, 19, 18, 39, 12, TimeSpan.Zero).AddTicks(1_234_560));
        observed.Failures[0].Recipient.Value.ShouldBe(MaskedMarker);
        observed.Failures[0].Reason.ShouldBe(EmailFailureReason.MailboxFull);
        observed.Failures[1].Recipient.Value.ShouldBe("a•••@example.org");
        observed.Failures[1].Reason.ShouldBe(EmailFailureReason.SoftBounce);
    }

    [Fact]
    public async Task Attempt_ShouldReadATimeWithNanoseconds_AndOneWithAnOffset()
    {
        var observed = await ObservedAsync(Page(
            Item(createdAt: "2026-10-19T18:39:12.123456789Z"),
            Item(createdAt: "2026-10-19T20:00:00+02:00")));

        observed.Failures[0].CreatedAt.UtcDateTime.ShouldBe(new DateTime(2026, 10, 19, 18, 39, 12, DateTimeKind.Utc).AddTicks(1_234_567));
        observed.Failures[1].CreatedAt.UtcDateTime.ShouldBe(new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Attempt_ShouldObserveAnEmptyPage_AsAnEmptyList() =>
        (await ObservedAsync(Page())).Failures.ShouldBeEmpty();

    [Theory]
    [InlineData("""["mailbox_not_found"]""", EmailFailureReason.MailboxNotFound)]
    [InlineData("""["hard_bounce"]""", EmailFailureReason.HardBounce)]
    [InlineData("""["mailbox_full"]""", EmailFailureReason.MailboxFull)]
    [InlineData("""["soft_bounce"]""", EmailFailureReason.SoftBounce)]
    [InlineData("""["spam"]""", EmailFailureReason.Spam)]
    [InlineData("""["blocklisted"]""", EmailFailureReason.Blocklisted)]
    [InlineData("""["greylisted"]""", EmailFailureReason.Other)]
    [InlineData("""["send_before_expiration"]""", EmailFailureReason.Other)]
    [InlineData("""["a_flag_from_next_year"]""", EmailFailureReason.Other)]
    [InlineData("""[]""", EmailFailureReason.Other)]
    [InlineData("""null""", EmailFailureReason.Other)]
    [InlineData("\"hard_bounce\"", EmailFailureReason.Other)]
    [InlineData("""[7, {"x":1}, ["spam"]]""", EmailFailureReason.Other)]
    [InlineData("""["greylisted", "hard_bounce", "soft_bounce"]""", EmailFailureReason.HardBounce)]
    [InlineData("""["hard_bounce", "mailbox_not_found"]""", EmailFailureReason.MailboxNotFound)]
    [InlineData("""["spam", "blocklisted", "mailbox_full"]""", EmailFailureReason.MailboxFull)]
    [InlineData("""["blocklisted", "spam"]""", EmailFailureReason.Spam)]
    [InlineData("""["unknown", 3, "blocklisted"]""", EmailFailureReason.Blocklisted)]
    public async Task Attempt_ShouldCloseTheReason_ByThePrecedence(string flags, EmailFailureReason expected) =>
        (await ObservedAsync(Page(Item(flags: flags)))).Failures.ShouldHaveSingleItem().Reason.ShouldBe(expected);

    [Fact]
    public async Task Attempt_ShouldReadAnItemWithoutFlags_AsOther() =>
        (await ObservedAsync(Page(Item().Replace("\"flags\":", "\"renamed\":", StringComparison.Ordinal))))
            .Failures.ShouldHaveSingleItem().Reason.ShouldBe(EmailFailureReason.Other);

    // ---- the recipient: never the reason a read fails ----

    [Theory]
    [InlineData("12345")]
    [InlineData("null")]
    [InlineData("{\"local\":\"zz\"}")]
    [InlineData("[\"zz-marker-4711@example.com\"]")]
    [InlineData("\"no-at-sign\"")]
    [InlineData("\"\"")]
    public async Task Attempt_ShouldShowTheMaskAlone_ForARecipientThatIsNoAddress(string recipient) =>
        (await ObservedAsync(Page(Item(recipient: recipient)))).Failures.ShouldHaveSingleItem()
            .Recipient.Value.ShouldBe(MaskedEmailAddress.Hidden);

    [Fact]
    public async Task Attempt_ShouldShowTheMaskAlone_ForARecipientLongerThanAnAddressCanBe()
    {
        var tooLong = new string('a', MaskedEmailAddress.MaxAddressLength - "@example.com".Length + 1) + "@example.com";
        var justFits = tooLong[1..];

        (await ObservedAsync(Page(Item(recipient: $"\"{tooLong}\"")))).Failures.ShouldHaveSingleItem()
            .Recipient.Value.ShouldBe(MaskedEmailAddress.Hidden);
        (await ObservedAsync(Page(Item(recipient: $"\"{justFits}\"")))).Failures.ShouldHaveSingleItem()
            .Recipient.Value.ShouldBe("a•••@example.com");
    }

    [Fact]
    public async Task Attempt_ShouldShowTheMaskAlone_ForARecipientThatIsNotValidUtf8()
    {
        var body = Encoding.UTF8.GetBytes(Page(Item(recipient: "\"zz\u00FF@example.com\"")));
        var at = Array.IndexOf(body, (byte)0xC3);
        body[at] = 0xFF;
        _transport.RespondWith(() => new ByteArrayContent(body));

        (await AttemptAsync()).Read.ShouldBeOfType<EmailRecentFailuresRead.Observed>().Failures.ShouldHaveSingleItem()
            .Recipient.Value.ShouldBe(MaskedEmailAddress.Hidden);
    }

    [Fact]
    public async Task Attempt_ShouldUnescapeTheRecipient_BeforeMaskingIt() =>
        (await ObservedAsync(Page(Item(recipient: "\"\\u0061dam@example.org\"")))).Failures.ShouldHaveSingleItem()
            .Recipient.Value.ShouldBe("a•••@example.org");

    // ---- the answer checked against the query ----

    [Theory]
    [InlineData("\"sent\"")]
    [InlineData("\"delivered\"")]
    [InlineData("\"FAILED\"")]
    [InlineData("null")]
    [InlineData("3")]
    public async Task Attempt_ShouldBeMalformed_WhenAnItemIsNotFailed(string status) =>
        await ShouldBeMalformedAsync(Page(Item(), Item(createdAt: CreatedAt(90), status: status)));

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenAnItemHasNoStatus() =>
        await ShouldBeMalformedAsync(Page(Item().Replace("\"status\":", "\"renamed\":", StringComparison.Ordinal)));

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-13-40T00:00:00Z")]
    [InlineData("")]
    public async Task Attempt_ShouldBeMalformed_WhenAnItemsTimeIsUnreadable(string createdAt) =>
        await ShouldBeMalformedAsync(Page(Item(createdAt: createdAt)));

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenAnItemHasNoTime() =>
        await ShouldBeMalformedAsync(Page(Item().Replace("\"created_at\":", "\"renamed\":", StringComparison.Ordinal)));

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenAnItemIsOlderThanThePeriod()
    {
        // The 24-hour window starts at 2026-10-18T19:00:00Z, the whole second before now's.
        _transport.Respond(HttpStatusCode.OK, Page(Item(createdAt: "2026-10-18T18:59:59Z")));
        (await AttemptAsync(EmailDeliveryPeriod.Last24Hours)).Read
            .ShouldBe(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Malformed));

        _transport.Respond(HttpStatusCode.OK, Page(Item(createdAt: "2026-10-18T19:00:00Z")));
        (await AttemptAsync(EmailDeliveryPeriod.Last24Hours)).Read.ShouldBeOfType<EmailRecentFailuresRead.Observed>();
    }

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenTheItemsAreNotNewestFirst() =>
        await ShouldBeMalformedAsync(Page(Item(createdAt: CreatedAt(90)), Item(createdAt: CreatedAt(30))));

    [Fact]
    public async Task Attempt_ShouldAcceptItemsCreatedInTheSameInstant() =>
        (await ObservedAsync(Page(Item(createdAt: CreatedAt(30)), Item(createdAt: CreatedAt(30))))).Failures.Count.ShouldBe(2);

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenThePageHoldsMoreThanTen()
    {
        var ten = Enumerable.Range(0, 10).Select(i => Item(createdAt: CreatedAt(i))).ToArray();
        (await ObservedAsync(Page(ten))).Failures.Count.ShouldBe(10);

        await ShouldBeMalformedAsync(Page([.. ten, Item(createdAt: CreatedAt(11))]));
    }

    [Theory]
    [InlineData("""{"total_count":0}""")]
    [InlineData("""{"emails":{}}""")]
    [InlineData("""{"emails":null}""")]
    [InlineData("""{"emails":[],"emails":[]}""")]
    [InlineData("""{"emails":["zz-marker-4711@example.com"]}""")]
    [InlineData("""[]""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Attempt_ShouldBeMalformed_WhenTheBodyHoldsNoListOfItems(string body) =>
        await ShouldBeMalformedAsync(body);

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenTheBodyIsLargerThanTheCap_WithoutALengthHeader()
    {
        var oversized = Page(Item()) + new string(' ', ScalewayRecentFailuresSource.MaxBodyBytes);
        _transport.Respond(HttpStatusCode.OK, oversized, omitContentLength: true);

        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenTheLengthHeaderAlreadySaysTooLarge()
    {
        _transport.Respond(HttpStatusCode.OK, Page(Item()), contentLength: ScalewayRecentFailuresSource.MaxBodyBytes + 1L);

        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public void TheCap_ShouldBeSixtyFourKibibytes() =>
        ScalewayRecentFailuresSource.MaxBodyBytes.ShouldBe(64 * 1024);

    // ---- a refusal and no answer ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, EmailProviderReadReason.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, EmailProviderReadReason.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailProviderReadReason.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, EmailProviderReadReason.ProviderError)]
    [InlineData(HttpStatusCode.InternalServerError, EmailProviderReadReason.ProviderError)]
    public async Task Attempt_ShouldMapEachRefusal_WithoutReadingItsBody(HttpStatusCode status, EmailProviderReadReason reason)
    {
        _transport.RespondWithUnreadableBody(status);

        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(reason));
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, EmailProviderReadReason.Unreachable)]
    [InlineData(HttpRequestError.SecureConnectionError, EmailProviderReadReason.Unreachable)]
    [InlineData(HttpRequestError.ResponseEnded, EmailProviderReadReason.ProviderError)]
    public async Task Attempt_ShouldMapATransportFailure(HttpRequestError error, EmailProviderReadReason reason)
    {
        _transport.Throw(new HttpRequestException(error, "transport said " + MarkerAddress));

        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(reason));
    }

    [Fact]
    public async Task ABodyCutOffMidRead_ShouldBeAProviderError_NotAThrow()
    {
        _transport.RespondWith(() => new StreamContent(
            new FailingStream(new HttpIOException(HttpRequestError.ResponseEnded, "transport said " + MarkerAddress))));

        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.ProviderError));
    }

    [Fact]
    public async Task Attempt_ShouldBeATimeout_WhenTheClientGivesUp()
    {
        _transport.Throw(new TaskCanceledException("client timeout", new TimeoutException()));

        (await AttemptAsync()).Read.ShouldBe(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Timeout));
    }

    [Fact]
    public async Task Attempt_ShouldPropagateTheCallersOwnCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await AttemptAsync(token: cancelled.Token));
    }

    // ---- the leak test ----

    [Fact]
    public async Task OnlyTheMaskedRecipient_ShouldLeaveTheAdapter_AndNoAddressReachesAnyLog()
    {
        var results = new List<EmailRecentFailuresRead>();
        foreach (var respond in new Action[]
                 {
                     () => _transport.Respond(HttpStatusCode.OK, Page(Item(), Item(createdAt: CreatedAt(90)))),
                     () => _transport.Respond(HttpStatusCode.OK, Page(Item(status: "\"sent\""))),
                     () => _transport.Respond(HttpStatusCode.OK, Page(Item(createdAt: "2026-01-01T00:00:00Z"))),
                     () => _transport.Respond(HttpStatusCode.OK, "{\"emails\":[{\"mail_rcpt\":\"" + MarkerAddress + "\""),
                     () => _transport.Respond(HttpStatusCode.Forbidden, "{\"echo\":\"" + MarkerAddress + "\"}"),
                     () => _transport.Throw(new HttpRequestException(HttpRequestError.ConnectionError, "transport said " + MarkerAddress)),
                 })
        {
            respond();
            results.Add((await AttemptAsync()).Read);
        }

        var observed = results[0].ShouldBeOfType<EmailRecentFailuresRead.Observed>();
        observed.Failures.ShouldAllBe(failure => failure.Recipient.Value == MaskedMarker);
        results.Skip(1).ShouldAllBe(read => read is EmailRecentFailuresRead.Failed);

        foreach (var text in results.Select(read => read.ToString()).Concat(observed.Failures.Select(failure => failure.ToString())))
        {
            text.ShouldNotContain(MarkerLocalPart);
        }

        foreach (var forbidden in new[] { MarkerLocalPart, MaskedMarker, "@example.com", ProjectId, SendKey, ReadKey })
        {
            _logs.Lines.ShouldAllBe(line => !line.Contains(forbidden, StringComparison.Ordinal));
        }

        _logs.Records.ShouldNotContain(r => r.Category.StartsWith("System.Net.Http", StringComparison.Ordinal));
        _logs.Records.Where(r => r.EventId == 3020).ShouldAllBe(r => r.Message.Contains("emails", StringComparison.Ordinal));
    }
}
