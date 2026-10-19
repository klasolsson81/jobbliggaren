using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1981 PR 2 — the delivery totals and the recent failures, end to end through the real pipeline: who may call them,
/// that every answer including a refusal and a 400 is private and uncacheable, that the period is one of three tokens,
/// and exactly what the browser receives. The provider is ApiFactory's scripted source, which answers "not configured"
/// unless a test scripts it. Every address is on a domain RFC 2606/6761 reserves.
/// </summary>
[Collection("Api")]
public sealed class AdminEmailDeliveryTests(ApiFactory factory)
{
    private const string DeliveryPath = "/api/v1/admin/email/delivery";
    private const string FailuresPath = "/api/v1/admin/email/failures";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private const string MarkerLocalPart = "zz-marker-4711";
    private static readonly DateTimeOffset Observed = new(2026, 10, 19, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 19, 1, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly EmailDeliveryStatisticsRead Counted = new EmailDeliveryStatisticsRead.Observed(40, 37, 3, Observed);
    private static readonly EmailDeliveryStatisticsRead NoFailures = new EmailDeliveryStatisticsRead.Observed(40, 40, 0, Observed);
    private static readonly EmailDeliveryStatisticsRead Sentinel = new EmailDeliveryStatisticsRead.Observed(149, 140, 9, Observed);
    private static readonly EmailDeliveryStatisticsRead BlindSentinel = new EmailDeliveryStatisticsRead.Observed(0, 0, 0, Observed);

    public static TheoryData<string> Paths() => [DeliveryPath, FailuresPath];

    // ---- who may call them ----

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Each_ShouldReturnPrivate401_WhenTheCallerHasNoSession(string path)
    {
        var response = await factory.CreateClient().GetAsync(path + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Each_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser(string path)
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(path + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("status");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Each_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked(string path)
    {
        var (admin, userId, _) = await AdminAsync(factory, NewToken(), Ct);
        (await admin.GetAsync(path + "?period=d7", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await DemoteAsync(factory, userId);

        var response = await admin.GetAsync(path + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DirectQuery_ShouldRefuseAnUnprivilegedCaller_WhenTheHttpEndpointIsBypassed(bool failures, bool authenticated)
    {
        IMessage query = failures
            ? new GetEmailRecentFailuresQuery(EmailDeliveryPeriod.Last7Days)
            : new GetEmailDeliveryQuery(EmailDeliveryPeriod.Last7Days);
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

    [Theory]
    [MemberData(nameof(Paths))]
    public void Each_ShouldCarryTheAdminPolicyAndTheAdminReadBucket(string path)
    {
        _ = factory.CreateClient();
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == path
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList().ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty();
        rateLimiting[^1].PolicyName.ShouldBe(RateLimitingExtensions.AdminReadPolicy);
    }

    // ---- the period ----

    public static TheoryData<string, string> BadPeriods()
    {
        var data = new TheoryData<string, string>();
        foreach (var path in new[] { DeliveryPath, FailuresPath })
        {
            foreach (var query in new[]
                     {
                         "", "?period=", "?period=H24", "?period=42", "?period=d30", "?period=Last24Hours", "?period=h24&period=d7",
                     })
            {
                data.Add(path, query);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadPeriods))]
    public async Task Each_ShouldAnswerAPrivate400_ForAnythingButTheThreeTokens(string path, string query)
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);

        var response = await admin.GetAsync(path + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Period");
    }

    [Theory]
    [InlineData("h24")]
    [InlineData("d3")]
    [InlineData("d7")]
    public async Task Each_ShouldEchoThePeriodToken_ItWasAskedFor(string token)
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);

        foreach (var path in new[] { DeliveryPath, FailuresPath })
        {
            var response = await admin.GetAsync($"{path}?period={token}", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await ReadAsync(response)).GetProperty("period").GetString().ShouldBe(token);
        }
    }

    [Fact]
    public async Task Each_ShouldStampTheResponseWithTheApiClock()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(new MutableFakeDateTimeProvider { UtcNow = Now });
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        foreach (var path in new[] { DeliveryPath, FailuresPath })
        {
            var response = await client.GetAsync(path + "?period=d7", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
                .ShouldBe(Now.ToString("O", CultureInfo.InvariantCulture), path);
        }
    }

    // ---- delivery: what the answer says ----

    [Fact]
    public async Task Delivery_ShouldAnswerTheProvidersCounts_PrivatelyAndStamped_WhenTheCanaryPasses()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = factory.EmailProviderReads.ScriptStatistics(Counted, Sentinel);

        var response = await admin.GetAsync(DeliveryPath + "?period=h24", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        DateTimeOffset.Parse(response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem(), System.Globalization.CultureInfo.InvariantCulture)
            .Offset.ShouldBe(TimeSpan.Zero);
        var body = await ReadAsync(response);
        body.GetProperty("period").GetString().ShouldBe("h24");
        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("observedAt").GetDateTimeOffset().ShouldBe(Observed);
        body.GetProperty("sent").GetInt64().ShouldBe(37);
        body.GetProperty("failed").GetInt64().ShouldBe(3);
    }

    [Fact]
    public async Task Delivery_ShouldGiveTheBrowserExactlyTheKeysItWasPromised_AndNothingElse()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = factory.EmailProviderReads.ScriptStatistics(Counted, Sentinel);

        var raw = await (await admin.GetAsync(DeliveryPath + "?period=d3", Ct)).Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(raw);

        Keys(document.RootElement).ShouldBe(["failed", "observedAt", "period", "reason", "sent", "status"]);
        foreach (var forbidden in new[] { "project", "scaleway.com", "http", "total" })
        {
            raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase).ShouldBeFalse($"the browser must not receive '{forbidden}'");
        }
    }

    [Theory]
    [InlineData("NotConfigured", "NotObserved", "NotConfigured")]
    [InlineData("ReadKeyMissing", "ReadKeyMissing", null)]
    [InlineData("Failed", "Failed", "Forbidden")]
    [InlineData("Blind", "NotObservable", null)]
    public async Task Delivery_ShouldAnswerEveryOtherState_WithNoFigures(string scripted, string status, string? reason)
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scope = scripted switch
        {
            "NotConfigured" => null,
            "ReadKeyMissing" => factory.EmailProviderReads.ScriptStatistics(
                new EmailDeliveryStatisticsRead.ReadKeyMissing(), new EmailDeliveryStatisticsRead.ReadKeyMissing()),
            "Failed" => factory.EmailProviderReads.ScriptStatistics(
                new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Forbidden), Sentinel),
            _ => factory.EmailProviderReads.ScriptStatistics(Counted, BlindSentinel),
        };

        var response = await admin.GetAsync(DeliveryPath + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var body = await ReadAsync(response);
        body.GetProperty("status").GetString().ShouldBe(status);
        if (reason is null)
        {
            body.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        else
        {
            body.GetProperty("reason").GetString().ShouldBe(reason);
        }

        foreach (var figure in new[] { "observedAt", "sent", "failed" })
        {
            body.GetProperty(figure).ValueKind.ShouldBe(JsonValueKind.Null, figure);
        }
    }

    // ---- failures: what the answer says ----

    [Fact]
    public async Task Failures_ShouldAnswerTheMaskedList_WithItsLimit()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var statistics = factory.EmailProviderReads.ScriptStatistics(Counted, Sentinel);
        using var failures = factory.EmailProviderReads.ScriptFailures(new EmailRecentFailuresRead.Observed(
        [
            new EmailFailure(Observed.AddMinutes(-21), MaskedEmailAddress.Of(MarkerLocalPart + "@example.com"), EmailFailureReason.MailboxNotFound),
            new EmailFailure(Observed.AddHours(-5), MaskedEmailAddress.Of("adam@example.org"), EmailFailureReason.Other),
        ], Observed));

        var response = await admin.GetAsync(FailuresPath + "?period=d7", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        raw.ShouldNotContain(MarkerLocalPart);
        using var document = JsonDocument.Parse(raw);
        var body = document.RootElement;
        Keys(body).ShouldBe(["failures", "limit", "observedAt", "period", "reason", "status"]);
        body.GetProperty("period").GetString().ShouldBe("d7");
        body.GetProperty("status").GetString().ShouldBe("Observed");
        body.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("observedAt").GetDateTimeOffset().ShouldBe(Observed);
        body.GetProperty("limit").GetInt32().ShouldBe(10);

        var items = body.GetProperty("failures").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        Keys(items[0]).ShouldBe(["createdAt", "reason", "recipient"]);
        items[0].GetProperty("createdAt").GetDateTimeOffset().ShouldBe(Observed.AddMinutes(-21));
        items[0].GetProperty("recipient").GetString().ShouldBe("z•••@example.com");
        items[0].GetProperty("reason").GetString().ShouldBe("MailboxNotFound");
        items[1].GetProperty("recipient").GetString().ShouldBe("a•••@example.org");
        items[1].GetProperty("reason").GetString().ShouldBe("Other");
    }

    [Fact]
    public async Task Failures_ShouldAnswerAnEmptyList_OnlyWhenTheCanaryAndThePeriodsCountTrustIt()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var failures = factory.EmailProviderReads.ScriptFailures(new EmailRecentFailuresRead.Observed([], Observed));

        using (factory.EmailProviderReads.ScriptStatistics(NoFailures, Sentinel))
        {
            var trusted = await ReadAsync(await admin.GetAsync(FailuresPath + "?period=h24", Ct));
            trusted.GetProperty("status").GetString().ShouldBe("Observed");
            trusted.GetProperty("failures").GetArrayLength().ShouldBe(0);
        }

        using (factory.EmailProviderReads.ScriptStatistics(Counted, Sentinel))
        {
            var untrusted = await ReadAsync(await admin.GetAsync(FailuresPath + "?period=h24", Ct));
            untrusted.GetProperty("status").GetString().ShouldBe("NotObservable");
            untrusted.GetProperty("failures").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Theory]
    [InlineData("NotConfigured", "NotObserved", "NotConfigured")]
    [InlineData("ReadKeyMissing", "ReadKeyMissing", null)]
    [InlineData("Failed", "Failed", "Timeout")]
    [InlineData("Blind", "NotObservable", null)]
    public async Task Failures_ShouldAnswerEveryOtherState_WithNoList(string scripted, string status, string? reason)
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var statistics = scripted == "Blind" ? factory.EmailProviderReads.ScriptStatistics(NoFailures, BlindSentinel) : null;
        using var failures = scripted switch
        {
            "NotConfigured" => null,
            "ReadKeyMissing" => factory.EmailProviderReads.ScriptFailures(new EmailRecentFailuresRead.ReadKeyMissing()),
            "Failed" => factory.EmailProviderReads.ScriptFailures(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Timeout)),
            _ => factory.EmailProviderReads.ScriptFailures(new EmailRecentFailuresRead.Observed([], Observed)),
        };

        var response = await admin.GetAsync(FailuresPath + "?period=d3", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var body = await ReadAsync(response);
        body.GetProperty("status").GetString().ShouldBe(status);
        if (reason is null)
        {
            body.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        else
        {
            body.GetProperty("reason").GetString().ShouldBe(reason);
        }

        body.GetProperty("observedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("failures").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("limit").GetInt32().ShouldBe(10);
    }

    // ---- plumbing ----

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
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
