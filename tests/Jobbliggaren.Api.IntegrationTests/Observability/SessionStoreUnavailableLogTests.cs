using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.Observability;
using Microsoft.Extensions.Logging;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Observability;

/// <summary>
/// Unit tests for <see cref="SessionStoreUnavailableLog"/> (#512, epic #484): it must emit exactly
/// one dedicated-event-id Error record per outage window (throttled), carry only the inner Redis
/// exception's type/message (§5 — no session-id/token/PII, which it structurally cannot see), and
/// use the <c>session_store_unavailable</c> event_name the TD-77 alarm keys on.
/// </summary>
public class SessionStoreUnavailableLogTests
{
    private static (SessionStoreUnavailableLog Log, CapturingLoggerProvider Provider) Build()
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        return (new SessionStoreUnavailableLog(factory.CreateLogger<SessionStoreUnavailableLog>()), provider);
    }

    [Fact]
    public void Emit_LogsErrorWithDedicatedEventIdAndInnerException()
    {
        var (log, provider) = Build();

        log.Emit(new RedisTimeoutException("Timeout performing GET (5000ms)", CommandStatus.Sent));

        var record = provider.Logs.ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.EventId.Id.ShouldBe(2050);
        record.Message.ShouldContain("event_name=session_store_unavailable");
        record.Message.ShouldContain("RedisTimeoutException");
    }

    [Fact]
    public void Emit_TwiceWithinWindow_LogsOnlyOnce()
    {
        var (log, provider) = Build();

        // A Redis outage fans out to every authenticated request; the coarse throttle must
        // collapse a burst to a single entry so the sink is not flooded.
        log.Emit(new RedisServerException("LOADING"));
        log.Emit(new RedisServerException("LOADING"));

        provider.Logs.Count.ShouldBe(1);
    }

    [Fact]
    public void Emit_LogsOnlyInnerTypeAndMessage_NoSessionData()
    {
        var (log, provider) = Build();

        // The method takes ONLY an Exception, so it cannot leak a session-id/token by
        // construction; assert the rendered message is exactly the two inner fields (§5).
        log.Emit(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "It was not possible to connect"));

        var record = provider.Logs.ShouldHaveSingleItem();
        record.Message.ShouldContain("inner_type=RedisConnectionException");
        record.Message.ShouldContain("inner_message=It was not possible to connect");
    }
}
