# Architect Advisory: ISessionStore Design (STEG 4b Turn 3)

**Date:** 2026-05-06
**Role:** Advisory only

## Q1: Namespace

**Recommendation:** `src/JobbPilot.Application/Common/Abstractions/ISessionStore.cs`.

**Reasoning:** The established convention in this codebase is unambiguous —
every infrastructure-facing interface that Application defines for
Infrastructure to implement lives in `Common/Abstractions/`:
`IAccessTokenRevocationStore`, `IRefreshTokenStore`, `IJwtTokenGenerator`,
`IAppDbContext`, `ICurrentUser`, `IUserAccountService`. `ISessionStore` is the
exact same kind of seam: an Application-defined contract over a stateful
external store. It belongs with its siblings.

The proposed `Application/Auth/Sessions/` folder mixes two concepts: an
*infrastructure abstraction* (the store) with a *bounded-context grouping*
(Auth/Sessions). JobbPilot does not currently use folder-by-bounded-context
on the Application side for infrastructure interfaces, and introducing it
now for one interface will fragment the discoverability of the auth seams —
`IRefreshTokenStore` and `IAccessTokenRevocationStore` already live in
`Common/Abstractions/`. Splitting `ISessionStore` away from them is a net
loss.

If a `Session` *DTO* is the concern, a DTO can live next to the interface in
`Common/Abstractions/` (records co-located with the interface that returns
them is the existing pattern — see `RefreshToken`-shaped types around
`IRefreshTokenStore`).

**No deviation from convention warranted.** Use `Common/Abstractions/`.

## Q2: Session record vs class

**Recommendation:** `public sealed record Session(...)` with positional
constructor — keep as proposed.

**Reasoning:** CLAUDE.md §2.2's "ingen public setter på entity-properties"
rule applies to **domain entities** (aggregates that protect invariants).
`Session` here is a read-model / DTO returned across the Application
boundary — it is the snapshot the caller receives, not a mutable aggregate.

CLAUDE.md §3.3 is the rule that applies: "DTOs = `record class`. Value
Objects = `record struct` eller `readonly record class`." `Session` is a
DTO (not a value-equal domain primitive), so `sealed record` (reference-
type record class) is correct.

`sealed` is good hygiene here — there is no inheritance use case and
sealing communicates "this is the shape, full stop."

The point that "`LastAccessedAt` will be updated by the Redis
implementation internally" does not change this: each `GetAsync` returns a
fresh snapshot. The record being immutable is exactly what we want at the
boundary; mutation is a Redis-side concern.

## Q3: SessionId type

**Recommendation:** Keep `string`. Do **not** introduce
`readonly record struct SessionId(string Value)`.

**Reasoning:** ADR 0011's strongly-typed-ID pattern is specifically for
**aggregate identities** that cross domain boundaries — `JobAdId`,
`JobSeekerId`, `ApplicationId` etc. wrapping `Guid`. The benefit there is
preventing "I passed a `JobSeekerId` where a `JobAdId` was expected"
bugs that the compiler can catch.

Session IDs in this codebase have a different character:

1. They are **opaque transport tokens**, not domain identities. The session
   ID is generated, handed to the client (cookie), and handed back. It
   never participates in domain logic, never appears on an aggregate, never
   gets compared against other ID types.
2. They live at exactly one boundary: the auth pipeline.
   `string` → `ISessionStore.GetAsync` → `Session.UserId` (the actual
   typed ID). After that, `UserId` is what the rest of the system uses.
3. The "is it a session-id or some other string?" question never arises in
   practice — `ISessionStore` only takes session IDs.

Wrapping in `SessionId(string Value)` adds ceremony (`.Value` everywhere
the string crosses a boundary: cookie read/write, logging, Redis keys)
without buying compile-time safety against a confusion that cannot occur.

CLAUDE.md §5.1's "primitive obsession" rule is about *domain* primitives
(emails, money, addresses) that have invariants and behavior. A 32-byte
base64url token that the system itself generated has no invariants worth
encoding in a type — if it came back from Redis, it is by construction
valid.

**Keep `string`.** If at some future point session IDs flow through more
than one method signature and there is real evidence of confusion, lift to
a struct then.

## Q4: Sliding expiration race condition

**Recommendation:** The race is acceptable for STEG 4b. **Drop
`LastAccessedAt` from the public `Session` record.**

**Reasoning:** Two separate concerns are tangled in the proposal:

1. **"Is this session still alive?"** — answered by Redis TTL. `GetAsync`
   returning non-null already means "alive." The sliding-expiration TTL
   reset on read provides the renewal semantics. This is correct and
   the race ("two concurrent reads both extend") is benign — both readers
   get a valid session, the TTL ends up extended once. No security or
   correctness impact.

2. **"When was this session last touched?"** — answered by `LastAccessedAt`.
   This requires a read-modify-write to Redis to be accurate. With
   `IDistributedCache` you cannot do this atomically: `GetAsync` returns
   the snapshot, then a separate `SetAsync` would be needed to update
   the timestamp. Two concurrent reads can each write back stale values
   in any order. The field becomes lossy and unreliable.

For STEG 4b's actual needs — "is this user authenticated, and renew the
session on activity" — concern (1) is sufficient. Concern (2) is observability/
audit data, and exposing a field that is *known to be stale under
concurrency* invites confusion in code review and tests: future authors
will see `LastAccessedAt` in the contract, assume it is authoritative,
and write logic that depends on it.

**Concrete recommendation:**

- Keep `CreatedAt` (set once, never mutated, accurate).
- Keep `ExpiresAt` (computed from store policy, accurate within the snapshot).
- **Remove `LastAccessedAt`** from the `Session` record.
- Sliding expiration is implemented invisibly via
  `DistributedCacheEntryOptions.SlidingExpiration` — the renewal happens,
  the caller does not need to see a timestamp for it.

If we later need authenticated last-activity (for "active devices" UI),
that is a Fas 1 feature and warrants its own decision — likely an explicit
`TouchAsync` on the store or a dedicated audit log, not a best-effort field
on a DTO.

## Q5: IDistributedCache vs IConnectionMultiplexer

**Recommendation:** Use `IDistributedCache`. Match the existing
`RedisAccessTokenRevocationStore` pattern.

**Reasoning:**

- `IDistributedCache` is already registered, configured (with the
  `jobbpilot:` instance prefix), and used by the only other Redis-backed
  auth store. Consistency is itself an architectural value here — one
  abstraction, one configuration path, one set of failure modes to
  understand.
- For STEG 4b's `ISessionStore` operations (Get / Create / Invalidate),
  `IDistributedCache` provides exactly what is needed:
  - `GetStringAsync` / `SetStringAsync` for the session payload
    (JSON-serialized `Session` minus its mutable bits).
  - `DistributedCacheEntryOptions.SlidingExpiration` for TTL renewal.
  - `RemoveAsync` for invalidation.
- `IConnectionMultiplexer` would only be justified if we needed:
  - Lua scripting for atomic read-modify-write (not needed; see Q4).
  - Pub/sub (not in scope).
  - Server-side `SCAN` for `InvalidateAllForUserAsync` (deferred to Fas 1
    per Q7, and even then a per-user index key is the better design, not
    `SCAN`).

  None of these apply to STEG 4b.

**Concrete interface consequence:** Because `IDistributedCache` cannot do
atomic read-modify-write, `LastAccessedAt` is dropped (per Q4). The
implementation stores the JSON payload at `session:{id}` with sliding
expiration, and the renewal is a property of the cache entry, not of the
DTO.

## Q6: DI lifetime

**Confirm: Scoped.**

**Reasoning:** All existing Redis-backed implementations
(`RedisAccessTokenRevocationStore`, `RefreshTokenStore`, etc.) are
registered `AddScoped`. The wrapped `IDistributedCache` is Singleton, which
is fine — Scoped-over-Singleton is a standard and correct pattern in
ASP.NET Core. Matching the existing convention has no downside and avoids
introducing a lifetime exception that future readers would have to puzzle
out.

If `RedisSessionStore` itself becomes stateless (which it should — all state
in Redis), the lifetime is functionally indistinguishable between Scoped
and Singleton, so Scoped wins purely on consistency.

## Q7: InvalidateAllForUserAsync

**Confirm deferral to Fas 1.**

**Reasoning:** The cited use cases (logout-all-devices, account deletion,
GDPR erasure) are real but none of them are in STEG 4b's scope. Adding the
method now would force a design choice (per-user index key? `SCAN`?
secondary store?) without a concrete consumer to validate the choice
against — exactly the kind of speculative API that ages badly.

**Interface change required when added (Fas 1):**

```csharp
Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct);
// Returns count of sessions invalidated (useful for audit / response copy).
```

Implementation will require a per-user session index in Redis (e.g.
`user-sessions:{userId}` as a SET of session IDs, maintained by `CreateAsync`
and `InvalidateAsync`). Document this in the Fas 1 ADR when the feature
lands — the per-user index is a non-trivial addition that touches every
write path.

**Note for the implementer now:** Build `CreateAsync` and `InvalidateAsync`
in a way that does **not** make adding the per-user index later painful —
i.e. don't bake assumptions that "session-id is the only key" into the
public contract. Keeping the interface narrow (just the three methods) is
exactly the right move.

## Proposed final interface

```csharp
// src/JobbPilot.Application/Common/Abstractions/ISessionStore.cs
namespace JobbPilot.Application.Common.Abstractions;

public interface ISessionStore
{
    Task<Session?> GetAsync(string sessionId, CancellationToken ct);

    Task<Session> CreateAsync(Guid userId, CancellationToken ct);

    Task<bool> InvalidateAsync(string sessionId, CancellationToken ct);
}

public sealed record Session(
    string Id,
    Guid UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
);
```

**Implementation contract (for the scaffolding agent):**

- `RedisSessionStore` uses `IDistributedCache` (matches
  `RedisAccessTokenRevocationStore`).
- Key format: `session:{id}` (instance prefix `jobbpilot:` is added
  automatically by `IDistributedCache` configuration).
- Payload: JSON-serialized `Session` record.
- TTL: `DistributedCacheEntryOptions.SlidingExpiration = <configured
  session-lifetime>`. `ExpiresAt` on the returned record is computed from
  `DateTimeOffset.UtcNow + SlidingExpiration` at read time and is a hint,
  not a guarantee — the renewal-on-access semantics make a precise
  expiration unrecoverable from the client side, which is acceptable.
- `IDateTimeProvider` is injected for `CreatedAt` (CLAUDE.md §5.1 forbids
  `DateTime.UtcNow`).
- Session ID generation: `RandomNumberGenerator.GetBytes(32)` →
  `Base64Url.Encode` (32 bytes = 256 bits, matches industry session-token
  entropy norms; encode as URL-safe base64 for cookie safety).
- DI lifetime: `Scoped`.
- `InMemorySessionStore` mirrors the contract using `ConcurrentDictionary`
  + `IDateTimeProvider` for tests; same lifetime.

## Deviations from proposal

1. **Namespace:** `Application/Common/Abstractions/` instead of
   `Application/Auth/Sessions/` — match established convention.
2. **`LastAccessedAt` removed** from the `Session` record — cannot be
   maintained accurately with `IDistributedCache`, and a known-stale field
   on a public DTO is a footgun. Sliding-expiration renewal still happens
   at the cache layer.
3. **`IDistributedCache` confirmed** as the Redis abstraction (the proposal
   was ambiguous between `IDistributedCache` and `IConnectionMultiplexer`;
   pick `IDistributedCache` for consistency).
4. **`SessionId` stays `string`** — explicitly rejecting the
   `readonly record struct` wrapping for this case; ADR 0011 applies to
   domain identities, not opaque transport tokens.

All other proposal elements (sealed record, three-method interface,
deferral of `InvalidateAllForUserAsync`, Scoped lifetime) are confirmed.

---

## Post-hoc-anteckning (2026-05-06, Turn 4)

**§3-rekommendationen "CurrentUser ska vara orörd" klargjord.**

Turn 3-rapporten rekommenderade att `CurrentUser` inte skulle behöva ändras i
Turn 4 eftersom `SessionAuthenticationHandler` sätter `HttpContext.User` direkt.
Det stämmer för den primära autentiseringsflödet.

Vad som missades: `LogoutCommandHandler` behöver veta *vilken* session som
ska invalideras, och det beslutet kan inte fattas i Application-lagret utan
tillgång till `SessionId`. Att gå direkt mot `HttpContext.Items["SessionId"]`
från Application är ett Clean Architecture-brott (ADR 0001) — Application
får inte bero på HttpContext.

Lösning: `ICurrentUser` utvidgades med `SessionId? SessionId` (läst från
`HttpContext.Items["SessionId"]` i `CurrentUser`-implementationen i
Infrastructure). Application-lagret ser bara `ICurrentUser.SessionId` —
ingen HttpContext-referens läcker in. Implementation genomförd i Phase 2.
