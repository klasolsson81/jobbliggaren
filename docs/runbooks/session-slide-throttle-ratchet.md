# Runbook — ratcheting the session sliding-write throttle (`Session:SlideThreshold`)

**Owner:** backend / auth. **Introduced by:** #746 PR-A (epic #737, finding `d2-auth-hot-path-7-roundtrips`).

## What the knob is

`RedisSessionStore.GetAsync` refreshes the session's sliding TTL — `SADD` + `KeyExpire` on the
user-sessions index SET and a `SetString` on the main key — on every authenticated read. The
throttle skips that rewrite while less than `SlideThreshold × SlidingTtl` has elapsed since the last
slide (`SlidAt` in the Redis payload). `SlideThreshold` is a `double` on `SessionStoreOptions`
(config key `Session:SlideThreshold`), validated at startup by `SessionStoreOptionsValidator`
(`ValidateOnStart()`) to `[0.0, 0.25]` — a bad value fails the boot, not a request.

**Shipped default: `0.0` = throttle OFF** (behaviour identical to pre-#746). The mechanism lands
inert; enabling it in production is a **deliberate ops decision**, gated by this runbook.

Measured effect (isolated Redis, 40-read browse session): sliding-write commands `160 → 0` when the
reads fall inside the window. Steady state: at most one slide per `SlideThreshold × SlidingTtl` per
session.

## The residual it introduces (TD-23 amendment — not a separate TD)

The user-sessions index SET self-heals a lost membership (a TD-23 partial-write orphan, or a Redis
`maxmemory` eviction, while the main key still lives) by re-`SADD`ing on each slide. The throttle
widens that self-heal cadence from **every read** to **every `SlideThreshold × SlidingTtl`**
(worst case: Persistent `30d × 0.25 = 7.5d`). At the shipped `0.0` the residual is **nil**.

- **Account deletion (GDPR Art. 17) is unaffected** at any threshold — the `:deleted` tombstone gate
  runs on every read *before* the throttle, and the absolute-cap eviction likewise.
- The residual only touches **logout-everywhere / password-reissue** reaching an *already-orphaned*
  session (index membership already lost), which `InvalidateAllForUserAsync` finds via the SET.

## Pre-ratchet checklist (before setting `SlideThreshold > 0` in production)

Required by the #746 PR-A review panel (security-auditor 0/0 APPROVE, code-reviewer, dotnet-architect,
test-writer) as the conditions under which enabling the throttle is safe:

1. **Let live sessions migrate first.** Sessions created before the PR-A deploy were written with
   `SlidingExpiration` (they carry `sldexp` and auto-slide on read) and only migrate to
   `AbsoluteExpirationRelativeToNow` on their first read. If the throttle is enabled while such a key
   is read *within* its window, the main key auto-slides but the throttle skips the SET refresh — a
   transient re-open of the #502 orphan for those keys. **Wait ≥ one max `SlidingTtl` (Persistent =
   30 days) after the PR-A deploy** so every live session has been rewritten as Absolute, or confirm
   via traffic that active sessions have all been read at least once.
2. **`>0.1` on the Persistent profile → add the `:revoked` read-path gate first.** Give
   `RedisSessionStore.GetAsync` a `:revoked`-tombstone check with the same placement as the
   `:deleted` gate, so logout-everywhere / password-reissue gets the same immediate read-path
   backstop as Art. 17 deletion (closes the widened self-heal window for compromise response).
3. **security-auditor sign-off on the chosen value** (the enforceable meaning of the CTO bind
   2026-07-19: the `[0.0, 0.25]` cap is mechanical; the `>0.1` sign-off is procedural). Changing
   `appsettings*` auto-triggers the security-auditor agent; an ops-level env/secret override does
   not, so route the change through a PR.
4. **Add the host-boot validation test** (`Host_ShouldFailToStart_WhenSlideThresholdExceedsCeiling`
   via `ApiFactory`) — PR-A ships the validator unit test + a DI-resolution test, but the eager
   `ValidateOnStart` boot-failure is only proven at the wiring-copy level. Land the full host-boot
   proof before relying on the ceiling in production.

## Enabling / measuring / rolling back

- **Enable:** set `Session:SlideThreshold` (start at `0.1`) via config/managed secrets and redeploy.
- **Measure:** `redis-cli INFO commandstats` around a scripted authenticated browse session, before
  and after; expect `cmdstat_sadd` / `cmdstat_pexpire` / `cmdstat_hset`-family calls to drop. See
  `docs/runbooks/performance-measurement.md`.
- **Roll back:** set `Session:SlideThreshold` back to `0.0` and redeploy — instant, no data change
  (the knob only governs write frequency; existing keys keep their TTLs).
