using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1737 (ADR 0142 D3, security Major 11) — what the password surface answers an account that has no
/// password. Such an account is what <see cref="AuthTestHelpers.RegisterAndGetSessionIdAsync"/> creates
/// (<c>UserManager.CreateAsync(user)</c>, no password), the shape 1a's first inbox proof and 1c's
/// <c>complete</c> both leave behind.
/// </summary>
[Collection("Api")]
public class PasswordlessAccountPasswordSurfaceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // Mirrors opts.Lockout.MaxFailedAccessAttempts (DependencyInjection).
    private const int MaxFailedAttempts = 5;

    private sealed record IdentityRow(int AccessFailedCount, DateTimeOffset? LockoutEnd, string? PasswordHash);

    private Task<HttpResponseMessage> LoginAsync(string email, string password, CancellationToken ct) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, ct);

    private Task<HttpResponseMessage> ForgotAsync(string email, CancellationToken ct) =>
        _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email }, ct);

    private int ResetMailCount(string email) =>
        _factory.Emails.Sent.Count(e => e.ToEmail == email && e.Kind == RecordedEmailKind.PasswordReset);

    private async Task<IdentityRow> IdentityRowOfAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(email)).ShouldNotBeNull();
        return new IdentityRow(user.AccessFailedCount, user.LockoutEnd, user.PasswordHash);
    }

    // The problem body minus the per-request trace id, so two responses can be compared whole.
    private static async Task<string> ComparableBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return string.Join(
            "|",
            json.EnumerateObject()
                .Where(p => p.Name != "traceId")
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value.GetRawText()}"));
    }

    [Fact]
    public async Task A_password_login_answers_a_passwordless_account_like_an_unknown_address_and_counts_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var passwordless = $"nopass-login-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, passwordless, ct: ct);
        (await IdentityRowOfAsync(passwordless)).PasswordHash.ShouldBeNull();

        HttpResponseMessage last = null!;
        for (var i = 0; i < MaxFailedAttempts + 2; i++)
            last = await LoginAsync(passwordless, "WrongPwd-123456", ct); // gitleaks:allow

        var unknown = await LoginAsync($"nobody-{Guid.NewGuid()}@example.se", "WrongPwd-123456", ct); // gitleaks:allow
        last.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ComparableBodyAsync(last, ct)).ShouldBe(await ComparableBodyAsync(unknown, ct));

        var row = await IdentityRowOfAsync(passwordless);
        row.AccessFailedCount.ShouldBe(0);
        row.LockoutEnd.ShouldBeNull();

        // The control: one wrong password against an account that HAS one is counted, so the zero above is
        // the gate's and not a counter that never moves on this host.
        var withPassword = $"haspass-login-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterWithPasswordAndGetSessionIdAsync(_factory, withPassword, ct: ct);
        await LoginAsync(withPassword, "WrongPwd-123456", ct); // gitleaks:allow
        (await IdentityRowOfAsync(withPassword)).AccessFailedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Forgot_password_mails_a_passwordless_account_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var passwordless = $"nopass-forgot-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, passwordless, ct: ct);

        (await ForgotAsync(passwordless, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // The channel is FIFO with one reader (ForgotPasswordTests.DrainDispatchAsync): a password account's
        // mail, requested AFTER the subject, arriving proves the subject was handled.
        var sentinel = $"haspass-forgot-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterWithPasswordAndGetSessionIdAsync(_factory, sentinel, ct: ct);
        (await ForgotAsync(sentinel, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ResetMailCount(sentinel) == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never delivered the sentinel");
            await Task.Delay(25, ct);
        }

        ResetMailCount(passwordless).ShouldBe(0);
    }

    /// <summary>
    /// UNREACHABLE STATE, declared: a VALID reset token for an account with no password. No path in
    /// <c>src/</c> produces it — every password removal (<c>UserManager.RemovePasswordAsync</c> in 1a's first
    /// inbox proof) rotates the security stamp, which kills outstanding tokens. The token is therefore minted
    /// by hand through <c>UserManager</c>, and the test asserts only that the read side degrades safely if a
    /// future removal path forgets the rotation.
    /// </summary>
    [Fact]
    public async Task A_valid_reset_token_cannot_give_a_passwordless_account_a_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"nopass-reset-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        Guid uid;
        string urlSafeToken;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email)).ShouldNotBeNull();
            uid = user.Id;
            urlSafeToken = Base64Url.EncodeToString(
                Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        }

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new { uid, token = urlSafeToken, newPassword = AuthTestHelpers.DefaultTestPassword },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.InvalidPasswordResetToken");
        (await IdentityRowOfAsync(email)).PasswordHash.ShouldBeNull();
    }
}
