using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public class RegisterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_register_with_valid_data_returns_session_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"reg-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "Test User",
            acceptTerms = true,
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_register_with_an_unstorable_address_returns_400_in_swedish()
    {
        // The wire contract of Auth.EmailNotStorable: the status comes from the error's kind, and the detail
        // is the house's own Swedish text, never Identity's English one.
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $" pad-{Guid.NewGuid():N}@example.se",
            password = "T3stlosen123456",
            displayName = "Test User",
            acceptTerms = true,
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("title").GetString().ShouldBe(AuthErrorCodes.EmailNotStorable);
        var detail = problem.GetProperty("detail").GetString();
        detail.ShouldBe(AuthErrorCodes.EmailNotStorableMessage);

        // Independent of the constant above, as in the duplicate test below.
        detail!.ShouldStartWith("E-postadressen");
        detail.ShouldNotContain("Username");
    }

    [Fact]
    public async Task POST_register_with_duplicate_email_returns_400()
    {
        // #481 Low — a duplicate registration must not leak account existence. The 400 is collapsed
        // to a generic Auth.DuplicateAccount whose message names NEITHER the field NOR the submitted
        // address (vs Identity's raw English "Username 'x@y.z' is already taken", which echoed the
        // email). The residual 200-vs-400 status oracle inherent to instant-login registration is
        // deferred by design (closing it needs email-confirmation-first registration) and is
        // deliberately NOT asserted here — this pins only the message/code normalization.
        var ct = TestContext.Current.CancellationToken;
        var email = $"dup-{Guid.NewGuid()}@example.com";
        var body = new { email, password = "T3stlosen123456", displayName = "First User", acceptTerms = true };

        await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);
        var second = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        var title = problem.GetProperty("title").GetString();
        var detail = problem.GetProperty("detail").GetString();

        // Generic, non-enumerating code + message, single-sourced from AuthErrorCodes so the wire copy
        // cannot silently drift. title == Auth.DuplicateAccount also proves the raw Identity code
        // (DuplicateUserName / DuplicateEmail) never surfaced as "Auth.{code}".
        title.ShouldBe(AuthErrorCodes.DuplicateAccount);
        detail.ShouldBe(AuthErrorCodes.DuplicateAccountMessage);

        // The two enumeration guards, independent of the constants above: the response body must echo
        // neither the submitted address nor Identity's raw English "is already taken" wording. The
        // '!' is honest — the ShouldBe above already fails the test if detail is null.
        detail!.ShouldNotContain(email);
        detail!.ShouldNotContain("is already taken");
    }

    // ---------- #1736 (ADR 0142 D6) — the terms checkbox on the wire ----------

    [Fact]
    public async Task POST_register_with_accept_terms_false_returns_400_naming_the_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"noterms-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "Test User",
            acceptTerms = false,
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The FluentValidation shape — an `errors` dictionary keyed by field — rather than a
        // ProblemDetails title: an unticked checkbox is a request-shape rejection, and the field
        // name is what tells the form which control to mark.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("errors", out var errors).ShouldBeTrue(
            "an unaccepted checkbox is a validation rejection, not a domain refusal");
        errors.EnumerateObject().Select(p => p.Name).ShouldContain(
            name => name.Equals(nameof(RegisterCommand.AcceptTerms), StringComparison.OrdinalIgnoreCase),
            "the rejected field is the terms checkbox");
    }

    [Fact]
    public async Task POST_register_without_the_accept_terms_field_returns_400()
    {
        // Fail-closed by construction: a body that OMITS the field binds AcceptTerms to
        // default(bool) = false, which the validator refuses. That is why the parameter carries no
        // default of its own — an `= true` would let an omitting client register without accepting.
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"noterms-omitted-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "Test User",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Named as the AcceptTerms rule, which is what separates "the body bound and the validator
        // refused it" from "the body did not bind at all". Only the first is the fail-closed
        // property; a 400 alone would hold either way.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("errors", out var errors).ShouldBeTrue();
        errors.EnumerateObject().Select(p => p.Name).ShouldContain(
            name => name.Equals(nameof(RegisterCommand.AcceptTerms), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task POST_register_with_accept_terms_stamps_the_job_seeker_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"terms-{Guid.NewGuid()}@example.com";
        var body = new
        {
            email,
            password = "T3stlosen123456",
            displayName = "Terms User",
            acceptTerms = true,
        };

        var before = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);
        var after = DateTimeOffset.UtcNow;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        user.ShouldNotBeNull();
        var seeker = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .JobSeekers.SingleAsync(js => js.UserId == user.Id, ct);

        seeker.TermsAcceptance.ShouldNotBeNull();
        seeker.TermsAcceptance.AcceptedAt.ShouldNotBe(default);
        // A second of slack at each end: terms_accepted_at is timestamptz (microsecond resolution),
        // so a stamp taken inside the request can round just outside a tick-precision bracket.
        seeker.TermsAcceptance.AcceptedAt.ShouldBeInRange(before.AddSeconds(-1), after.AddSeconds(1));
        seeker.TermsAcceptance.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);
        seeker.TermsAcceptance.PrivacyPolicyVersion
            .ShouldBe(TermsAcceptance.CurrentPrivacyPolicyVersion);
    }
}
