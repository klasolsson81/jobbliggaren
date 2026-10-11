# ADR 0162 — The notification bell is a polled, read-only union of per-source feeds: one row per (source, ad), a token read first, and a consent gate on the match source

**Date:** 2026-10-11
**Status:** Accepted
**Decider:** `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; ruling on Q1–Q15 and the binding final shape,
report-only; `docs/reviews/2026-10-11-1666-form-cto.md`, local), on the brief (`…-form-brief.md`) and the reports of `dotnet-architect`
(`…-form-dotnet-architect.md`), `security-auditor` (`…-form-security-auditor.md`; one GDPR Blocker, B1, adopted in full) and
`design-reviewer` (`…-form-design-reviewer.md`). Klas Olsson's standing directions are applied here, not re-decided: the notification
doctrine (2026-09-05), dark mode (2026-06-28 and 2026-06-30), accessibility is not MVP (2026-10-06), and the product decision recorded in
ADR 0080 Beslut 5 (2026-06-24). The round needed no STOPP to Klas.
**Amends:** nothing. ADR 0080 is read and cited, not amended: its amendment belongs to #2106 (ADR 0161). ADR 0018 is applied through its
Amendment of 2026-10-10, not changed.
**Amendment 2026-10-11:** the panel holds notifications only (#2130; D3, D4, D6, D11) — see
[§Amendment 2026-10-11](#amendment-2026-10-11--the-panel-holds-notifications-only-2130).
**Related:** [#1666](https://github.com/klasolsson81/jobbliggaren/issues/1666) (the issue this ADR ships under) ·
[#1662](https://github.com/klasolsson81/jobbliggaren/issues/1662) (the notification epic) · #2106 (a Toppmatch within minutes, in parallel) ·
#2107 (the e-mail notice controls) · #1663 (smart-watch criterion hits, not built: the next source) · #1576 (the follow window rule) ·
#273 (the uncapped new-match count) · ADR 0018 (Amendment 2026-10-10: no write on a GET) · ADR 0045 (budgets) · ADR 0080 (Beslut 5
consent, Beslut 6 the watermark) · ADR 0087 D8 (the opaque watch join) · ADR 0113 (every `JobAds` read carries a lifecycle decision) ·
ADR 0120 (a rendered count is true or absent, clauses 1–5) · ADR 0140 (the Översikt event log, deferred) · ADR 0144 (the self-evident is
not explained) · ADR 0146 D3 (`IReplayOnConcurrencyConflict`) · ADR 0148 (the stale Server Action boundary) · ADR 0154 (every merge goes
live) · ADR 0161 (#2106: the five-minute matching run, unmerged when this was written)
**Measured against:** `origin/main` at `beac82b17`, 2026-10-11. D1–D13 describe what the branch `feat/notifications-bell` (PR #2125)
builds; nothing here is merged at the time of writing.

> **Provenance.** Written by `adr-keeper` for the driving session from the CTO's ruling and the delivered code (CLAUDE.md §9.2, §13). The
> sources, the unit, the token protocol, the horizon, the acknowledgement rule, the consent gate, the count bound, the cadence and the
> scope boundary are the CTO's ruling; the mandatory agents review the implementation in the PR. Numbers that describe the branch (nine
> statements, `MaxCountedHits`) are dated measurements or derivations, not promises. ADR 0161 was read in #2106's worktree on 2026-10-11
> and may change before it merges.

---

## Context

The bell in the shell header (`NotificationsBell`, `app-shell.tsx:69-106` at `6292ee856`) is a popover that always rendered "Inga nya
aviseringar." and read nothing. #1666 makes it a live, per-user feed. Five facts shape the design.

- **"New" already has one home per source.** Two watermarks on `JobSeeker`: `LastSeenMatchesAt` (ADR 0080 Beslut 6; the new-match count is
  uncapped by the #273 contract) and `LastSeenFollowedAdsAt` (`NewFollowedCompanyAdSet`, the single definition of ADR 0120 shared by the
  Översikt count and the `/foretag/bevakade/nya` list; the #1576 rule). Each is acknowledged by visiting its destination, which posts the
  newest row's `createdAt` to a `/seen` endpoint. Both seen commands are `IReplayOnConcurrencyConflict`, and the setters clamp to now and
  only move forward. Sent without a body, either falls back to the clock.
- **Constraints the CTO set.** The bell reads existing rows only (`UserJobAdMatch`, `FollowedCompanyAdHit`): no store of its own and no
  writing job. It writes nothing per open, because `job_seekers` carries a whole-row `xmin` token (ADR 0146). It is a union of sources so
  that #1663 adds one later. It edits no shared DI root (#2106 owns them) and takes no migration (the token is released to #2106).
- **The decision is architectural.** A union across two bounded contexts, a static registry instead of DI, a token protocol and a horizon
  rule are read-model decisions. ADR 0140 deferred a real event log to #1662/#1666. And the #1576 rule that a window is never acknowledged
  past exists only in code comments (`ListNewFollowedCompanyAdsQueryHandler.cs:57-60`), so a ruling that departs from it for one case has
  no binding home without an ADR.
- **Match rows are produced under consent and outlive it.** `BackgroundMatchingJob` writes them only for users who hold
  `NotificationConsent.BackgroundMatch`, but a withdrawal (`JobSeeker.UpdateNotificationConsent`) stamps the withdrawal and deletes nothing,
  and no existing read path tests consent. A bell that read the rows on `UserId` alone would keep notifying after a withdrawal.
- **A poll multiplies cost.** The follow count's grade path loads every new hit into memory and grades every "endast matchade" ad
  (`GetNewFollowedCompanyAdCountQueryHandler.cs:88-92`). Översikt paid that once per page load; a bell pays it every five minutes for every
  visible tab, and nothing limits how many companies a user follows (`CompanyWatchCriterion.MaxPerUser = 20` limits criteria).

## Decision

**The bell is a read-only union of the sources' existing rows, fetched by polling.** Each source answers, under the definition its own
context already owns, an exact unread count, the token that acknowledges exactly that count, and its newest rows. Acknowledgement goes
only through the two existing `/seen` writers. Nothing is written on any GET.

### D1 — The source contract: a static list of stateless internal sources

`Application/NotificationFeed/` holds `internal interface INotificationFeedSource` and
`internal static IReadOnlyList<INotificationFeedSource> NotificationFeedSources.All { get; }`, which lists `MatchFeedSource` and
`FollowedCompanyAdFeedSource`. A source is **stateless**: one instance serves every request, and anything per request travels in
`internal sealed record NotificationFeedReadContext(Db, UserId, MaxItems, ProfileBuilder, PerUserSearch)`.

- **No DI registration, no DI-root edit.** The handler is generator-registered and therefore `public`; a public constructor cannot take an
  `internal` interface (CS0051), and registering the sources would need public types and an edit to a root #2106 owns.
- **The reflection test** (`NotificationFeedSourcesTests`, `Application.UnitTests`) fails the build when a concrete source class is missing
  from `All` or listed twice, when the sources' `Kind`s are not exactly the members of `NotificationFeedSourceKind`, one each, or when a
  source holds an instance field.
- **The handler** (`GetMyNotificationFeedQueryHandler`, public) injects `IAppDbContext`, `ICurrentUser`, `IMatchProfileBuilder` and
  `IPerUserJobAdSearchQuery`, and runs the sources one after another, never `Task.WhenAll`: they share one scoped, non-thread-safe
  `DbContext`. With no signed-in user it returns `NotificationFeedDto.Empty`.
- **The promise is stated honestly.** A new source is one class, one line and possibly a constructor dependency, and **always a
  `security-auditor` review**, because every source is a new data flow into a per-user surface. The reflection test proves a source is
  listed, not that it was reviewed.
- **The contract every source keeps** is the docblock of `INotificationFeedSource`: it reads existing rows only; "new" is its context's own
  watermark and predicate, composed and never restated; its token is the newest unread row's timestamp, read first (D3); it is read under
  the legal ground its rows were produced under (D6); it is stateless.

### D2 — The unit: a (source, ad) pair, never merged across sources

**A notification is one row per source event, labelled by its reason.** An ad that is both a background match and a followed company's new
ad is **two** notifications for two reasons, each acknowledged through its own watermark by its own writer. Within the follow source the
hits collapse to one row per ad (`NewFollowedCompanyAdSet.CollapseToAds`, #1576: an ad belongs iff at least one of its hits passes
`Includes`). The badge is the sum of the per-source counts, so the same ad contributes once per source.

- A merge across sources would tie one row's "unread" to two watermarks and two writers: a coupling between two bounded contexts for no
  gain. Each source is read under its own legal ground (D6), which a merged row could not honour.
- **A follow row is unread** iff an *included* hit with `CreatedAt` after the watermark exists, and its `occurredAt` is the newest
  included hit of that ad in the window. An ad whose only new hit the grade filter excludes is therefore no notification, rather than a
  phantom row beside a count that did not count it. The number of unread rows is at most the source's `unreadCount` and equal to it when
  the whole set fits the window.

### D3 — The token protocol: read first, then bound the count and the list by it

Per source, in this order:

1. `token = MaxAsync(x => (DateTimeOffset?)x.CreatedAt)` over the source's new set (the cast keeps `MAX` from throwing on an empty set). For
   the follow source this is the newest of **all** `NewHits`, including those the grade filter excludes, so an excluded older hit cannot
   hold the watermark down and re-show the same ads (#1576).
2. The count uses `CreatedAt <= token`.
3. The list uses `CreatedAt <= (token ?? watermark)`. When both are null the source has no rows and reads no list.
4. The token reaches the client as a string and travels **unchanged** to the source's existing `/seen` endpoint. Postgres keeps microseconds
   and a JavaScript `Date` keeps milliseconds, so a parsed and re-serialised token lands just below its own row and the badge would never
   reach zero.

The effect is that the number shown is exactly what the token acknowledges. A row committed after step 1 is neither counted nor listed, and
stays unread for the next poll (pinned for both sources by `A_match_committed_after_the_token_is_neither_counted_nor_listed` and
`A_followed_hit_committed_after_the_token_is_neither_counted_nor_listed`).

**The single-writer assumption, and its lapse condition.** The protocol is sound only if a row committed after a read is stamped later
than every row that read saw. That holds while **one writer per source** produces the rows, and today each producer runs under
`[DisableConcurrentExecution(timeoutInSeconds: 1800)]` (`BackgroundMatchingWorker.cs:19`, `CompanyWatchScanWorker.cs:19`, at `6292ee856`).
**The lapse condition is a second concurrent producer of either source's rows** (for example, if #2106 lets two matching runs overlap, or
a writer other than the job is added). Then a row stamped before the token could commit after it, and both the bell and `/matchningar` would
acknowledge it unseen. The protocol must be revisited before any such producer ships. ADR 0161 keeps a lock on the matching worker with a
shorter wait; whether that holds is that ADR's, and this one only records what depends on it.

**An older residual is not widened:** a row stamped before the token but committed after both statements is acknowledged without having
been counted. That is true of both destinations today.

### D4 — The merge, the window and the horizon

The panel shows the **20 newest rows, read ones included**, newest first, on **Active ads only** (D8). The merge orders by `occurredAt`
descending, then `kind`, then `jobAdId`, and takes 20. Counts are never capped by the row limit.

- **The match source** reads its exact top 20 by `CreatedAt` descending and filters nothing after reading, so it has **no horizon**.
- **The follow source** reads the newest `HitsPerRow × MaxItems + 1` hits (`HitsPerRow = 3`) and
  filters them after reading: the inclusion rule, the grade filter, the collapse to ads. An ad whose included hits all lie beyond the
  window can therefore be missing. When the hit just past the window exists, its `CreatedAt` is the source's **horizon**.
- **The merge cuts at the newest horizon any source reports.** The merged list is clipped to `occurredAt > max(horizons)` before the cap.
- **One grading call per poll**, over the union of the counted and the displayed ad ids.

### D5 — Acknowledgement: an explicit "mark all" acknowledges the whole counted set (CTO Q3, Q3a)

**A Server Action calls the two existing `/seen` writers.** `markNotificationsReadAction` sends each source's `acknowledgedThrough` token
through `markMatchesSeen` and `markFollowedAdsSeen`, on a click only, sequentially and independently: one may succeed while the other
fails, and the bell fetches the feed again afterwards so it shows what actually happened (ADR 0148's boundary applies).

- **Token hygiene.** Each token is validated as an ISO date-time with an offset before the call and then passed on as the original string.
  A `null` or invalid token sends **no** POST for that source: never `undefined`, which the backend reads as "through now" and which would
  swallow rows the user never saw.
- **The scope of the rule (Q3a).** An **explicit** "Markera alla som lästa" over a **shown, exact** count acknowledges that source's whole
  counted set (`CreatedAt <= token`), including rows outside the 20 shown. An **implicit** acknowledgement by visiting a destination keeps
  the #1576 window rule, and `/foretag/bevakade/nya` still acknowledges only the window it showed. The explicit act is not a silent
  swallow: the user acts on a number the header printed. **Precedent:** `/matchningar` already acknowledges past its 50-row display cap
  (#273): `GetMyMatchesQueryHandler` lists the newest 50, and the page posts the newest row's `createdAt`, which covers every older new
  match, shown or not.
- **The condition for the rule** is that the header shows exactly what will be acknowledged. D7 keeps every displayed count exact, and in
  the refused state there is no number and therefore no "Markera alla".
- **Security.** A Server Action is a POST under Next's Origin check (ADR 0018 layer 2). It adds no write surface: two existing commands,
  owner-scoped, monotone, under `MeWritePolicy`. A forged token moves only the caller's own watermark.

### D6 — Consent per source: each source is read under the legal ground its rows were produced under

**The match source is read only while `NotificationConsent.BackgroundMatch` holds** (`security-auditor`'s B1; ADR 0080 Beslut 5, GDPR
Art. 6(1)(a) and 7(3)). The source tests the user's `JobSeeker` against that `Specification<JobSeeker>`, the same definition the
background job's due set uses, so the rule lives in one place. Without current consent the source returns no rows, `unreadCount: 0` and
`acknowledgedThrough: null`, and the wire shape does not change.

- **The gate sits on the source**, not in `NewMatchSet`: `/matchningar` and the new-match count keep reading the rows, and
  `NotificationConsent.cs` is read and not edited.
- **The bell is a notification**, and ADR 0080 Beslut 5 stops dispatch on withdrawal; the privacy policy promises "Då upphör vi att
  skicka dem". A notification after a withdrawal is processing for a purpose whose consent was withdrawn, and it would make a published
  transparency statement untrue (Art. 5(1)(a), 12(1)).
- **The follow source is not gated.** Its rows are produced for every active follow without consent, they already render on Översikt and
  `/foretag/bevakade/nya`, and its consent governs e-mail only.
- **No amendment to ADR 0080 is made here.** #2106 owns it. Whether the e-mail off-switch of #2107 withdraws `BackgroundMatch` or only the
  e-mail channel decides whether the bell goes quiet with it, and that is for #2107.
- **Not widened:** match rows stay in place after a withdrawal and stay visible on `/matchningar` until the ad expires. That is documented
  in the processing register (`security-auditor` read it at `gdpr-processing-register.md:600`) and is a separate question; B1 stops the bell
  from enlarging it. `security-auditor`'s Escalation went to Klas through the driving session. His decision is needed only if someone
  wants matches shown after a withdrawal, which would need new consent and privacy text.
- **Register and policy.** No register row, no privacy-text change and no `CurrentPrivacyPolicyVersion` bump, on `security-auditor`'s ruling,
  which holds only while B1 holds: the bell shows existing rows to the data subject on the same origin, with no new category, recipient
  or purpose, and the grade is the category the profiling already produced.

### D7 — Counts are exact; the follow grade path is bounded, and past the bound there is no number (CTO Q7)

- **Match source:** an exact `COUNT` over `NewMatchSet` with `CreatedAt <= token`, cheap through `(UserId, CreatedAt DESC)`. The #273
  contract (uncapped) is the definition, and capping it gains nothing.
- **Follow source, common path** (no "endast matchade" watch): `COUNT(DISTINCT job_ad_id)` with `<= token` in SQL. Nothing is loaded and
  nothing is graded.
- **Follow source, grade path:** the hit read becomes a probe, `OrderBy(CreatedAt).Take(MaxCountedHits + 1)`. Within the bound the count is
  exact through `ResolveMatchingAsync` and `CollapseToAds`. Past it the source returns **`unreadCount: null`**. It never counts over the
  truncated read and never renders "N+": ADR 0120 clause 5, then clause 3. The refusal is decided by the one extra row, so "more than
  the bound" is told from "exactly the bound" without a second query.
- **`MaxCountedHits = 10_000`** (`FollowedCompanyAdFeedSource`) is the bell's own constant, deliberately not tied to
  `ListNewFollowedCompanyAdsQueryHandler.MaxRows` or `CriterionMatchingAdSetResolver.MaxSetSize` (ADR 0120: reused as a pattern, never
  welded). It is **derived, not measured**: at the bucket's 12 calls a minute it caps graded ids at 120 000 per user and minute, the order
  `CompanyWatchCriteriaList` already accepts (20 criteria × 2 000 × 3 a minute sustained). It is a size bound and makes no latency claim.
- **The bound is a parameter of the shared count** (`NewFollowedCompanyAdSet.LoadCountInputAsync(…, upperBound, maxGradeHits, …)`), so the
  refusal can be tested with a small value (`ReadAsync_PastTheCountBound_RefusesTheCount_ButStillListsRows`). Översikt passes neither
  argument, so its SQL and its result stay byte-identical and its tests are unmodified.
- **Rendering.** While every source count is non-null the total is their sum: the badge shows the exact number up to 99 and "99+" above
  (ADR 0120 clause 2), and the header always shows the exact number. In the **refused state** (any source `null`) the badge neither
  disappears nor shows a digit: it is an empty pill, the button's accessible name says unread items exist without a number, the header has
  no count line, "Markera alla som lästa" is not shown, and the rows render as usual. A missing badge on a bell reads as zero, so a refusal
  must not look like "no badge". A refusal needs more than 10 000 unacknowledged followed hits on an account with an "endast matchade" watch,
  so it works as a guard.
- **Pre-existing, named and not graded:** Översikt's `GET …/followed-company-ads/new-count` runs the same unbounded grade path under
  `MeListRead`. No agent graded it, it was not measured, and nothing is filed for it. The bound parameter gives it a way to adopt the same
  limit; doing so changes Översikt's rendered contract and is a separate change-reason.

### D8 — The wire: one GET, a pinned DTO, Active ads only

`GET /api/v1/me/notification-feed` (`MeNotificationFeedEndpoints.cs`, tag `Me`, `RequireAuthorization()`, `Cache-Control: private,
no-store`) takes **no route or query parameters**: the user comes from `ICurrentUser` only, so there is no IDOR surface. It sits in its own
file because "notification" already names the e-mail consents under `/api/v1/me`.

- **The DTO** `NotificationFeedDto(Items, Sources)`. An item is `{ kind, jobAdId, title, companyName, occurredAt, isUnread, grade | null }`
  and a source is `{ kind, unreadCount: int?, acknowledgedThrough }`, with `kind` as the string enum `NotificationFeedSourceKind`
  (`Match`, `FollowedCompanyAd`). **Never in it:** an organisation number, a `CompanyWatchId`, a criterion, match evidence, a score or rank,
  `NotificationStatus` or `SeenAt`, the ad's external URL, or any consent flag. The row links to `/jobb/{jobAdId}`. Rows are sorted by time,
  never by grade. A match row carries its **stored** grade; a follow row carries none and no grade is
  computed for display.
- **Lifecycle (ADR 0113).** The gate is the allow-list `Status == Active`, never `!= Archived`; an Art. 17 tombstone gives neither a row
  nor a count. It is inherited from `NewMatchSet.Matches` and `NewFollowedCompanyAdSet.NewHits`; no source adds a `Status` predicate, and
  `JobAds` is joined at statement level, never inside an expression lambda. `JobAdLifecycleReadRegistry` carries
  `NewMatchSet.Matches` (moved from the count handler), `MatchFeedSource.ReadAsync` and `FollowedCompanyAdFeedSource.ReadBoundedAsync`.
- **One definition per source.** `NewMatchSet.Matches(db, userId, since)` returns the entity (`IQueryable<UserJobAdMatch>`), because a
  constructor projection cannot be composed onto and throws inside EF's shaper; `GetMyNewMatchCountQueryHandler` is a thin wrapper over
  it. The follow source composes `NewFollowedCompanyAdSet` and writes no predicate of its own.

### D9 — Delivery: polling through a BFF route, and no write on any GET

**The client polls; the server never writes to answer.** ADR 0018's Amendment of 2026-10-10 lets a top-level cross-site GET reach an
`(app)` page signed in, so rule 3 ("no state changes on GET") now carries the CSRF protection, and any new write at render reopens the
amendment. The bell is mounted on every `(app)` and `(admin)` page, so it writes nothing on open, hover or poll.

- **Cadence.** `useNotificationFeed` reads on mount, every five minutes while the tab is visible, and when the tab becomes visible or the
  panel opens (each throttled to 30 s). Five minutes is half of the ten-minute ad ingest: "at once" for in-app use once #2106 lands. The
  pattern is `admin-overview-live`: an `AbortController` and one read in flight, aborted when the tab hides; 401 and 403 stop the poll;
  `Retry-After` on a 429 is obeyed; a failed read **keeps the last value and marks it stale**, and never becomes "nothing unread" (ADR 0120).
- **The BFF route** `app/api/me/notification-feed/route.ts` is GET only, writes nothing, and sets `private, no-store` on **every** branch. It
  forwards `request.signal`, ignores the query string, and re-projects the body through a Zod allow-list. Status mapping: 401→401, 403→403,
  429→429 with `Retry-After`, everything else→502 with an empty body, **never a 200 carrying empty or invented data**. `/api` lies outside
  the proxy's matcher, so the route never meets the Strict-cookie bounce.
- **No layout fetch** (shell TTFB, ADR 0045). **No SSE and no WebSocket**: a new transport on the single box for a five-minute freshness
  need.

### D10 — The rate-limit bucket

The route has its own policy, `notification-feed-read` (`RateLimitingExtensions.NotificationFeedReadPolicy`), configured by
**`RateLimitingOptions.NotificationFeedRead`**. The numbers and their derivation live there and only there. It is a token bucket
partitioned on the `sub` claim; an anonymous caller gets `NoLimiter`, which is sound because `RequireAuthorization` answers 401 first.

- **Not `MeListRead`:** the cost class differs (two sources, and with an "endast matchade" watch a profile build and a grading round
  trip), and a background poll on every page must neither starve Översikt's fan-out nor be starved by it. The option's docblock says to
  re-derive when a source is added (#1663).
- **The measured statement count** without an "endast matchade" watch is nine, pinned by
  `One_request_without_an_only_matched_watch_runs_nine_statements`; the profile build and the grading call are the added cost with one.

### D11 — The empty state describes and never invites

The never-anything and nothing-unread states say what appears in the bell and ask for nothing. The heading is "Inga aviseringar än" and
the one sentence describes what lands there ("Nya matchningar och nya annonser från företag du bevakar visas här." on the branch; the final
wording is confirmed by `design-reviewer` and `security-auditor` in the build round). **The bell has no settings link at all**, and its
footer carries only the two destination links (`/matchningar`, `/foretag/bevakade/nya`).

- ADR 0080 Beslut 5 records Klas's product decision (2026-06-24): the opt-in invitation lives in the settings only, passive, with no nudge,
  banner or modal, and "the system does not solicit". An imperative inside the bell ("Slå på matchningen …") is exactly such an invitation,
  and a recorded Klas decision settles a disagreement between two agents.
- There is no follow invitation either: inviting one source and staying silent about the other reads as a hint, and ADR 0144 says the
  self-evident is not explained. Settings stay reachable through the user menu and Mina sidor.

### D12 — Dark mode is handled structurally only

The panel stays correct by construction and is never looked at (Klas 2026-06-28 and 2026-06-30: light-only verification applies even when a
criterion asks for both themes). A scoped `[data-theme="dark"] .jp-notif` rule re-pins the match-grade leaf ramp
(`--jp-leaf-100/600/700/900`), `--jp-follow` and `--jp-follow-bg` to their light values; Stark text keeps `--jp-on-fill`; no `--jp-info*`
text sits on the panel surface; and DESIGN.md §3 carries the extension in the same PR with a §6 entry for the badge and the row form. No new
tokens. No dark render is made and `DARK_MODE_ENABLED` is not flipped. This is a recorded override of the dark-render half of
`design-reviewer`'s B1, which stays a Blocker and closes on the structural diff.

### D13 — What this decision is not

- **It does not deliver ADR 0140's deferred event log.** The bell is a view over existing rows with a watermark; it has no event store,
  no status changes and no company events. Epic #1662 remains that log's home, and Översikt's "Senaste händelser" is unchanged.
- **Web Push is out of scope.** It is a new processing operation with a new recipient, a new category of personal data and its own
  consent, and Web Push is tracked in its own issue (#2124).
- **No migration.** The Q8 index is deferred with a trigger (Consequences).

## Alternatives considered

- **Sources through DI.** Rejected (D1): it forces public types and an edit to a root #2106 owns, and the OCP gain is the same.
- **One merged row per ad across sources.** Rejected (D2): it couples two acknowledgements and two writers inside one row.
- **Q3 (a): acknowledge only by visiting the destinations.** Rejected: the user cannot bring the badge to zero from inside the bell
  (`design-reviewer`'s B2).
- **Q3 (c): a new combined acknowledge command.** Rejected: a new write surface with no security gain, and ADR 0146 D3 would need an
  amendment.
- **An unbounded exact follow count.** Rejected (D7): an unbounded load into memory on a poll path breaks AGENTS.md §5 and ADR 0045.
- **Saturating at 100 included ads.** Rejected (D7): it does not bound the worst case. Stopping early only helps when ads pass the grade
  filter; the expensive case is an "endast matchade" watch on a large employer whose ads mostly grade below Good, where every candidate is
  graded before anything is known. This is the CTO's analysis and was not measured.
- **The `MeListRead` bucket.** Rejected (D10).
- **A layout fetch.** Rejected (D9): it adds to shell TTFB.
- **SSE or WebSocket.** Rejected (D9).
- **An imperative empty state.** Rejected (D11): it contradicts ADR 0080 Beslut 5.
- **Dark renders and a `DARK_MODE_ENABLED` flip.** Rejected (D12).
- **Web Push in this PR.** Rejected (D13): a new processing operation.
- **Split backend and frontend PRs.** Rejected: a backend-only merge goes live (ADR 0154) as an endpoint and a policy with no caller, and
  the wire semantics (null, token, refusal) and their only consumer have to be reviewed together.
- **The Q8 index now.** Rejected: nothing measured shows a need, and a migration costs a Klas GO and the most dangerous hotspot while #2106
  runs alongside.

## Consequences

### Positive

- The bell is true or absent: every shown number is exact, and a refusal, a failed read and a withdrawn consent each show something
  other than zero.
- Each count is the existing definition bounded by a token, so the bell cannot disagree with Översikt or `/matchningar` for the same
  state (Testcontainers parity tests, both sources), and "Markera alla" acknowledges exactly the printed number.
- No new write surface, no store, no migration, no shared DI-root edit; a source costs one class and one line.
- A withdrawn background-match consent silences the match source at read time without touching `/matchningar` or the count.

### Negative and risks

- **s1. Over 10 000 unacknowledged followed hits** on an account with an "endast matchade" watch shows "olästa finns" without a number and
  without "Markera alla". Honesty is chosen over a number.
- **s2. Översikt's own unbounded grade path** is left as it was (D7); if `security-auditor` grades it, CLAUDE.md §9.6 routes it.
- **s3. The single-writer assumption** (D3) is carried by a lock, not by a test of this ADR. A second concurrent producer of either source
  makes the bell and `/matchningar` swallow rows.
- **s4. An ad that is both a match and a followed hit counts twice** in the badge, once per source, by design (D2).
- **s5. The Q8 index.** On a table that keeps growing, the follow count path is O(hits per user) through
  `ix_followed_company_ad_hits_user_id`, with no measurement behind that, and no age-based pruning of hits was found
  (`dotnet-architect`). With two accounts today the cost is negligible. **Trigger:** add `(user_id, created_at DESC)` on
  `followed_company_ad_hits`, replacing `ix_followed_company_ad_hits_user_id` (the prefix gives the same equality on `user_id`, so the write
  cost nets to zero), **when a measurement shows the follow count path outside ADR 0045's budget**. That needs `db-migration-writer` and
  Klas's migration GO. The deferral is a named skip in the PR body, not an issue.
- **s6. Dark mode is correct by construction and never looked at** (D12).
- **s7. A poll slides the session window** on each authenticated read; `security-auditor` measured that every sliding window is at least
  24 hours and every absolute cap stays, so a visible tab changes no inactivity limit of consequence.
- **s8. Match rows stay after a withdrawal** and stay visible on `/matchningar` (D6): not created by this decision, not widened by it.
- **s9. `getNewMatchCount()` on the web side has no production caller** (the bell reads the definition through `NewMatchSet`); retiring
  it and its route is a separate change-reason and not part of this PR.
- **s10. The bucket must be re-derived when a source is added.** A new source also needs a `security-auditor` review, for example #1663,
  where an enskild firma's organisation number is its holder's personnummer.

**Lapse triggers.** This decision must be re-examined when: a second concurrent producer of either source's rows appears (D3); a write
appears at render under `(app)` or `(admin)` (D9, which reopens ADR 0018's amendment); a source is added (s10); the e-mail off-switch of
#2107 changes what withdraws `BackgroundMatch` (D6); or a measurement puts the follow count path outside ADR 0045's budget (s5).

## Implementation

One vertical slice in one PR (backend, frontend, DESIGN.md and this ADR), with no migration.

- **Backend.** `Application/NotificationFeed/` (`INotificationFeedSource`, `NotificationFeedSources`, `MatchFeedSource`,
  `FollowedCompanyAdFeedSource`, `GetMyNotificationFeedQuery` and its handler, `NotificationFeedDto`, `NotificationFeedSourceKind`);
  `Application/Matching/Queries/NewMatchSet.cs`; `NewFollowedCompanyAdSet` (`LoadCountInputAsync`, `CountAds`, `GradeCandidates`);
  `Api/Endpoints/MeNotificationFeedEndpoints.cs`; `RateLimitingOptions.NotificationFeedRead` with its policy and constant.
- **Frontend.** `app/api/me/notification-feed/route.ts`, `lib/actions/notification-feed.ts`, `lib/hooks/use-notification-feed.ts`,
  `components/shell/notifications-bell.tsx`, the `.jp-notif` rules in `globals.css`, the DESIGN.md §3 and §6 entries, the new message keys
  in `messages/{sv,en}/common.json`, and the route in the Playwright fakes.
- **Tests.** `Application.UnitTests/NotificationFeed/` (the reflection test, both sources, the merge and the horizon, the refusal);
  `Api.IntegrationTests/NotificationFeed/NotificationFeedEndpointTests.cs` (cross-user isolation, anonymous→401, the wiring of
  authorization and policy, consent in both directions through `JobSeeker.UpdateNotificationConsent`, parity with the two existing
  counts, the tokens against the existing `/seen` endpoints, a row committed after the token, the nine-statement pin); the lifecycle
  registry entries; and the Server Action and component tests.
- **Pointers owed by the PR.** One line at the #1576 rule (the oldest-first comment in `ListNewFollowedCompanyAdsQueryHandler`) and one at
  the Server Action, each pointing here; the binding contract also lives in the `INotificationFeedSource` docblock and the tests above.
- **Coordination, by comment on #1662.** #1666 takes no migration and releases that token to #2106; it claims the `app/globals.css` and
  `DESIGN.md` hotspots for its window; the single-writer residual goes to #2106 and the consent question to #2107.

## References

- `docs/reviews/2026-10-11-1666-form-cto.md` (the ruling) · `…-form-brief.md` · `…-form-dotnet-architect.md` · `…-form-security-auditor.md` ·
  `…-form-design-reviewer.md`
- Code: `src/Jobbliggaren.Application/NotificationFeed/` · `src/Jobbliggaren.Application/Matching/Queries/NewMatchSet.cs` ·
  `src/Jobbliggaren.Application/CompanyWatches/Queries/NewFollowedCompanyAdSet.cs` ·
  `src/Jobbliggaren.Api/Endpoints/MeNotificationFeedEndpoints.cs` · `src/Jobbliggaren.Api/RateLimiting/RateLimitingOptions.cs`
  (`NotificationFeedRead`) · `web/jobbliggaren-web/src/app/api/me/notification-feed/route.ts` ·
  `web/jobbliggaren-web/src/lib/actions/notification-feed.ts` · `web/jobbliggaren-web/src/lib/hooks/use-notification-feed.ts` ·
  `web/jobbliggaren-web/src/components/shell/notifications-bell.tsx`
- ADR 0018 (Amendment 2026-10-10), 0045, 0080, 0087, 0113, 0120, 0140, 0144, 0146, 0148, 0154, 0161 · AGENTS.md §2.5, §4, §5, §8 ·
  CLAUDE.md §6.5, §9.2, §9.6, §12
- Martin, *Clean Architecture* (2017), ch. 7–8 (SRP, OCP) · Evans, *Domain-Driven Design* (2003), "Bounded Contexts" · Hunt and Thomas,
  *The Pragmatic Programmer* (1999), DRY

---

## Amendment 2026-10-11 — the panel holds notifications only (#2130)

**Date:** 2026-10-11
**Source:** `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2): the ruling on the #2130 form round, A4 and point 13 of
its binding form for PR 1 (`docs/reviews/2026-10-11-2130-form-cto.md`, local), on the brief (`…-form-brief.md`, `…-form-brief-addendum.md`) and
the reports of `dotnet-architect` (`…-form-dotnet-architect.md`), `security-auditor` (`…-form-security-auditor.md`), `design-reviewer`
(`…-form-design-reviewer.md`) and `db-migration-writer` (`…-form-db-migration-writer.md`, a consultation without grades), all local. Klas
Olsson's direction of 2026-10-11 on #2130 is applied, not re-decided. The round needed no STOPP to Klas. Recorded by `adr-keeper` for the
driving session from the ruling; it adds no decision of its own.
**Status:** Accepted. D4 and D11 are changed below, D3 gets a dated correction and D6 a dated row. Where the text above differs from what is
stated here, this amendment governs, and the original text stays as the record.
**Measured against:** `origin/main` at `0887dcca3`, 2026-10-11. The amended D4 and D11 describe what PR 1 of #2130 builds (branch
`feat/bell-real-notifications`), which is not merged at the time of writing; the D3 and D6 rows describe `origin/main`.

### Context

ADR 0162 made the bell a live feed (#1666). #2130 records Klas's direction of 2026-10-11: the bell holds notifications, not fixed links that
are always shown, and a setting decides what counts as a notification. Its first acceptance criterion (AC1) is that the panel contains no
link that is not a notification, except at most one "Visa alla" to a notifications page. D11's footer carries two fixed links
(`/matchningar`, `/foretag/bevakade/nya`), so it conflicts with AC1.

Removing the footer is not enough on its own, because D4 as decided can hide unread rows from the panel. Two findings of the form round show
it, both derived from the code and not measured on the box.

- **The selection ignores read state.** It takes the 20 newest rows with read ones included, so 20 newer read matches push out three older
  unread follow rows while the badge counts three. `design-reviewer`'s Blocker 1: the footer, the unread-first selection and the
  not-in-list line land in one PR and never separately.
- **The horizon clips unread rows, and the follow source can count unread ads it never returns** (`dotnet-architect`'s Viktigt 1 and 2).
  The follow window reads the newest 61 hits with read ones included, so every user with at least 61 hits on Active ads has a horizon, and
  the merge then drops every match older than the 61st hit, unread or not. On the grade path the count reads and grades up to
  `MaxCountedHits` hits while the list reads the window and filters it afterwards, so when an "endast matchade" watch excludes the whole
  window the source reports `unreadCount >= 1` and no unread row.

### Decision

**The panel holds notifications and nothing else.** D4 and D11 are changed below; D3 gets a dated correction and D6 a dated row. D1, D5, D7,
D8, D9, D12 and D13 are unchanged.

#### D3 — dated correction of the lock citation

D3 cites `[DisableConcurrentExecution(timeoutInSeconds: 1800)]` on both producers, at `6292ee856`. The matching worker's lock has been 120 s
since ADR 0161 (`BackgroundMatchingWorker.cs:28`); the follow scan's stays 1800 s (`CompanyWatchScanWorker.cs:19`), both read at `0887dcca3`.
There is still one writer per table, so the lapse condition (a second concurrent producer of either source's rows) is unchanged.

#### D4 — the selection, the horizon and the hidden count (changed)

The panel shows **every unread row the sources return first, up to the cap of 20 across the sources, and then the newest read rows above the
newest horizon**, on Active ads only (D8). This replaces D4's "20 newest rows, read ones included". Grounds: ADR 0120, AC2 and
`dotnet-architect`'s Viktigt 1.
With `ByTime` as `occurredAt` descending, then `kind`, then `jobAdId`, `GetMyNotificationFeedQueryHandler.Merge` selects (`maxItems` is 20):

```csharp
unread = ByTime(all.Where(i => i.IsUnread)).Take(maxItems)
read   = ByTime(all.Where(i => !i.IsUnread && (horizon is not {} cut || i.OccurredAt > cut))).Take(maxItems - unread.Count)
items  = ByTime(unread.Concat(read))
```

- **The horizon cuts read rows only.** An unread row is never clipped at it. This replaces D4's "The merged list is clipped to
  `occurredAt > max(horizons)` before the cap". The match source still has no horizon, and the follow source's horizon is still the
  `CreatedAt` of the hit just past its window.
- **Display is by time only**, with no grouping of unread rows in the client, so a row never moves when its state changes (which
  click-to-read needs). Accepted: an unread row can sit below newer read rows in a panel that scrolls. With more than 20 unread rows the
  panel shows the newest 20 of those the sources returned and no read row.
- **The follow source's grade path lists the unread ads its window did not reach.** When the count is on the grade path and the window
  yields fewer distinct unread included ads than `min(unreadCount, MaxItems)` (for example because an "endast matchade" watch excludes the
  window's newest hits), the source takes the rows from the counted hits. It applies the inclusion rule to `countInput.GradeHits` with
  `resolution.Matching`, which already covers those candidates, so there is still **one grading
  call per poll**; it keeps each ad's newest included hit, orders the ads newest first and takes `MaxItems`. It then reads title and company
  name for the ads not already in the window, in one more statement that runs only in this case. That statement composes `NewHits` (which
  carries the `Status == Active` gate, D8) onto a join against `JobAds` for the display columns, bounded by time (from the oldest wanted
  ad's newest included hit up to the token), and never sends an id list to the database (no `Contains` over the strongly typed id). The ads
  leave the source as unread rows, one per ad. The common path and the refused state are unchanged, and D10's nine-statement pin, taken
  without an "endast matchade" watch, is unaffected. The read stays inside `ReadBoundedAsync`, where `JobAdLifecycleReadRegistry` declares a
  second site for it (the registry owes one decision per `get_JobAds` call). It ships in PR 1 because it has the same change reason as removing the footer: without it, once the footer is gone, an unread ad
  that is counted but not listed would be a dead end.
- **The number of unread notifications not in the list is exact or absent.** It is the sum of the sources' counts minus the unread rows
  shown, it is never negative, and it is rendered only when every source count is a number (ADR 0120 clauses 3 and 5). The line reads
  "N olästa visas inte i listan." and never says "older": the list shows the newest unread rows among those the sources returned, not the
  newest in total, so nothing guarantees that the rows left out are older.

The selection keeps six invariants. The ruling lists the unit, source, Testcontainers and client tests that pin them.

- **M1** No read row is shown while a returned unread row is left out.
- **M2** Every unread row shown is counted.
- **M3** The number of unread rows not shown is the sum of the `unreadCount`s minus the unread rows shown: never negative, and rendered only
  when every count is a number.
- **M4** The counts are never capped by the row cap.
- **M5** The fill with read rows is gap-free above the horizon.
- **M6** The rows are displayed in time order.

#### D6 — a dated row

**2026-10-11.** `security-auditor` graded a **GDPR Blocker** in delivered behaviour that lies outside the bell: the match rows the consented
run created are kept after `BackgroundMatch` is withdrawn, and `/matchningar` keeps listing them while their ad is Active
(`JobSeeker.cs:130-155`, `GetMyMatchesQueryHandler.cs:62-83` and `AccountHardDeleter.cs:247,249`, read at `0887dcca3`). It is open with
Klas (asked 2026-10-11) and does not block #2130, which neither causes it nor is blocked by it; it is tracked in #2138. The ruling lists
what is not built while it is open. This row replaces the last sentence of the "Not widened" bullet above ("His decision is needed only if
someone wants matches shown after a withdrawal, which would need new consent and privacy text"). **B1 is unchanged**: the match source is
still read only while `NotificationConsent.BackgroundMatch` holds, and the assessment that no register row, no privacy-text change and no
`CurrentPrivacyPolicyVersion` bump is needed still holds only while B1 holds.

#### D11 — the panel holds notifications and nothing else (changed)

D11 is changed as follows. Grounds: AC1, `design-reviewer`'s Blocker 1 and A1, ADR 0080 Beslut 5 and ADR 0144.

- **No footer, no destination links, no settings link and no gear.** The only links in the panel are the rows' titles, each the row's one
  link to `/jobb/{jobAdId}`. This replaces D11's sentence that the footer carries the two destination links. The rows are unchanged (reason
  chip, "Ny", time, title, company; the title is semibold while the row is unread), and so is the sheet at 480 px and below. The header holds
  "Notiser", the count line, and "Markera alla som lästa" only when the total is exact and above 0. The settings are reached through the
  user menu, Inställningar, Notiser.
- **There is no unread dot in PR 1.** #2130's ask text has it in PR 1; it comes with click-to-read in PR 2 instead, because a dot that stays
  after the row is opened would show a state that no longer holds (ADR 0047 point 2). This deviates from the ask text's timing and not from AC1.
- **No "Visa alla".** Decided, not built. AC1 allows at most one and none fits within it; a page would cost a top-level route, keyset
  pagination over two sources with the grade path per page, its own shape in D8, its own budget in D10 and every state of the page, and
  would duplicate two lists that already exist. Trigger: #1663 lands a third source, or Klas asks. When it is built it is one link after the
  last row that replaces the not-in-list line, with the settings link in the page's header, read behind the B1 gate.
- **The empty state is one line**, "Du har inga notiser.", which describes and invites nothing. This replaces D11's heading "Inga aviseringar
  än" and its one sentence. With no rows and a source that refuses to count, the panel shows a line without a number, worded by
  `design-reviewer`, and never "Du har inga notiser." while the badge shows an empty pill (ADR 0120). With no rows and an exact total above 0
  the not-in-list line stands alone.
- **The bell is named "Notiser"** (it was "Aviseringar") in the heading, the button's accessible name, the live region and the load and
  error texts, as the rest of the product names them (`design-reviewer`'s Major 3).
- **The not-in-list line exists** (D4): after the last row, inside the list's scroll area, with no link and no `role="status"`.

### Decided for PR 2, not built

- **Storage:** a non-nullable `bool` named after its channel on each source row (one migration, Klas's GO); no store, overlay or timestamp.
- **Read state:** the bell's read state never moves a watermark (Klas, 2026-10-11), so D5's mechanism and the exception in Q3a end in PR 2.
- **Settings:** the bell's per-type settings decide only what reaches the bell, never what Översikt or the lists show, and switching a type
  back on marks what arrived while it was off as read.
- **Click-to-read**, with the unread dot, and its own write bucket (neither `me-write` nor `follow-seen-mark`).
- **The sources** return their newest unread rows explicitly, and the contract text in D1 changes.
- **The index swap Q8**, as a separately named line in the migration that Klas can strike.
- **A new ADR** amends 0162, its number taken last: the title's "read-only", D5's "adds no write surface" and D13's "No migration" become
  false there, and it also amends D7, D8, D10 and the trigger in s5.

### Cross-references

- #2130 (this change) · #2138 (the open GDPR Blocker, D6) · #1662 (the notification epic) · #1663 (the next source, and the "Visa alla"
  trigger)
- ADR 0161 (the matching worker's lock, D3) · ADR 0120 (clauses 3 and 5: the hidden count is exact or absent, D4) · ADR 0080 (Beslut 5, the
  consent and the settings-only invitation, D11; Beslut 6, the watermark the bell stops moving in PR 2) · ADR 0164 (#2107, merged 2026-10-11 as `b52994c32`: its D1 has
  the bell read neither the e-mail master switch nor the per-type pauses) · ADR 0047 (point 2, D11)
- Local reports: `docs/reviews/2026-10-11-2130-form-cto.md` (the ruling) · `…-form-brief.md` · `…-form-brief-addendum.md` ·
  `…-form-dotnet-architect.md` · `…-form-security-auditor.md` · `…-form-design-reviewer.md` · `…-form-db-migration-writer.md`
