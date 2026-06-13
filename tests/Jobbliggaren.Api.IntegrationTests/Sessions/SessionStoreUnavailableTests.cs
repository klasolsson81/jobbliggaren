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
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

/// <summary>
/// Verifierar att SessionStoreUnavailableException returnerar 503, inte 401 eller 500.
/// Säkerhetskrav: infrastrukturincident ska inte se ut som autentiseringsfel (ADR 0017 Turn 4).
/// </summary>
[Collection("Api")]
public class SessionStoreUnavailableTests(ApiFactory factory)
{
    [Fact]
    public async Task GET_me_when_session_store_unavailable_returns_503_not_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Registrera en giltig session via fungerande store
        var goodClient = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(goodClient, ct: ct);

        // Bygg en ny factory-instans med mockad ISessionStore som kastar
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
    public async Task GET_me_when_session_store_unavailable_does_not_return_401()
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

    private sealed class BrokenSessionStoreFactory : WebApplicationFactory<Program>
    {
        private readonly ApiFactory _parentFactory;

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

                var broken = Substitute.For<ISessionStore>();
                var fakeInner = new InvalidOperationException("Redis är nere (test-stub).");
                broken.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
                    .Throws(new SessionStoreUnavailableException("Redis är nere (test-stub).", fakeInner));
                services.AddScoped<ISessionStore>(_ => broken);
            });
        }
    }
}
