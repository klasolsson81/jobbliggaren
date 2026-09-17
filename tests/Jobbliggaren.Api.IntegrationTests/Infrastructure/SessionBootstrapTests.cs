using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// ADR 0142 D9 amendment 2026-09-17 — the two session bootstraps in <see cref="AuthTestHelpers"/>
/// reach no registration endpoint, and each produces its own account shape. Both run against the
/// registrations-CLOSED host, whose <c>POST /api/v1/auth/register</c> refuses (the counterfactual is
/// <see cref="Auth.RegistrationsClosedTests"/>), so a session that authenticates there was minted
/// without that endpoint.
/// </summary>
[Collection("Api")]
public sealed class SessionBootstrapTests(ApiFactory factory)
{
    [Fact]
    public async Task RegisterAndGetSessionIdAsync_on_the_closed_host_mints_a_persistent_session_for_a_confirmed_passwordless_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var host = factory.RegistrationsClosedHost();
        var email = $"bootstrap-passwordless-{Guid.NewGuid():N}@example.se";

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(host, email, ct: ct);

        await AssertAuthenticatesAsync(host, sessionId, ct);
        var (user, session) = await ReadStateAsync(host, email, sessionId, ct);
        user.PasswordHash.ShouldBeNull();
        user.EmailConfirmed.ShouldBeTrue();
        session.Lifetime.ShouldBe(SessionLifetime.Persistent);
    }

    [Fact]
    public async Task RegisterWithPasswordAndGetSessionIdAsync_on_the_closed_host_mints_a_session_profile_session_for_an_unconfirmed_user_with_a_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var host = factory.RegistrationsClosedHost();
        var email = $"bootstrap-password-{Guid.NewGuid():N}@example.se";

        var sessionId = await AuthTestHelpers.RegisterWithPasswordAndGetSessionIdAsync(host, email, ct: ct);

        await AssertAuthenticatesAsync(host, sessionId, ct);
        var (user, session) = await ReadStateAsync(host, email, sessionId, ct);
        user.PasswordHash.ShouldNotBeNull();
        user.EmailConfirmed.ShouldBeFalse();
        session.Lifetime.ShouldBe(SessionLifetime.Session);
    }

    private static async Task AssertAuthenticatesAsync(
        WebApplicationFactory<Program> host, string sessionId, CancellationToken ct)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync("/api/v1/me", ct);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<(ApplicationUser User, Session Session)> ReadStateAsync(
        WebApplicationFactory<Program> host, string email, string sessionId, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(email);
        user.ShouldNotBeNull();

        var session = await scope.ServiceProvider.GetRequiredService<ISessionStore>()
            .GetAsync(SessionId.FromRaw(sessionId), ct);
        session.ShouldNotBeNull();

        return (user, session);
    }
}
