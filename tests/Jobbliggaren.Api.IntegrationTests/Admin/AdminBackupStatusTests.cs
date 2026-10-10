using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Backup;
using Jobbliggaren.Application.Admin.Backup.Queries.GetBackupStatus;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1982 (ADR 0157) — the Backup card's read, end to end through the real pipeline: who may call it, that every
/// answer including the refusals is private and uncacheable, what the browser is allowed to receive, and that the
/// answer follows the host's file and the API's clock and nothing else. The success paths read the golden files
/// the host sampler publishes.
/// </summary>
[Collection("Api")]
public sealed class AdminBackupStatusTests(ApiFactory factory) : IDisposable
{
    private const string EndpointPath = "/api/v1/admin/overview/backup";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private const string BackupFileName = "backup.json";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 13, 41, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset Sampled = new(2026, 10, 10, 13, 41, 2, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jbl-backup-status-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ---- who may call it ----

    [Fact]
    public async Task Backup_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Backup_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("lastSuccess");
    }

    [Fact]
    public async Task Backup_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(EndpointPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(EndpointPath, Ct);

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
            if (authenticated)
            {
                var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = Ct };
                context.Request.Headers.Authorization = $"Bearer {client!.DefaultRequestHeaders.Authorization!.Parameter}";
                accessor.HttpContext = context;
                var auth = await context.AuthenticateAsync("Bearer");
                auth.Succeeded.ShouldBeTrue();
                context.User = auth.Principal.ShouldNotBeNull();
                await Should.ThrowAsync<ForbiddenException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetBackupStatusQuery(), Ct));
            }
            else
            {
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetBackupStatusQuery(), Ct));
            }
        }
        finally
        {
            accessor.HttpContext = previous;
            client?.Dispose();
        }
    }

    [Fact]
    public void Backup_ShouldCarryTheAdminPolicyAndTheAdminReadBucket()
    {
        _ = factory.CreateClient();
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == EndpointPath
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList().ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty();
        rateLimiting[^1].PolicyName.ShouldBe(RateLimitingExtensions.AdminReadPolicy);
    }

    // ---- what the answer follows ----

    [Fact]
    public async Task Backup_ShouldSayNotObserved_WhenTheBridgeIsNotConfigured()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(directory: null, new MutableFakeDateTimeProvider { UtcNow = Now });
        using var client = ClientFor(host, session);

        var response = await client.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetString().ShouldBe("NotObserved");
        body.GetProperty("reason").GetString().ShouldBe("NotConfigured");
        body.GetProperty("observedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("lastSuccess").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Backup_ShouldSayNotObserved_UntilTheSamplerHasPublished()
    {
        Directory.CreateDirectory(_directory);
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(_directory, new MutableFakeDateTimeProvider { UtcNow = Now });
        using var client = ClientFor(host, session);

        var body = await (await client.GetAsync(EndpointPath, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        body.GetProperty("status").GetString().ShouldBe("NotObserved");
        body.GetProperty("reason").GetString().ShouldBe("NotSampledYet");
    }

    [Fact]
    public async Task Backup_ShouldAnswerTheHostsOwnObservation_AndStampTheResponseWithTheApiClock()
    {
        Publish("backup-recorded-scheduled.json");
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(_directory, new MutableFakeDateTimeProvider { UtcNow = Now });
        using var client = ClientFor(host, session);

        var response = await client.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
            .ShouldBe(Now.ToString("O", CultureInfo.InvariantCulture));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("observedAt").GetDateTimeOffset().ShouldBe(Sampled);
        body.GetProperty("stale").GetBoolean().ShouldBeFalse();
        var last = body.GetProperty("lastSuccess");
        last.GetProperty("state").GetString().ShouldBe("Recorded");
        last.GetProperty("completedAt").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 10, 10, 2, 19, 7, TimeSpan.Zero));
        last.GetProperty("overdue").GetBoolean().ShouldBeFalse();
        var timer = body.GetProperty("timer");
        timer.GetProperty("state").GetString().ShouldBe("Scheduled");
        timer.GetProperty("nextRunAt").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 10, 10, 13, 49, 41, TimeSpan.Zero));
    }

    [Fact]
    public async Task Backup_ShouldNeverLetAnOldObservationBecomeFresh_HoweverLateItIsRead()
    {
        Publish("backup-recorded-scheduled.json");
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(_directory, new MutableFakeDateTimeProvider { UtcNow = Now.AddDays(2) });
        using var client = ClientFor(host, session);

        var body = await (await client.GetAsync(EndpointPath, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        body.GetProperty("observedAt").GetDateTimeOffset().ShouldBe(Sampled);
        body.GetProperty("stale").GetBoolean().ShouldBeTrue();
        body.GetProperty("lastSuccess").GetProperty("overdue").GetBoolean().ShouldBeTrue(
            "two days after a run that ended at 02:19 is overdue by the API's clock");
    }

    [Fact]
    public async Task Backup_ShouldAnswerTheStateTheBoxIsInUntilTheBackupIsSwitchedOn()
    {
        Publish("backup-missing-inactive.json");
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(_directory, new MutableFakeDateTimeProvider { UtcNow = Now });
        using var client = ClientFor(host, session);

        var body = await (await client.GetAsync(EndpointPath, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("lastSuccess").GetProperty("state").GetString().ShouldBe("Missing");
        body.GetProperty("lastSuccess").GetProperty("completedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("timer").GetProperty("state").GetString().ShouldBe("Inactive");
        body.GetProperty("timer").GetProperty("nextRunAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Backup_ShouldGiveTheBrowserExactlyTheKeysItWasPromised_AndNothingElse()
    {
        Publish("backup-recorded-scheduled.json");
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(_directory, new MutableFakeDateTimeProvider { UtcNow = Now });
        using var client = ClientFor(host, session);

        var raw = await (await client.GetAsync(EndpointPath, Ct)).Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(raw);

        Keys(document.RootElement).ShouldBe(["lastSuccess", "observedAt", "reason", "stale", "status", "timer"]);
        Keys(document.RootElement.GetProperty("lastSuccess")).ShouldBe(["completedAt", "overdue", "state"]);
        Keys(document.RootElement.GetProperty("timer")).ShouldBe(["nextRunAt", "state"]);
        foreach (var forbidden in new[] { "startedAt", "2026-10-10T02:15:41", _directory, BackupFileName, "systemctl", "jobbliggaren-backup" })
        {
            raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase).ShouldBeFalse($"the browser must not receive '{forbidden}'");
        }
    }

    [Fact]
    public async Task Backup_ShouldAnswerFailedWithAReason_AndEchoNothingOfAFileItRefuses()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, BackupFileName), "{\"SECRET-VALUE-4711\": nope", Ct);
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = HostWith(_directory, new MutableFakeDateTimeProvider { UtcNow = Now });
        using var client = ClientFor(host, session);

        var response = await client.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        raw.ShouldNotContain("SECRET-VALUE-4711");
        using var document = JsonDocument.Parse(raw);
        document.RootElement.GetProperty("status").GetString().ShouldBe("Failed");
        document.RootElement.GetProperty("reason").GetString().ShouldBe("InvalidFormat");
        document.RootElement.GetProperty("lastSuccess").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Backup_ShouldReturnPrivate500_WhenTheSourceFailsUnexpectedly()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBackupSampleSource>();
            services.AddSingleton<IBackupSampleSource>(new ThrowingSource());
        }));
        using var client = ClientFor(host, session);

        var response = await client.GetAsync(EndpointPath, Ct);

        // The Development host shows exception details by design, so the body is not asserted here;
        // that production does not is IncludeErrorDetailGuardTests' to hold.
        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        ShouldBePrivate(response);
    }

    private sealed class ThrowingSource : IBackupSampleSource
    {
        public ValueTask<BackupSampleRead> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SECRET-MESSAGE-4711");
    }

    // ---- plumbing ----

    private void Publish(string fixture)
    {
        Directory.CreateDirectory(_directory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "HostBridgeFixtures", fixture),
            Path.Combine(_directory, BackupFileName),
            overwrite: true);
    }

    private WebApplicationFactory<Program> HostWith(string? directory, IDateTimeProvider clock) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["HostBridge:Directory"] = directory }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton(clock);
            });
        });

    private static HttpClient ClientFor(WebApplicationFactory<Program> host, string session)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);
        return client;
    }

    private static string[] Keys(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static void ShouldBePrivate(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }
}
