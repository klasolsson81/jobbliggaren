using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
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
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the stored DMARC aggregate reports, end to end through the real pipeline: who may call the
/// read, that every answer including a refusal is private and uncacheable, and exactly what the browser receives. The
/// stored rows are written by the production store (<see cref="SqlDmarcReportStore"/>) in the order the ingestion job
/// writes them, with reports made by <see cref="DmarcAggregateReport.Accept"/> (AGENTS.md §5 Tests); the mailbox itself
/// is the Worker's and never reached here. No other test writes the DMARC tables in this database, so each test starts
/// and ends with them empty. Every address is from the documentation ranges (RFC 5737, RFC 3849).
/// </summary>
[Collection("Api")]
public sealed class AdminEmailDmarcTests(ApiFactory factory) : IAsyncLifetime
{
    private const string DmarcPath = "/api/v1/admin/email/dmarc";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private const long Epoch = 4_000_000_007;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] BodyKeys =
    [
        "conflictingReports", "coveredFrom", "coveredTo", "failingMessages", "from", "ingestion", "mailbox", "messages",
        "reporters", "reports", "sourceCount", "sources", "status", "to",
    ];

    public async ValueTask InitializeAsync() => await EmptyTheTablesAsync();

    public async ValueTask DisposeAsync() => await EmptyTheTablesAsync();

    /// <summary>10:00 UTC a few days from today, close enough for the session to stay valid.</summary>
    private static DateTimeOffset DaysAhead(int days) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days).AddHours(10);

    // ---- who may call it ----

    [Fact]
    public async Task Dmarc_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(DmarcPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Dmarc_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(DmarcPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("status");
    }

    [Fact]
    public async Task Dmarc_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(DmarcPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(DmarcPath, Ct);

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
            var query = new GetDmarcReportsQuery();
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
    public void Dmarc_ShouldCarryTheAdminPolicyAndTheAdminReadBucket()
    {
        _ = factory.CreateClient();
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == DmarcPath
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList().ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty();
        rateLimiting[^1].PolicyName.ShouldBe(RateLimitingExtensions.AdminReadPolicy);
    }

    // ---- what the answer says ----

    [Fact]
    public async Task Dmarc_ShouldAnswerNotObservable_WithEveryFigureNull_WhenNoRunHasWrittenItsState()
    {
        var now = DaysAhead(2);
        using var host = HostAt(now);
        var admin = await AdminClientAsync(host);

        var response = await admin.GetAsync(DmarcPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
            .ShouldBe(now.ToString("O", CultureInfo.InvariantCulture));
        var body = await ReadAsync(response);
        Keys(body).ShouldBe(BodyKeys);
        body.GetProperty("status").GetString().ShouldBe("NotObservable");
        foreach (var absent in BodyKeys.Where(key => key != "status"))
        {
            body.GetProperty(absent).ValueKind.ShouldBe(JsonValueKind.Null, absent);
        }
    }

    [Fact]
    public async Task Dmarc_ShouldAnswerObserved_FromWhatTheIngestionStored_WithTheSourcesInTheirOrder()
    {
        var now = DaysAhead(2);
        var seeded = await IngestAsync(now);
        using var host = HostAt(now);
        var admin = await AdminClientAsync(host);

        var response = await admin.GetAsync(DmarcPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
            .ShouldBe(now.ToString("O", CultureInfo.InvariantCulture));
        var body = await ReadAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("from").GetDateTimeOffset().ShouldBe(now.AddDays(-7));
        body.GetProperty("to").GetDateTimeOffset().ShouldBe(now);
        body.GetProperty("coveredFrom").GetDateTimeOffset().ShouldBe(seeded.CoveredFrom);
        body.GetProperty("coveredTo").GetDateTimeOffset().ShouldBe(seeded.CoveredTo, "an inclusive end is shown exclusive");
        body.GetProperty("reports").GetInt64().ShouldBe(2);
        body.GetProperty("reporters").GetInt64().ShouldBe(2);
        body.GetProperty("sourceCount").GetInt64().ShouldBe(4);
        body.GetProperty("messages").GetInt64().ShouldBe(51);
        body.GetProperty("failingMessages").GetInt64().ShouldBe(4);
        body.GetProperty("conflictingReports").GetInt64().ShouldBe(0);

        var ingestion = body.GetProperty("ingestion");
        ingestion.GetProperty("lastAttemptAt").GetDateTimeOffset().ShouldBe(seeded.RunStart);
        ingestion.GetProperty("lastSuccessAt").GetDateTimeOffset().ShouldBe(seeded.RunStart);
        ingestion.GetProperty("latestFailure").ValueKind.ShouldBe(JsonValueKind.Null);
        ingestion.GetProperty("stale").GetBoolean().ShouldBeFalse();

        var mailbox = body.GetProperty("mailbox");
        mailbox.GetProperty("skipped").GetInt64().ShouldBe(1);
        mailbox.GetProperty("rejected").GetInt64().ShouldBe(1);
        mailbox.GetProperty("volumeCapped").GetInt64().ShouldBe(0);
        mailbox.GetProperty("abandoned").GetInt64().ShouldBe(0);
        mailbox.GetProperty("retryPending").GetInt64().ShouldBe(1);

        body.GetProperty("sources").EnumerateArray()
            .Select(s => string.Join('|',
                s.GetProperty("address").GetString(),
                s.GetProperty("headerFrom").GetString(),
                s.GetProperty("messages").GetInt64(),
                s.GetProperty("dkimAligned").GetInt64(),
                s.GetProperty("spfAligned").GetInt64(),
                s.GetProperty("failingDmarc").GetInt64(),
                s.GetProperty("flagged").GetBoolean()))
            .ShouldBe(
            [
                "198.51.100.7|jobbliggaren.se|3|0|0|3|True",
                "203.0.113.9|mail.jobbliggaren.se|1|0|0|1|True",
                "192.0.2.10|jobbliggaren.se|42|42|42|0|False",
                "2001:db8::1|jobbliggaren.se|5|5|0|0|False",
            ], "flagged first, then failing, messages and address; one row per source and domain over both reports");
    }

    [Fact]
    public async Task Dmarc_ShouldGiveTheBrowserExactlyTheKeysItWasPromised_AndEachAddressOnlyInCanonicalForm()
    {
        var now = DaysAhead(2);
        await IngestAsync(now);
        using var host = HostAt(now);
        var admin = await AdminClientAsync(host);

        var raw = await (await admin.GetAsync(DmarcPath, Ct)).Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        Keys(root).ShouldBe(BodyKeys);
        Keys(root.GetProperty("ingestion")).ShouldBe(["lastAttemptAt", "lastSuccessAt", "latestFailure", "stale"]);
        Keys(root.GetProperty("mailbox")).ShouldBe(["abandoned", "rejected", "retryPending", "skipped", "volumeCapped"]);
        var sources = root.GetProperty("sources").EnumerateArray().ToList();
        sources.Count.ShouldBe(4);
        foreach (var source in sources)
        {
            Keys(source).ShouldBe(
                ["address", "dkimAligned", "failingDmarc", "flagged", "headerFrom", "messages", "spfAligned"]);
            source.GetProperty("address").ValueKind.ShouldBe(JsonValueKind.String, "an address is a string, never an object");
        }

        // The reports wrote one address IPv4-mapped and one in full upper-case hex; the browser sees neither form.
        raw.ShouldNotContain("ffff", Case.Insensitive);
        raw.ShouldNotContain("0db8", Case.Insensitive);
        foreach (var address in new[] { "198.51.100.7", "203.0.113.9", "192.0.2.10", "2001:db8::1" })
        {
            Occurrences(raw, address).ShouldBe(1, address);
        }

        foreach (var absent in new[] { "reporter.example", "second.example", "report-a", "report-b" })
        {
            raw.ShouldNotContain(absent, Case.Insensitive, "a reporter's own name and report id never reach the browser");
        }
    }

    // ---- the ingestion, as the job writes it ----

    private sealed record Seeded(DateTimeOffset RunStart, DateTimeOffset CoveredFrom, DateTimeOffset CoveredTo);

    /// <summary>
    /// One successful run an hour before <paramref name="now"/>, through the production store in the job's order: the
    /// state row, the epoch, then each message claimed before anything about it is stored. Two reports are stored, one
    /// message is skipped as forensic, one is refused by acceptance, and one is handed back for a retry.
    /// </summary>
    private async Task<Seeded> IngestAsync(DateTimeOffset now)
    {
        var runStart = now.AddHours(-1);
        var receivedAt = DmarcRetention.ReceivedAtOf(now.AddHours(-2), runStart);
        var dayBefore = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-1);
        var twoDaysBefore = dayBefore.AddDays(-1);

        // Google's inclusive end (begin + 86399), which acceptance turns into the exclusive end one second later.
        var first = Accepted(Candidate(
            "reporter.example", "report-a", dayBefore, dayBefore.AddSeconds(86_399),
            [
                Record("192.0.2.10", 40, "pass", "pass"),
                Record("::ffff:198.51.100.7", 3, "fail", "fail"),
                Record("2001:0DB8:0000:0000:0000:0000:0000:0001", 5, "pass", "fail"),
            ]), receivedAt);
        // Microsoft's exclusive end, from another reporter.
        var second = Accepted(Candidate(
            "second.example", "report-b", twoDaysBefore, twoDaysBefore.AddDays(1),
            [
                Record("192.0.2.10", 2, "pass", "pass"),
                Record("203.0.113.9", 1, "temperror", "fail", "Mail.Jobbliggaren.se."),
            ]), receivedAt);
        var refused = DmarcAggregateReport.Accept(Candidate(
            "reporter.example", "report-c", dayBefore, dayBefore.AddDays(1),
            [Record("192.0.2.11", 1, "pass", "pass")], policyDomain: "other.example"), receivedAt)
            .ShouldBeOfType<DmarcAcceptance.Rejected>();

        // The Worker's own registration of the store, over this host's database; the Api composes none of it.
        await using var scope = factory.Services.CreateAsyncScope();
        var worker = new ServiceCollection()
            .AddLogging()
            .AddSingleton(scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .AddDmarcReportIngestionStores();
        await using var provider = worker.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var run = provider.CreateAsyncScope();
        var store = run.ServiceProvider.GetRequiredService<IDmarcReportStore>();
        store.ShouldBeOfType<SqlDmarcReportStore>();
        await WriteRunAsync(store, runStart, receivedAt, first, second, refused.Reason);

        return new Seeded(runStart, twoDaysBefore, dayBefore.AddDays(1));
    }

    /// <summary>The store's calls through its port, in the order <c>DmarcReportIngestionJob</c> makes them.</summary>
    private static async Task WriteRunAsync(
        IDmarcReportStore store,
        DateTimeOffset runStart,
        DateTimeOffset receivedAt,
        DmarcAggregateReport first,
        DmarcAggregateReport second,
        DmarcRejection refused)
    {
        (await store.ReadStateAsync(Ct)).ShouldBeNull("each test starts with the tables empty");
        await store.SweepCrashesAsync(Ct);
        await store.EnsureStateAsync(runStart, Ct);
        (await store.EnterEpochAsync(Epoch, Ct)).ShouldBeTrue();

        await store.ClaimAsync(Epoch, 1, receivedAt, Ct);
        (await store.StoreAsync(Epoch, 1, first, Ct)).ShouldBe(DmarcMessageOutcome.Stored);
        await store.ClaimAsync(Epoch, 2, receivedAt, Ct);
        (await store.StoreAsync(Epoch, 2, second, Ct)).ShouldBe(DmarcMessageOutcome.Stored);
        await store.ClaimAsync(Epoch, 3, receivedAt, Ct);
        await store.SetOutcomeAsync(Epoch, 3, DmarcOutcomes.OutcomeOf(DmarcSkipReason.Forensic), Ct);
        await store.ClaimAsync(Epoch, 4, receivedAt, Ct);
        await store.SetOutcomeAsync(Epoch, 4, DmarcOutcomes.OutcomeOf(refused), Ct);
        await store.ClaimAsync(Epoch, 5, receivedAt, Ct);
        await store.SetOutcomeAsync(Epoch, 5, DmarcMessageOutcome.RetryPending, Ct);

        await store.RecordAttemptAsync(runStart, DmarcRunOutcome.Succeeded, Ct);
        await store.AnalyzeAsync(Ct);
    }

    private static DmarcRecordCandidate Record(
        string sourceIp, long count, string dkim, string spf, string headerFrom = "jobbliggaren.se") =>
        new(sourceIp, count, headerFrom, dkim, spf);

    private static DmarcReportCandidate Candidate(
        string reporter,
        string reportId,
        DateTimeOffset begins,
        DateTimeOffset ends,
        IReadOnlyList<DmarcRecordCandidate> records,
        string policyDomain = DmarcIngestionLimits.PolicyDomain) =>
        new(
            reporter,
            reportId,
            policyDomain,
            begins.ToUnixTimeSeconds(),
            ends.ToUnixTimeSeconds(),
            records,
            SHA256.HashData(Encoding.UTF8.GetBytes($"{reporter}|{reportId}")));

    private static DmarcAggregateReport Accepted(DmarcReportCandidate candidate, DateTimeOffset receivedAt) =>
        DmarcAggregateReport.Accept(candidate, receivedAt)
            .ShouldBeOfType<DmarcAcceptance.Accepted>("a test report must pass the acceptance bounds production applies")
            .Report;

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

    private async Task EmptyTheTablesAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM dmarc_report_rows; DELETE FROM dmarc_reports; DELETE FROM dmarc_mailbox_messages; "
            + "DELETE FROM dmarc_mailbox_state;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

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

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static void ShouldBePrivate(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }
}
