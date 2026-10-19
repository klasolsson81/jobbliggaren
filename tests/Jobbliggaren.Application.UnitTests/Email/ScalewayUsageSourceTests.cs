using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Admin.Email;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The allowance read against Scaleway's <c>project-consumption</c> (#1981, ADR 0178), through the real composition
/// (<see cref="EmailUsageSourceCollectionExtensions.AddEmailUsageSource"/> and the real named client) with only the
/// transport replaced. Every log line from every category is captured, so "never logged" is measured on the whole
/// process output and not on the adapter's own logger alone.
/// </summary>
public sealed class ScalewayUsageSourceTests : IDisposable
{
    private const string Region = "fr-par";
    private const string ProjectId = "11111111-2222-3333-4444-555555555555";
    private const string SecretKey = "SCW-SECRET-KEY-4711"; // gitleaks:allow
    private const string ReadKey = "SCW-READ-KEY-4712"; // gitleaks:allow
    private const string BodyMarker = "BODY-MARKER-4711";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 19, 30, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ScriptedTransport _transport = new();
    private readonly CapturingLogSink _logs = new();
    private readonly ServiceProvider _provider;

    public ScalewayUsageSourceTests() => _provider = BuildProvider();

    private ServiceProvider BuildProvider(TimeSpan? readTimeout = null, string? readKey = ReadKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Scaleway",
                ["Email:Scaleway:Region"] = Region,
                ["Email:Scaleway:SecretKey"] = SecretKey,
                ["Email:Scaleway:ProjectId"] = ProjectId,
                ["Email:ScalewayRead:SecretKey"] = readKey,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider(Now));
        services.AddOptions<ScalewayEmailOptions>().Bind(configuration.GetSection(ScalewayEmailOptions.SectionName));
        services.AddEmailUsageSource(configuration);
        var client = services.AddHttpClient(ScalewayClientRegistration.UsageHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _transport);
        if (readTimeout is { } timeout)
        {
            client.ConfigureHttpClient(c => c.Timeout = timeout);
        }

        return services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private ValueTask<ProviderAttempt<EmailProviderUsageRead>> AttemptAsync(CancellationToken? token = null) =>
        _provider.GetRequiredService<IProviderUsageAttemptSource>().AttemptUsageAsync(token ?? Ct);

    private ValueTask<ProviderAttempt<EmailProviderPlanRead>> PlanAsync(CancellationToken? token = null) =>
        _provider.GetRequiredService<IProviderUsageAttemptSource>().AttemptPlanAsync(token ?? Ct);

    private static string SubscriptionsBody(params string[] offerNames) =>
        "{\"offer_subscriptions\":["
        + string.Join(",", offerNames.Select(name =>
            $$"""{"organization_id":"{{ProjectId}}","project_id":"{{ProjectId}}","offer_name":"{{name}}","included_monthly_emails":300,"note":"{{BodyMarker}}"}"""))
        + "],\"total_count\":" + offerNames.Length + "}";

    private static string ConsumptionBody(string monthly = "184", string webhooks = "0") =>
        $$"""
        {"project_id":"{{ProjectId}}","domains_count":1,"dedicated_ips_count":0,
         "monthly_emails_count":{{monthly}},"webhooks_count":{{webhooks}},"custom_blocklists_count":0,
         "note":"{{BodyMarker}}"}
        """;

    // ---- the request ----

    [Fact]
    public async Task Attempt_ShouldSendOneAuthenticatedGet_ToTheProjectsConsumption()
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody());

        await AttemptAsync();

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.ShouldBe(new Uri(
            $"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/project-consumption?project_id={ProjectId}"));
        request.AuthToken.ShouldBe(ReadKey);
    }

    [Fact]
    public async Task EveryRead_SendsTheReadKey_NeverTheSendKey()
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody());
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.OK, SubscriptionsBody("essential"));
        await PlanAsync();
        _transport.Respond(HttpStatusCode.Unauthorized, "{}");
        await AttemptAsync();
        await PlanAsync();

        _transport.Requests.Count.ShouldBe(4);
        _transport.Requests.ShouldAllBe(request => request.AuthToken == ReadKey);
    }

    [Fact]
    public async Task WithoutAReadKey_NothingIsRead_AndTheSendKeyIsNeverUsedInItsPlace()
    {
        using var provider = BuildProvider(readKey: null);
        var source = provider.GetRequiredService<IEmailProviderUsageSource>();

        (await source.ReadUsageAsync(Ct)).ShouldBe(new EmailProviderUsageRead.ReadKeyMissing());
        (await source.ReadPlanAsync(Ct)).ShouldBe(new EmailProviderPlanRead.ReadKeyMissing());

        _transport.Requests.ShouldBeEmpty();
        provider.GetService<IProviderUsageAttemptSource>().ShouldBeNull();
    }

    [Fact]
    public async Task WithoutAReadKey_StartupWritesOneInformationLine_NamingTheKey_AndNoValue()
    {
        using var provider = BuildProvider(readKey: null);

        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(Ct);
        }

        var line = _logs.Records.Where(r => r.EventId == 3021).ShouldHaveSingleItem();
        line.Level.ShouldBe(LogLevel.Information);
        line.Message.ShouldContain("Email:ScalewayRead:SecretKey");
        foreach (var forbidden in new[] { SecretKey, ProjectId })
        {
            _logs.Lines.ShouldAllBe(text => !text.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheReadClient_ShouldHaveItsOwnFiveSecondTimeout_AndTheRegionInItsBaseAddress()
    {
        var client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient(ScalewayClientRegistration.UsageHttpClientName);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
        client.BaseAddress.ShouldBe(new Uri($"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/"));
    }

    [Fact]
    public async Task Attempt_ShouldSendExactlyOnce_WhenTheProviderAnswers503()
    {
        _transport.Respond(HttpStatusCode.ServiceUnavailable, "{}");

        await AttemptAsync();

        _transport.Requests.Count.ShouldBe(1);
    }

    // ---- an answer ----

    [Fact]
    public async Task Attempt_ShouldReadTheCountAndTheWebhooks_StampedWithTheClock()
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody(monthly: "184", webhooks: "2"));

        var observed = (await AttemptAsync()).Read.ShouldBeOfType<EmailProviderUsageRead.Observed>();

        observed.MonthlyEmailsCount.ShouldBe(184);
        observed.WebhooksCount.ShouldBe(2);
        observed.ObservedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Attempt_ShouldReadTheLargestCountTheWireCarries() =>
        await ShouldObserveAsync(ConsumptionBody(monthly: uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), uint.MaxValue);

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("-3")]
    [InlineData("null")]
    [InlineData("{\"n\":1}")]
    public async Task Attempt_ShouldLeaveOnlyTheWebhookCountUnknown_WhenItCannotBeRead(string webhooks)
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody(webhooks: webhooks));

        var observed = (await AttemptAsync()).Read.ShouldBeOfType<EmailProviderUsageRead.Observed>();

        observed.MonthlyEmailsCount.ShouldBe(184);
        observed.WebhooksCount.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"project_id":"x"}""")]
    [InlineData("""{"monthly_emails_count":-1}""")]
    [InlineData("""{"monthly_emails_count":4294967296}""")]
    [InlineData("""{"monthly_emails_count":1.5}""")]
    [InlineData("""{"monthly_emails_count":"184"}""")]
    [InlineData("""{"monthly_emails_count":null}""")]
    [InlineData("""{"nested":{"monthly_emails_count":184}}""")]
    [InlineData("""[{"monthly_emails_count":184}]""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Attempt_ShouldBeMalformed_NeverZero_WhenTheCountIsNotThere(string body)
    {
        _transport.Respond(HttpStatusCode.OK, body);

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenTheBodyIsLargerThanTheCap_WithoutALengthHeader()
    {
        var oversized = "{\"monthly_emails_count\":184}" + new string(' ', ScalewayUsageSource.MaxBodyBytes);
        _transport.Respond(HttpStatusCode.OK, oversized, omitContentLength: true);

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public async Task Attempt_ShouldBeMalformed_WhenTheLengthHeaderAlreadySaysTooLarge()
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody(), contentLength: ScalewayUsageSource.MaxBodyBytes + 1L);

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(EmailProviderReadReason.Malformed));
    }

    // ---- the plan ----

    [Fact]
    public async Task Plan_ShouldSendOneAuthenticatedGet_ToTheProjectsOfferSubscriptions()
    {
        _transport.Respond(HttpStatusCode.OK, SubscriptionsBody("essential"));

        await PlanAsync();

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.ShouldBe(new Uri(
            $"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/offer-subscriptions?project_id={ProjectId}"));
        request.AuthToken.ShouldBe(ReadKey);
    }

    [Theory]
    [InlineData("essential", EmailPlan.Essential)]
    [InlineData("scale", EmailPlan.Scale)]
    [InlineData("unknown_name", EmailPlan.Unknown)]
    [InlineData("Essential", EmailPlan.Unknown)]
    [InlineData("enterprise", EmailPlan.Unknown)]
    public async Task Plan_ShouldMapTheOneSubscriptionsName_OntoAClosedPlan(string offerName, EmailPlan expected)
    {
        _transport.Respond(HttpStatusCode.OK, SubscriptionsBody(offerName));

        var observed = (await PlanAsync()).Read.ShouldBeOfType<EmailProviderPlanRead.Observed>();

        observed.Plan.ShouldBe(expected);
        observed.ObservedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("essential,scale")]
    [InlineData("essential,essential")]
    public async Task Plan_ShouldBeUnknown_UnlessThereIsExactlyOneSubscription(string offerNames)
    {
        _transport.Respond(HttpStatusCode.OK, SubscriptionsBody(offerNames.Split(',', StringSplitOptions.RemoveEmptyEntries)));

        (await PlanAsync()).Read.ShouldBeOfType<EmailProviderPlanRead.Observed>().Plan.ShouldBe(EmailPlan.Unknown);
    }

    [Fact]
    public async Task Plan_ShouldBeUnknown_WhenTheOneSubscriptionHasNoReadableName()
    {
        _transport.Respond(HttpStatusCode.OK, """{"offer_subscriptions":[{"offer_name":7}]}""");

        (await PlanAsync()).Read.ShouldBeOfType<EmailProviderPlanRead.Observed>().Plan.ShouldBe(EmailPlan.Unknown);
    }

    [Theory]
    [InlineData("""{"total_count":1}""")]
    [InlineData("""{"offer_subscriptions":{"offer_name":"essential"}}""")]
    [InlineData("""[{"offer_name":"essential"}]""")]
    [InlineData("not json")]
    public async Task Plan_ShouldBeMalformed_WhenTheBodyHoldsNoSubscriptionArray(string body)
    {
        _transport.Respond(HttpStatusCode.OK, body);

        (await PlanAsync()).Read.ShouldBe(new EmailProviderPlanRead.Failed(EmailProviderReadReason.Malformed));
    }

    [Fact]
    public async Task Plan_ShouldBeUnknown_NotAThrow_WhenTheNameIsNotValidUtf8()
    {
        byte[] body = [.. """{"offer_subscriptions":[{"offer_name":"essential"""u8, 0xC3, 0x28, .. "\"}]}"u8];
        _transport.RespondWith(() => new ByteArrayContent(body));

        (await PlanAsync()).Read.ShouldBeOfType<EmailProviderPlanRead.Observed>().Plan.ShouldBe(EmailPlan.Unknown);
    }

    [Fact]
    public async Task Plan_ShouldMapARefusal_WithoutReadingItsBody()
    {
        _transport.RespondWithUnreadableBody(HttpStatusCode.Forbidden);

        (await PlanAsync()).Read.ShouldBe(new EmailProviderPlanRead.Failed(EmailProviderReadReason.Forbidden));
    }

    // ---- a refusal ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, EmailProviderReadReason.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, EmailProviderReadReason.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailProviderReadReason.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, EmailProviderReadReason.ProviderError)]
    [InlineData(HttpStatusCode.InternalServerError, EmailProviderReadReason.ProviderError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailProviderReadReason.ProviderError)]
    public async Task Attempt_ShouldMapEachRefusal_WithoutReadingItsBody(HttpStatusCode status, EmailProviderReadReason reason)
    {
        _transport.RespondWithUnreadableBody(status);

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(reason));
    }

    [Fact]
    public async Task Attempt_ShouldCarryTheProvidersRetryAfter_InSeconds()
    {
        _transport.Respond(HttpStatusCode.TooManyRequests, "{}", retryAfter: new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)));

        (await AttemptAsync()).RetryAfter.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task Attempt_ShouldCarryTheProvidersRetryAfter_AsAnHttpDate()
    {
        _transport.Respond(HttpStatusCode.ServiceUnavailable, "{}", retryAfter: new RetryConditionHeaderValue(Now.AddMinutes(5)));

        (await AttemptAsync()).RetryAfter.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Attempt_ShouldCarryNoRetryAfter_WhenItIsAbsentOrPast()
    {
        _transport.Respond(HttpStatusCode.TooManyRequests, "{}");
        (await AttemptAsync()).RetryAfter.ShouldBeNull();

        _transport.Respond(HttpStatusCode.TooManyRequests, "{}", retryAfter: new RetryConditionHeaderValue(Now.AddMinutes(-1)));
        (await AttemptAsync()).RetryAfter.ShouldBeNull();
    }

    // ---- no answer ----

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, EmailProviderReadReason.Unreachable)]
    [InlineData(HttpRequestError.ConnectionError, EmailProviderReadReason.Unreachable)]
    [InlineData(HttpRequestError.SecureConnectionError, EmailProviderReadReason.Unreachable)]
    [InlineData(HttpRequestError.ResponseEnded, EmailProviderReadReason.ProviderError)]
    public async Task Attempt_ShouldMapATransportFailure(HttpRequestError error, EmailProviderReadReason reason)
    {
        _transport.Throw(new HttpRequestException(error, "transport said " + BodyMarker));

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(reason));
    }

    [Fact]
    public async Task Attempt_ShouldBeATimeout_WhenTheClientGivesUpAndTheCallerDidNot()
    {
        _transport.Throw(new TaskCanceledException("client timeout", new TimeoutException()));

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(EmailProviderReadReason.Timeout));
    }

    [Fact]
    public async Task AStalledBody_ShouldEndAsATimeout_EvenOnTheSharedReadsUncancellableToken()
    {
        _transport.RespondWith(() => new StreamContent(new StallingStream()));
        using var provider = BuildProvider(readTimeout: TimeSpan.FromMilliseconds(200));

        var read = await provider.GetRequiredService<IEmailProviderUsageSource>()
            .ReadUsageAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        read.ShouldBe(new EmailProviderUsageRead.Failed(EmailProviderReadReason.Timeout));
    }

    [Fact]
    public async Task ABodyCutOffMidRead_ShouldBeAProviderError_NotAThrow()
    {
        _transport.RespondWith(() => new StreamContent(
            new FailingStream(new HttpIOException(HttpRequestError.ResponseEnded, "transport said " + BodyMarker))));

        (await AttemptAsync()).Read.ShouldBe(new EmailProviderUsageRead.Failed(EmailProviderReadReason.ProviderError));
    }

    [Fact]
    public async Task ABodyResetMidRead_ShouldBeAProviderError_NotAThrow()
    {
        _transport.RespondWith(() => new StreamContent(new FailingStream(new IOException("transport said " + BodyMarker))));

        (await PlanAsync()).Read.ShouldBe(new EmailProviderPlanRead.Failed(EmailProviderReadReason.ProviderError));
    }

    [Fact]
    public async Task Attempt_ShouldPropagateTheCallersOwnCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await AttemptAsync(cancelled.Token));
    }

    // ---- what is never logged ----

    [Fact]
    public async Task NoCategory_ShouldEverLogTheProjectId_TheKey_TheBody_OrTheTransportsMessage()
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody());
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.Forbidden, "{\"echo\":\"" + BodyMarker + "\"}");
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.OK, "{\"echo\":\"" + BodyMarker + "\"}");
        await AttemptAsync();
        _transport.Throw(new HttpRequestException(HttpRequestError.ConnectionError, "transport said " + BodyMarker));
        await AttemptAsync();
        _transport.RespondWith(() => new StreamContent(
            new FailingStream(new HttpIOException(HttpRequestError.ResponseEnded, "transport said " + BodyMarker))));
        await AttemptAsync();
        _transport.Respond(HttpStatusCode.OK, SubscriptionsBody("essential"));
        await PlanAsync();
        _transport.Respond(HttpStatusCode.Forbidden, "{\"echo\":\"" + BodyMarker + "\"}");
        await PlanAsync();

        foreach (var forbidden in new[] { ProjectId, SecretKey, ReadKey, BodyMarker, "project_id", "organization_id" })
        {
            _logs.Lines.ShouldAllBe(line => !line.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AFailure_ShouldBeLoggedWithItsReasonAndStatus_AndNothingElse()
    {
        _transport.Respond(HttpStatusCode.Forbidden, "{}");

        await AttemptAsync();

        var line = _logs.Records.Where(r => r.EventId == 3020).ShouldHaveSingleItem();
        line.Level.ShouldBe(LogLevel.Warning);
        line.Message.ShouldContain("Forbidden");
        line.Message.ShouldContain("403");
    }

    [Fact]
    public async Task TheReadClient_ShouldWriteNoHttpClientLogsAtAll()
    {
        _transport.Respond(HttpStatusCode.OK, ConsumptionBody());

        await AttemptAsync();

        _logs.Records.ShouldNotContain(r => r.Category.StartsWith("System.Net.Http", StringComparison.Ordinal));
    }

    private async Task ShouldObserveAsync(string body, long expected)
    {
        _transport.Respond(HttpStatusCode.OK, body);
        (await AttemptAsync()).Read.ShouldBeOfType<EmailProviderUsageRead.Observed>().MonthlyEmailsCount.ShouldBe(expected);
    }
}
