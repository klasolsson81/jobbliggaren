using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

[Collection("Api")]
public sealed class AdminAccountOverviewAccessTests(ApiFactory factory)
{
    private const string OverviewPath = "/api/v1/admin/overview/accounts";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Overview_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(OverviewPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Overview_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(OverviewPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Overview_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(OverviewPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Unreachable today: no production path removes the role. This asserts safe read-side degradation only.
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(OverviewPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Overview_ShouldRefuseTheNextRead_WhenTheAdminHasLoggedOut()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.PostAsync("/api/v1/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await admin.GetAsync(OverviewPath, Ct);

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
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetAccountOverviewQuery(), Ct));
            }
            else
            {
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetAccountOverviewQuery(), Ct));
            }
        }
        finally
        {
            accessor.HttpContext = previous;
            client?.Dispose();
        }
    }

    [Fact]
    public async Task Overview_ShouldReturnPrivateFailure_WhenItsDirectoryCannotRead()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var directory = Substitute.For<IAccountDirectory>();
        directory.GetOverviewAsync(Arg.Any<IReadOnlyList<Jobbliggaren.Application.Common.Abstractions.CivilDayWindow>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AccountDirectoryOverview>(new TimeoutException()));
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountDirectory>();
            services.AddScoped(_ => directory);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await client.GetAsync(OverviewPath, Ct);

        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("\"newAccounts\"");
    }

    [Fact]
    public async Task SourceReads_ShouldStampTheInjectedClock_WhenAuditAndFailedJobReadsSucceed()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        var now = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow.AddSeconds(1);
        var clock = new MutableFakeDateTimeProvider { UtcNow = now };
        // A fresh Hangfire store produces zero failed jobs and an empty page; this substitutes only that observation.
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring.FailedCount().Returns(0L);
        monitoring.FailedJobs(0, 50).Returns(new JobList<FailedJobDto>(new Dictionary<string, FailedJobDto>()));
        var storage = Substitute.For<JobStorage>();
        storage.GetMonitoringApi().Returns(monitoring);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
            services.RemoveAll<JobStorage>();
            services.AddSingleton(storage);
        }));
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        foreach (var path in new[] { "/api/v1/admin/audit-log?page=1&pageSize=5", "/api/v1/admin/jobs/failed" })
        {
            var response = await admin.GetAsync(path, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
                .ShouldBe(now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            ShouldBePrivate(response);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            body.GetProperty("items").GetArrayLength().ShouldBeLessThanOrEqualTo(path.Contains("audit-log") ? 5 : 50);
            body.TryGetProperty("sampledAt", out _).ShouldBeFalse();
        }
        monitoring.Received(1).FailedCount();
        monitoring.Received(1).FailedJobs(0, 50);
    }

    public static TheoryData<object> InvalidRegistrationBounds =>
    [
        new { registeredFrom = "2026-10-08T00:00:00Z" },
        new { registeredBefore = "2026-10-08T00:00:00Z" },
        new { registeredFrom = "2026-10-09T00:00:00Z", registeredBefore = "2026-10-08T00:00:00Z" },
    ];

    [Theory]
    [MemberData(nameof(InvalidRegistrationBounds))]
    public async Task Search_ShouldReturnPrivate400_WhenTheRegistrationBoundsAreIncompleteOrReversed(object body)
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);

        var response = await SearchAsync(admin, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Search_ShouldReturnAnEmptyPeriod_WhenObservationFallsExactlyAtSwedishMidnight()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var midnight = new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero);

        var response = await SearchAsync(admin, new { registeredFrom = midnight, registeredBefore = midnight }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Items(body).ShouldBeEmpty();
        body.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(0);
        body.GetProperty("counts").GetProperty("total").GetInt32().ShouldBe(0);
    }

    private static void ShouldBePrivate(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }
}
