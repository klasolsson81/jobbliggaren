using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.Observability;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Logging;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Observability;

/// <summary>
/// Unit tests for <see cref="StoreUnavailableLog"/> (#512, epic #484; store-aware since #1735): it must emit
/// exactly one dedicated-event-id Error record per outage window AND STORE (throttled), carry ONLY the inner
/// Redis exception's TYPE — never its message, which can embed the operated key and with it a raw userId
/// (§5 / GDPR Art. 5(1)(c) data-minimisation) — and use the <c>store_unavailable</c> event_name the TD-77
/// alarm keys on. Every emit goes through a real exception's <c>Store</c> and <c>InnerType</c>, the way
/// <c>Program.cs</c> calls it.
/// </summary>
public class StoreUnavailableLogTests
{
    private static (StoreUnavailableLog Log, CapturingLoggerProvider Provider, ILoggerFactory Factory) Build()
    {
        var provider = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        // The caller disposes the factory (via `using var _ = factory`) so it stays alive across
        // the Emit + assertions — disposing it here would log against a disposed factory.
        return (new StoreUnavailableLog(factory.CreateLogger<StoreUnavailableLog>()), provider, factory);
    }

    private static SessionStoreUnavailableException SessionOutage(Exception inner) =>
        new("Redis-session-store är inte tillgänglig.", inner);

    private static void Emit(StoreUnavailableLog log, StoreUnavailableException outage) =>
        log.Emit(outage.Store, outage.InnerType);

    [Fact]
    public void Emit_LogsErrorWithDedicatedEventIdStoreAndInnerType()
    {
        var (log, provider, factory) = Build();
        using var _ = factory;

        Emit(log, SessionOutage(
            new RedisTimeoutException(CommandFlags.None, "Timeout performing GET (5000ms)", CommandStatus.Sent)));

        var record = provider.Logs.ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.EventId.Id.ShouldBe(2050);
        record.Message.ShouldContain("event_name=store_unavailable");
        record.Message.ShouldContain($"store={SessionStoreUnavailableException.StoreName}");
        record.Message.ShouldContain("inner_type=RedisTimeoutException");
    }

    [Fact]
    public void Emit_TwiceWithinWindow_ForOneStore_LogsOnlyOnce()
    {
        var (log, provider, factory) = Build();
        using var _ = factory;

        // A Redis outage fans out to every request on that store; the coarse throttle must
        // collapse a burst to a single entry so the sink is not flooded.
        Emit(log, SessionOutage(RedisFaults.Loading("LOADING")));
        Emit(log, SessionOutage(RedisFaults.Loading("LOADING")));

        provider.Logs.Count.ShouldBe(1);
    }

    [Fact]
    public void Emit_ForTheOtherStoreWithinTheWindow_IsNotSwallowedByTheFirst()
    {
        var (log, provider, factory) = Build();
        using var _ = factory;

        // Two instances, two failure domains. One shared window would let a session outage hide the first
        // entry of a volatile-Redis outage, and the other way round.
        Emit(log, SessionOutage(RedisFaults.Loading("LOADING")));
        Emit(log, new VolatileRedisUnavailableException(nameof(RedisConnectionException)));
        Emit(log, SessionOutage(RedisFaults.Loading("LOADING")));
        Emit(log, new VolatileRedisUnavailableException(nameof(RedisConnectionException)));

        var messages = provider.Logs.Select(r => r.Message).ToList();
        messages.Count.ShouldBe(2);
        messages[0].ShouldContain($"store={SessionStoreUnavailableException.StoreName} ");
        messages[1].ShouldContain($"store={VolatileRedisUnavailableException.StoreName} ");
    }

    [Fact]
    public void Emit_LogsInnerTypeOnly_NeverTheExceptionMessageOrItsEmbeddedUserId()
    {
        var (log, provider, factory) = Build();
        using var _ = factory;

        // StackExchange.Redis embeds the operated key in the exception message
        // (IncludeDetailInExceptions defaults true). A user-keyed op's key carries the raw userId
        // Guid — the log must carry the TYPE only, so neither the message nor the userId appears.
        var userId = Guid.NewGuid();
        Emit(log, SessionOutage(new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            CommandFlags.None,
            $"It was not possible to connect; command=GET, key=jobbliggaren:user:{userId}:deleted",
            null,
            CommandStatus.Unknown)));

        var record = provider.Logs.ShouldHaveSingleItem();
        record.Message.ShouldContain("inner_type=RedisConnectionException");
        record.Message.ShouldNotContain(userId.ToString());
        record.Message.ShouldNotContain("user:");
    }
}
