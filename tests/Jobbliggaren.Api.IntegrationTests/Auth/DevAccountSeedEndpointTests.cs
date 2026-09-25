using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// POST /api/v1/dev/accounts, the Development seed seam (ADR 0142 part 5a), on the shared Development host. The
/// account it opens is written by <see cref="AccountRegistrar"/>, the writer <c>complete</c> uses, and the
/// proof that it is a state production produces is the login's own classification:
/// <see cref="LoginSubjectResolver"/> resolves it to <see cref="LoginSubject.Active"/>, and a code login signs in.
/// </summary>
[Collection("Api")]
public class DevAccountSeedEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ReservedAddress(string label) => $"seed-{label}-{Guid.NewGuid():N}@seed.jobbliggaren.test";

    private Task<HttpResponseMessage> SeedAsync(string? email) =>
        _client.PostAsJsonAsync("/api/v1/dev/accounts", new { email }, Ct);

    private async Task<ApplicationUser?> UserOfAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<LoginSubject> ResolveAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LoginSubjectResolver>().ResolveAsync(email, Ct);
    }

    [Fact]
    public async Task A_reserved_address_gets_an_account_the_login_resolves_Active_and_a_code_login_signs_it_in()
    {
        var email = ReservedAddress("arc");

        var seeded = await SeedAsync(email);

        seeded.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await seeded.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();
        (await ResolveAsync(email)).ShouldBeOfType<LoginSubject.Active>();
        _factory.Emails.LoginChallenges.ShouldNotContain(m => m.ToEmail == email);
        _factory.Emails.Sent.ShouldNotContain(m => m.ToEmail == email);

        var requested = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var challengeId = (await requested.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("challengeId").GetString()!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!_factory.Emails.LoginChallenges.Any(m => m.ToEmail == email))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        var mail = _factory.Emails.LoginChallenges.Single(m => m.ToEmail == email)
            .Content.ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();
        var verified = await _client.PostAsJsonAsync(
            "/api/v1/auth/challenge/verify", new { challengeId, code = mail.Code.Reveal() }, Ct);

        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await verified.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("outcome").GetString()
            .ShouldBe("signedIn");
    }

    [Fact]
    public async Task The_seeded_account_is_the_state_complete_writes()
    {
        var email = ReservedAddress("parity");

        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Field for field what LoginChallengeCompleteTests pins for a registration through the code flow.
        var user = (await UserOfAsync(email)).ShouldNotBeNull();
        user.PasswordHash.ShouldBeNull();
        user.EmailConfirmed.ShouldBeTrue();
        user.UserName.ShouldBe(email);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == user.Id, Ct);
        profile.TermsAcceptance.ShouldNotBeNull();
        profile.TermsAcceptance.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);
        profile.TermsAcceptance.PrivacyPolicyVersion.ShouldBe(TermsAcceptance.CurrentPrivacyPolicyVersion);
        (await db.AuditLogEntries.AsNoTracking().CountAsync(
            e => e.AggregateId == user.Id && e.EventType == AccountRegistrar.AccountCreatedAuditEventType, Ct))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Seeding_an_address_twice_leaves_one_account_and_writes_nothing_more()
    {
        var email = ReservedAddress("twice");
        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var normalized = users.NormalizeEmail(email);
        (await users.Users.CountAsync(u => u.NormalizedEmail == normalized, Ct)).ShouldBe(1);
        var userId = (await UserOfAsync(email)).ShouldNotBeNull().Id;
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking().CountAsync(
            e => e.AggregateId == userId && e.EventType == AccountRegistrar.AccountCreatedAuditEventType, Ct))
            .ShouldBe(1);
    }

    [Fact]
    public async Task An_address_at_a_domain_that_could_be_a_mailbox_answers_404_and_nothing_is_written()
    {
        var email = $"seed-real-{Guid.NewGuid():N}@example.se";

        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await UserOfAsync(email)).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_address_answers_400(string? email)
    {
        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_identity_row_without_a_profile_answers_409_and_stays_without_one()
    {
        // The state CompleteLoginChallengeCommandHandler leaves when its save throws after the Identity write
        // committed, and AccountHardDeleter's step 2h, which deletes the profile before the Identity row.
        var email = ReservedAddress("orphan");
        Guid userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            userId = (await scope.ServiceProvider.GetRequiredService<IPasswordlessAccountCreator>()
                .CreatePasswordlessUserAsync(email, Ct)).Value;
        }

        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var check = _factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.IgnoreQueryFilters()
            .AnyAsync(js => js.UserId == userId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_profile_pending_deletion_answers_409_and_keeps_its_deletion_date()
    {
        // JobSeeker.SoftDelete is the actor: the delete-account flow calls it to open the restore window.
        var email = ReservedAddress("pending");
        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var userId = (await UserOfAsync(email)).ShouldNotBeNull().Id;
        DateTimeOffset? deletedAt;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profile = await db.JobSeekers.SingleAsync(js => js.UserId == userId, Ct);
            profile.SoftDelete(scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
            await db.SaveChangesAsync(Ct);
        }

        await using (var read = _factory.Services.CreateAsyncScope())
        {
            // Read back as stored: Postgres keeps microseconds, so the in-memory value would not compare equal.
            deletedAt = (await read.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.IgnoreQueryFilters()
                .AsNoTracking().SingleAsync(js => js.UserId == userId, Ct)).DeletedAt;
        }

        deletedAt.ShouldNotBeNull();

        (await SeedAsync(email)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var check = _factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(js => js.UserId == userId, Ct)).DeletedAt.ShouldBe(deletedAt);
    }
}
