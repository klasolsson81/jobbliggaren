using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.CompanyRegistry;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisBoundaryFailureTests(RedisBoundaryFixture fixture) : IClassFixture<RedisBoundaryFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompanyCache_ApiIdentity_CachesTheRealProvidersPublicResult()
    {
        var organization = OrganizationNumber.Create("5592804784").Value;
        var expected = CompanyRegistryLookup.Found(new CompanyRegistryEntry(organization.Value, "Synthetic company AB"));
        var inner = Substitute.For<ICompanyRegistry>();
        inner.LookupAsync(organization, Arg.Any<CancellationToken>()).Returns(expected);
        var cache = new CachedCompanyRegistry(inner, RedisBoundaryFixture.Cache(fixture.Api),
            Options.Create(new CompanyRegistryOptions()));

        (await cache.LookupAsync(organization, Ct)).ShouldBe(expected);
        (await cache.LookupAsync(organization, Ct)).Entry.ShouldBe(expected.Entry);
        await inner.Received(1).LookupAsync(organization, Arg.Any<CancellationToken>());
        var key = "jobbliggaren:company-registry:v1:" + organization.Value;
        (await fixture.PersistentAdmin.GetDatabase().KeyTimeToLiveAsync(key)).ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
        var denied = await Should.ThrowAsync<RedisServerException>(() => fixture.Worker.GetDatabase().HashGetAsync(key, "data"));
        denied.Message.ShouldContain("NOPERM");
    }

    [Theory]
    [InlineData(false, RedisBoundaryFixture.ApiPersistent)]
    [InlineData(false, RedisBoundaryFixture.WorkerPersistent)]
    [InlineData(true, RedisBoundaryFixture.ApiVolatile)]
    public async Task Connection_WrongPassword_RejectsEvenTheCorrectIdentity(bool isVolatile, string user)
    {
        var store = isVolatile ? fixture.Volatile : fixture.Persistent;
        await Should.ThrowAsync<RedisConnectionException>(() =>
            ConnectionMultiplexer.ConnectAsync(fixture.OptionsFor(store, user, RedisBoundaryFixture.NewPassword())));
    }

    [Theory]
    [InlineData(false, "health-persistent")]
    [InlineData(true, "health-volatile")]
    public async Task HealthIdentity_OnlyPing_CannotReadDataOrAdminister(bool isVolatile, string user)
    {
        var container = isVolatile ? fixture.Volatile : fixture.Persistent;
        foreach (var command in new[] { new[] { "GET", "jobbliggaren:landing:stats:v1" },
            ["HMGET", "jobbliggaren:landing:stats:v1", "data"], ["ACL", "LIST"] })
        {
            var result = await container.ExecAsync(["sh", "-c",
                "user=$1; shift; redis-cli -e --user \"$user\" --askpass \"$@\" < /test/health-password",
                "probe", user, .. command], Ct);
            result.ExitCode.ShouldNotBe(0);
            (result.Stdout + result.Stderr).ShouldContain("NOPERM");
        }
    }
    [Fact]
    public async Task ChallengeAdapter_Outage_FailsClosedAndReconnectsWithoutFallback()
    {
        await using var isolated = new RedisBoundaryFixture();
        await isolated.InitializeAsync();
        var store = new RedisLoginChallengeStore(isolated.Challenge, new EphemeralDataProtectionProvider(),
            NullLogger<RedisLoginChallengeStore>.Instance);
        var id = ChallengeId.Generate();
        var email = Guid.NewGuid() + "@example.com";
        var credentials = await store.PutAsync(new NewLoginChallenge(id, email, ChallengeCredentials.CodeAndLink, true), Ct);
        await isolated.Volatile.PauseAsync(Ct);
        var error = await Should.ThrowAsync<LoginChallengeStoreUnavailableException>(() =>
            store.ConsumeCodeAsync(ChallengeId.Generate(), credentials.Code!.Value, Ct));
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain(email);
        error.ToString().ShouldNotContain(credentials.Code!.Value.Reveal());
        error.ToString().ShouldNotContain(isolated.Password(RedisBoundaryFixture.ApiVolatile));

        await isolated.Volatile.UnpauseAsync(Ct);
        await isolated.Challenge.GetDatabase().PingAsync();
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        isolated.Challenge.ConnectionRestored += (_, _) => restored.TrySetResult();
        await isolated.VolatileAdmin.GetDatabase().ExecuteAsync("CLIENT", "KILL", "USER", RedisBoundaryFixture.ApiVolatile);
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        await isolated.Challenge.GetDatabase().PingAsync();
        (await store.ConsumeCodeAsync(id, credentials.Code!.Value, Ct)).IsVerified.ShouldBeTrue();
        var fresh = ChallengeId.Generate();
        var issued = await store.PutAsync(new NewLoginChallenge(fresh, email, ChallengeCredentials.CodeAndLink, true), Ct);
        (await store.ConsumeCodeAsync(fresh, issued.Code!.Value, Ct)).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task ChallengeAdapter_LostProtectionKey_LogsOnlyFailureType()
    {
        var original = new RedisLoginChallengeStore(fixture.Challenge, new EphemeralDataProtectionProvider(),
            NullLogger<RedisLoginChallengeStore>.Instance);
        var id = ChallengeId.Generate();
        var email = Guid.NewGuid() + "@example.com";
        var credentials = await original.PutAsync(new NewLoginChallenge(id, email, ChallengeCredentials.CodeAndLink, true), Ct);

        // Key-ring loss is a real recovery case; the reader must degrade safely without logging the payload.
        var logger = new RecordingLogger<RedisLoginChallengeStore>();
        var recovered = new RedisLoginChallengeStore(fixture.Challenge, new EphemeralDataProtectionProvider(), logger);
        (await recovered.ConsumeCodeAsync(id, credentials.Code!.Value, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        logger.Records.ShouldNotBeEmpty();
        foreach (var record in logger.Records)
        {
            record.Message.ShouldContain("CryptographicException");
            var rendered = record.Message + string.Join(" ", record.Properties.Select(x => x.Value));
            foreach (var secret in new[] { email, id.Reveal(), credentials.Code.Value.Reveal(),
                credentials.Link!.Value.Reveal(), fixture.Password(RedisBoundaryFixture.ApiVolatile) })
                rendered.ShouldNotContain(secret);
        }
    }

    [Fact]
    public async Task Ping_WhenDataPermissionChanges_DoesNotAttestTheAcl()
    {
        await using var isolated = new RedisBoundaryFixture();
        await isolated.InitializeAsync();
        await isolated.VolatileAdmin.GetDatabase().ExecuteAsync("ACL", "SETUSER", RedisBoundaryFixture.ApiVolatile, "clearselectors");
        await isolated.Challenge.GetDatabase().PingAsync();
        var store = new RedisLoginChallengeStore(isolated.Challenge, new EphemeralDataProtectionProvider(),
            NullLogger<RedisLoginChallengeStore>.Instance);
        await Should.ThrowAsync<LoginChallengeStoreUnavailableException>(() =>
            store.PutAsync(new NewLoginChallenge(ChallengeId.Generate(), "synthetic@example.com", ChallengeCredentials.CodeAndLink, true), Ct));
    }
}
