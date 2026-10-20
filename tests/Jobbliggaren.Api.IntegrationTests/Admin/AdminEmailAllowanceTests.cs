using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;
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
/// #1981 (ADR 0178) — the allowance read, end to end through the real pipeline: who may call it, that every answer
/// including the refusals is private and uncacheable, and exactly what the browser receives. The provider is
/// ApiFactory's scripted source, which answers "not configured" unless a test scripts it.
/// </summary>
[Collection("Api")]
public sealed class AdminEmailAllowanceTests(ApiFactory factory)
{
    private const string EndpointPath = "/api/v1/admin/email/allowance";
    private const string SampledAtHeader = "X-Admin-Sampled-At";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 19, 31, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Observed = new(2026, 10, 18, 19, 30, 40, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- who may call it ----

    [Fact]
    public async Task Allowance_ShouldReturnPrivate401_WhenTheCallerHasNoSession()
    {
        var response = await factory.CreateClient().GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBePrivate(response);
    }

    [Fact]
    public async Task Allowance_ShouldReturnPrivate403_WhenTheCallerIsAnOrdinaryUser()
    {
        var client = await UserAsync(factory, NewToken(), Ct);

        var response = await client.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ShouldBePrivate(response);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("usage");
    }

    [Fact]
    public async Task Allowance_ShouldRefuseTheNextRead_WhenTheAdminRoleHasBeenRevoked()
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
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetEmailAllowanceQuery(), Ct));
            }
            else
            {
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetEmailAllowanceQuery(), Ct));
            }
        }
        finally
        {
            accessor.HttpContext = previous;
            client?.Dispose();
        }
    }

    [Fact]
    public void Allowance_ShouldCarryTheAdminPolicyAndTheAdminReadBucket()
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

    // ---- what the answer says ----

    [Fact]
    public async Task Allowance_ShouldSayNotObserved_WithTheTermsStillPresent_WhenNoProviderIsConfigured()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);

        var response = await admin.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var body = await ReadAsync(response);
        body.GetProperty("terms").GetProperty("includedMonthlyEmails").GetInt32().ShouldBe(300);
        var usage = body.GetProperty("usage");
        usage.GetProperty("status").GetString().ShouldBe("NotObserved");
        usage.GetProperty("reason").GetString().ShouldBe("NotConfigured");
        usage.GetProperty("sent").ValueKind.ShouldBe(JsonValueKind.Null);
        usage.GetProperty("estimatedExtraCostOre").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Allowance_ShouldAnswerTheProvidersCount_WithTheComputedCost_AndStampTheResponseWithTheApiClock()
    {
        var (_, _, session) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = Scripted(new EmailProviderUsageRead.Observed(1_301, 0, Observed));
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(new MutableFakeDateTimeProvider { UtcNow = Now });
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);

        var response = await client.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ShouldBePrivate(response);
        response.Headers.GetValues(SampledAtHeader).ShouldHaveSingleItem()
            .ShouldBe(Now.ToString("O", CultureInfo.InvariantCulture));

        var body = await ReadAsync(response);
        var terms = body.GetProperty("terms");
        terms.GetProperty("plan").GetString().ShouldBe("Essential");
        terms.GetProperty("includedMonthlyEmails").GetInt32().ShouldBe(300);
        terms.GetProperty("pricePerThousandExtra").GetDecimal().ShouldBe(0.25m);
        terms.GetProperty("currency").GetString().ShouldBe("EUR");
        terms.GetProperty("priceReadOn").GetString().ShouldBe("2026-10-18");
        terms.GetProperty("monthlySendLimit").GetInt32().ShouldBe(10_000);
        terms.GetProperty("costCurrency").GetString().ShouldBe("SEK");
        terms.GetProperty("sekPerEur").GetDecimal().ShouldBe(11.1450m);
        terms.GetProperty("exchangeRateOn").GetString().ShouldBe("2026-10-16");

        var usage = body.GetProperty("usage");
        usage.GetProperty("status").GetString().ShouldBe("Observed");
        usage.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        usage.GetProperty("observedAt").GetDateTimeOffset().ShouldBe(Observed);
        usage.GetProperty("month").GetString().ShouldBe("2026-10");
        usage.GetProperty("sent").GetInt64().ShouldBe(1_301);
        usage.GetProperty("billable").GetInt64().ShouldBe(1_001);
        usage.GetProperty("estimatedExtraCostOre").GetInt64().ShouldBe(349);
        usage.GetProperty("aboveHalfOfSendLimit").GetBoolean().ShouldBeFalse();
        usage.GetProperty("webhooksCount").GetInt32().ShouldBe(0);

        var plan = body.GetProperty("plan");
        plan.GetProperty("status").GetString().ShouldBe("Observed");
        plan.GetProperty("active").GetString().ShouldBe("Essential");
        plan.GetProperty("matches").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Allowance_ShouldWithholdTheCost_ButKeepTheCount_WhenTheProvidersPlanIsAnother()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = Scripted(
            new EmailProviderUsageRead.Observed(1_301, 0, Observed),
            new EmailProviderPlanRead.Observed(EmailPlan.Scale, Observed));

        var body = await ReadAsync(await admin.GetAsync(EndpointPath, Ct));

        body.GetProperty("plan").GetProperty("active").GetString().ShouldBe("Scale");
        body.GetProperty("plan").GetProperty("matches").GetBoolean().ShouldBeFalse();
        body.GetProperty("usage").GetProperty("sent").GetInt64().ShouldBe(1_301);
        body.GetProperty("usage").GetProperty("estimatedExtraCostOre").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Allowance_ShouldGiveTheBrowserExactlyTheKeysItWasPromised_AndNothingElse()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = Scripted(new EmailProviderUsageRead.Observed(184, 0, Observed));

        var raw = await (await admin.GetAsync(EndpointPath, Ct)).Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(raw);

        Keys(document.RootElement).ShouldBe(["plan", "terms", "usage"]);
        Keys(document.RootElement.GetProperty("plan")).ShouldBe(["active", "matches", "reason", "status"]);
        Keys(document.RootElement.GetProperty("terms")).ShouldBe(
            ["costCurrency", "currency", "exchangeRateOn", "includedMonthlyEmails", "monthlySendLimit", "plan", "pricePerThousandExtra", "priceReadOn", "sekPerEur"]);
        Keys(document.RootElement.GetProperty("usage")).ShouldBe(
            ["aboveHalfOfSendLimit", "billable", "estimatedExtraCostOre", "month", "observedAt", "reason", "sent",
             "status", "webhooksCount"]);
        foreach (var forbidden in new[] { "project", "scaleway.com", "http" })
        {
            raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase).ShouldBeFalse($"the browser must not receive '{forbidden}'");
        }
    }

    [Fact]
    public async Task Allowance_ShouldAnswerAFailureWithItsReason_AndNoFigures()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = Scripted(new EmailProviderUsageRead.Failed(EmailProviderReadReason.Forbidden));

        var response = await admin.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var usage = (await ReadAsync(response)).GetProperty("usage");
        usage.GetProperty("status").GetString().ShouldBe("Failed");
        usage.GetProperty("reason").GetString().ShouldBe("Forbidden");
        usage.GetProperty("sent").ValueKind.ShouldBe(JsonValueKind.Null);
        usage.GetProperty("observedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Allowance_ShouldSayReadKeyMissing_WithNoReasonAndNoFigures_WhenTheReadKeyIsNotConfigured()
    {
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        using var scripted = factory.EmailProviderReads.Script(
            new EmailProviderUsageRead.ReadKeyMissing(), new EmailProviderPlanRead.ReadKeyMissing());

        var response = await admin.GetAsync(EndpointPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBePrivate(response);
        var body = await ReadAsync(response);
        body.GetProperty("terms").GetProperty("includedMonthlyEmails").GetInt32().ShouldBe(300);
        var usage = body.GetProperty("usage");
        usage.GetProperty("status").GetString().ShouldBe("ReadKeyMissing");
        foreach (var figure in new[] { "reason", "observedAt", "month", "sent", "billable", "estimatedExtraCostOre", "aboveHalfOfSendLimit", "webhooksCount" })
        {
            usage.GetProperty(figure).ValueKind.ShouldBe(JsonValueKind.Null, figure);
        }

        var plan = body.GetProperty("plan");
        plan.GetProperty("status").GetString().ShouldBe("ReadKeyMissing");
        foreach (var figure in new[] { "reason", "active", "matches" })
        {
            plan.GetProperty(figure).ValueKind.ShouldBe(JsonValueKind.Null, figure);
        }
    }

    // ---- plumbing ----

    private IDisposable Scripted(EmailProviderUsageRead read, EmailProviderPlanRead? plan = null) =>
        factory.EmailProviderReads.Script(read, plan ?? new EmailProviderPlanRead.Observed(EmailPlan.Essential, Observed));

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
