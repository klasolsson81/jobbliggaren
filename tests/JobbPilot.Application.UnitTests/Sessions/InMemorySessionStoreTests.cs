using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Sessions;

public class InMemorySessionStoreTests
{
    private static readonly IOptions<SessionStoreOptions> DefaultOptions =
        Options.Create(new SessionStoreOptions { Ttl = TimeSpan.FromDays(14) });

    private static InMemorySessionStore CreateStore(MutableFakeDateTimeProvider? time = null) =>
        new(time ?? new MutableFakeDateTimeProvider(), DefaultOptions);

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithMatchingUserId_WhenCalled()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(userId, ct);

        session.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithNonEmptyId_WhenCalled()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), ct);

        session.Id.Reveal().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnUniqueIds_WhenCalledTwice()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var s1 = await store.CreateAsync(Guid.NewGuid(), ct);
        var s2 = await store.CreateAsync(Guid.NewGuid(), ct);

        s1.Id.Reveal().ShouldNotBe(s2.Id.Reveal());
    }

    [Fact]
    public async Task CreateAsync_ShouldSetExpiresAtTo14DaysAfterCreatedAt_WhenCalled()
    {
        var time = new MutableFakeDateTimeProvider();
        var store = CreateStore(time);
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), ct);

        (session.ExpiresAt - session.CreatedAt).ShouldBe(TimeSpan.FromDays(14));
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ShouldReturnSession_WhenSessionExists()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var created = await store.CreateAsync(userId, ct);
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

        var session = await store.CreateAsync(Guid.NewGuid(), ct);
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

        var session = await store.CreateAsync(Guid.NewGuid(), ct);
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

        await store.CreateAsync(Guid.NewGuid(), ct);
        var session = await store.CreateAsync(Guid.NewGuid(), ct);

        time.UtcNow = time.UtcNow.AddDays(15);

        var result = await store.GetAsync(session.Id, ct);

        result.ShouldBeNull();
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

        var session = await store.CreateAsync(Guid.NewGuid(), ct);
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

        var session = await store.CreateAsync(Guid.NewGuid(), ct);
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
            Enumerable.Range(0, 100).Select(_ => store.CreateAsync(Guid.NewGuid(), ct)));

        sessions.Select(s => s.Id.Reveal()).Distinct().Count().ShouldBe(100);
    }

    [Fact]
    public async Task InvalidateAsync_ShouldNotThrow_WhenCalledConcurrentlyOnSameSession()
    {
        var store = CreateStore();
        var ct = TestContext.Current.CancellationToken;

        var session = await store.CreateAsync(Guid.NewGuid(), ct);

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

        var session = await store1.CreateAsync(Guid.NewGuid(), ct);
        var result = await store2.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }
}
