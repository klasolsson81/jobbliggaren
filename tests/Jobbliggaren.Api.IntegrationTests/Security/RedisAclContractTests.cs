using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.Registration;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Landing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisAclContractTests(RedisBoundaryFixture fixture) : IClassFixture<RedisBoundaryFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string LandingKey = "jobbliggaren:landing:stats:v1";

    private RedisSessionStore Sessions(MutableFakeDateTimeProvider clock) =>
        new(RedisBoundaryFixture.Cache(fixture.Api), fixture.Api, clock, Options.Create(new SessionStoreOptions()));

    private RedisLoginChallengeStore Challenges() =>
        new(fixture.ChallengeAdapter, new EphemeralDataProtectionProvider(), NullLogger<RedisLoginChallengeStore>.Instance);

    [Fact]
    public async Task SessionStore_ApiIdentity_CreatesSlidesRotatesAndRevokes()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = FakeDateTimeProvider.Now.UtcNow };
        var store = Sessions(clock);
        var user = Guid.NewGuid();
        var session = await store.CreateAsync(user, SessionLifetime.Persistent, Ct);
        var key = SessionKey(session.Id);
        var db = fixture.PersistentAdmin.GetDatabase();
        var original = await db.HashGetAsync(key, "data");

        clock.UtcNow += TimeSpan.FromHours(25);
        (await store.GetAsync(session.Id, Ct)).ShouldNotBeNull().UserId.ShouldBe(user);
        (await db.HashGetAsync(key, "data")).ShouldNotBe(original);

        var rotated = await store.RotateAsync(session.Id, Ct);
        rotated.ShouldNotBeNull();
        (await store.GetAsync(rotated.NewId, Ct)).ShouldNotBeNull().UserId.ShouldBe(user);
        (await store.GetAsync(session.Id, Ct)).ShouldNotBeNull();
        var graceTtl = await db.KeyTimeToLiveAsync(key);
        graceTtl.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        (await store.GetAsync(session.Id, Ct)).ShouldNotBeNull();
        (await db.KeyTimeToLiveAsync(key)).ShouldNotBeNull().ShouldBeLessThanOrEqualTo(graceTtl.Value);
        (await store.InvalidateAsync(session.Id, Ct)).ShouldBeTrue();

        (await store.InvalidateAsync(rotated.NewId, Ct)).ShouldBeTrue();
        (await store.GetAsync(rotated.NewId, Ct)).ShouldBeNull();

        var first = await store.CreateAsync(user, SessionLifetime.Persistent, Ct);
        var second = await store.CreateAsync(user, SessionLifetime.Persistent, Ct);
        (await store.InvalidateAllForUserAsync(user, Ct)).ShouldBe(2);
        (await store.GetAsync(first.Id, Ct)).ShouldBeNull();
        (await store.GetAsync(second.Id, Ct)).ShouldBeNull();

        var reissued = await store.CreateAsync(user, SessionLifetime.Persistent, Ct);
        (await store.GetAsync(reissued.Id, Ct)).ShouldNotBeNull();
        await store.MarkUserDeletedAsync(user, Ct);
        (await store.GetAsync(reissued.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task LandingCache_WorkerWritesApiReads_RejectsOppositeDirections()
    {
        var expected = new LandingStatsDto(12, 3, false, FakeDateTimeProvider.Now.UtcNow);
        var writer = new RedisLandingStatsCache(RedisBoundaryFixture.Cache(fixture.Worker));
        var reader = new RedisLandingStatsCache(RedisBoundaryFixture.Cache(fixture.Api));
        await writer.SetAsync(expected, Ct);

        (await reader.GetAsync(Ct)).ShouldBe(expected);
        var control = fixture.PersistentAdmin.GetDatabase();
        (await control.HashGetAsync(LandingKey, "sldexp")).ToString().ShouldBe("-1");
        (await control.KeyTimeToLiveAsync(LandingKey)).ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);

        await DeniedAsync(() => fixture.Worker.GetDatabase().HashGetAsync(LandingKey, "data"));
        await DeniedAsync(() => fixture.Api.GetDatabase().HashSetAsync(LandingKey, [new HashEntry("data", "changed")]));
        await DeniedAsync(() => fixture.Api.GetDatabase().KeyExpireAsync(LandingKey, TimeSpan.FromSeconds(1)));
        (await reader.GetAsync(Ct)).ShouldBe(expected);
    }

    [Fact]
    public async Task SessionKeys_WorkerAttempts_CannotReadWriteDeleteOrChangeExpiry()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = FakeDateTimeProvider.Now.UtcNow };
        var store = Sessions(clock);
        var user = Guid.NewGuid();
        var session = await store.CreateAsync(user, SessionLifetime.Persistent, Ct);
        await store.InvalidateAllForUserAsync(user, Ct);
        session = await store.CreateAsync(user, SessionLifetime.Persistent, Ct);
        await store.MarkUserDeletedAsync(user, Ct);
        var keys = new[] { SessionKey(session.Id), $"jobbliggaren:user:{user}:sessions",
            $"jobbliggaren:user:{user}:revoked", $"jobbliggaren:user:{user}:deleted" };
        var admin = fixture.PersistentAdmin.GetDatabase();
        var worker = fixture.Worker.GetDatabase();

        foreach (var key in keys)
        {
            var before = await admin.KeyDumpAsync(key);
            before.ShouldNotBeNull();
            var ttl = await admin.KeyTimeToLiveAsync(key);
            await DeniedAsync(() => worker.HashGetAsync(key, "data"));
            await DeniedAsync(() => worker.HashSetAsync(key, [new HashEntry("data", "changed")]));
            await DeniedAsync(() => worker.KeyDeleteAsync(key));
            await DeniedAsync(() => worker.KeyExpireAsync(key, TimeSpan.FromSeconds(1)));
            (await admin.KeyDumpAsync(key)).ShouldBe(before);
            (await admin.KeyTimeToLiveAsync(key)).ShouldNotBeNull().ShouldBeGreaterThan(ttl!.Value - TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(CooldownScopes.ResendConfirm)]
    [InlineData(CooldownScopes.AccountExists)]
    [InlineData(CooldownScopes.PasswordReset)]
    public async Task Cooldown_ApiIdentity_AllowsOnlyItsRecordedScopes(string scope)
    {
        var gate = new RedisCooldownGate(RedisBoundaryFixture.Cache(fixture.Api));
        var subject = Guid.NewGuid() + "@example.com";
        (await gate.TryBeginAsync(scope, subject, TimeSpan.FromSeconds(60), Ct)).ShouldBeTrue();
        (await gate.TryBeginAsync(scope, subject, TimeSpan.FromSeconds(60), Ct)).ShouldBeFalse();
        var key = $"jobbliggaren:cd/{scope}/v1/{SubjectFingerprint.Hex(subject)}";
        await DeniedAsync(() => fixture.Worker.GetDatabase().HashGetAsync(key, "data"));
        await DeniedAsync(() => fixture.Worker.GetDatabase().KeyDeleteAsync(key));
    }

    [Fact]
    public async Task Challenges_ApiVolatileIdentity_ReplacesConsumesAndSurvivesScriptCacheLoss()
    {
        var store = Challenges();
        var email = Guid.NewGuid() + "@example.com";
        var old = ChallengeId.Generate();
        var oldCredentials = await store.PutAsync(new NewLoginChallenge(old, email, ChallengeCredentials.CodeAndLink, true), Ct);
        var current = ChallengeId.Generate();
        var credentials = await store.PutAsync(new NewLoginChallenge(current, email, ChallengeCredentials.CodeAndLink, true), Ct);
        (await store.ConsumeCodeAsync(old, oldCredentials.Code!.Value, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);

        var wrong = LoginCode.FromRaw(credentials.Code!.Value.Reveal() == "000000" ? "111111" : "000000");
        (await store.ConsumeCodeAsync(current, wrong, Ct)).Outcome.ShouldBe(ChallengeOutcome.Wrong);
        await fixture.VolatileAdmin.GetDatabase().ExecuteAsync("SCRIPT", "FLUSH");
        (await store.ConsumeCodeAsync(current, wrong, Ct)).Outcome.ShouldBe(ChallengeOutcome.Wrong);
        (await store.ConsumeCodeAsync(current, wrong, Ct)).Outcome.ShouldBe(ChallengeOutcome.Burned);
        (await store.ConsumeLinkAsync(credentials.Link!.Value, Ct)).ShouldNotBeNull().ProvenEmail.ShouldBe(email);
        (await store.ConsumeLinkAsync(credentials.Link.Value, Ct)).ShouldBeNull();

        var once = ChallengeId.Generate();
        var issued = await store.PutAsync(new NewLoginChallenge(once, email, ChallengeCredentials.CodeAndLink, true), Ct);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => store.ConsumeCodeAsync(once, issued.Code!.Value, Ct)));
        results.Count(x => x.IsVerified).ShouldBe(1);
        (await store.ConsumeLinkAsync(issued.Link!.Value, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task BoundChallenges_ApiVolatileIdentity_ReplaceConsumeAndAdmitOnlyTheirOwnCommands()
    {
        var store = Challenges();
        var owner = new ChallengeBinding(ChallengePurpose.Reauthentication, Guid.NewGuid());
        var email = Guid.NewGuid() + "@example.com";
        var old = ChallengeId.Generate();
        var oldCode = await store.PutBoundAsync(new NewBoundChallenge(old, email, owner), Ct);
        var current = ChallengeId.Generate();
        var code = await store.PutBoundAsync(new NewBoundChallenge(current, email, owner), Ct);

        (await store.ConsumeBoundCodeAsync(old, oldCode, owner, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        var wrong = LoginCode.FromRaw(code.Reveal() == "000000" ? "111111" : "000000");
        (await store.ConsumeBoundCodeAsync(current, wrong, owner, Ct)).Outcome.ShouldBe(ChallengeOutcome.Wrong);
        (await store.ConsumeBoundCodeAsync(current, code, owner, Ct)).IsVerified.ShouldBeTrue();

        var live = ChallengeId.Generate();
        await store.PutBoundAsync(new NewBoundChallenge(live, email, owner), Ct);
        var recordKey = RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(live));
        var indexKey = RedisLoginChallengeStore.BoundIndexKey(owner);
        var db = fixture.Challenge.GetDatabase();
        await DeniedAsync(() => db.HashGetAllAsync(recordKey));
        await DeniedAsync(() => db.StringGetAsync(indexKey));
        await DeniedAsync(() => db.KeyDeleteAsync(indexKey));
        (await fixture.VolatileAdmin.GetDatabase().KeyExistsAsync(recordKey)).ShouldBeTrue();
    }

    [Fact]
    public async Task Budgets_ApiVolatileIdentity_EnforcesEveryProductionScopeAndTtl()
    {
        var budget = new RedisRateBudget(fixture.ChallengeAdapter);
        var scopes = new[] { LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60)),
            LoginChallengePolicy.MailBudget, LoginChallengePolicy.CodeBudget, LoginChallengePolicy.UnknownAddressMailBudget,
            LoginChallengePolicy.ReauthCooldown(TimeSpan.FromSeconds(60)), LoginChallengePolicy.ReauthCodeBudget,
            ChangeEmailPolicy.UserCooldown(TimeSpan.FromSeconds(60)), ChangeEmailPolicy.TargetCooldown(TimeSpan.FromSeconds(60)),
            ChangeEmailPolicy.UserTargetsDailyBudget };
        foreach (var scope in scopes)
        {
            var subject = scope == LoginChallengePolicy.UnknownAddressMailBudget
                ? LoginChallengePolicy.UnknownAddressMailSubject : Guid.NewGuid() + "@example.com";
            var admitted = await Task.WhenAll(Enumerable.Range(0, scope.Limit + 3)
                .Select(_ => budget.TryConsumeAsync(scope, subject, Ct)));
            admitted.Count(x => x).ShouldBe(scope.Limit);
            var key = RedisRateBudget.Key(scope, subject);
            var before = await fixture.VolatileAdmin.GetDatabase().KeyTimeToLiveAsync(key);
            before.ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
            (await budget.TryConsumeAsync(scope, subject, Ct)).ShouldBeFalse();
            (await fixture.VolatileAdmin.GetDatabase().KeyTimeToLiveAsync(key)).ShouldNotBeNull()
                .ShouldBeLessThanOrEqualTo(before.Value);
        }
    }

    [Fact]
    public async Task GrantsAndClaims_ApiVolatileIdentity_IssueRedeemOnceAndClaimOnceWithTtl()
    {
        var grants = new RedisGrantStore(
            fixture.ChallengeAdapter, new EphemeralDataProtectionProvider(), NullLogger<RedisGrantStore>.Instance);
        var email = Guid.NewGuid() + "@example.com";
        var bearer = GrantAssertion.Bearer(GrantPurpose.LoginComplete);
        var admin = fixture.VolatileAdmin.GetDatabase();

        var token = await grants.IssueAsync(new GrantSubject.LoginComplete(email), Ct);
        (await admin.KeyTimeToLiveAsync(RedisGrantStore.Key(token))).ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
        (await grants.RedeemAsync(token, bearer, Ct)).ShouldBe(new GrantSubject.LoginComplete(email));
        (await grants.RedeemAsync(token, bearer, Ct)).ShouldBeNull();

        var claim = new RedisRegistrationClaim(fixture.ChallengeAdapter);
        (await claim.TryClaimAsync(email, Ct)).ShouldBeTrue();
        (await claim.TryClaimAsync(email, Ct)).ShouldBeFalse();
        (await admin.KeyTimeToLiveAsync(RedisRegistrationClaim.Key(email))).ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task GrantAndClaimKeys_ApiVolatileIdentity_AdmitOnlyTheirOwnCommands()
    {
        var grants = new RedisGrantStore(
            fixture.ChallengeAdapter, new EphemeralDataProtectionProvider(), NullLogger<RedisGrantStore>.Instance);
        var email = Guid.NewGuid() + "@example.com";
        var grantKey = RedisGrantStore.Key(await grants.IssueAsync(new GrantSubject.LoginComplete(email), Ct));
        (await new RedisRegistrationClaim(fixture.ChallengeAdapter).TryClaimAsync(email, Ct)).ShouldBeTrue();
        var claimKey = RedisRegistrationClaim.Key(email);
        var db = fixture.Challenge.GetDatabase();

        await DeniedAsync(() => db.StringGetAsync(grantKey));
        await DeniedAsync(() => db.KeyDeleteAsync(grantKey));
        await DeniedAsync(() => db.KeyExpireAsync(grantKey, TimeSpan.FromHours(1)));
        await DeniedAsync(() => db.ScriptEvaluateAsync("return redis.call('GET', KEYS[1])", [new RedisKey(grantKey)]));
        await DeniedAsync(() => db.StringGetAsync(claimKey));
        await DeniedAsync(() => db.StringGetDeleteAsync(claimKey));
        await DeniedAsync(() => db.KeyDeleteAsync(claimKey));

        var challengeKey = RedisLoginChallengeStore.RecordKey(RedisLoginChallengeStore.RecordSegment(ChallengeId.Generate()));
        await DeniedAsync(() => db.StringGetDeleteAsync(challengeKey));
        (await fixture.VolatileAdmin.GetDatabase().KeyExistsAsync(grantKey)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, RedisBoundaryFixture.ApiPersistent)]
    [InlineData(false, RedisBoundaryFixture.WorkerPersistent)]
    [InlineData(true, RedisBoundaryFixture.ApiVolatile)]
    public async Task ApplicationIdentity_AdministrativeAndUnknownOperations_AreDenied(bool isVolatile, string user)
    {
        // The adversarial client disables its local guard so Redis must make the authorization decision.
        var options = fixture.OptionsFor(isVolatile ? fixture.Volatile : fixture.Persistent, user);
        options.AllowAdmin = true;
        using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var db = connection.GetDatabase();
        foreach (var (command, arguments) in new (string, object[])[]
        {
            ("ACL", ["LIST"]), ("FLUSHALL", []), ("FLUSHDB", []), ("KEYS", ["*"]),
            ("SCAN", ["0"]), ("SAVE", []), ("MODULE", ["LIST"]), ("REPLICAOF", ["NO", "ONE"]),
        })
            await DeniedAsync(() => db.ExecuteAsync(command, arguments));
        await DeniedAsync(() => db.StringSetAsync("jobbliggaren:unregistered:v1:synthetic", "value"));
        await DeniedAsync(() => db.HashSetAsync("jobbliggaren:unregistered:v1:synthetic", [new HashEntry("data", "value")]));
    }

    [Theory]
    [InlineData(false, RedisBoundaryFixture.ApiVolatile)]
    [InlineData(true, RedisBoundaryFixture.ApiPersistent)]
    [InlineData(true, RedisBoundaryFixture.WorkerPersistent)]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    [InlineData(false, "default")]
    public async Task Connection_WrongStoreOrUnknownIdentity_RefusesAuthentication(bool isVolatile, string user)
    {
        var store = isVolatile ? fixture.Volatile : fixture.Persistent;
        var password = user is "unknown" or "default" ? RedisBoundaryFixture.NewPassword() : fixture.Password(user);
        await Should.ThrowAsync<RedisConnectionException>(() => ConnectionMultiplexer.ConnectAsync(fixture.OptionsFor(store, user, password)));
    }

    [Fact]
    public async Task LuaAndTransaction_VolatileIdentity_CannotCrossItsKeyBoundary()
    {
        // The fixture operator creates a sentinel outside the role's namespace for this adversarial test.
        const string key = "foreign:synthetic";
        var admin = fixture.VolatileAdmin.GetDatabase();
        await admin.StringSetAsync(key, "unchanged");
        var db = fixture.Challenge.GetDatabase();
        await DeniedAsync(() => db.ScriptEvaluateAsync("return redis.call('HGET', KEYS[1], 'p')", [new RedisKey(key)]));
        var hiddenKey = await Should.ThrowAsync<RedisServerException>(() =>
            db.ScriptEvaluateAsync("return redis.call('HGET', ARGV[1], 'p')", [], [new RedisValue(key)]));
        hiddenKey.Message.ShouldContain("ACL failure in script");
        var budgetScope = LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60));
        var subject = Guid.NewGuid() + "@example.com";
        (await new RedisRateBudget(fixture.ChallengeAdapter).TryConsumeAsync(budgetScope, subject, Ct)).ShouldBeTrue();
        var allowedKey = RedisRateBudget.Key(budgetScope, subject);
        var transaction = db.CreateTransaction();
        var allowed = transaction.StringIncrementAsync(allowedKey);
        var forbidden = transaction.StringIncrementAsync(key);
        await Should.ThrowAsync<RedisException>(() => transaction.ExecuteAsync());
        await Should.ThrowAsync<RedisException>(() => forbidden);
        await Should.ThrowAsync<RedisException>(() => allowed);
        (await admin.StringGetAsync(allowedKey)).ToString().ShouldBe("1");
        (await admin.StringGetAsync(key)).ToString().ShouldBe("unchanged");
        await DeniedAsync(() => fixture.Api.GetDatabase().ScriptEvaluateAsync("return 1"));
        await DeniedAsync(() => fixture.Worker.GetDatabase().ScriptEvaluateAsync("return 1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthProbe_ActualPolicy_RejectsAnonymousAndWrongCredentials(bool isVolatile)
    {
        var container = isVolatile ? fixture.Volatile : fixture.Persistent;
        var kind = isVolatile ? "volatile" : "persistent";
        var success = await container.ExecAsync(["sh", "/test/healthcheck.sh", "health-" + kind, "/test/health-password"], Ct);
        success.ExitCode.ShouldBe(0);
        var wrong = await container.ExecAsync(["sh", "/test/healthcheck.sh", "health-" + kind, "/test/wrong-password"], Ct);
        wrong.ExitCode.ShouldNotBe(0);
        (wrong.Stdout + wrong.Stderr).ShouldBeEmpty();
        var anonymous = await container.ExecAsync(["redis-cli", "-e", "PING"], Ct);
        anonymous.ExitCode.ShouldNotBe(0);
        (anonymous.Stdout + anonymous.Stderr).ShouldContain("NOAUTH");
    }

    [Fact]
    public async Task HealthProbe_WrongCredentialWithAnonymousFallback_RejectsMisleadingPong()
    {
        await using var isolated = new RedisBoundaryFixture();
        await isolated.InitializeAsync();
        await isolated.PersistentAdmin.GetDatabase().ExecuteAsync("ACL", "SETUSER", "default", "on", "nopass", "+ping");
        var result = await isolated.Persistent.ExecAsync(
            ["sh", "/test/healthcheck.sh", "health-persistent", "/test/wrong-password"], Ct);
        result.ExitCode.ShouldNotBe(0);
        (result.Stdout + result.Stderr).ShouldBeEmpty();
    }

    [Fact]
    public async Task Credentials_RotationAndRevocation_RejectsOldAndEstablishedClients()
    {
        await using var isolated = new RedisBoundaryFixture();
        await isolated.InitializeAsync();
        var admin = isolated.PersistentAdmin.GetDatabase();
        var replacement = RedisBoundaryFixture.NewPassword();
        await admin.ExecuteAsync("ACL", "SETUSER", RedisBoundaryFixture.WorkerPersistent, "#" + RedisBoundaryFixture.Hash(replacement));
        var next = await isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.WorkerPersistent, replacement);
        await next.GetDatabase().PingAsync();
        await admin.ExecuteAsync("ACL", "SETUSER", RedisBoundaryFixture.WorkerPersistent,
            "!" + RedisBoundaryFixture.Hash(isolated.Password(RedisBoundaryFixture.WorkerPersistent)));
        await admin.ExecuteAsync("CLIENT", "KILL", "USER", RedisBoundaryFixture.WorkerPersistent);
        await Should.ThrowAsync<RedisConnectionException>(() => isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.WorkerPersistent));
        await Should.ThrowAsync<RedisException>(() => isolated.Worker.GetDatabase().PingAsync());
        var reconnected = await isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.WorkerPersistent, replacement);
        await reconnected.GetDatabase().PingAsync();
        await admin.ExecuteAsync("ACL", "DELUSER", RedisBoundaryFixture.WorkerPersistent);
        await Should.ThrowAsync<RedisException>(() => reconnected.GetDatabase().PingAsync());
        await Should.ThrowAsync<RedisConnectionException>(() => isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.WorkerPersistent, replacement));

        // The fixture operator updates the mounted policy source as well as the live ACL.
        var revokedPolicy = string.Join('\n', isolated.RenderPolicy("persistent").Split('\n')
            .Where(line => !line.StartsWith("user worker-persistent ", StringComparison.Ordinal))) + "\n";
        await isolated.Persistent.CopyAsync(Encoding.UTF8.GetBytes(revokedPolicy), "/etc/redis/users.acl", ct: Ct);
        await isolated.Persistent.StopAsync(Ct);
        await isolated.Persistent.StartAsync(Ct);
        using var control = await isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.Admin);
        await control.GetDatabase().PingAsync();
        await Should.ThrowAsync<RedisConnectionException>(() => isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.WorkerPersistent));
        await Should.ThrowAsync<RedisConnectionException>(() => isolated.ConnectAsync(isolated.Persistent, RedisBoundaryFixture.WorkerPersistent, replacement));
    }

    private static string SessionKey(SessionId id) =>
        "jobbliggaren:session:" + Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(id.Reveal())));

    private static async Task DeniedAsync(Func<Task> operation)
    {
        var exception = await Should.ThrowAsync<RedisServerException>(operation);
        exception.Message.ShouldContain("NOPERM");
    }
}
