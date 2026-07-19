using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The #746 PR-B / audit d2+d4 COUNTERFACTUAL: role resolution moved from an eager per-request
/// <c>IClaimsTransformation</c> to the on-demand <c>AdminRoleAuthorizationHandler</c>, so an authenticated
/// NON-admin request (the /oversikt fan-out; a 429'd flood) resolves ZERO roles, while an admin request
/// still resolves them (lazily, per request — immediate-revoke intact).
///
/// <para>
/// A test-only <c>IUserAccountService</c> decorator counts <c>GetRolesAsync</c> calls (the identity query
/// the audit targets). Under the OLD transformation every authenticated request would have counted ≥1;
/// the assertion of 0 for a non-admin request is what proves the perf win. Login / <c>/me</c> do NOT call
/// <c>IUserAccountService.GetRolesAsync</c> (login uses <c>ValidateCredentialsAsync</c> → the UserManager
/// directly; <c>/me</c> uses <c>GetAccountSummaryAsync</c>), so the only caller on these paths is the Admin
/// handler — making the count a clean signal.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class AdminRoleLazyResolutionCountTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly RoleResolutionCounter _counter = new();
    private readonly WebApplicationFactory<Program> _host;

    public AdminRoleLazyResolutionCountTests(ApiFactory factory)
    {
        _factory = factory;
        // Derived host with the counting decorator — its own instance so the count never races another
        // class (and [Collection("Api")] serializes execution). xUnit news this class per test method, so
        // this builds one host per method; the EF ManyServiceProvidersCreatedWarning is process-wide but
        // only a couple of derived hosts exist across the whole suite (this + the cached
        // email-confirmation one), far under the 20 threshold. The admin promote below uses
        // _factory.Services while requests go through _host — both share the collection fixture's Postgres.
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IUserAccountService>(sp =>
                new CountingUserAccountService(
                    ActivatorUtilities.CreateInstance<UserAccountService>(sp), _counter))));
    }

    public void Dispose() => _host.Dispose();

    private async Task<(HttpClient client, Guid userId)> RegisterAuthenticatedClientAsync(CancellationToken ct)
    {
        var client = _host.CreateClient();
        var email = $"count-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var userId = Guid.Parse(meJson.GetProperty("userId").GetString()!);
        return (client, userId);
    }

    [Fact]
    public async Task Non_admin_authenticated_request_resolves_zero_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterAuthenticatedClientAsync(ct);

        // Reset after the registration/login/first-/me noise, then hit a non-admin endpoint.
        Interlocked.Exchange(ref _counter.Value, 0);
        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();

        // The Admin policy is never evaluated on a non-admin endpoint → the handler never runs → 0 role
        // queries. Under the removed eager transformation this would have been ≥1.
        Volatile.Read(ref _counter.Value).ShouldBe(0);
    }

    [Fact]
    public async Task Admin_request_resolves_roles_lazily_and_authorizes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAuthenticatedClientAsync(ct);

        using (var scope = _factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (!await roleManager.RoleExistsAsync(Roles.Admin))
                (await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();
            var user = await userManager.FindByIdAsync(userId.ToString())
                ?? throw new InvalidOperationException("User not found.");
            (await userManager.AddToRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
        }

        Interlocked.Exchange(ref _counter.Value, 0);
        var response = await client.GetAsync("/api/v1/admin/audit-log?pageSize=1", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Volatile.Read(ref _counter.Value).ShouldBeGreaterThanOrEqualTo(1); // resolved lazily, on demand
    }

    private sealed class RoleResolutionCounter
    {
        public int Value;
    }

    // Delegates every IUserAccountService member to the real service; counts only GetRolesAsync.
    private sealed class CountingUserAccountService(IUserAccountService inner, RoleResolutionCounter counter)
        : IUserAccountService
    {
        public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct)
        {
            Interlocked.Increment(ref counter.Value);
            return inner.GetRolesAsync(userId, ct);
        }

        public Task<Result<Guid>> CreateUserAsync(string email, string password, CancellationToken ct)
            => inner.CreateUserAsync(email, password, ct);

        public Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
            => inner.ChangePasswordAsync(userId, currentPassword, newPassword, ct);

        public Task DeleteUserAsync(Guid userId, CancellationToken ct)
            => inner.DeleteUserAsync(userId, ct);

        public Task<Result<UserCredentials>> ValidateCredentialsAsync(string email, string password, CancellationToken ct)
            => inner.ValidateCredentialsAsync(email, password, ct);

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken ct)
            => inner.GetEmailAsync(userId, ct);

        public Task<AccountSummary?> GetAccountSummaryAsync(Guid userId, CancellationToken ct)
            => inner.GetAccountSummaryAsync(userId, ct);

        public Task<bool> IsEmailTakenAsync(string email, CancellationToken ct)
            => inner.IsEmailTakenAsync(email, ct);

        public Task<Result<string>> GenerateChangeEmailTokenAsync(Guid userId, string newEmail, CancellationToken ct)
            => inner.GenerateChangeEmailTokenAsync(userId, newEmail, ct);

        public Task<Result> ConfirmChangeEmailAsync(Guid userId, string newEmail, string urlSafeToken, CancellationToken ct)
            => inner.ConfirmChangeEmailAsync(userId, newEmail, urlSafeToken, ct);

        public Task<Result<string>> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken ct)
            => inner.GenerateEmailConfirmationTokenAsync(userId, ct);

        public Task<Result> ConfirmEmailAsync(Guid userId, string urlSafeToken, CancellationToken ct)
            => inner.ConfirmEmailAsync(userId, urlSafeToken, ct);

        public Task<EmailConfirmationResend?> TryPrepareEmailConfirmationResendAsync(string email, CancellationToken ct)
            => inner.TryPrepareEmailConfirmationResendAsync(email, ct);
    }
}
