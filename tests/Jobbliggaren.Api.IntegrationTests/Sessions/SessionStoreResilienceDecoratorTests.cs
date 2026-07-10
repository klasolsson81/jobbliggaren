using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionStoreResilienceDecorator"/> (#511, epic #484). The decorator
/// must translate the two degraded-Redis exceptions the inner store does NOT itself wrap —
/// <see cref="RedisTimeoutException"/> and <see cref="RedisServerException"/> — into the
/// <see cref="SessionStoreUnavailableException"/> contract (→ 503), pass an already-translated
/// contract exception straight through WITHOUT double-wrapping, leave the happy path untouched,
/// and NOT translate unrelated exceptions (a real bug must still surface as 500). Pure (fake
/// inner, no I/O), so deliberately no <c>[Collection("Api")]</c> — it needs no shared database.
/// </summary>
public class SessionStoreResilienceDecoratorTests
{
    private readonly ISessionStore _inner = Substitute.For<ISessionStore>();
    private readonly SessionStoreResilienceDecorator _sut;

    public SessionStoreResilienceDecoratorTests() =>
        _sut = new SessionStoreResilienceDecorator(_inner);

    [Fact]
    public async Task GetAsync_WhenInnerThrowsRedisTimeout_TranslatesToSessionStoreUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        // RedisTimeoutException derives from TimeoutException, NOT RedisException — the case a
        // naive `catch (RedisException)` would miss. This is the most common degraded state.
        var timeout = new RedisTimeoutException("Timeout performing GET", CommandStatus.Sent);
        _inner.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>()).Throws(timeout);

        var ex = await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _sut.GetAsync(SessionId.FromRaw("does-not-matter"), ct));

        ex.InnerException.ShouldBeSameAs(timeout);
    }

    [Fact]
    public async Task GetAsync_WhenInnerThrowsRedisServerException_TranslatesToSessionStoreUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        // RedisServerException (e.g. LOADING during an RDB restart) derives from RedisException.
        var loading = new RedisServerException("LOADING Redis is loading the dataset in memory");
        _inner.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>()).Throws(loading);

        var ex = await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _sut.GetAsync(SessionId.FromRaw("x"), ct));

        ex.InnerException.ShouldBeSameAs(loading);
    }

    [Fact]
    public async Task GetAsync_WhenInnerAlreadyTranslated_PassesThroughWithoutDoubleWrap()
    {
        var ct = TestContext.Current.CancellationToken;
        // The inner store already wraps RedisConnectionException into the contract exception.
        // The decorator must re-throw that SAME instance, never wrap a contract inside a contract.
        var contract = new SessionStoreUnavailableException(
            "down",
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "no conn"));
        _inner.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>()).Throws(contract);

        var ex = await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _sut.GetAsync(SessionId.FromRaw("x"), ct));

        ex.ShouldBeSameAs(contract);
        ex.InnerException.ShouldBeOfType<RedisConnectionException>();
    }

    [Fact]
    public async Task GetAsync_WhenInnerThrowsUnrelatedException_DoesNotTranslate()
    {
        var ct = TestContext.Current.CancellationToken;
        // A genuine bug (not an outage) must NOT be masked as a 503 — it stays a 500.
        var unrelated = new InvalidOperationException("bug, not an outage");
        _inner.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>()).Throws(unrelated);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => _sut.GetAsync(SessionId.FromRaw("x"), ct));

        ex.ShouldBeSameAs(unrelated);
    }

    [Fact]
    public async Task GetAsync_HappyPath_ReturnsInnerResultUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = new Session(
            SessionId.FromRaw("abc123"), Guid.NewGuid(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        _inner.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>()).Returns(session);

        var result = await _sut.GetAsync(SessionId.FromRaw("abc123"), ct);

        result.ShouldBeSameAs(session);
    }

    [Fact]
    public async Task CreateAsync_WhenInnerThrowsRedisTimeout_TranslatesToSessionStoreUnavailable()
    {
        // The login WRITE path is translated too, not just the auth READ path — a Redis blip
        // during login must also surface as 503, uniformly (senior-cto-advisor Decision 1).
        var ct = TestContext.Current.CancellationToken;
        _inner.CreateAsync(Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Throws(new RedisTimeoutException("Timeout performing SET", CommandStatus.Sent));

        await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _sut.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct));
    }

    [Fact]
    public async Task MarkUserDeletedAsync_WhenInnerThrowsRedisServerException_TranslatesToSessionStoreUnavailable()
    {
        // The void-returning arm goes through the same single Guard translation site.
        var ct = TestContext.Current.CancellationToken;
        _inner.MarkUserDeletedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Throws(new RedisServerException("LOADING"));

        await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _sut.MarkUserDeletedAsync(Guid.NewGuid(), ct));
    }
}
