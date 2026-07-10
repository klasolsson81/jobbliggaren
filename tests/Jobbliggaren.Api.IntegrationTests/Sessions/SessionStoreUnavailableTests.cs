using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

/// <summary>
/// Verifierar hela 503-vägen end-to-end: en inre <see cref="RedisTimeoutException"/> (degraderad
/// Redis) översätts av <see cref="SessionStoreResilienceDecorator"/> (#511) till
/// <c>SessionStoreUnavailableException</c> → 503 (inte 401 eller 500), och 503-vägen loggar en
/// dedikerad <c>session_store_unavailable</c>-Error via <c>SessionStoreUnavailableLog</c> (#512).
/// Säkerhetskrav: infrastrukturincident ska inte se ut som autentiseringsfel (ADR 0017 Turn 4).
/// </summary>
[Collection("Api")]
public class SessionStoreUnavailableTests(ApiFactory factory)
{
    [Fact]
    public async Task GET_me_when_session_store_times_out_returns_503_not_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Registrera en giltig session via fungerande store
        var goodClient = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(goodClient, ct: ct);

        // Bygg en ny factory-instans där den inre store:n timeout:ar (degraderad Redis) och
        // wrappas av den RIKTIGA decoratorn — precis som i produktion.
        await using var brokenFactory = new BrokenSessionStoreFactory(factory);
        var brokenClient = brokenFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
        });
        brokenClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await brokenClient.GetAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GET_me_when_session_store_times_out_does_not_return_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var goodClient = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(goodClient, ct: ct);

        await using var brokenFactory = new BrokenSessionStoreFactory(factory);
        var brokenClient = brokenFactory.CreateClient();
        brokenClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await brokenClient.GetAsync("/api/v1/me", ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized,
            "503 (service unavailable) ska inte framstå som 401 (autentiseringsfel) — " +
            "det döjer infrastrukturincidenter som autentiseringsproblem.");
    }

    [Fact]
    public async Task GET_me_when_session_store_unavailable_logs_session_store_unavailable_error()
    {
        var ct = TestContext.Current.CancellationToken;

        var goodClient = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(goodClient, ct: ct);

        await using var brokenFactory = new BrokenSessionStoreFactory(factory);
        var brokenClient = brokenFactory.CreateClient();
        brokenClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await brokenClient.GetAsync("/api/v1/me", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        // #512: the outage must be observable — a dedicated Error event on the 503 path, so
        // the TD-77 5xx alarm has a signal. Filter on the dedicated event-id (the request
        // pipeline emits many other records); assert on the one we own.
        var unavailableLogs = brokenFactory.LogProvider.Logs
            .Where(l => l.EventId.Id == 2050).ToList();
        unavailableLogs.ShouldNotBeEmpty();
        var record = unavailableLogs[0];
        record.Level.ShouldBe(LogLevel.Error);
        record.Message.ShouldContain("event_name=session_store_unavailable");
        // §5/data-minimisation: the log must not leak the bearer token / session-id, nor the inner
        // Redis exception message (it can embed the operated key). The broken inner threw
        // "Timeout performing GET (5000ms)" — its text must not appear in the log.
        record.Message.ShouldNotContain(sessionId);
        record.Message.ShouldNotContain("Timeout performing GET");
    }

    private sealed class BrokenSessionStoreFactory : WebApplicationFactory<Program>
    {
        private readonly ApiFactory _parentFactory;

        public CapturingLoggerProvider LogProvider { get; } = new();

        public BrokenSessionStoreFactory(ApiFactory parentFactory)
        {
            _parentFactory = parentFactory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _parentFactory.GetType()
                .GetMethod("ConfigureWebHost",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_parentFactory, [builder]);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionStore>();

                // Inner store times out (the degraded-Redis state #511 targets); the REAL
                // decorator translates it to the SessionStoreUnavailableException contract, so
                // this test exercises #511's translation AND #512's logging end-to-end.
                var innerTimingOut = Substitute.For<ISessionStore>();
                innerTimingOut.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
                    .Throws(new RedisTimeoutException("Timeout performing GET (5000ms)", CommandStatus.Sent));
                services.AddScoped<ISessionStore>(_ => new SessionStoreResilienceDecorator(innerTimingOut));

                services.AddSingleton<ILoggerProvider>(LogProvider);
            });
        }
    }
}
