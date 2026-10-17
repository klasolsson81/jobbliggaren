# ADR 0169 — The bell takes one notification out with an X: a second flag on the source row, held by a CHECK, written only by the user's click and never cleared

**Date:** 2026-10-12
**Status:** Accepted
**Decider:** `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2, report-only): the ruling on the #2151 form round
(`docs/reviews/2026-10-12-2151-form-cto.md`, local), with the reports of `dotnet-architect` (`…-form-dotnet-architect.md`: no Kritiskt, four Viktigt, three
Nice-to-have) and `security-auditor` (`…-form-security-auditor.md`: the plan approved, 0 Blocker and 0 Major, three Minor, conditions V18–V28). Klas Olsson's
directive of 2026-10-12 is applied, not re-decided (the rule in the Context). The round needed no STOPP to Klas; one GO is owed (the migration, D7) and one
residual is for his information (s1).
**Amends:** ADR 0168: D1 (a second flag beside the read flag), D3 (a fourth marker member, a third POST, the shared bucket) and D9 (a clarification of what
"hiding of match rows" means while #2138 is open). D2 stands unchanged, and for a stated reason (D2'). D6's two window statements gain one predicate each. 0168 stays
as the record and points to ADR 0169.
**Amended by [ADR 0173](./0173-an-ad-is-one-notification-in-the-bell-a-match-takes-precedence-the-follow-source-leaves-out-the-ads-the-match-source-holds-and-a-click-or-an-x-reaches-every-sources-rows-of-the-ad.md) (2026-10-17, #2158):** the X, like the read mark, reaches every source's rows of the ad. The Context bullet "A follow notification is a (source, ad) pair", D4' (the X reaches every source; `IsInEnum` is gone from the command's validator), D5' (extended: an X on a follow row may dismiss a match row while the consent is held, user-initiated and bell-only) and s1 (narrowed: for an ad the match source holds, while the gate holds, the producer race of s1 no longer surfaces) are changed there, and Alternative G's objection to a correlated NOT EXISTS in every statement is answered there; where the text below differs, ADR 0173 governs and this text stays as the record.
**Related:** [#2151](https://github.com/klasolsson81/jobbliggaren/issues/2151) · #1662 (the notification epic) · #1663 (the next source) · #2138 (the open GDPR
Blocker) · #2130 and ADR 0168 (the bell's read state) · ADR 0018 (Amendment 2026-10-10: no write at render) · 0113 · 0120 · 0125 · 0140 · 0146 (D3, D6) · 0162
**Measured against:** `origin/main` at `30ec9d2a4` (#2149's merge) and the branch `claude/bell-dismiss-single-notification-48f9af`, read on 2026-10-12. Nothing here is
merged.

> **Provenance.** Written by `adr-keeper` for the driving session from the CTO's ruling, the two form reports and the delivered code (CLAUDE.md §9.2, §13); it adds no
> decision of its own. The decisions, the invariants and the "not built" list are the CTO's; the conditions V18–V28 are `security-auditor`'s and are cited by number;
> the delivered facts were read from the branch on 2026-10-12. Figures are marked test-asserted, derived or documented (the bucket arithmetic and the statement
> counts are derived, not measured), and none was re-run for this ADR. The mandatory agents review the implementation in the PR.

---

## Context

ADR 0168 gave the bell a read flag per source row and two delivery switches per type. A row stays in the bell until it ages out through its watermark or horizon;
the user can read it but not remove it. Five facts shape this PR.

- **Klas's directive (2026-10-12).** One X per notification in the header bell that removes just that notification from the bell, and nothing else: no "Rensa lästa",
  no ageing of read rows, no "Visa alla", no undo. The rule of 2026-10-11 binds it: what the bell does never changes what Översikt, `/matchningar` or
  `/foretag/bevakade/nya` shows (0168 Context).
- **"Read" cannot mean "gone".** The bell lists read rows too (0168 D6: the horizon cuts read rows only), and the X must work on exactly those. A row removed from the list
  must also stop counting if it was unread, without teaching the token, the count, mark-through or the backlog mark a third state (0168 D2).
- **The match aggregate has no method for its flags.** Every write on a match row is one set-based `ExecuteUpdate` that carries the consent (0168 D1, D3), so the
  invariant "a removed row is a read row" would live inside one statement while six others rest on it.
- **A follow notification is a (source, ad) pair over several hits** (one per watch), so one X touches every hit of the ad the owner has.
- **ADR 0168 D9 reads against the X, word for word.** It forbids "hiding of match rows" while #2138 is open, and the X is a hidden match row in one channel
  (`security-auditor`, Minor 1).

## Decision

**A second boolean, `dismissed_from_notification_feed`, sits on each source row beside `read_in_notification_feed`. A dismissed row is by invariant a read row, held by
a CHECK. Only the user's click, through the two source classes, ever sets it, and nothing ever clears it. The flag is the bell's own: it removes the row from the
bell's list and from nothing else, and nothing is deleted.**

### D1' — The flag, the invariant and the CHECK (CTO Beslut 1; architect; V18, V20, V22). Extends 0168 D1

- `bool DismissedFromNotificationFeed` (`dismissed_from_notification_feed`, `NOT NULL DEFAULT false`) on `UserJobAdMatch` and `FollowedCompanyAdHit`. An existing row
  reads `false`: no backfill. No timestamp (0168 V10, V22), no reason, no index.
- **The invariant** `dismissed ⇒ read`, one CHECK per table: `ck_user_job_ad_matches_dismissed_implies_read` and `ck_followed_company_ad_hits_dismissed_implies_read`, both
  `NOT dismissed_from_notification_feed OR read_in_notification_feed`. Every dismiss write sets both flags. Precedent for a CHECK through `ToTable(…, t =>
  t.HasCheckConstraint(…))`: `JobSeekerConfiguration`, `FeedbackSubmissionConfiguration`, `TaxonomySnapshotMetaConfiguration`.
- **Why a CHECK.** On follows the invariant lives in the aggregate; on matches it exists only inside one `ExecuteUpdate`. The CHECK turns a future match writer that
  sets one column into a loud failure and not into a dismissed row that silently shows as unread. This is the compensation for a deliberate departure: **the match
  aggregate has no method for the dismissed flag**, as it has none for the read flag (0168 D1), against "state transitions go through explicit methods" (AGENTS.md §2.2).
  The follow side keeps the method.
- **Writers: only the two source classes.**
  - **Match:** ONE `ExecuteUpdate` that sets both flags, filtered on owner, ad and `!Dismissed` (never on the read flag), with the `NotificationConsent.BackgroundMatch`
    `EXISTS` over the user's `JobSeeker` in the same statement (0168 D3). Never `NotificationMailGate`, never the type's switch.
  - **Follow:** the owner's hits of the ad that are not yet dismissed, loaded (filter `!Dismissed`, not `!Read`) and marked through
    `FollowedCompanyAdHit.DismissFromNotificationFeed()`, which sets both flags. The unit of work commits them. No consent gate, as for the follow read mark.
  - Why `!Dismissed` and never `!Read`: the architect's Viktigt 1. Copying `MarkRowAsync`'s `!Read` filter would make the X a 204 no-op on a row already read, which is the
    main case, and would behave differently for a row read in the bell and one read through the watermark.
- **Readers: only the two WINDOW statements,** `MatchFeedSource`'s list and `FollowedCompanyAdFeedSource`'s window, each with `!DismissedFromNotificationFeed` in the SQL
  `where` before `Take` (never in memory: a dismissed hit would otherwise use one of `HitsPerRow`'s slots, move the horizon and reach grading, Art. 5(1)(c)). The predicate
  is added in the bell's own query, never in `NewMatchSet` or `NewHits`, which Översikt and the lists share (Klas 2026-10-11).
- **The rule as the architect wrote it.** Statements that admit read rows exclude dismissed rows; statements that already carry `!ReadInNotificationFeed` exclude them by
  the invariant. So the dismissed filter is **stated once, by the CHECK, and not repeated**: the match unread fallback, the follow common-path and grade-path
  beyond-window statements, the follow count input (`LoadCountInputAsync`), the mark-through and the backlog mark do not name the new column. The plan's extra predicates on
  them were redundant and would breed "some `!Read` statements say it, others don't" (architect N1).

### D2' — What stays as it is, because of the invariant (CTO "must carry" 2). 0168 D2 unchanged

The token (`MAX(CreatedAt)` over the watermark-new set, no flag predicate), the count (the destination's count minus rows read in the bell), parity as an inequality
(Översikt's number at least the bell's), mark-through, the backlog mark and the "not in list" line are unchanged **because** a dismissed row is a read row: they
already leave it out, and a row dismissed from a counted set lowers the bell's count by exactly the rows read, as a read mark does. The parity test, the nine and the
eleven statements (0168 D6) need no change; they are expected unchanged because only `where` clauses were added and the dismiss is its own request, and were not re-run.

### D3' — One-way (CTO Beslut 3). New invariant

**No writer ever sets either flag back to false:** not the switch backlog mark, not "Markera alla", not a list visit, not switching a type off and on, not the mail path.
The `INotificationFeedReadMarker` docblock says so ("No member ever clears either flag"); the CTO requires one test per writer, which the PR panel verifies. The only reverse path is the migration's
Down, destructive by this repository's convention (D7). There is no undo.

### D4' — The wire, the marker and the bucket (CTO Beslut 4; V23, V25). Extends 0168 D3

- `POST /api/v1/me/notification-feed/{source}/{jobAdId:guid}/dismiss`, `NotificationFeedMarkPolicy`, `RequireAuthorization` from the group. **POST and not DELETE:** nothing
  is deleted (RFC 9110 §9.3.5), and on a match row a DELETE would read as the undecided #2138 deletion. It does not collide with `/{source}/read` (different segment count).
- `{source}` is the closed set of 0168 (`matches`, `followed-company-ads`); an unknown one is a 400 before any read (`UnknownSource()` reused), as is an empty id. **204 for every
  request from a signed-in owner:** a missing, foreign, ended or already dismissed row, or a match whose consent is withdrawn, so there is no 404 oracle. The owner comes
  from `ICurrentUser` only. Nothing is read from `JobAds`, so no `JobAdLifecycleReadRegistry` entry is owed. New `DismissNotificationCommand`, handler and validator
  (`IsInEnum`, `NotEmpty`); not audited, no `JobSeeker` mutator, not `IReplayOnConcurrencyConflict` or `IOwnsAccountTransaction` (the writer-replay guard of 0146 D3 holds).
- **The marker.** `DismissRowAsync` is the **fourth member of `INotificationFeedReadMarker`**, not a separate interface: the consent-in-the-same-statement rule belongs to
  every bell write, and the reflection test then forces a third source (#1663) to implement it. A separate interface would need a parallel list and a second reflection
  test, with no ISP gain. The docblock's "What a mark is" paragraph is rewritten.
- **The bucket** is the existing `notification-feed-mark` (TokenBucket per `sub`, 60 per 60 s, QueueLimit 0), shared by all three POSTs. Never `me-write` (a withdrawal must
  not meet a 429 the X caused, Art. 7(3)); no bucket of its own (same writer class, same rows, same user). `security-auditor`'s arithmetic, derived and not measured: a row
  costs at most two calls (open, then X); a panel's burst is 20 reads + 20 X + 2 "Markera alla" = 42, under 60; the X alone is at most 40 a minute (20 per 30 s) under the
  refill of 60; the pathological open-then-remove of every row is about 80 a minute, which the burst carries for about three minutes per tab. A 429 costs a row that comes
  back and a notification, no data and no consent. The `RateLimitingOptions.NotificationFeedMark` docblock is re-derived with these figures.

### D5' — Clarification of 0168 D9: what "hiding of match rows" means (CTO "must carry" 4; security Minor 1; V20)

0168 D9 forbids, while #2138 is open, "deletion, thinning or hiding of match rows". That is read as **system-initiated hiding, or hiding on any surface other than the bell's
own list**, as a substitute for or a foretaste of #2138's answer. The user's own X in the bell, under the consent gate, is not that: it writes a flag on the row itself,
nothing another surface reads (Översikt, `/matchningar`, `NewMatchSet`, the e-mail), the flag is erased with the row, and Klas's answer to #2138 covers it with no second
deletion path, the same as the read flag (0168 D9). The bell already shows no match row while the consent is withdrawn (0168 B1), so the X cannot reach a row in #2138's
surface.

**V20, binding:** the only writer is the user's click through the two sources. No job or system writer, none that clears the flag, never a substitute for Art. 17 or Art. 7(3),
and never written as a consequence of a withdrawn consent (for example "hide rows with withdrawn consent"). **A system writer of this flag is Blocker class** (a pretend
deletion, #2138). Nothing else in 0168 D9 changes.

### D6' — The client (CTO Beslut 5; V24; design). Extends 0168 D4 and D7

- **A Server Action fired without `await`** (`dismissNotificationAction`), like the read mark: no `revalidatePath`, `refresh`, `updateTag`, cookie write or `redirect`; zod
  `strictObject`, `encodeURIComponent` on the id. Triggered only by the button's `onClick`, never by render, effect, hover, focus, prefetch or `onAuxClick`.
- **The row goes at once; the count falls only on the server's confirmation** (exact or absent, ADR 0120): a pending set of keys hides the row, a confirmed dismissal removes
  it from the answer in hand and lowers its source's count by one if it was unread. A refusal or rejection brings the row back, because nothing was deleted, with an
  alert notice (`dismissFailed`). No forced read per click. V25 asks that no second X is sent for a row already pending or gone; the PR panel verifies it.
- **A stale read cannot bring a confirmed dismissal back.** A read that started before a confirmed dismissal is patched with it, as with a confirmed read mark, in memory only;
  **a dismissal outranks a mark**, so a read mark confirmed later does not demote it.
- **Focus** moves before the row leaves the DOM: to the next row's X, else the previous row's, else the panel heading; never to the body. The polite region announces the title.
- **Markup.** The X is a `<button class="jp-icon-btn">` (38 px, 44 px at ≤ 768 px) in a third grid column, a **sibling of the row link and never inside it**, above the link's
  overlay so its click opens nothing. Its accessible name carries the title and the company. DESIGN.md §6 gains the X.

### D7' — One migration (CTO "must carry" 6). Extends 0168 D8

`AddNotificationFeedDismissal` (`20261012085507`, Previous `20261012042409_AddNotificationFeedReadState`). Up: `SET LOCAL lock_timeout = '3s'` first; two
`ADD COLUMN dismissed_from_notification_feed boolean NOT NULL DEFAULT false`; the two CHECKs. No backfill, no index. As in 0168 D8, `ADD COLUMN` with a constant default is
catalog-only but holds ACCESS EXCLUSIVE until commit, and `lock_timeout` turns a queue behind a long transaction into a loud deploy failure the next reconcile retries. Each
CHECK validates existing rows under the lock already held; every row passes (the column is false everywhere), on tables of 0 matches and 379 hits on 2026-10-11 (from
`docs/current-work.md`, not re-measured). `NOT VALID`/`VALIDATE` was not needed at this size. **Down is destructive:** it drops the CHECKs and both columns, so every dismissed
notification returns to the bell as a read one (no row was deleted, and the read flag stays); a code rollback makes `Migrate` refuse (schema ahead, exit 3) until `MIGRATE_ALLOW_SCHEMA_AHEAD` names this id. A per-migration
test covers Up, Down and Up again. **Klas's migration GO is owed for this migration; the GO for #2149 does not carry.**

### D8' — Legal position (`security-auditor` (d); V22, V26, V27). Extends 0168 D5's position

A boolean, no timestamp or reason: no consumer reads the time, and a "removed when" series would be behaviour data without a purpose. Retention and erasure are inherited
(`AccountHardDeleter` deletes both tables; backups follow the row). No new category, recipient or table, so no grant for the exposure list (ADR 0125 Case 2). The field note at the
match-row entry of the local processing register (about line 601, main copy) is extended with `dismissed_from_notification_feed` on both tables, with the same "inherits the
row's lifecycle and Art. 17 cascade"; no policy sentence and no `CurrentPrivacyPolicyVersion` bump (V27). No new log call or scope that carries the ad id; V9's relative form of
0168 s7 stands (V26). Art. 15 is manual today (no self-service export exists), and the field note makes the column visible to that extract.

### D9' — Not built, and not asked about (CTO Beslut 5)

"Rensa lästa", ageing of read rows, "Visa alla", an "Ångra" or any undo, dismiss-all or bulk dismiss, a "visa borttagna" view, any setting, any counter of dismissed rows, and any
change to Översikt, `/matchningar`, `/foretag/bevakade/nya`, `NewMatchSet`, `NewHits` or the producers.

## Alternatives considered

- **A — a second boolean plus the CHECK.** Chosen (D1'). *For:* three states (unread < read < dismissed) by two booleans with the fourth combination forbidden; the token, count,
  mark-through and backlog stay closed for change (OCP); a stray writer fails loudly; no table. *Against:* a second UPDATE writer's flag to keep in step, a destructive Down.
- **B — an independent flag, no CHECK.** *For:* no constraint. *Against:* a dismissed-but-unread row becomes representable, and five statements must learn the flag.
- **C — a tri-state replacing the bool.** *For:* one column. *Against:* rewrites a shipped column and every `!Read` predicate.
- **D — an overlay table.** *For:* no change to the source tables. *Against:* the objections of 0168 Alternatives B stand unchanged (a new aggregate, a `DbSet` in the hotspot, a
  register row, marks outliving rows).
- **E — `DELETE` instead of POST.** *For:* it says "remove". *Against:* no resource is removed (RFC 9110 §9.3.5), and on a match row it reads as the undecided #2138 deletion.
- **F — its own rate-limit bucket.** *For:* isolation. *Against:* same writer class, same rows, same user; 42 of 60 in a burst fits the shared bucket (D4').
- **G — ad-level keying: "hidden if any hit is dismissed".** *For:* closes the follow residual (s1). *Against:* a correlated `NOT EXISTS` in every statement, and it inverts
  0168 D2's "unread while any included hit is".
- **H — the producer inheriting a dismissal** (a new hit for a dismissed ad is born dismissed). *For:* closes s1 at the source. *Against:* fires 0168's own lapse trigger and
  couples the producer to the notification context.
- **I — a separate dismisser interface.** *For:* a narrower contract. *Against:* a parallel list and a second reflection test, more surface and no ISP gain (D4').

## Consequences

### Positive

- The bell can be cleared one notification at a time, with no change to Översikt, the lists, matching, the token or the count; Klas's rule stays a dependency direction.
- The invariant is stated once and enforced by the database for every writer, so six statements rest on it without repeating it.
- No new table, no register row beyond a field note, no policy text; retention and erasure inherited; #2138's answer covers the flag with the row.

### Negative and risks

- **s1. Residual, derived from code and not observed: a removed follow notification can return as one unread row, in one narrow case.** The CTO's reading: the scan takes `now` once per run
  (`CompanyWatchScanJob.cs:75`), reads ads with `CreatedAt > since` and no upper bound (`:209`, `:231`), dedups per (ad, watch) only (`:306`) and advances the watermark to that run's
  `now` (`:321`, `JobSeeker.cs:322-332`), while `JobAd.CreatedAt` is set once (`JobAd.cs:118`). So an ad ingested after run N captured `now` but early enough for N's query to load it is
  loaded again by run N+1; if the user added a new watch on the same organisation between the runs, that ad gets a second hit from the new watch, born undismissed, and shows as one unread
  row. Watches that hit the same ad in one run are created in one `SaveChanges`, so one X covers them. The architect read the producer as unable to produce a newer hit for a dismissed
  ad in the ordinary case (`:228-236`, `:291-322`; matches have `UNIQUE(user, job_ad)` and an insert-only producer) and did not list the boundary race. **Both readings are recorded;
  they agree it is a premise on the producer, not a guarantee.** The keying is unchanged (Alternatives G, H). Lapse trigger: a backfill on a new follow, or a reset watermark.
  The ordinary case is pinned by `CompanyWatchScanJobIntegrationTests.RunAsync_AWatchAddedAfterAScan_GetsNoHitForAnAdThatScanAlreadyCovered`; the boundary race itself is not.
- **s2. Consent withdrawn in another tab, then the X:** 204 with no write (the gate), the client removes the row, and after a new consent the row is back. Correct: no write under a
  withdrawal. Not a defect.
- **s3. Down drops every dismissal;** the notifications return to the bell as read ones (the read flag stays). **There is no undo**, by directive.
- **s4. A dismissed row is gone from the bell for good but not from the lists,** which still show it by design (Klas's rule); the two surfaces differ on purpose.
- **s5. The bucket figures are derived** (D4'); the match-row consent race and the unmeasured V9 relative form carry over from 0168 (s7).

**Lapse triggers.** Single named home: this ADR. Named reader: Klas. This decision is re-examined when:
1. any writer clears either flag, or a writer outside the two source classes writes the dismissed flag (D3', D5', V20);
2. a reader other than the two window statements reads it, or a producer reads it (D1', V21);
3. #2138 is answered in either direction (D5');
4. a timestamp, a reason or a copy of the flag goes to a log or store (ADR 0140, #1663; V22);
5. the flag is used as input, for example "do not show this company again", which is a new purpose;
6. "Rensa lästa", a bulk dismiss, "Visa alla" or a third source arrives, which re-derives the bucket and needs 0168's V16 predicate ("Visa alla" must decide whether dismissed rows
   show there);
7. a self-service Art. 15 export is built (it must include the column);
8. a concurrency token is added to either table (0168 s8);
9. the producer premise of s1 changes (a backfill on follow, a reset watermark).
The lapse triggers of 0168 stand.

## Implementation

One PR on `claude/bell-dismiss-single-notification-48f9af`. The ADR is promoted with `git add -f` (it sits in the ignored 0071–0199 range; precedent 0164, 0168); the README row is
`docs-keeper`'s. 0168 gets a pointer here.

- **Domain:** `UserJobAdMatch.DismissedFromNotificationFeed`; `FollowedCompanyAdHit.DismissedFromNotificationFeed` and `DismissFromNotificationFeed()`.
- **Application:** `NotificationFeed/Sources/{INotificationFeedReadMarker,MatchFeedSource,FollowedCompanyAdFeedSource}.cs`; `NotificationFeed/Commands/DismissNotification/*`
  (command, handler, validator).
- **Api:** `Endpoints/MeNotificationFeedEndpoints.cs` (the route; docblock "Two POST marks" rewritten); `RateLimiting/RateLimitingOptions.cs` (`NotificationFeedMark` docblock).
- **Infrastructure:** `UserJobAdMatchConfiguration`, `FollowedCompanyAdHitConfiguration` (column and CHECK), `AppDbContextModelSnapshot`,
  `Migrations/20261012085507_AddNotificationFeedDismissal.cs` (and Designer).
- **Frontend:** `web/jobbliggaren-web/src/lib/actions/notification-feed.ts`, `src/lib/api/notification-feed.ts`, `src/lib/hooks/use-notification-feed.ts`,
  `src/components/shell/notification-feed-provider.tsx`, `notifications-bell.tsx`, `src/app/globals.css`, the messages (sv, en), DESIGN.md §6.
- **Tests** (condition V19): the Npgsql consent-gate test with a positive control, a seed that differs from it only in the consent withdrawn through the production endpoint, both
  columns asserted unchanged, and a hand mutation of the `EXISTS` and of the `!Dismissed` filter reported in the PR; the X on a row read in the bell and on one read through the
  watermark, both sources; one test per writer for the one-way rule; the pin `Oversikt_and_the_lists_never_read_the_bells_flag` extended with the new column name (V21); the migration test (Up, Down and Up again). Required by the CTO and `security-auditor`; the PR panel verifies them, and none was re-run for this ADR.
- **Owed outside the diff:** Klas's migration GO for this migration; the field note in the local processing register (main copy); the PR panel (`code-reviewer`,
  `dotnet-architect`, `security-auditor`, `design-reviewer`, `db-migration-writer`, `test-writer`).

## Escalations carried verbatim

The CTO's "Till Klas" block is quoted in Swedish, as sent to Klas, without edits (diffed against the source). The first item is information and needs no answer; the second is the
GO this ADR does not give. `security-auditor` marked "Eskalering till Klas: nej" for this round.

**From `docs/reviews/2026-10-12-2151-form-cto.md`, "Till Klas":**

> ## Till Klas
> - **Information, ingen fråga:** En borttagen bevakningsnotis kan komma tillbaka i ett enda smalt fall. Det kräver att annonsen kom in mitt under en bevakningskörning och att du lägger till en ny bevakning på samma företag innan nästa körning. Då kommer den tillbaka som oläst, eftersom det är en ny träff från en ny bevakning. Jag har mätt det i koden, inte sett det hända. Det står som en namngiven rest i ADR:en, och nyckeln ändras inte för det.
> - Migrations-GO:n för #2151 begärs av sessionen med migrationssammanfattningen. GO:n för #2149 gäller inte här.

## References

- `docs/reviews/2026-10-12-2151-form-cto.md` (the ruling) · `…-form-dotnet-architect.md` · `…-form-security-auditor.md` (all local)
- Code: `src/Jobbliggaren.Application/NotificationFeed/` · `src/Jobbliggaren.Api/Endpoints/MeNotificationFeedEndpoints.cs` ·
  `src/Jobbliggaren.Infrastructure/Persistence/Migrations/20261012085507_AddNotificationFeedDismissal.cs` · `web/jobbliggaren-web/src/components/shell/notifications-bell.tsx` ·
  `web/jobbliggaren-web/src/lib/hooks/use-notification-feed.ts`
- ADR 0018 (Amendment 2026-10-10), 0113, 0120, 0125, 0140, 0146, 0162, 0168 · AGENTS.md §2.2, §3.6, §5 · CLAUDE.md §9.2, §9.6, §12
- Martin, *Clean Architecture* (2017), ch. 8 (OCP) · RFC 9110 §9.3.5 (DELETE)
