# ADR 0168 — The bell reads per notification with a flag on the source row: two switches decide what reaches it, and neither the flag nor the switches ever move a watermark

**Date:** 2026-10-12
**Status:** Accepted
**Decider:** `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2, report-only): the ruling on the #2130 form round (B1–B16, C1–C12 and
its binding scope for PR 2; `docs/reviews/2026-10-11-2130-form-cto.md`, local), on the brief and addendum (`…-form-brief.md`, `…-form-brief-addendum.md`) and the
reports of `dotnet-architect` (`…-form-dotnet-architect.md`), `security-auditor` (`…-form-security-auditor.md`: 0 Blocker and 0 Major in the form, conditions
V1–V14, one GDPR Blocker outside #2130, #2138), `design-reviewer` (`…-form-design-reviewer.md`) and `db-migration-writer` (`…-form-db-migration-writer.md`, a
consultation without grades). Its triage of PR 1 (`…-pr1-cto-triage.md`) added the common-path under-fill to this PR. Klas Olsson's direction of 2026-10-11 is
applied, not re-decided (the rule in the Context). The round needed no STOPP to Klas; one GO is owed (the migration, D8) and the rest was for his information.
**Amends:** ADR 0162 and its Amendment 2026-10-11: the title ("read-only"), D1 (the source contract), D2, D3 (the token's destination), D4 (the grade-path-only
reserve), D5 (replaced), D7, D8, D10, D11 and D13, and s5 of its Consequences. D6 is extended by one more AND, not changed. 0162 stays as the record and points to ADR 0168.
**Amended by:** ADR 0169 (#2151, the X on a notification): a second flag beside D1's, a fourth marker member and a third POST beside D3's, and a clarification of what D9's "hiding of match rows" means. D2 stands unchanged.
**Related:** [#2130](https://github.com/klasolsson81/jobbliggaren/issues/2130) (this is its PR 2) · #1662 (the notification epic) · #1663 (the next source) ·
#2138 (the open GDPR Blocker) · #2107 and ADR 0164 (D1: the bell reads neither the e-mail master switch nor the per-type pauses; the form of the off-stamps) ·
ADR 0018 (Amendment 2026-10-10: no write at render) · 0045 · 0080 (Beslut 5, 6) · 0113 · 0120 · 0125 · 0140 · 0146 (D3, D6) · 0154 · 0161
**Measured against:** `origin/main` at `98ae225f7` (#2131's merge, the base after the rebase of 2026-10-12) and the branch `feat/bell-read-state-and-settings`
at the commit that carries this ADR, read on 2026-10-12. Nothing here is merged.

> **Provenance.** Written by `adr-keeper` for the driving session from the CTO's ruling and the delivered code (CLAUDE.md §9.2, §13); it adds no decision of
> its own. The decisions, the invariants and the "not built" list are the CTO's; the delivered facts were read from the branch and its tests on 2026-10-12.
> Numbers that describe the branch (nine and eleven statements, the mark bucket, the migration's locks) are test-asserted, derived or documented behaviour, each
> marked so, and none was re-run for this ADR. The mandatory agents review the implementation in the PR; the pre-code consults of `security-auditor` (the mark bucket's figures, conditions V1 to V17) and `dotnet-architect` (the
> read marker, the explicit unread reads) are `docs/reviews/2026-10-12-2130-pr2-form-security-auditor.md` and `…-form-dotnet-architect.md` (local).

---

## Context

ADR 0162 made the bell a read-only union whose only state was two watermarks on `job_seekers`, `LastSeenMatchesAt` and `LastSeenFollowedAdsAt`, acknowledged by
visiting a list or by "Markera alla som lästa" through the lists' own `/seen` writers (0162 D5). PR 1 of #2130 (merged 2026-10-11 as `20dd7bf33`, the amendment)
made the panel hold notifications only and decided, but did not build, the rest. Five facts shape this PR.

- **Klas's rule (2026-10-11), binding on every PR of #2130.** The bell's settings per type decide only what reaches the bell and never change what Översikt,
  `/matchningar`, `/foretag/bevakade/nya` or any other surface shows. From PR 2 the bell's read state never writes anything another surface reads. Översikt keeps
  its own setting (the gear, `useNoticePrefs`).
- **The watermark cannot carry that rule.** A bell setting that filters inside a source makes a watermark-based "Markera alla" acknowledge rows the user never
  saw, on the bell and on Översikt and the lists alike (`dotnet-architect`, Viktigt 3). A click per row has no home in a watermark, and a write to `job_seekers`
  per click would collide with its `xmin` token (ADR 0146) and sit on `me-write`, whose bucket carries the consent withdrawals (Art. 7(3)).
- **The rows already exist and have one producer each.** `user_job_ad_matches` and `followed_company_ad_hits` are written by one job each under
  `DisableConcurrentExecution` (120 s for matching since ADR 0161, 1800 s for the follow scan), carry no concurrency token and no soft delete, and are erased with
  the account. `followed_company_ad_hits.seen_at` is the e-mail dedup authority, Pending hits only, and cannot stand for "read in the bell" (finding F3).
- **Three sentences of 0162 become false:** the title's "read-only", D5's "adds no write surface" and D13's "No migration".
- **Match rows outlive a withdrawn consent.** `security-auditor` graded that a GDPR Blocker in delivered behaviour outside the bell (#2138, open with Klas).
  Whatever this PR adds must not widen it.

## Decision

**The bell keeps one boolean on each source row, "read in the bell", and the user keeps one switch per notification type, "reaches the bell". Both belong to the
bell alone: neither is a consent, a watermark or an input to anything else.** The lists acknowledge their watermarks and the bell reads them; the bell never moves
`LastSeenMatchesAt` or `LastSeenFollowedAdsAt`. A visit to a list still counts as read in the bell, with no new write.

### D1 — The flag: a boolean on the source row, named after its channel (CTO B1, B15, C3). Changes 0162 title, D1, D13

- `bool ReadInNotificationFeed` (`read_in_notification_feed`, `NOT NULL DEFAULT false`) on `UserJobAdMatch` and `FollowedCompanyAdHit`. An existing row reads
  `false`, which is today's meaning, so there is no backfill; a `now()` backfill would hide notifications nobody has seen.
- It is a channel flag, a different clock from the e-mail state machine (`NotificationStatus`, `SentAt`) and from the watermarks, and it is not `SeenAt`. A
  timestamp was refused: no named consumer reads the time (data minimisation, `security-auditor` V10).
- **Writers:** the two source classes and nobody else. Every write on a match row is one set-based `ExecuteUpdate` that carries the consent (D3), so the match
  aggregate has no method that sets the flag; the follow source's row mark loads the ad's few hits and calls `FollowedCompanyAdHit.MarkReadInNotificationFeed()`, and its
  mark-through and backlog are `ExecuteUpdate` too. `MarkSeen`, `MarkQueued`, `MarkSent` and `MarkFailed` never touch the flag and it never touches `SeenAt`,
  `NotificationStatus` or `SentAt` (F3, pinned both ways on the follow aggregate; on matches, the mail path never clears a mark in `MatchFeedSourceReadStateTests` and a
  mark leaves the mail state alone on Npgsql).
- **Readers:** the two sources and, only when the bell asks, `NewFollowedCompanyAdSet.LoadCountInputAsync`. Never input to matching, grading, sorting or mail.
  Översikt's and the lists' statements never name the column (pinned on the statement text by `Oversikt_and_the_lists_never_read_the_bells_flag`).
- No index of its own: the watermark bounds every scan to the head of the existing `(user_id, created_at)` index, and a partial "unread" index would never shrink,
  since most rows leave "unread" through the watermark and keep `false` (`db-migration-writer`, derived).
- The bell stays a view over existing rows: no store, no producer change, no dispatcher. ADR 0140's event log stays with #1662; a future log table carries its own
  flag and becomes one source more (B3).

### D2 — Unread in the bell, the token, and parity as an inequality (CTO B2, C4). Changes 0162 D2, D3, D7

- **Unread** = new since the source's watermark AND not read in the bell AND the type is on AND (matches: the consent is held; follows: the hit is included by the
  #1576 rule). A follow ad is unread while any included hit of it is; its moment is its newest included hit. One deviation, accepted by `dotnet-architect`: a row the
  fallback names from outside the window takes the newest included UNREAD hit as its moment, so an ad with an older unread and a newer read hit, both beyond the window,
  is listed at the older one. Inside the window the merge keeps the later of the two moments (pinned).
- **The token is unchanged:** `MAX(CreatedAt)` over the watermark-new set with no flag predicate, read first. A token over unread rows only would leave a row read one
  by one above it, still "new" for Översikt and the list, and "Markera alla" would not show when nothing is unread (I1). D3's order stands; its point 4 changes
  destination only: the token still travels as the original string, now to the bell's own read-through endpoint (D3 below), not to `/seen`.
- **The count** is the destination's count minus the rows read in the bell, so D7's parity becomes an inequality (I6): per source the bell's unread is at most
  Översikt's new count and equal while nothing was read one by one. Two parity tests: without marks the numbers are equal, with marks the bell's is lower by exactly
  the number marked (`The_bells_count_is_Oversikts_count_minus_exactly_the_rows_read_in_the_bell`). Counts stay exact or absent (ADR 0120). The labels I1 to I7 in this
  decision are the architect's invariants for the read state (`…-form-dotnet-architect.md`, B2).

### D3 — Read marks: three writes behind `INotificationFeedReadMarker` (CTO B7, B11, B12, B13). Changes 0162 D1, D5, D8, D10

- **The marker.** A source also implements the internal `INotificationFeedReadMarker` (ISP), so how a row is counted, listed and marked lives in one class and a new
  source is one class. The reflection test fails the build if a source is not its own marker. Three members: a **row mark** (a click reads the whole (source, ad) pair,
  which for follows is every hit of the ad the owner has, loaded and marked through the aggregate; for matches it is one statement), a **mark-through** (every watermark-new, unread row with
  `CreatedAt <= token`; a row that arrived after the token stays unread, I4) and a **backlog mark** (D5). `ExecuteUpdate` is core EF, so no port.
- **The wire.** `POST /api/v1/me/notification-feed/{source}/{jobAdId}/read` and `POST …/{source}/read` with a required body `{throughToken}`. `{source}` is a closed
  set (`matches`, `followed-company-ads`): anything else is a 400 before any read, as is an empty id; a non-GUID segment matches no route. The owner comes from
  `ICurrentUser` only. The feed's DTO is unchanged.
- **204 for every request from a signed-in owner:** a missing, foreign or ended row, or a match whose consent is withdrawn, so there is no 404 oracle. A match row is
  never written while the consent is withdrawn: the consent is applied in the same statement as the write (an `EXISTS` over the user's `JobSeeker`), not read before it
  (the B1 gate; #2138). The mark does not apply the user's switch for the type, because the backlog mark runs while it is off. The row mark reads no `JobAds`, so the
  owner's own row of an ended ad is flagged; the feed never lists it, so nothing observes it and no lifecycle-registry entry is owed.
- **Not a `JobSeeker` write.** No audit, no replay marker, no `JobSeeker` mutator, so the writer-replay guard (ADR 0146 D3) holds and a mark can neither collide with a
  consent write on `xmin` nor spend a withdrawal's replay cap (ADR 0146 D6) (I5).
- **What marks:** a click on a row in the bell (plain, modified or middle: all three open the ad and count), "Markera alla som lästa" per source, and a visit to the
  source's list (through the watermark, as before). Opening the ad any other way marks nothing (B12): it would be a new write at render, which reopens ADR 0018's
  Amendment 2026-10-10 and 0162 D9's lapse trigger.
- **The bucket** `notification-feed-mark`: TokenBucket per `sub`, QueueLimit 0, 60 per 60 s (ten tokens per ten seconds), one bucket for both marks, neither `me-write`
  (a withdrawal must not meet a 429 caused by clicking through notifications) nor `follow-seen-mark` (the ad-open auto-mark would drain it). The figures are
  `security-auditor`'s PR 2 consult as `RateLimitingOptions.NotificationFeedMark` records them, derived and not measured: 22 calls in seconds is the burst (twenty rows
  opened with ctrl-click, then "Markera alla" at one call per source), about 20 marks per 30 s per tab the sustained demand.

### D4 — The client: a Server Action fired without waiting (CTO B7, C12). Changes 0162 D5

- `markNotificationReadAction` is called without `await` from the click that also opens the ad, often as the intercepted job modal. It does no `revalidatePath`,
  `refresh`, `updateTag`, cookie write or `redirect`, and returns only its own value. A refusal or rejection is a refused mark: the dot comes back at the next read.
- The dot goes at once. The count falls by one only when the server has confirmed, which is exact at the token's bound, and there is no forced re-read per click, so
  the read bucket does not depend on how fast the user clicks. A read that started before a confirmed mark is patched with the marks confirmed since (in memory
  only). "Markera alla" (`markAllNotificationsReadAction`, sequential and independent per source, a null or malformed token sends nothing) and a saved switch read again
  at once.
- **Measured 2026-10-12** in Playwright against a production build with the fixture backend (`web/jobbliggaren-web/tests/job-modal/notifications-bell.spec.ts`, two
  cases; recorded in the spec and the action's docblock, not re-run for this ADR): the modal opens inside 2.5 s and the tagged modal node stays mounted, with the page
  behind it unmoved (the heading's x position), while the mark is held 0 ms and 5 000 ms and after the answer lands; the mark arrives once and no watermark moves.
  **A limit the spec records:** a variant of the action that also called `revalidatePath` passed too, so the spec does not detect a re-rendering action. The guard
  is the action's own unit test, which asserts that every re-rendering and cookie call is untouched.

### D5 — Two switches decide what reaches the bell (CTO B4, B5, B6, C8). Extends 0162 D6

- **Storage.** Two nullable stamps appended to `Preferences`, `NotificationFeedMatchesOffAt` and `NotificationFeedFollowedAdsOffAt`, `null` meaning on: the form of
  ADR 0164's pauses, the one shape where a missing jsonb key, the CLR default and SQL `NULL` all mean on (a legacy row is pinned on the CLR read by
  `LegacyRow_WrittenBeforeAddNotificationFeedReadState_ReadsBothBellSwitchesOn`). Off stamps once and keeps the first stamp while it lasts; on clears it. The profile
  DTO carries two derived booleans, never the stamps.
- **Delivery choices, never consents.** `JobSeeker.SetNotificationFeedMatches` and `SetNotificationFeedFollowedAds` each write only their own stamp: no consent
  field, no withdrawal stamp, no mail pause, not the other switch. Neither direction is refused while the consent is off; the value is kept (ADR 0164 D2).
- **The gate.** `NotificationFeedGate`: matches = `NotificationConsent.BackgroundMatch` held, by reference and never copied, AND the matches switch on; follows = the
  follows switch on. Setting AND consent, never OR. It reads no mail pause, so it is not `NotificationMailGate` (ADR 0164 D1, D3). With the type off the source stops
  after the one statement that evaluates the gate: no watch and no row is read (Art. 5(1)(c)).
- **Commands.** `PUT /api/v1/me/notification-feed/matches` and `…/followed-company-ads`, body `{enabled}` required, `MeWritePolicy`; one audited, replay-marked command
  each (`JobSeeker.NotificationFeedMatchesUpdated`, `…FollowedAdsUpdated`; payload `{"enabled":bool}` and nothing else). One control, one write: a replay writes only
  its own field and cannot re-grant a withdrawal (ADR 0164 D5; `A_withdrawal_committed_inside_a_bell_switch_save_stands_and_the_switch_still_lands`).
- **Switching on does not flood.** The endpoint first marks everything created after the type's off-stamp as read, then sends the command that clears the stamp. The
  stamp is the key, so the order matters, and the mark is composed in the endpoint because the writer-replay guard forbids a bulk write inside a replayed `JobSeeker`
  command. Rows from before the switch-off stay as they were. A switch-on is one request on the settings bucket.
- **UI.** Two switches on `/mina-sidor/notiser`, each at the head of its type's group before the e-mail rows, on from the start: "Nya matchningar som notis i
  appen" and "Nya annonser som notis i appen". The matches switch renders only while the match consent is on and keeps its value while hidden, with no status line and
  no nudge (ADR 0080 Beslut 5); the consent switches stay always visible and usable. The switches ignore the e-mail switches (ADR 0164 D1).
- **Legal position** (`security-auditor` B6). A read mark and an in-app setting are no new category, purpose or recipient: the same class as the watermarks and
  `seen_at`. Retention and erasure are inherited from the row. A field note at the match-row entry of the local processing register; no policy sentence and no
  `CurrentPrivacyPolicyVersion` bump. It holds only while setting AND consent, no switch writes a consent field, nothing copies a title or company name to a store, no
  surface reads match rows without the gate, and the owner never comes from the wire. Break one and the Blocker class reopens.

### D6 — The sources return their newest unread rows explicitly (CTO Läsläge, PR 1 triage). Changes 0162 D1 (contract text), D4

- **Contract.** A source returns its newest unread rows up to the cap, on every path that has a counted set, plus its newest rows to fill, each row once. The
  `INotificationFeedSource` docblock says so; the merge never has to guess whether an unread row was left out. The
  refused state (past `MaxCountedHits`) has no counted set to take rows from and returns its window only, as before.
- **Match:** the newest-first list, and when read rows crowd unread ones out of it a second statement names the newest unread rows. **Follow:** the PR 1 grade-path
  reserve (from the counted hits, no second grading call) now has a common-path twin: one grouped statement (`GROUP BY job_ad_id`, ordered by the grouped
  `MAX(created_at)` descending, limited to the cap) joined to `JobAds`. Both conditional statements run only on a shortfall, and both inherit the Active gate from
  `NewMatchSet` and `NewHits`. `JobAdLifecycleReadRegistry` declares them (ADR 0113): a second site in `MatchFeedSource.ReadAsync`, and in `FollowedCompanyAdFeedSource` a
  common-path site and the grade-path site, both now in `ReadUnreadBeyondWindowAsync`.
- **The horizon** still cuts read rows only (0162 Amendment D4).
- **Statement counts**, test-asserted on Npgsql and not re-run for this ADR: nine in the base case (match: gate and watermark, token, count, list; follow: gate and
  watermark, watches, token, count, window), `One_request_without_an_only_matched_watch_runs_nine_statements`; eleven when read rows hide unread ones in both sources,
  `A_request_whose_read_rows_hide_unread_ones_in_both_sources_runs_eleven_statements`. With an "endast matchade" watch the profile build and the grading call come
  on top (0162 D10). `notification-feed-read` stays 12 per 60 s in this PR: its docblock derives 12 from the call frequency of visible tabs and records no
  per-statement budget, and no source is added; no latency against ADR 0045's budget was measured for this ADR. The eleven is the input if a measurement or #1663 calls for a
  re-derivation.

### D7 — The row shows a dot and no "Ny" (CTO A3, C1). Changes 0162 D11 and DESIGN.md §6

- An unread row shows an 8 px dot in its own grid column (`--jp-ink-1`, `--jp-r-pill`, `aria-hidden`), in the title's row and never beside the reason chip, whose
  Toppmatch variant has a dot of its own. A read row leaves the column empty, so nothing moves; assistive technology gets "Oläst" as text in the link's
  description. The "Ny" tag is gone; an unread title is still semibold, so two cues do not rest on colour. The dot is neutral ink, never green or info-blue.
- PR 1 kept "Ny" because a dot that outlives the click would show a state that no longer holds (ADR 0047 point 2). With click-to-read it does not. Rows stay in time
  order, so a row never moves when it is read. DESIGN.md §6 is rewritten in this PR. The panel still holds no link but the rows' own: no footer, no gear, no
  "Visa alla" (0162 Amendment D11 stands).

### D8 — One migration, and the index swap as a line Klas can strike (CTO B9, B14, C10). Changes 0162 D13 and s5

- `AddNotificationFeedReadState` (`20261012042409`, Previous `20261011102453_AddNotificationMailPauses`, scaffolded on a `main` that holds #2107's squash). Up:
  `SET LOCAL lock_timeout = '3s'` first; `ADD read_in_notification_feed boolean NOT NULL DEFAULT false` on the two tables; the two `Preferences` stamps move the snapshot
  only (jsonb, no DDL); and, as a separately named block, `DROP INDEX ix_followed_company_ad_hits_user_id` then `CREATE INDEX
  ix_followed_company_ad_hits_user_created_at (user_id, created_at DESC)`.
- **Locks** (PostgreSQL 18 documentation read by `db-migration-writer` on 2026-10-12, and measured by it on a throwaway `postgres:18.3`). `ADD COLUMN` takes ACCESS
  EXCLUSIVE, and the two are held to commit; a constant default lives in the table's metadata, so there is no rewrite. The real exposure is queueing behind an open transaction, which
  `lock_timeout` turns into a loud deploy failure that the next reconcile retries (every merge goes live on the one box, ADR 0154). Inside the migration's transaction the table already holds the `ADD COLUMN`'s
  ACCESS EXCLUSIVE, so the DROP and the CREATE INDEX also block reads, for milliseconds at this table's size; the swap is not `CONCURRENTLY`.
- **The swap replaces 0162 s5's measurement trigger with reasons.** The migration and the GO are paid by this PR anyway; this PR adds access paths keyed
  `(user_id, created_at)` on that table (the token, the newest-first window, mark-through, the backlog mark, the grouped unread statement); and the index that goes is a
  prefix of `ux_followed_company_ad_hits_user_jobad_watch` and `ix_followed_company_ad_hits_user_status`, so the index count and the write cost are unchanged and the
  Art. 17 sweep reads the new index through its `user_id` prefix. No measurement shows it earning its keep, and none can at the box's row counts (0 matches and 379
  hits on 2026-10-11, from `docs/current-work.md`, not re-measured; at that size the planner scans sequentially).
- **Down is destructive** by this repository's convention: it drops both columns, so every read mark is lost and every notification reads unread again, and it
  reverses the swap. A code rollback makes `Migrate` refuse (schema ahead, exit 3) until `MIGRATE_ALLOW_SCHEMA_AHEAD` names this exact id. A per-migration test runs Up,
  Down and Up as `jobbliggaren_app`. One GO from Klas covers the lot, and he can strike the swap; striking it touches seven artefacts (the migration, its Designer and the
  snapshot, `FollowedCompanyAdHitConfiguration`, `FollowedCompanyAdHitPersistenceTests`, the migration test and this section) and a new head.

### D9 — What this decision is not (CTO B3, the #2138 list). Changes 0162 D13

- Not ADR 0140's event log, and not Web Push (#2124). Not a "Visa alla" page: decided, not built, due when #1663 lands or Klas asks; it would replace the not-in-list
  line, carry the settings link in its header, read behind the match gate and bring its own wire form and read budget.
- Not, while #2138 is open: a store that copies match rows; a notification surface that reads match rows without the consent gate; deletion, thinning or hiding of
  match rows; changed consent behaviour for `/matchningar` or `NewMatchSet`; a write from the bell on a match row while the consent is withdrawn (a no-op, 204). The
  flag sits on the row itself, so Klas's answer to #2138 covers it with no second deletion path.
- Not a gear in the panel, and not a change to Översikt's own setting.

## Alternatives considered

- **A — a boolean on the source rows.** Chosen (D1). *For:* no table, no register row, no policy text, erasure and retention inherited; two columns, reversible;
  producers and the single-writer assumption untouched; the unit stays (kind, ad). *Against:* a second UPDATE writer beside the mail claims on the same rows, safe only
  while no concurrency token exists (a lapse trigger); one migration under Klas's GO.
- **B — an overlay table of read marks keyed (user, kind, ad).** The reserve if `security-auditor` had required read data to be deletable apart from the row; she did not.
  *For:* no change to the source tables. *Against:* a new aggregate, a `DbSet` in the `AppDbContext` hotspot, a `CascadeMap` entry, a grant from Klas for the exposure list,
  a register row and a retention rule; marks outlive deleted source rows, and a mark needs an existence check or it writes rubbish for arbitrary ids. Excluded before the round:
  a read set inside the `job_seekers` jsonb (every click would contend on the `xmin` row) and client-side marks (an inexact badge, ADR 0120).
- **C — a notification store written by the producers.** *For:* the shape of a log. *Against:* the follow source's inclusion is decided at read time, so a write-time copy
  freezes it or becomes a copy plus `read_at`; a backfill is a second writer and opens 0162 D3's lapse; no dispatcher exists and the aggregates are event-free; it couples
  the producers to the notification context and doubles the erasure surface of the open Blocker.
- **D — a read timestamp (`InAppReadAt`, `bell_read_at`) instead of a boolean.** *For:* it says when. *Against:* no named consumer reads the time (V10), and an `*At`
  name invites confusion with `SeenAt`.
- **E — keep the watermark as the read state** (the architect's options 1 and 3: whole-source switches only, or an accepted swallow). *For:* no migration, and the
  delivered mechanism. *Against:* option 1 forces a flood on switch-on or a swallow; option 3 is the swallow that #1576 and 0162 D5's own condition exist to forbid; and
  a per-click write to `job_seekers` collides with its `xmin` token.
- **F — a combined acknowledge command.** Carried from 0162 (Q3(c)) and still rejected: a new write surface with no security gain, and ADR 0146 D3 would need an
  amendment.
- **Narrower rejections in the same ruling:** a read-time filter "on since" in place of the backlog mark (it loses notifications from before the switch-off after an
  off-on cycle and needs two stamps); marking when the ad is opened anywhere (a write at render); the mark on `me-write` or `follow-seen-mark` (D3); a forced re-read per
  click (the read bucket would depend on click speed); a bell fully independent of list visits (a one-off mark at deploy and the same fact read in two places, Klas's
  option, not taken).

## Consequences

### Positive

- Klas's rule becomes a dependency direction. No setting can swallow a row on another surface, the bell never writes `job_seekers` (no `xmin` collision with a consent
  write), and ADR 0080 Beslut 6 reads again in its original form.
- No new table: no register row beyond a field note, no policy text or date, retention and erasure inherited, no grant for the exposure list (ADR 0125: a boolean column on
  tables already on it).
- Producers, the single-writer assumption of 0162 D3 and the e-mail paths are untouched; no backfill; reversible by two columns.
- A switch has no consent field, so a replay from a stale tab cannot re-grant a withdrawal.

### Negative and risks

- **s1. A behaviour change, announced to Klas at merge.** "Markera alla som lästa" no longer acknowledges the sources' watermarks, so it no longer clears Översikt's
  followed-ads count or the "new" marks on the lists (it did until PR 1). Klas's rule.
- **s2. The bell and Översikt can show different numbers for one source,** the bell's at most Översikt's (D2, I6). By design.
- **s3. Unread notifications beyond the 20 shown** are counted by the not-in-list line and reachable only through the lists. "Visa alla" is not built.
- **s4. Opening an ad any other way leaves the notification unread** (D3, B12), so a dot can outlive an ad opened from `/jobb`. Accepted.
- **s5. Residual: the producer may commit a row between the backlog mark and the stamp clear,** so that row shows as unread. A new notification, not a flood; harmless.
  Ground: the endpoint's own comment and the CTO's order of the two writes.
- **s6. Residual: `PUT …/matches` still works through the API while the consent is withdrawn** (the UI hides the switch). The backlog mark is then a consent-gated
  no-op, so rows created while the type was off and the consent on stay unread and can show when the consent returns. Derived from the code, not measured.
- **s7. Residual: the `{source}` route shape was inferred,** from the architect's proposal and the `…/ad-hits/{jobAdId}/seen` precedent, not measured against a production
  client. Its only client is this repository's own Server Action, and the Playwright fake mirrors the same strings, so the spec does not check it independently. `security-auditor`'s V9: the ad id and the `UserId` probably reach every event of that request through the
  hosting scope, as they do for the sibling routes `…/ad-hits/{jobAdId}/seen` and `GET /job-ads/{id}`; inferred from the assembly, not observed in a sink, and not
  caused by this diff. The PR adds no log call or scope that carries the ad id, the token or a count.
- **s8. Residual: neither source table has a concurrency token,** so a read mark and a producer's insert or mail claim never conflict. EF writes only the changed
  columns, and the lapse trigger below names the day this stops being safe.
- **s9. A rollback to an image older than the two stamps drops them on that user's next preference write,** which switches both types back on without the backlog
  mark. ADR 0164 records the same mechanism for its pauses (the architect's reading, derived, not measured as a rollback).
- **s10. The bell gate's `== null` is pinned on the CLR read of a legacy row, not in SQL:** no test runs the gate's query against a row that lacks the keys.
- **s11. The Playwright measurement does not detect a revalidating action** (D4); the no-re-render rule is a guard.
- **s12. The index swap is taken without a measurement** (D8), and the read bucket's figure is not re-derived for eleven statements (D6).
- **s13. This ADR's s-numbers are its own;** 0162's are cited as "0162 s5".

**Lapse triggers.** Single named home: this ADR. Named reader: Klas. This decision is re-examined when: a concurrency token is added to `user_job_ad_matches` or
`followed_company_ad_hits` (s8); anything other than the two source classes writes `ReadInNotificationFeed`, or anything other than the two sources and the bell's own
count reads it (D1, V10); consent behaviour for match rows changes in answer to #2138 (D9); a third source (#1663) or a "Visa alla" page arrives, which re-derives both the
read and the mark bucket; or the bell ever reads the e-mail master switch or a mail pause (ADR 0164 D1). The lapse triggers of 0162 and its Amendment stand.

## Implementation

One PR on `feat/bell-read-state-and-settings`: the backend, the frontend, DESIGN.md §6 and this ADR. The ADR is promoted with `git add -f` (it sits in the ignored
0071–0199 range; precedent 0164) so the #1663 session can read it. 0162 gets a pointer here; the README row is `docs-keeper`'s.

- **Backend.** Domain: `UserJobAdMatch`, `FollowedCompanyAdHit`, `Preferences`, `JobSeeker`, `NotificationFeedGate`. Application: `NotificationFeed/Sources/*`,
  `NotificationFeed/Commands/{MarkNotificationRead,MarkNotificationsReadThrough,MarkNotificationFeedBacklogRead}`, `JobSeekers/Commands/SetNotificationFeed{Matches,FollowedAds}`,
  `NewFollowedCompanyAdSet`. Api: `MeNotificationFeedEndpoints`, `RateLimitingOptions` and `RateLimitingExtensions`. Infrastructure: the two configurations, the migration and the snapshot.
- **Frontend.** The Server Actions, the API client, `use-notification-feed`, `notification-feed-provider`, `notifications-bell`, `notifications-section`, the messages, `globals.css`.
- **Tests.** F3 on both aggregates; `NotificationFeedGate` and the setters; the source and marker tests (Application.UnitTests); `NotificationFeedReadStateEndpointTests` on
  Npgsql (isolation, 401, the closed `{source}` set, 204, the consent gate, parity, the switch-on backlog, the two statement counts); the migration test; the Playwright spec.
- **PR 1's named skips are closed here.** The common-path under-fill (D6); the source-level Npgsql test of the grade path with hits the grader excludes
  (`On_the_grade_path_hits_the_grader_excludes_fill_the_window_and_the_older_matching_unread_ads_are_still_listed`); and the ordering pins of the grade-path
  fallback, each killed by a hand mutation of its own line (an ad at its newest included hit and not its oldest, the cap on the lowest ad ids when more ads share
  the newest moment, an unread fallback row keeping the later of its two moments:
  `ReadAsync_OnTheGradePath_NamesAnAdAtItsNewestIncludedHit_NotItsOldest`, `…_WhenMoreAdsShareTheNewestMomentThanTheCapTakes_TheCapFallsOnTheLowestAdIds`,
  `…_TheUnreadFallbackRowKeepsTheLaterMomentOfTheWindowsRowOfTheSameAd`).
- **Owed outside the diff.** Klas's migration GO (CLAUDE.md §6.5, §9.2); the field note in the local processing register, in the main copy; the PR 2 panel (`code-reviewer`,
  `dotnet-architect`, `security-auditor`, `design-reviewer`, `db-migration-writer`, `test-writer`) and `design-reviewer`'s renders of the flow and of `/mina-sidor/notiser` at 1280, 375 and 320.

## Escalations carried verbatim

The CTO's "Till Klas" items 5 to 9 and `security-auditor`'s Escalation touch this decision. They are quoted in Swedish, as sent to Klas, without edits (the blocks were
spliced from the sources and diffed). Items 5 to 7 are decided above; item 8 is the GO this ADR does not give; item 9 and the Escalation's point 1 are #2138, open with Klas and unchanged on 2026-10-11
(0162 Amendment, D6 row; `docs/current-work.md`, local).

**From `docs/reviews/2026-10-11-2130-form-cto.md`, "Till Klas":**

> **Ditt direktiv om inställningarna**
>
> 5. **Ditt direktiv är en bindande regel:** notisinställningarna styr bara vad som når klockan, aldrig vad Översikten, Mina matchningar eller Nya annonser från bevakade företag visar.
>    - Från PR 2 flyttar klockan heller aldrig de listornas "nytt"-markering. Det gäller både ett klick och "Markera alla som lästa".
>    - Det är en ändring mot i dag. I dag nollställer "Markera alla som lästa" även Översiktens tal för nya annonser från bevakade företag och NY på de två listorna. Så blir det också i PR 1, eftersom klockan saknar egen läsmarkering tills PR 2:s migration.
>    - Det enda som går åt andra hållet: när du öppnar Mina matchningar eller Nya annonser från bevakade företag räknas de raderna som lästa även i klockan.
>    - Vill du att klockan ska vara helt fristående även från listbesöken kostar det en engångsmarkering när PR 2 driftsätts, och du får läsa samma sak på två ställen. Säg till om du vill det.
> 6. **Att öppna en annons någon annanstans markerar inte notisen som läst.** Det gör bara ett klick i klockan eller ett besök på listan. Annars blir det en ny skrivning när en sida öppnas, och det tillåter vår CSRF-regel inte utan en ny säkerhetsgranskning.
> 7. **När du slår på en notistyp igen** dyker inget upp från tiden den var avstängd. Det som var notiser innan du stängde av ligger kvar som förut.
>
> **Migrationen och säkerhetsfrågan**
>
> 8. **PR 2 behöver din migrations-GO, en gång, när #2107 (PR #2135) är mergad.** Den innehåller:
>    - två kolumner, läst i klockan ja/nej, på matchnings- och bevakningsraderna;
>    - två avstängd-stämplar i inställningarna, utan tabelländring;
>    - på en egen rad som du kan stryka: ett indexbyte på bevakningsträffarna, där ett överflödigt index går ut och ett som klockans sorterade läsning använder kommer in.
>
>    Ingen ny tabell, så du behöver inte bevilja något för exponeringslistan.
> 9. **Säkerhetsfrågan om sparade matchningar efter ett återkallat samtycke är öppen och blockerar inte #2130.**
>    - Inget raderas, och inget ändras i hur matchningar lagras, medan frågan är öppen.
>    - Ett issue spårar frågan med security-auditors eskalering ordagrant.
>    - När du bestämmer dig blir det ett eget arbete, utanför klockan.

**From `docs/reviews/2026-10-11-2130-form-security-auditor.md`, "Escalation":**

> Klas, två saker från formrundan för #2130.
>
> 1. Det här fanns före #2130 och blockerar inte #2130. När någon stänger av "Matcha nya annonser åt mig" slutar vi matcha och mejla. Men matchningar som redan har gjorts raderas inte. De visas fortfarande på /matchningar så länge annonsen är aktiv, och de ligger kvar i databasen tills kontot raderas. Klockan visar dem inte, eftersom min B1 från #1666 står kvar. Samtycket var den enda grunden för de raderna. Enligt art. 17(1)(b) ska de därför raderas när samtycket dras tillbaka, om det inte finns någon annan grund, och jag ser ingen. Det här är en GDPR-Blocker, så vägen med accepterad risk finns inte. Du behöver välja:
>    - (a) En användares matchningar raderas när hen drar tillbaka samtycket. (Rek.)
>    - (b) Matchningarna behålls och visas. Då måste en annan rättslig grund skrivas in i integritetspolicyn och registret, och jag hittar ingen som håller. Ett beslut att behålla dem ändå är ditt, inte sessionens, och jag signerar det inte.
>
> 2. Det här behövs bara om CTO väljer en ny tabell för lästa notiser, alltså alternativ (a) eller (b) i briefen. En ny tabell med uppgifter om användare kommer med i säkerhetskopian, och då blir listan över vad en återställning exponerar längre (ADR 0125 Case 2). Hittills har varje sådan utökning krävt att du beviljar den (ADR 0139 och ADR 0156). Väljer CTO alternativ (c), en markering på de rader som redan finns, behövs inget beslut av dig.

## References

- `docs/reviews/2026-10-11-2130-form-cto.md` (the ruling) · `…-form-brief.md` · `…-form-brief-addendum.md` · `…-form-dotnet-architect.md` · `…-form-security-auditor.md` ·
  `…-form-design-reviewer.md` · `…-form-db-migration-writer.md` · `…-pr1-cto-triage.md` · `2026-10-12-2130-pr2-form-security-auditor.md` ·
  `2026-10-12-2130-pr2-form-dotnet-architect.md` (all local)
- Code: `src/Jobbliggaren.Application/NotificationFeed/` · `src/Jobbliggaren.Application/CompanyWatches/Queries/NewFollowedCompanyAdSet.cs` ·
  `src/Jobbliggaren.Domain/JobSeekers/NotificationFeedGate.cs` · `src/Jobbliggaren.Api/Endpoints/MeNotificationFeedEndpoints.cs` ·
  `src/Jobbliggaren.Infrastructure/Persistence/Migrations/20261012042409_AddNotificationFeedReadState.cs` ·
  `web/jobbliggaren-web/src/lib/actions/notification-feed.ts` · `web/jobbliggaren-web/src/components/shell/notifications-bell.tsx` ·
  `web/jobbliggaren-web/tests/job-modal/notifications-bell.spec.ts`
- ADR 0018 (Amendment 2026-10-10), 0045, 0047, 0080, 0113, 0120, 0125, 0140, 0146, 0154, 0161, 0162 (with its Amendment 2026-10-11), 0164 · AGENTS.md §2.1, §3.6, §5 ·
  CLAUDE.md §6.5, §9.2, §9.6, §12
- Martin, *Clean Architecture* (2017), ch. 7, 13, 14 (SRP, CCP, ADP) · Evans, *Domain-Driven Design* (2003), "Bounded Contexts" · Hunt and Thomas, *The Pragmatic
  Programmer* (1999), YAGNI and KISS
