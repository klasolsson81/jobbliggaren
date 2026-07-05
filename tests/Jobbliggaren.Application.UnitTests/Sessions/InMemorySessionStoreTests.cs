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

    // #626/#2b1: the session keeps the lifetime profile it was created under. A
    // Persistent session has a 30d sliding window, so a single read at +20d keeps it
    // alive — where a Legacy session (14d sliding) would already be dead from
    // inactivity. Proves profile selection. (Persistent's cap stays 30d until the
    // rotation driver ships; the longer 180d reach lands there.)
    [Fact]
    public async Task GetAsync_ShouldUsePersistentSlidingWindow_WhenCreatedPersistent()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var persistent = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);
        var legacy = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        // +20d since the single create-time activity: within Persistent's 30d window,
        // past Legacy's 14d window.
        time.UtcNow = time.UtcNow.AddDays(20);
        (await store.GetAsync(persistent.Id, ct)).ShouldNotBeNull();
        (await store.GetAsync(legacy.Id, ct)).ShouldBeNull();
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

    // ── RotateAsync (#2b1) ─────────────────────────────────────────────────────

    [Fact]
    public async Task RotateAsync_ShouldMintNewIdAndRetireOld_WhenDue()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);

        time.UtcNow = time.UtcNow.AddHours(25); // past the 24h rotation interval

        var rotation = await store.RotateAsync(session.Id, ct);

        rotation.ShouldNotBeNull();
        rotation!.NewId.Reveal().ShouldNotBe(session.Id.Reveal());
        // Old id retired, new id valid.
        (await store.GetAsync(session.Id, ct)).ShouldBeNull();
        (await store.GetAsync(rotation.NewId, ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldPreserveCreatedAt_WhenRotated()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);
        var originalCreatedAt = session.CreatedAt;

        time.UtcNow = time.UtcNow.AddHours(25);
        var rotation = await store.RotateAsync(session.Id, ct);

        var rotated = await store.GetAsync(rotation!.NewId, ct);
        // CreatedAt carried verbatim → the absolute cap cannot be reset by rotation.
        rotated!.CreatedAt.ShouldBe(originalCreatedAt);
    }

    [Fact]
    public async Task RotateAsync_ShouldReturnNull_WhenNotDue()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);

        time.UtcNow = time.UtcNow.AddHours(1); // well within the 24h interval

        (await store.RotateAsync(session.Id, ct)).ShouldBeNull();
        // Still the same, valid session.
        (await store.GetAsync(session.Id, ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldReturnNull_ForNonRotatingProfile()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        // Legacy has RotationInterval = 0 → never rotates, however long we wait.
        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        time.UtcNow = time.UtcNow.AddDays(5);

        (await store.RotateAsync(session.Id, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldReturnNull_WhenSessionMissing()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        (await store.RotateAsync(SessionId.FromRaw("never-existed"), ct)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldElectSingleWinner_WhenConcurrent()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);
        time.UtcNow = time.UtcNow.AddHours(25);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => store.RotateAsync(session.Id, ct)));

        // Exactly one caller rotates; the rest lose the election and get null.
        results.Count(r => r is not null).ShouldBe(1);
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
