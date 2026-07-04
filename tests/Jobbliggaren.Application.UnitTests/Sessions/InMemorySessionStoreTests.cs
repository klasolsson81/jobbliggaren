using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Sessions;

public class InMemorySessionStoreTests
{
    private static readonly IOptions<SessionStoreOptions> DefaultOptions =
        Options.Create(new SessionStoreOptions { Legacy = new SessionLifetimeProfile { SlidingTtl = TimeSpan.FromDays(14) } });

    private static InMemorySessionStore CreateStore(MutableFakeDateTimeProvider? time = null) =>
        new(time ?? new MutableFakeDateTimeProvider(), DefaultOptions);

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithMatchingUserId_WhenCalled()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(userId, SessionLifetime.Legacy, ct);

        session.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithNonEmptyId_WhenCalled()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        session.Id.Reveal().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnUniqueIds_WhenCalledTwice()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var s1 = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var s2 = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        s1.Id.Reveal().ShouldNotBe(s2.Id.Reveal());
    }

    [Fact]
    public async Task CreateAsync_ShouldSetExpiresAtTo14DaysAfterCreatedAt_WhenCalled()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        (session.ExpiresAt - session.CreatedAt).ShouldBe(TimeSpan.FromDays(14));
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ShouldReturnSession_WhenSessionExists()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var created = await store.CreateAsync(userId, SessionLifetime.Legacy, ct);
        var fetched = await store.GetAsync(created.Id, ct);

        fetched.ShouldNotBeNull();
        fetched!.Id.Reveal().ShouldBe(created.Id.Reveal());
        fetched.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSessionDoesNotExist()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var result = await store.GetAsync(SessionId.FromRaw("nonexistent-id"), ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSessionWasInvalidated()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        await store.InvalidateAsync(session.Id, ct);
        var result = await store.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldExtendExpiresAt_WhenCalledAfterTimeAdvances()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var originalExpiry = session.ExpiresAt;

        time.UtcNow = time.UtcNow.AddDays(1);

        var fetched = await store.GetAsync(session.Id, ct);

        fetched.ShouldNotBeNull();
        fetched!.ExpiresAt.ShouldBeGreaterThan(originalExpiry);
        (fetched.ExpiresAt - time.UtcNow).ShouldBe(TimeSpan.FromDays(14));
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSessionExpired()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        time.UtcNow = time.UtcNow.AddDays(15);

        var result = await store.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }

    // #481 Low / #620: the absolute lifetime cap ends a session at CreatedAt +
    // AbsoluteTtl (default 30d) however actively it is used. Reads at 10d and 20d
    // slide it (well within the 14d sliding window because each read resets it), yet
    // the read at 31d must return null — proving the cap binds AND that reads never
    // reset CreatedAt (else 31d would still be "10d since last read" and survive).
    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenAbsoluteCapExceeded_DespiteActiveReads()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        time.UtcNow = time.UtcNow.AddDays(10);
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
        time.UtcNow = time.UtcNow.AddDays(10); // +20d, last read 10d ago → within sliding window
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();

        time.UtcNow = time.UtcNow.AddDays(11); // +31d total → past the 30d absolute cap
        (await store.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    // Boundary (#620 review): exactly at CreatedAt + AbsoluteTtl the session is spent —
    // returns null, and must NOT reach the clamp (a zero-length sliding window throws on
    // the Redis path). Inclusive (>=) check. Reads slide it to the ceiling first.
    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenExactlyAtAbsoluteCap()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        time.UtcNow = time.UtcNow.AddDays(10);
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
        time.UtcNow = time.UtcNow.AddDays(10); // +20d
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();

        time.UtcNow = session.CreatedAt.AddDays(30); // exactly the 30d ceiling
        (await store.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    // The clamp: near the cap, a read slides the session only up to the ceiling, never
    // past it — so ExpiresAt tracks the cap, not a full fresh sliding window.
    [Fact]
    public async Task GetAsync_ShouldClampExpiryToAbsoluteCap_WhenNearCeiling()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        // Slide with reads inside the 14d window so only the cap (not inactivity) binds.
        time.UtcNow = time.UtcNow.AddDays(10);
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
        time.UtcNow = time.UtcNow.AddDays(10); // +20d
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();

        time.UtcNow = time.UtcNow.AddDays(9); // +29d, 1 day of absolute cap remains (< 14d sliding)
        var fetched = await store.GetAsync(session.Id, ct);

        fetched.ShouldNotBeNull();
        (fetched!.ExpiresAt - time.UtcNow).ShouldBe(TimeSpan.FromDays(1));
        fetched.ExpiresAt.ShouldBe(session.CreatedAt.AddDays(30));
    }

    // #626: the session keeps the lifetime profile it was created under. A Persistent
    // session uses the 180d cap (default profile), so it survives well past the 30d
    // Legacy ceiling that a default session would hit — proving profile selection.
    [Fact]
    public async Task GetAsync_ShouldUsePersistentCap_WhenCreatedPersistent()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);

        // Slide with reads inside the 30d persistent window, out to +40d — impossible
        // for a Legacy session (30d cap). Still valid → the Persistent profile is used.
        time.UtcNow = time.UtcNow.AddDays(20);
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
        time.UtcNow = time.UtcNow.AddDays(20); // +40d
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
    }

    // A Session-profile (unticked "Håll mig inloggad") session is short-lived: past its
    // 24h ceiling it is gone, where a Legacy session would still be valid.
    [Fact]
    public async Task GetAsync_ShouldUseSessionCap_WhenCreatedSession()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Session, ct);

        time.UtcNow = time.UtcNow.AddHours(12);
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
        time.UtcNow = time.UtcNow.AddHours(13); // +25h → past the 24h cap
        (await store.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenInputIsEmptyString()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var result = await store.GetAsync(SessionId.FromRaw(string.Empty), ct);

        result.ShouldBeNull();
    }

    // ── InvalidateAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidateAsync_ShouldReturnTrue_WhenSessionExists()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var result = await store.InvalidateAsync(session.Id, ct);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReturnFalse_WhenSessionDoesNotExist()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var result = await store.InvalidateAsync(SessionId.FromRaw("never-existed"), ct);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReturnFalse_WhenSessionAlreadyInvalidated()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        await store.InvalidateAsync(session.Id, ct);
        var result = await store.InvalidateAsync(session.Id, ct);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReturnFalse_WhenInputIsEmptyString()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var result = await store.InvalidateAsync(SessionId.FromRaw(string.Empty), ct);

        result.ShouldBeFalse();
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldProduceUniqueIds_WhenCalledConcurrently()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var sessions = await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct)));

        sessions.Select(s => s.Id.Reveal()).Distinct().Count().ShouldBe(100);
    }

    [Fact]
    public async Task InvalidateAsync_ShouldNotThrow_WhenCalledConcurrentlyOnSameSession()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        var results = await Task.WhenAll(
            store.InvalidateAsync(session.Id, ct),
            store.InvalidateAsync(session.Id, ct));

        // Exactly one must have found the session; the other gets false
        results.Count(r => r).ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSeparateStoreInstance()
    {
        var store1 = CreateStore();
        var store2 = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store1.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var result = await store2.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }
}
