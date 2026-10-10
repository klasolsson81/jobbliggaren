using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Auth;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1982 (ADR 0159) — the services card's observations: who may ask, what the answer is made of, and that it is
/// private on every path. The three ports are scripted and the real hosted service is removed in the derived hosts:
/// <c>ApiFactory</c> would otherwise start it, reading the Hangfire storage, Redis and audit log of a test host.
/// </summary>
[Collection("Api")]
public sealed class AdminServiceObservationAccessTests(ApiFactory factory)
{
    private const string ServicesPath = "/api/v1/admin/overview/services";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Services_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(ServicesPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Services_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(ServicesPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("backgroundJobs");
    }

    [Fact]
    public async Task Services_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(ServicesPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Unreachable today: no production path removes the role. This asserts safe read-side degradation only.
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(ServicesPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Services_ShouldRefuseTheNextRead_WhenTheAdminHasLoggedOut()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.PostAsync("/api/v1/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await admin.GetAsync(ServicesPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetServiceObservationQuery(), Ct));
            }
            else
            {
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetServiceObservationQuery(), Ct));
            }
        }
        finally
        {
            accessor.HttpContext = previous;
            client?.Dispose();
        }
    }

    [Fact]
    public async Task Services_ShouldCarryTheReadInstantAndOnlyTheAgreedFields_WhenAnAdminAsks()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var clock = new MutableFakeDateTimeProvider { UtcNow = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow };
        var sources = new Sources(clock.UtcNow);
        using var host = Scripted(sources, clock);
        await host.Services.GetRequiredService<ServiceObservationSampler>().SampleAsync(Ct);
        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await admin.GetAsync(ServicesPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
            .ShouldBe(clock.UtcNow.ToString("O", CultureInfo.InvariantCulture), "the header is the API read instant");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Names(body).ShouldBe([
            "staleAfterSeconds", "workerAliveWithinSeconds", "importOverdueAfterSeconds",
            "backgroundJobs", "volatileStore", "jobImport"]);
        Names(body.GetProperty("backgroundJobs")).ShouldBe(["state", "sampledAt", "heartbeatAt"]);
        Names(body.GetProperty("volatileStore")).ShouldBe(["state", "sampledAt"]);
        Names(body.GetProperty("jobImport")).ShouldBe(["state", "sampledAt", "lastRunAt", "errors"]);
        body.GetProperty("backgroundJobs").GetProperty("state").GetString().ShouldBe("Alive");
        body.GetProperty("volatileStore").GetProperty("state").GetString().ShouldBe("Reachable");
        body.GetProperty("jobImport").GetProperty("state").GetString().ShouldBe("Recorded");
        body.GetProperty("jobImport").GetProperty("errors").GetInt32().ShouldBe(2);
        // The rows were sampled 5 s before the read: their own time is not the read time, which is the header's.
        DateTimeOffset.Parse(body.GetProperty("volatileStore").GetProperty("sampledAt").GetString()!, CultureInfo.InvariantCulture)
            .ShouldBeLessThan(clock.UtcNow);
    }

    // Actor: the storage driver. A Worker source that cannot read Hangfire's storage throws what Npgsql throws when
    // the schema is missing (the default ApiFactory host is exactly that), so that is the exception scripted here.
    [Fact]
    public async Task Services_ShouldAnswer200WithOneUnknownRow_WhenOneSourceCannotBeRead()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var clock = new MutableFakeDateTimeProvider { UtcNow = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow };
        var sources = new Sources(clock.UtcNow) { WorkerThrows = new PostgresException("relation does not exist", "ERROR", "ERROR", "42P01") };
        using var host = Scripted(sources, clock);
        await host.Services.GetRequiredService<ServiceObservationSampler>().SampleAsync(Ct);
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await admin.GetAsync(ServicesPath, Ct);

        // A source that cannot be read is a state of the answer, not a failure of the request.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("backgroundJobs").GetProperty("state").GetString().ShouldBe("Unknown");
        body.GetProperty("backgroundJobs").GetProperty("heartbeatAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("volatileStore").GetProperty("state").GetString().ShouldBe("Reachable");
        body.GetProperty("jobImport").GetProperty("state").GetString().ShouldBe("Recorded");
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldNotContain("42P01");
        text.ShouldNotContain("relation");
    }

    // Actor: a failing log sink in the Mediator pipeline throws inside mediator.Send. The stubbed reader is only the
    // seam that makes an exception escape the handler; what is asserted is the privacy of the route's 5xx.
    [Fact]
    public async Task Services_ShouldReturnPrivateFailure_WhenTheReaderThrows()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var reader = Substitute.For<IServiceObservationReader>();
        reader.Current.Returns(_ => throw new TimeoutException("never reaches the page"));
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IServiceObservationReader>();
            services.AddSingleton(reader);
            RemoveHostedService(services);
        }));
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await admin.GetAsync(ServicesPath, Ct);

        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("\"backgroundJobs\"");
    }

    [Fact]
    public void Services_ShouldCarryTheAdminPolicyAndTheAdminReadBucket()
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == ServicesPath
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList()
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.AdminReadPolicy);
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
    }

    [Fact]
    public void Services_TheApiRegistersTheSampler_AsOneSingletonServingBothTheReaderAndTheTick()
    {
        var sampler = factory.Services.GetRequiredService<ServiceObservationSampler>();

        factory.Services.GetRequiredService<IServiceObservationReader>().ShouldBeSameAs(sampler);
        factory.Services.GetRequiredService<IServiceObservationSampler>().ShouldBeSameAs(sampler);
        factory.Services.GetServices<IHostedService>().OfType<ServiceObservationService>().ShouldHaveSingleItem();
    }

    // The probe filters on this name. A rename of the check would turn the row into a permanent Unknown, silently.
    [Fact]
    public void Services_TheVolatileRedisCheckIsRegisteredUnderTheNameTheProbeFiltersOnAndOnTheReadyTag()
    {
        var registration = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
            .Registrations
            .Where(r => r.Name == VolatileRedisHealthCheckExtensions.HealthCheckName)
            .ToList()
            .ShouldHaveSingleItem();

        registration.Tags.ShouldContain(VolatileRedisHealthCheckExtensions.ReadyTag);
    }

    [Fact]
    public void Services_TheRealAdaptersAreWhatTheApiComposes()
    {
        factory.Services.GetRequiredService<IWorkerHeartbeatSource>().GetType().Name.ShouldBe("HangfireWorkerHeartbeatSource");
        factory.Services.GetRequiredService<IVolatileStoreProbe>().GetType().Name.ShouldBe("ReadinessVolatileStoreProbe");
        factory.Services.GetRequiredService<IImportRunRecords>().GetType().Name.ShouldBe("SqlImportRunRecords");
    }

    private WebApplicationFactory<Program> Scripted(Sources sources, MutableFakeDateTimeProvider clock) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWorkerHeartbeatSource>();
            services.AddSingleton<IWorkerHeartbeatSource>(sources);
            services.RemoveAll<IVolatileStoreProbe>();
            services.AddSingleton<IVolatileStoreProbe>(sources);
            services.RemoveAll<IImportRunRecords>();
            services.AddSingleton<IImportRunRecords>(sources);
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
            RemoveHostedService(services);
        }));

    private static void RemoveHostedService(IServiceCollection services)
    {
        var hosted = services.Single(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(ServiceObservationService));
        services.Remove(hosted);
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    private static void ShouldBePrivate(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    /// <summary>All three ports in one scripted object; the defaults are a healthy system whose import ran 4 minutes ago.</summary>
    private sealed class Sources(DateTimeOffset now) : IWorkerHeartbeatSource, IVolatileStoreProbe, IImportRunRecords
    {
        public Exception? WorkerThrows { get; init; }

        public WorkerHeartbeatReading Read() =>
            WorkerThrows is { } ex ? throw ex : new WorkerHeartbeatReading(true, now.AddSeconds(-7));

        public Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<StreamImportRun?> LatestStreamRunAsync(
            JobSource source, DateTimeOffset notBefore, CancellationToken cancellationToken) =>
            Task.FromResult<StreamImportRun?>(new StreamImportRun(now.AddMinutes(-4), 2));
    }
}
