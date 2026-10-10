using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Application.Admin.HostObservations.Queries.GetHostObservation;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1982 (ADR 0158) — the Server card's host readings: who may ask, what the answer is made of, and that it is
/// private on every path. The probe is scripted and the real hosted service is removed in the derived hosts:
/// <c>ApiFactory</c> would otherwise start it, reading the machine the test runs on.
/// </summary>
[Collection("Api")]
public sealed class AdminHostObservationAccessTests(ApiFactory factory)
{
    private const string HostPath = "/api/v1/admin/overview/host";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Host_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(HostPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Host_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(HostPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("cpu");
    }

    [Fact]
    public async Task Host_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(HostPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Unreachable today: no production path removes the role. This asserts safe read-side degradation only.
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(HostPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Host_ShouldRefuseTheNextRead_WhenTheAdminHasLoggedOut()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.PostAsync("/api/v1/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await admin.GetAsync(HostPath, Ct);

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
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetHostObservationQuery(), Ct));
            }
            else
            {
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetHostObservationQuery(), Ct));
            }
        }
        finally
        {
            accessor.HttpContext = previous;
            client?.Dispose();
        }
    }

    [Fact]
    public async Task Host_ShouldCarryTheReadingsApiReadInstantAndOnlyTheAgreedFields_WhenAnAdminAsks()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var probe = new ScriptedHostProbe();
        var clock = new MutableFakeDateTimeProvider { UtcNow = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow };
        using var host = Scripted(probe, clock);
        var sampler = host.Services.GetRequiredService<HostObservationSampler>();
        sampler.Sample();
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        probe.Cpu = ProbeResult.Ok(new CpuCounters(1_600, 20_400, TimeSpan.FromSeconds(4_030)));
        sampler.Sample();
        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await admin.GetAsync(HostPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
            .ShouldBe(clock.UtcNow.ToString("O", CultureInfo.InvariantCulture), "the header is the API read instant");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Names(body).ShouldBe(["readAt", "staleAfterSeconds", "cpu", "memory", "disk"]);
        body.GetProperty("staleAfterSeconds").GetInt32().ShouldBe(120);
        body.GetProperty("cpu").GetProperty("state").GetString().ShouldBe("Available");
        Names(body.GetProperty("cpu")).ShouldBe(["state", "sampledAt", "value"]);
        body.GetProperty("cpu").GetProperty("value").GetProperty("percent").GetDouble().ShouldBe(5.0);
        body.GetProperty("memory").GetProperty("value").GetProperty("totalBytes").GetInt64().ShouldBe(8_135_992L * 1024);
        body.GetProperty("disk").GetProperty("value").GetProperty("freeBytes").GetInt64().ShouldBe(242_287_181_824);
        // The reading is 5 s old and its own time is not the read time.
        DateTimeOffset.Parse(body.GetProperty("cpu").GetProperty("sampledAt").GetString()!, CultureInfo.InvariantCulture)
            .ShouldBeLessThan(DateTimeOffset.Parse(body.GetProperty("readAt").GetString()!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Host_ShouldAnswer200WithFailedReadings_WhenTheProbeCannotReadTheHost()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var probe = new ScriptedHostProbe
        {
            Cpu = ProbeResult.Fail<CpuCounters>(HostMetricReason.ReadFailed),
            Memory = ProbeResult.Fail<MemoryReading>(HostMetricReason.PlatformUnsupported),
        };
        var clock = new MutableFakeDateTimeProvider { UtcNow = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow };
        using var host = Scripted(probe, clock);
        host.Services.GetRequiredService<HostObservationSampler>().Sample();
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await admin.GetAsync(HostPath, Ct);

        // A reading that cannot be taken is a state of the answer, not a failure of the request.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("cpu").GetProperty("state").GetString().ShouldBe("Failed");
        body.GetProperty("memory").GetProperty("state").GetString().ShouldBe("NotObservable");
        body.GetProperty("disk").GetProperty("state").GetString().ShouldBe("Available");
        body.GetProperty("cpu").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldNotContain("ReadFailed");
        text.ShouldNotContain("PlatformUnsupported");
    }

    [Fact]
    public async Task Host_ShouldReturnPrivateFailure_WhenTheReaderThrows()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var reader = Substitute.For<IHostObservationReader>();
        reader.Current.Returns(_ => throw new TimeoutException("never reaches the page"));
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostObservationReader>();
            services.AddSingleton(reader);
            RemoveHostedService(services);
        }));
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await admin.GetAsync(HostPath, Ct);

        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        ShouldBePrivate(response);
        // The host is Development here, whose error body may carry detail; what must hold is that no reading leaks.
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("\"cpu\"");
    }

    [Fact]
    public void Host_ShouldCarryTheAdminPolicyAndTheAdminReadBucket()
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == HostPath
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList()
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.AdminReadPolicy);
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
    }

    [Fact]
    public void TheApiRegistersTheSamplerAsOneSingletonServingBothTheReaderAndTheTick()
    {
        var sampler = factory.Services.GetRequiredService<HostObservationSampler>();

        factory.Services.GetRequiredService<IHostObservationReader>().ShouldBeSameAs(sampler);
        factory.Services.GetRequiredService<IHostObservationSampler>().ShouldBeSameAs(sampler);
        factory.Services.GetServices<IHostedService>().OfType<HostObservationService>().ShouldHaveSingleItem();
    }

    private WebApplicationFactory<Program> Scripted(ScriptedHostProbe probe, MutableFakeDateTimeProvider clock) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostObservationProbe>();
            services.AddSingleton<IHostObservationProbe>(probe);
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
            RemoveHostedService(services);
        }));

    private static void RemoveHostedService(IServiceCollection services)
    {
        var hosted = services.Single(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(HostObservationService));
        services.Remove(hosted);
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    private static void ShouldBePrivate(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    /// <summary>A probe whose readings the test sets; the defaults are a healthy host.</summary>
    private sealed class ScriptedHostProbe : IHostObservationProbe
    {
        public ProbeResult<CpuCounters> Cpu { get; set; } =
            ProbeResult.Ok(new CpuCounters(1_000, 9_000, TimeSpan.FromSeconds(4_000)));

        public ProbeResult<MemoryReading> Memory { get; set; } =
            ProbeResult.Ok(new MemoryReading(8_135_992L * 1024, 5_743_556L * 1024));

        public ProbeResult<DiskReading> Disk { get; set; } =
            ProbeResult.Ok(new DiskReading(269_205_880_832, 242_287_181_824, 253_339_029_504));

        public ProbeResult<CpuCounters> ReadCpu() => Cpu;

        public ProbeResult<MemoryReading> ReadMemory() => Memory;

        public ProbeResult<DiskReading> ReadDisk() => Disk;

        public ProbeResult<HostView> ReadHostView() => ProbeResult.Ok(HostView.HostScoped);
    }
}
