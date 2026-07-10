using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.Observability;
using Microsoft.Extensions.Logging;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Observability;

/// <summary>
/// Unit tests for <see cref="SessionStoreUnavailableLog"/> (#512, epic #484): it must emit exactly
/// one dedicated-event-id Error record per outage window (throttled), carry ONLY the inner Redis
/// exception's TYPE — never its message, which can embed the operated key and with it a raw userId
/// (§5 / GDPR Art. 5(1)(c) data-minimisation) — and use the <c>session_store_unavailable</c>
/// event_name the TD-77 alarm keys on.
/// </summary>
public class SessionStoreUnavailableLogTests
{
    private static (SessionStoreUnavailableLog Log, CapturingLoggerProvider Provider, ILoggerFactory Factory) Build()
    {
        var provider = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        // The caller disposes the factory (via `using var _ = factory`) so it stays alive across
        // the Emit + assertions — disposing it here would log against a disposed factory.
        return (new SessionStoreUnavailableLog(factory.CreateLogger<SessionStoreUnavailableLog>()), provider, factory);
    }

    [Fact]
    public void Emit_LogsErrorWithDedicatedEventIdAndInnerType()
    {
        var (log, provider, factory) = Build();
        using var _ = factory;

        log.Emit(new RedisTimeoutException("Timeout performing GET (5000ms)", CommandStatus.Sent));

        var record = provider.Logs.ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.EventId.Id.ShouldBe(2050);
        record.Message.ShouldContain("event_name=session_store_unavailable");
        record.Message.ShouldContain("inner_type=RedisTimeoutException");
    }

    [Fact]
    public void Emit_TwiceWithinWindow_LogsOnlyOnce()
    {
        var (log, provider, factory) = Build();
        using var _ = factory;

        // A Redis outage fans out to every authenticated request; the coarse throttle must
        // collapse a burst to a single entry so the sink is not flooded.
        log.Emit(new RedisServerException("LOADING"));
        log.Emit(new RedisServerException("LOADING"));

        provider.Logs.Count.ShouldBe(1);
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
        log.Emit(new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            $"It was not possible to connect; command=GET, key=jobbliggaren:user:{userId}:deleted"));

        var record = provider.Logs.ShouldHaveSingleItem();
        record.Message.ShouldContain("inner_type=RedisConnectionException");
        record.Message.ShouldNotContain(userId.ToString());
        record.Message.ShouldNotContain("user:");
    }
}
