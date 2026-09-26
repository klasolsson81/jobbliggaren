using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// ADR 0142 D9 amendment 2026-09-17 — the session bootstrap in <see cref="AuthTestHelpers"/> reaches no
/// registration endpoint. It runs against the registrations-CLOSED host, whose
/// <c>POST /api/v1/auth/challenge/complete</c> refuses a new account (the counterfactual is
/// <see cref="Auth.LoginChallengeCompleteTests"/>), so a session that authenticates there was minted without
/// that endpoint.
/// </summary>
[Collection("Api")]
public sealed class SessionBootstrapTests(ApiFactory factory)
{
    [Fact]
    public async Task RegisterAndGetSessionIdAsync_on_the_closed_host_mints_a_persistent_session_for_a_confirmed_passwordless_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var host = factory.GetRegistrationsClosedHost();
        var email = $"bootstrap-passwordless-{Guid.NewGuid():N}@example.se";

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(host, email, ct: ct);

        await AssertAuthenticatesAsync(host, sessionId, ct);
        var (user, session) = await ReadStateAsync(host, email, sessionId, ct);
        user.PasswordHash.ShouldBeNull();
        user.EmailConfirmed.ShouldBeTrue();
        session.Lifetime.ShouldBe(SessionLifetime.Persistent);
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
        session.UserId.ShouldBe(user.Id);

        (await scope.ServiceProvider.GetRequiredService<IAppDbContext>().JobSeekers
            .AsNoTracking()
            .AnyAsync(js => js.UserId == user.Id, ct))
            .ShouldBeTrue();

        return (user, session);
    }
}
