using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1737 (ADR 0142 D3) — what proving a NEW address leads to, through the real routes. The base host has
/// registration open (<see cref="ApiFactory"/>); the kill-switch is thrown on that same host, because each
/// host keeps its own keyring and a challenge minted on one cannot be opened on another.
/// </summary>
[Collection("Api")]
public class LoginChallengeNewAddressTests(ApiFactory factory)
{
    private const string GrantKeys = "jobbliggaren:auth/grant/*";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"na-{label}-{Guid.NewGuid():N}@example.se";

    private List<RecordedLoginChallenge> MailsTo(string email) =>
        _factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList();

    private async Task<(string ChallengeId, LoginChallengeEmail Mail)> MintAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (MailsTo(email).Count == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        return (body.RootElement.GetProperty("challengeId").GetString()!, MailsTo(email).Single().Content);
    }

    private async Task<JsonElement> VerifiedAsync(string challengeId, string code)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<HashSet<string>> KeysAsync(string connectionString, string pattern)
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        return mux.GetServer(mux.GetEndPoints().Single()).Keys(pattern: pattern).Select(k => k.ToString()).ToHashSet();
    }

    private async Task<bool> HasIdentityRowAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(email) is not null;
    }

    [Fact]
    public async Task A_code_proven_new_address_gets_a_grant_on_the_volatile_instance_and_no_account_yet()
    {
        var email = NewAddress("consent");
        var before = await KeysAsync(_factory.VolatileRedisConnectionString, GrantKeys);
        var (challengeId, mail) = await MintAsync(email);
        var code = mail.ShouldBeOfType<LoginChallengeEmail.NewAccountCode>().Code.Reveal();

        var body = await VerifiedAsync(challengeId, code);

        body.GetProperty("outcome").GetString().ShouldBe("consentRequired");
        body.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["grantToken", "outcome"]);
        var token = body.GetProperty("grantToken").GetString().ShouldNotBeNull();

        // Nothing durable is written before the terms are accepted (ADR 0142 D3, security Major 6).
        (await HasIdentityRowAsync(email)).ShouldBeFalse();

        // One new grant key, on the instance that cannot persist, under a name that is not the token.
        var written = (await KeysAsync(_factory.VolatileRedisConnectionString, GrantKeys)).Except(before).ToList();
        written.ShouldHaveSingleItem().ShouldNotContain(token);
        (await KeysAsync(_factory.DurableRedisConnectionString, "*")).ShouldNotContain(k => k.Contains("auth/grant"));
    }

    [Fact]
    public async Task The_kill_switch_thrown_between_mint_and_proof_is_honoured_and_no_grant_is_written()
    {
        var email = NewAddress("switch");
        var (challengeId, mail) = await MintAsync(email);
        var code = mail.ShouldBeOfType<LoginChallengeEmail.NewAccountCode>().Code.Reveal();
        var before = await KeysAsync(_factory.VolatileRedisConnectionString, GrantKeys);

        // The flag moves only with a restart, which hands every reader of IOptions<AuthOptions> one new value
        // while redis-volatile keeps the record. Setting that one instance is the same state; the finally
        // restores it for the rest of the collection, which runs serially.
        var flags = _factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        JsonElement body;
        try
        {
            flags.RegistrationsOpen = false;
            body = await VerifiedAsync(challengeId, code);
        }
        finally
        {
            flags.RegistrationsOpen = true;
        }

        body.GetProperty("outcome").GetString().ShouldBe("registrationClosed");
        (await KeysAsync(_factory.VolatileRedisConnectionString, GrantKeys)).Except(before).ShouldBeEmpty();
        (await HasIdentityRowAsync(email)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_identity_row_without_a_profile_is_mailed_a_code_and_then_told_it_is_unavailable()
    {
        // The orphan the registration path leaves when the Identity user committed and the profile did not
        // (#1349; OrphanedIdentityActivationTests enumerates its producers). The plan groups it with an
        // address that has no account, so it is mailed a code; the proof never adopts it.
        var email = NewAddress("orphan");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<IUserAccountService>()
                .CreateUserAsync(email, Helpers.AuthTestHelpers.DefaultTestPassword, Ct);
            created.IsSuccess.ShouldBeTrue();
        }

        var before = await KeysAsync(_factory.VolatileRedisConnectionString, GrantKeys);
        var (challengeId, mail) = await MintAsync(email);
        var code = mail.ShouldBeOfType<LoginChallengeEmail.NewAccountCode>().Code.Reveal();

        var body = await VerifiedAsync(challengeId, code);

        body.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["outcome"]);
        (await KeysAsync(_factory.VolatileRedisConnectionString, GrantKeys)).Except(before).ShouldBeEmpty();

        await using var read = _factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<IAppDbContext>();
        var userId = (await read.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(email)).ShouldNotBeNull().Id;
        (await db.JobSeekers.IgnoreQueryFilters().AnyAsync(js => js.UserId == userId, Ct)).ShouldBeFalse();
    }
}
