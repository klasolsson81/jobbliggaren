using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

public sealed class RedisSessionGenerationTests(SharedPlainRedisFixture redis) : IAsyncLifetime, IClassFixture<SharedPlainRedisFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MutableFakeDateTimeProvider _clock = new();
    private RedisCache _cache = null!;
    private ConnectionMultiplexer _connection = null!;
    private RedisSessionStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await redis.FlushAsync();
        _cache = new RedisCache(Options.Create(new RedisCacheOptions
        {
            Configuration = redis.ConnectionString,
            InstanceName = "jobbliggaren:",
        }));
        _connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        _store = new RedisSessionStore(_cache, _connection, _clock, Options.Create(new SessionStoreOptions
        {
            Persistent = new SessionLifetimeProfile
            {
                SlidingTtl = TimeSpan.FromDays(30),
                AbsoluteTtl = TimeSpan.FromDays(180),
                RotationInterval = TimeSpan.FromHours(24),
            },
        }));
    }

    public async ValueTask DisposeAsync()
    {
        _cache.Dispose();
        await _connection.DisposeAsync();
    }

    private async Task<Session> CreateAsync(Guid userId, long revision) =>
        (await _store.CreateAsync(userId, new AccountAccessProof(revision, userId, revision),
            SessionLifetime.Persistent, Ct)).ShouldNotBeNull();

    private static string Member(SessionId id, bool legacy = false) =>
        $"session:{(legacy ? "" : "v2:")}" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(id.Reveal())))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<JsonObject> PayloadAsync(SessionId id, bool legacy = false) =>
        JsonNode.Parse((await _cache.GetStringAsync(Member(id, legacy), Ct)).ShouldNotBeNull())
            .ShouldNotBeNull().AsObject();

    private Task WriteAsync(SessionId id, JsonObject payload, bool legacy = false) =>
        _cache.SetStringAsync(Member(id, legacy), payload.ToJsonString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) }, Ct);

    // The retired writer omitted both fields. This seam first pins that today's writer emits strict v2.
    private async Task<Session> LegacyAsync(Guid userId)
    {
        var current = await CreateAsync(userId, 0);
        var payload = await PayloadAsync(current.Id);
        payload["version"].ShouldNotBeNull().GetValue<int>().ShouldBe(2);
        payload["accessRevision"].ShouldNotBeNull().GetValue<long>().ShouldBe(0);
        payload.Remove("version").ShouldBeTrue();
        payload.Remove("accessRevision").ShouldBeTrue();
        await WriteAsync(current.Id, payload, legacy: true);
        await _cache.RemoveAsync(Member(current.Id), Ct);
        var db = _connection.GetDatabase();
        (await db.SetRemoveAsync($"jobbliggaren:user:{userId}:sessions", Member(current.Id))).ShouldBeTrue();
        (await db.SetAddAsync($"jobbliggaren:user:{userId}:sessions", Member(current.Id, legacy: true))).ShouldBeTrue();
        return current;
    }

    [Fact]
    public async Task CreateAsync_ShouldWriteStrictV2AndOriginalRevision_WhenProofCarriesAccountGeneration()
    {
        var session = await CreateAsync(Guid.NewGuid(), 2);
        var payload = await PayloadAsync(session.Id);

        payload["version"].ShouldNotBeNull().GetValue<int>().ShouldBe(2);
        payload["accessRevision"].ShouldNotBeNull().GetValue<long>().ShouldBe(2);
        (await _cache.GetStringAsync(Member(session.Id, legacy: true), Ct)).ShouldBeNull();
        (await _store.GetAsync(session.Id, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(2);
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("missing-revision")]
    [InlineData("wrong-version")]
    [InlineData("negative-revision")]
    public async Task GetAsync_ShouldRefuseMalformedV2WithoutLegacyFallback_WhenCurrentKeyExists(string corruption)
    {
        var legacy = await LegacyAsync(Guid.NewGuid());
        var current = (await PayloadAsync(legacy.Id, legacy: true)).DeepClone().AsObject();
        current["version"] = 2;
        current["accessRevision"] = 0;
        // Declared unreachable writer corruption: only fail-closed read behavior is asserted.
        switch (corruption)
        {
            case "missing-version": current.Remove("version"); break;
            case "missing-revision": current.Remove("accessRevision"); break;
            case "wrong-version": current["version"] = 1; break;
            case "negative-revision": current["accessRevision"] = -1; break;
        }
        await WriteAsync(legacy.Id, current);

        (await _store.GetAsync(legacy.Id, Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("accessRevision")]
    public async Task GetAsync_ShouldRefusePretendedLegacy_WhenANewGenerationFieldIsPresent(string field)
    {
        var legacy = await LegacyAsync(Guid.NewGuid());
        var payload = await PayloadAsync(legacy.Id, legacy: true);
        // Neither writer emits this mixed shape; the safe reader must not treat explicit zero as absence.
        payload[field] = 0;
        await WriteAsync(legacy.Id, payload, legacy: true);

        (await _store.GetAsync(legacy.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldMigrateGenuineLegacyAndKeepGraceReadable_WhenGenerationIsZero()
    {
        var legacy = await LegacyAsync(Guid.NewGuid());
        (await _store.GetAsync(legacy.Id, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(0);
        _clock.UtcNow = _clock.UtcNow.AddHours(25);

        var rotation = (await _store.RotateAsync(legacy.Id, Ct)).ShouldNotBeNull();

        (await _store.GetAsync(rotation.NewId, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(0);
        (await PayloadAsync(rotation.NewId))["version"].ShouldNotBeNull().GetValue<int>().ShouldBe(2);
        (await _store.GetAsync(legacy.Id, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(0);
        var grace = await PayloadAsync(legacy.Id, legacy: true);
        grace.ContainsKey("version").ShouldBeFalse();
        grace.ContainsKey("accessRevision").ShouldBeFalse();
    }

    [Fact]
    public async Task RotateAsync_ShouldPreserveAccountRevision_WhenCurrentSessionRotates()
    {
        var session = await CreateAsync(Guid.NewGuid(), 2);
        _clock.UtcNow = _clock.UtcNow.AddHours(25);

        var rotation = (await _store.RotateAsync(session.Id, Ct)).ShouldNotBeNull();

        (await _store.GetAsync(rotation.NewId, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(2);
    }

    [Fact]
    public async Task InvalidateBeforeRevisionAsync_ShouldRemoveOldAndLegacyOnly_WhenDelayedCleanupFindsNewSessions()
    {
        var user = Guid.NewGuid();
        var old = await CreateAsync(user, 0);
        var legacy = await LegacyAsync(user);
        var fresh = await CreateAsync(user, 2);

        (await _store.InvalidateBeforeRevisionAsync(user, 1, Ct)).ShouldBe(2);

        (await _store.GetAsync(old.Id, Ct)).ShouldBeNull();
        (await _store.GetAsync(legacy.Id, Ct)).ShouldBeNull();
        (await _store.GetAsync(fresh.Id, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(2);
    }
}
