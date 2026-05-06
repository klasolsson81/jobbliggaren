# Test Plan: ISessionStore (STEG 4b Turn 3)

**Date:** 2026-05-06
**Role:** Advisory only — test plan, not implementation
**Author:** test-writer agent
**Scope:** `ISessionStore` + `InMemorySessionStore` + `RedisSessionStore`

---

## Test organization

Two test projects are involved:

| Project | Purpose | Tests proposed |
|---------|---------|----------------|
| `tests/JobbPilot.Application.UnitTests` | Pure unit tests, no I/O | Conformance tests for `InMemorySessionStore`, session-id entropy tests |
| `tests/JobbPilot.Api.IntegrationTests` | Testcontainers Redis 8 + Postgres 18 | Conformance tests for `RedisSessionStore`, TTL/sliding-expiration tests, connection-failure tests |

### Class structure

The conformance contract is shared via an abstract base class. Both implementations inherit from it so the same behavioral specification is enforced on both.

```
tests/JobbPilot.Application.UnitTests/
└── Sessions/
    ├── SessionStoreConformanceTests.cs       (abstract base)
    ├── InMemorySessionStoreTests.cs          (inherits base + adds in-memory specifics)
    └── SessionIdEntropyTests.cs              (pure unit, no store needed)

tests/JobbPilot.Api.IntegrationTests/
└── Sessions/
    ├── RedisSessionStoreTests.cs             (inherits conformance base, uses Testcontainers)
    ├── RedisSessionStoreTtlTests.cs          (TTL + sliding-expiration, separate fixture)
    └── RedisSessionStoreFailureTests.cs      (connection-failure scenarios)
```

**Rationale for splitting Redis tests into three classes:** the conformance suite, the TTL suite, and the failure suite each need different fixture lifecycles (shared container vs. dedicated short-TTL container vs. controllable container). Mixing them in one class makes `IAsyncLifetime` brittle.

---

## 1. Conformance test base

**File:** `tests/JobbPilot.Application.UnitTests/Sessions/SessionStoreConformanceTests.cs`

Abstract class. Subclasses provide the concrete `ISessionStore` via a template-method `CreateStore()`.

```csharp
public abstract class SessionStoreConformanceTests
{
    protected abstract ISessionStore CreateStore();

    // ... test methods below
}
```

### Test methods

| Method name | Scenario |
|-------------|----------|
| `CreateAsync_ShouldReturnSessionWithMatchingUserId_WhenCalled` | Verifies `Session.UserId` equals the input `Guid` |
| `CreateAsync_ShouldReturnSessionWithNonEmptyId_WhenCalled` | `Session.Id` is non-null and non-empty |
| `CreateAsync_ShouldReturnUniqueIds_WhenCalledTwice` | Two sequential creates produce different IDs |
| `CreateAsync_ShouldSetCreatedAtAndLastAccessedAtToSameInstant_WhenCalled` | `CreatedAt == LastAccessedAt` on freshly created session |
| `CreateAsync_ShouldSetExpiresAtTo14DaysAfterCreatedAt_WhenCalled` | `ExpiresAt - CreatedAt == TimeSpan.FromDays(14)` (within tolerance) |
| `GetAsync_ShouldReturnSession_WhenSessionExists` | Round-trip: create → get → same id, same userId |
| `GetAsync_ShouldReturnNull_WhenSessionDoesNotExist` | Unknown id returns null |
| `GetAsync_ShouldReturnNull_WhenSessionWasInvalidated` | Create → invalidate → get returns null |
| `GetAsync_ShouldUpdateLastAccessedAt_WhenCalled` | `LastAccessedAt` is more recent after second `GetAsync` than after `CreateAsync` (sliding semantics) |
| `InvalidateAsync_ShouldReturnTrue_WhenSessionExists` | Existing session → true |
| `InvalidateAsync_ShouldReturnFalse_WhenSessionDoesNotExist` | Unknown id → false |
| `InvalidateAsync_ShouldReturnFalse_WhenSessionAlreadyInvalidated` | Idempotent: second invalidate returns false |
| `GetAsync_ShouldReturnNull_WhenInputIsEmptyOrWhitespace` | Defensive: empty/whitespace ids must not throw, return null |
| `InvalidateAsync_ShouldReturnFalse_WhenInputIsEmptyOrWhitespace` | Same defensive contract |

**Note on `LastAccessedAt`:** the assertion needs an `IDateTimeProvider` injected into the store, otherwise the test is flaky on fast machines. Recommend that `ISessionStore` implementations take `IDateTimeProvider` (or `TimeProvider` from .NET 8+) so tests can advance time deterministically with `FakeTimeProvider`.

---

## 2. InMemorySessionStore-specific tests

**File:** `tests/JobbPilot.Application.UnitTests/Sessions/InMemorySessionStoreTests.cs`

```csharp
public class InMemorySessionStoreTests : SessionStoreConformanceTests
{
    protected override ISessionStore CreateStore() =>
        new InMemorySessionStore(new FakeTimeProvider());

    // Plus the specifics below
}
```

| Method name | Scenario |
|-------------|----------|
| `CreateAsync_ShouldProduceUniqueIds_WhenCalledConcurrently` | 100 parallel `CreateAsync` calls via `Parallel.ForEachAsync` → all 100 IDs distinct |
| `GetAsync_ShouldReturnNull_WhenSessionStoreInstanceIsDifferent` | Two separate `InMemorySessionStore` instances do not share state |
| `GetAsync_ShouldReturnNull_WhenSessionExpiredViaFakeTimeProvider` | Use `FakeTimeProvider`, advance 15 days, verify `GetAsync` returns null without invoking `InvalidateAsync` |
| `InvalidateAsync_ShouldNotThrow_WhenCalledConcurrentlyOnSameSession` | Race two `InvalidateAsync` calls on same id; one returns true, one returns false, neither throws |

**Test helper needed:** `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` (already approved in CLAUDE.md anti-patterns section).

---

## 3. RedisSessionStore-specific tests

**File:** `tests/JobbPilot.Api.IntegrationTests/Sessions/RedisSessionStoreTests.cs`

This class inherits the conformance base but overrides `CreateStore()` to return a `RedisSessionStore` backed by a Testcontainers Redis instance. Lifecycle managed via `IAsyncLifetime`.

```csharp
public class RedisSessionStoreTests : SessionStoreConformanceTests, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:8-alpine")
        .Build();

    private IDistributedCache _cache = default!;
    private FakeTimeProvider _time = default!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        // wire up _cache via StackExchangeRedisCache against _redis.GetConnectionString()
        _time = new FakeTimeProvider();
    }

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    protected override ISessionStore CreateStore() =>
        new RedisSessionStore(_cache, _time);
}
```

The class inherits the entire conformance suite — no method names need to be re-listed here. Only the additional Redis-specific tests live in separate classes (below).

### TTL & sliding expiration (separate file)

**File:** `tests/JobbPilot.Api.IntegrationTests/Sessions/RedisSessionStoreTtlTests.cs`

These tests deliberately inspect Redis directly (via a `IConnectionMultiplexer` injected alongside `IDistributedCache`) to assert on the actual TTL on the key.

| Method name | Scenario |
|-------------|----------|
| `CreateAsync_ShouldSetRedisTtlToApproximately14Days_WhenCalled` | After `CreateAsync`, `redis.KeyTimeToLive(key)` is between `13d 23h 55s` and `14d 0h 5s` |
| `GetAsync_ShouldResetRedisTtlTo14Days_WhenSessionExists` | Create → wait/advance 1 hour → `GetAsync` → TTL is back to ~14 days, not ~13 days 23 hours |
| `GetAsync_ShouldReturnNull_WhenSessionExpiredInRedis` | Use a short-TTL variant (see helper below), wait past TTL, verify `GetAsync` returns null |
| `InvalidateAsync_ShouldRemoveKeyImmediately_WhenCalled` | After `InvalidateAsync`, `redis.KeyExists(key)` is false (no waiting for TTL) |
| `RoundTrip_ShouldPreserveAllSessionFields_WhenSerializedAndDeserialized` | Create with specific UserId/timestamps → fetch → all fields equal byte-for-byte (UTC, ticks-equal) |

**Test helper needed:** `ShortTtlRedisSessionStoreFactory` — produces a `RedisSessionStore` configured with a 2-second TTL instead of 14 days. The implementer should expose TTL as constructor parameter or via `IOptions<SessionStoreOptions>` so this is testable without `Thread.Sleep(14d)`.

```csharp
internal static class ShortTtlRedisSessionStoreFactory
{
    public static RedisSessionStore Create(
        IDistributedCache cache,
        TimeProvider time,
        TimeSpan ttl) => new(cache, time, new SessionStoreOptions { Ttl = ttl });
}
```

---

## 4. Session-id entropy tests

**File:** `tests/JobbPilot.Application.UnitTests/Sessions/SessionIdEntropyTests.cs`

Pure unit tests — no `ISessionStore` instance needed. They verify a static `SessionIdGenerator.Generate()` helper (or whatever the implementer names it). If the entropy logic is private inside `InMemorySessionStore` / `RedisSessionStore`, the implementer should extract it to a shared static helper to make these tests possible.

| Method name | Scenario |
|-------------|----------|
| `Generate_ShouldProduceIdOf43Characters_WhenCalled` | base64url-encoded 32 bytes (no padding) = 43 chars |
| `Generate_ShouldProduceUrlSafeCharactersOnly_WhenCalled` | Regex match: `^[A-Za-z0-9\-_]+$` (no `+`, `/`, or `=`) |
| `Generate_ShouldProduceZeroCollisions_When10000IdsAreGenerated` | `HashSet<string>` of 10 000 ids has count 10 000 |
| `Generate_ShouldNotContainPaddingCharacters_WhenCalled` | No `=` characters anywhere |
| `Generate_ShouldUseRandomNumberGenerator_NotPredictableSource` | Property test: 100 IDs sampled, byte-distribution chi-square ≥ a sane lower bound (or simpler: assert no two consecutive IDs share a 6-character prefix more than once across 100 samples) |

**Note:** the entropy test in row 5 is a "smoke test" for randomness, not a cryptographic proof. Do not over-engineer it. If the implementer uses `RandomNumberGenerator.GetBytes(32)`, the contract is met by construction; the test exists as a regression guard against someone replacing it with `Guid.NewGuid()` or `Random.Shared`.

---

## 5. Connection failure behavior

**File:** `tests/JobbPilot.Api.IntegrationTests/Sessions/RedisSessionStoreFailureTests.cs`

### Recommended behavior (proposed contract)

When Redis is unavailable, `RedisSessionStore` should **throw** rather than silently return null/false. Rationale:

- Returning null on `GetAsync` would be indistinguishable from "session not found", causing the auth middleware to log the user out — a **silent security degradation** that violates the principle of failing loud.
- Returning false on `InvalidateAsync` during a Redis outage is worse: the caller believes the session is still valid and may retry, when in fact the operation never reached Redis.
- The existing JobbPilot pattern (CLAUDE.md §3.4) is "unexpected errors → exceptions". A Redis outage is unexpected.

The thrown exception should be a typed `SessionStoreUnavailableException` (subclass of `InvalidOperationException` or a project-specific base) so the API middleware can map it to HTTP 503.

### Test mechanism

**Mechanism A (preferred):** stop the Testcontainers Redis container mid-test using `_redis.StopAsync()`. The next operation against `IDistributedCache` will throw `RedisConnectionException` from StackExchange.Redis, which `RedisSessionStore` should wrap.

**Mechanism B (fallback if A is flaky):** point the `RedisSessionStore` at a port where nothing is listening (e.g. `localhost:1` with a 1-second connect timeout). Faster but doesn't test mid-flight failure.

Recommend **A** for primary coverage and **B** as a faster smoke test. Do not mock `IDistributedCache` for these tests — that would only verify our code paths around the mock, not the real Redis client behavior.

### Test methods

| Method name | Scenario |
|-------------|----------|
| `GetAsync_ShouldThrowSessionStoreUnavailableException_WhenRedisIsDown` | Stop container → `GetAsync` throws typed exception |
| `CreateAsync_ShouldThrowSessionStoreUnavailableException_WhenRedisIsDown` | Stop container → `CreateAsync` throws typed exception |
| `InvalidateAsync_ShouldThrowSessionStoreUnavailableException_WhenRedisIsDown` | Stop container → `InvalidateAsync` throws typed exception |
| `GetAsync_ShouldNotLeakRawRedisConnectionException_WhenRedisIsDown` | Verify the wrapped exception is the typed one, not raw `RedisConnectionException` (encapsulation contract) |
| `GetAsync_ShouldRecover_WhenRedisComesBackOnline` | Stop → assert throws → restart → assert `GetAsync` works again (no permanent state corruption in store) |

**Important caveat for the implementer:** `Testcontainers.Redis` does not always release ports cleanly between `StopAsync` and a subsequent `StartAsync` on the same container instance. The "recover" test may need a fresh `RedisBuilder` or an explicit wait loop.

---

## Test helpers needed

| Helper | Location | Purpose |
|--------|----------|---------|
| `SessionStoreConformanceTests` (abstract) | `tests/JobbPilot.Application.UnitTests/Sessions/` | Shared behavioral contract |
| `ShortTtlRedisSessionStoreFactory` | `tests/JobbPilot.Api.IntegrationTests/Sessions/` | Build `RedisSessionStore` with ~2s TTL for expiry tests |
| `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) | NuGet package | Deterministic time advancement |
| `RedisFixture` (optional) | `tests/JobbPilot.Api.IntegrationTests/Sessions/` | xUnit `IClassFixture` to share one Redis container across the conformance suite (saves ~3s × N tests) |

---

## Implementation notes

### Things the implementer must know

1. **`ISessionStore` should accept `TimeProvider` (not `IDateTimeProvider`)** — `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` is the modern approach and integrates with `IDistributedCache`'s expiration semantics natively. If the project standard is `IDateTimeProvider`, then it should wrap `TimeProvider` internally so the store can use `TimeProvider.GetUtcNow()` for Redis TTL calculations.

2. **TTL must be configurable via `IOptions<SessionStoreOptions>`** — otherwise the short-TTL tests in section 3 are impossible. Default value: 14 days. Config key: `Session:Ttl`.

3. **Session-id generator should be a separate static class** (e.g. `SessionIdGenerator.Generate()`) — keeps it testable without spinning up Redis or in-memory store. If the entropy logic ends up duplicated in both stores, the entropy test cannot guard the Redis path.

4. **Redis serialization format** — recommend `System.Text.Json` with explicit options, not BinaryFormatter (deprecated). Test the round-trip explicitly so a future serializer swap doesn't silently break sessions in production.

5. **Concurrent access to `InMemorySessionStore`** — must use `ConcurrentDictionary<string, Session>` and produce session IDs from a thread-safe `RandomNumberGenerator`. The concurrent-create test will catch a `Dictionary<,>` regression immediately.

6. **The `IAsyncLifetime` pattern in xUnit v3** uses `ValueTask` (not `Task`) — make sure the integration test fixtures match the JobbPilot v3 convention already established in `ApiFactory`.

7. **Do NOT share the existing `ApiFactory` Redis container with these tests.** The session store tests need to control container lifecycle for the failure scenarios. Use a dedicated `RedisContainer` per test class or per fixture.

### Status of this advisory

These tests are **RED** — production code (`ISessionStore`, `InMemorySessionStore`, `RedisSessionStore`, `SessionStoreOptions`, `SessionIdGenerator`, `SessionStoreUnavailableException`) does not yet exist. The implementer (Klas or an implementation agent) writes production code until the tests turn **GREEN**, then test-writer is consulted again for refactor-phase suggestions.

### Open questions for `dotnet-architect`

1. Should `Session` be a domain aggregate or a pure DTO? If aggregate, it belongs in `JobbPilot.Domain` and the test file structure needs to move.
2. Should `SessionStoreUnavailableException` extend `DomainException` or a new infrastructure-level base? The current `DomainException` semantics (HTTP 400 in middleware) are wrong for this case — we want HTTP 503.
3. Should `InvalidateAsync` return `bool` or `void`? The current "true means session existed" contract leaks implementation detail; a simpler `Task InvalidateAsync(...)` may be cleaner. If changed, the conformance tests need adjusting.
