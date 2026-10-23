using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email.Counting;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Time;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1981 PR 3 (ADR 0181) — our own counts per mail kind, end to end through the real pipeline: who may call the read,
/// that every answer including a refusal and a 400 is private and uncacheable, that the period is one of three tokens,
/// and exactly what the browser receives. The counts are written by the production tally under a host clock set a few
/// days ahead (AGENTS.md §5 Tests), close enough for the session to stay valid; no other test writes the table in this
/// database, so each test starts and ends with it empty. The recording start is the one the migration wrote.
/// </summary>
[Collection("Api")]
public sealed class AdminEmailKindsTests(ApiFactory factory) : IAsyncLifetime
{
    private const string KindsPath = "/api/v1/admin/email/kinds";
    private static readonly SwedishCalendar Calendar = new();
    private static readonly EmailDeliveryStatisticsRead Sentinel =
        new EmailDeliveryStatisticsRead.Observed(149, 140, 9, DateTimeOffset.UnixEpoch);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await EmptyTheTableAsync();

    public async ValueTask DisposeAsync() => await EmptyTheTableAsync();

    /// <summary>10:00 UTC a few days from today: midday in Stockholm, so the Swedish date is never in doubt.</summary>
    private static DateTimeOffset DaysAhead(int days) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days).AddHours(10);

    // ---- who may call it ----

    [Fact]
    public async Task Kinds_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(KindsPath + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Kinds_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(KindsPath + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("status");
    }

    [Fact]
    public async Task Kinds_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(KindsPath + "?period=d7", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(KindsPath + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectQuery_ShouldRefuseAnUnprivilegedCaller_WhenTheHttpEndpointIsBypassed(bool authenticated)
    {
        var client = authenticated ? await UserAsync(factory, NewToken(), Ct) : null;
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = null;
        try
        {
            var query = new GetEmailSendCountsQuery(EmailDeliveryPeriod.Last7Days);
            if (authenticated)
            {
                var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = Ct };
                context.Request.Headers.Authorization = $"Bearer {client!.DefaultRequestHeaders.Authorization!.Parameter}";
                accessor.HttpContext = context;
                var auth = await context.AuthenticateAsync("Bearer");
                auth.Succeeded.ShouldBeTrue();
                context.User = auth.Principal.ShouldNotBeNull();
                await Should.ThrowAsync<ForbiddenException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, Ct));
            }
            else
            {
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, Ct));
            }
        }
        finally
        {
            accessor.HttpContext = previous;
            client?.Dispose();
        }
    }

    [Fact]
    public void Kinds_ShouldCarryTheAdminPolicyAndTheAdminReadBucket()
    {
        _ = factory.CreateClient();
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == KindsPath
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList().ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty();
        rateLimiting[^1].PolicyName.ShouldBe(RateLimitingExtensions.AdminReadPolicy);
    }

    // ---- the period ----

    [Theory]
    [InlineData("")]
    [InlineData("?period=")]
    [InlineData("?period=H24")]
    [InlineData("?period=42")]
    [InlineData("?period=d30")]
    [InlineData("?period=Last24Hours")]
    [InlineData("?period=h24&period=d7")]
    public async Task Kinds_ShouldAnswerAPrivate400_ForAnythingButTheThreeTokens(string query)
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);

        var response = await admin.GetAsync(KindsPath + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Period");
    }

    // ---- what the answer says ----

    [Fact]
    public async Task Kinds_ShouldAnswerEveryKindInOrder_WithItsAcceptedFailedAndLatestFailure()
    {
        var now = DaysAhead(3);
        using var host = HostAt(now);
        var admin = await AdminClientAsync(host);
        await WarmAsync(admin);
        var tally = TallyOf(host);
        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);
        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);
        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Timeout);
        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Refused);
        await tally.RecordAsync(EmailKind.FeedbackReply, EmailSendOutcome.Accepted);

        var response = await admin.GetAsync(KindsPath + "?period=h24", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        var body = await ReadAsync(response);
        body.GetProperty("period").GetString().ShouldBe("h24");
        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("from").GetString().ShouldBe(Iso(Calendar.DayOf(now.AddHours(-24))), "24 hours back is yesterday");
        body.GetProperty("coverage").GetString().ShouldBe("Complete");
        body.GetProperty("coveredFrom").ValueKind.ShouldBe(JsonValueKind.Null);
        var kinds = body.GetProperty("kinds").EnumerateArray().ToList();
        kinds.Select(k => k.GetProperty("kind").GetString()).ShouldBe(
        [
            "login-challenge", "account-email-change-requested-notification", "email-changed-notification",
            "match-notification", "followed-company-notification", "feedback-received-notification", "feedback-reply",
        ]);
        kinds[0].GetProperty("accepted").GetInt64().ShouldBe(2);
        kinds[0].GetProperty("failed").GetInt64().ShouldBe(2);
        var latest = kinds[0].GetProperty("latestFailure");
        latest.GetProperty("day").GetString().ShouldBe(Iso(Calendar.DayOf(now)));
        latest.GetProperty("outcomes").EnumerateArray().Select(o => o.GetString()).ShouldBe(["Refused", "Timeout"]);
        kinds[6].GetProperty("accepted").GetInt64().ShouldBe(1);
        kinds[6].GetProperty("failed").GetInt64().ShouldBe(0);
        kinds[6].GetProperty("latestFailure").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Kinds_ShouldGiveTheBrowserExactlyTheKeysItWasPromised_AndNothingElse()
    {
        using var host = HostAt(DaysAhead(3));
        var admin = await AdminClientAsync(host);
        await WarmAsync(admin);
        await TallyOf(host).RecordAsync(EmailKind.MatchNotification, EmailSendOutcome.ProviderError);

        var raw = await (await admin.GetAsync(KindsPath + "?period=d3", Ct)).Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(raw);

        Keys(document.RootElement).ShouldBe(["coverage", "coveredFrom", "from", "kinds", "period", "status"]);
        var match = document.RootElement.GetProperty("kinds").EnumerateArray().ElementAt(3);
        Keys(match).ShouldBe(["accepted", "failed", "kind", "latestFailure"]);
        Keys(match.GetProperty("latestFailure")).ShouldBe(["day", "outcomes"]);
    }

    [Fact]
    public async Task Kinds_ShouldAnswerNotObserved_WhereNoMailIsHandedToAProvider()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var notConfigured = factory.EmailSendCounts.AnswerNotConfigured();

        var body = await ReadAsync(await admin.GetAsync(KindsPath + "?period=d7", Ct));

        body.GetProperty("status").GetString().ShouldBe("NotObserved");
        foreach (var absent in new[] { "from", "coverage", "coveredFrom", "kinds" })
        {
            body.GetProperty(absent).ValueKind.ShouldBe(JsonValueKind.Null, absent);
        }
    }

    [Fact]
    public async Task Kinds_ShouldAnswerNotObservable_WhenOurZeroMeetsTheProvidersSends()
    {
        var now = DaysAhead(4);
        using var host = HostAt(now);
        var admin = await AdminClientAsync(host);
        using var scripted = factory.EmailProviderReads.ScriptStatistics(
            new EmailDeliveryStatisticsRead.Observed(5, 4, 1, now), Sentinel);

        var body = await ReadAsync(await admin.GetAsync(KindsPath + "?period=d7", Ct));

        body.GetProperty("status").GetString().ShouldBe("NotObservable");
        body.GetProperty("from").GetString().ShouldBe(Iso(Calendar.DayOf(now.AddHours(-168))));
        foreach (var absent in new[] { "coverage", "coveredFrom", "kinds" })
        {
            body.GetProperty(absent).ValueKind.ShouldBe(JsonValueKind.Null, absent);
        }
    }

    [Theory]
    [InlineData("sent none")]
    [InlineData("failed")]
    public async Task Kinds_ShouldShowOurZeros_WhenTheProviderIsNoWitnessAgainstThem(string provider)
    {
        using var host = HostAt(DaysAhead(5));
        var admin = await AdminClientAsync(host);
        using var scripted = provider switch
        {
            "sent none" => factory.EmailProviderReads.ScriptStatistics(
                new EmailDeliveryStatisticsRead.Observed(0, 0, 0, DateTimeOffset.UnixEpoch), Sentinel),
            "failed" => factory.EmailProviderReads.ScriptStatistics(
                new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Timeout), Sentinel),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Not a witness."),
        };

        var body = await ReadAsync(await admin.GetAsync(KindsPath + "?period=h24", Ct));

        body.GetProperty("status").GetString().ShouldBe("Observed", provider);
        body.GetProperty("kinds").EnumerateArray()
            .ShouldAllBe(k => k.GetProperty("accepted").GetInt64() == 0 && k.GetProperty("failed").GetInt64() == 0);
    }

    [Fact]
    public async Task Kinds_ShouldSayPartial_FromTheRecordingStart_WhenThePeriodReachesFurtherBack()
    {
        var started = await RecordingStartAsync();
        using var host = HostAt(started.AddHours(1));
        var admin = await AdminClientAsync(host);
        await WarmAsync(admin);
        await TallyOf(host).RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);

        var body = await ReadAsync(await admin.GetAsync(KindsPath + "?period=d7", Ct));

        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("coverage").GetString().ShouldBe("Partial");
        body.GetProperty("coveredFrom").GetDateTimeOffset().ShouldBe(started);
    }

    // ---- helpers ----

    private WebApplicationFactory<Program> HostAt(DateTimeOffset now) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(new MutableFakeDateTimeProvider { UtcNow = now });
        }));

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> host)
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);
        return client;
    }

    /// <summary>One read first, so the host's database model and pool are built before the tally's budget is measured.</summary>
    private static async Task WarmAsync(HttpClient admin) =>
        (await admin.GetAsync(KindsPath + "?period=d7", Ct)).EnsureSuccessStatusCode();

    private static SqlEmailSendTally TallyOf(WebApplicationFactory<Program> host) =>
        new(host.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SqlEmailSendTally>.Instance);

    private async Task<DateTimeOffset> RecordingStartAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT started_at FROM email_send_counting", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    private async Task EmptyTheTableAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM email_send_daily_counts", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string DatabaseConnectionString()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test host's AppDbContext has no connection string.");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    private static string[] Keys(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static void ShouldBePrivate(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }
}
