# ADR 0173 — An ad is one notification in the bell: a match takes precedence, the follow source leaves out the ads the match source holds, and a click or an X reaches every source's rows of the ad

**Date:** 2026-10-17
**Status:** Accepted
**Decider:** Klas Olsson, for the form (answer 1: the match row is the ad's notification), the order of work (answer 2: #2158 before #1663) and the acceptance that a follow
notification returns when match notifications stop reaching the bell (answer 3, R2): the three answers of 2026-10-17, quoted under "Klas's decisions, verbatim". D2–D8 are the
driving session's mechanism, reviewed by the mandatory panel in the PR. No ruling of `senior-cto-advisor` or `security-auditor` is recorded here; D6 is written for the latter's
review.
**Amends:** ADR 0162: the title's "one row per (source, ad)", D2, the effect of D3, D6's "The follow source is not gated", the Alternatives row "One merged row per ad across
sources", Positive's "cannot disagree with Översikt" and s4. ADR 0168: D1's readers, D2's unread definition and its equality clause (I6 becomes I6'), D3's "a click reads the whole
(source, ad) pair" and Alternative A's "the unit stays (kind, ad)". ADR 0169: the Context bullet "A follow notification is a (source, ad) pair", D4', D5' and s1; its Alternative G's
objection is answered. Each stays as the record and carries one header line that points here.
**Related:** [#2158](https://github.com/klasolsson81/jobbliggaren/issues/2158) (this decision ships under it) · [#2163](https://github.com/klasolsson81/jobbliggaren/issues/2163)
(the mail duplicate, not fixed here) · #1662 (the notification epic) · #1663 (the next source) · #1664 and #1665 (the other notification senders) · #2138 (the open GDPR Blocker) ·
#2130 and ADR 0168 · #2151 and ADR 0169 · ADR 0009 (`IAppDbContext` exposes no `Database`) · 0045 · 0080 (Beslut 5) · 0113 · 0120 · 0161 ("Out of scope") · 0162 (D1, D2, D6) ·
0164 (D1: the bell reads no mail pause)
**Measured against:** `origin/main` at `154212486` (#2160's merge) and the branch `fix/bell-one-row-per-ad` at the code commit `0f8dbb51b`, in the worktree
`C:/tmp/jbl-notis-dubbel`, read on 2026-10-17. Nothing here is merged.

> **Provenance.** Written by `adr-keeper` for the driving session from Klas's three answers of 2026-10-17, the driving session's design and the delivered code (CLAUDE.md §9.2,
> §13); it adds no decision of its own. Every statement about the code was read from the worktree on 2026-10-17. Figures are marked test-asserted (a test asserts it; none was
> re-run for this ADR), derived from the code, or documented. The residuals R1–R10 are derived from the code, not observed. The hand mutations that show the pins bite and the
> panel's verdicts are in the PR body and are not repeated here. The mail duplicate in D8 was measured by the driving session on Testcontainers on 2026-10-17 and filed as #2163.
> The ADR sits in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0168–0170), because the code comments point at "ADR 0173".

---

## Context

ADR 0162 D2 made the bell's unit a (source, ad) pair and refused to merge across sources; ADR 0168 kept it ("the unit stays (kind, ad)") and ADR 0169 repeated it for the X. An ad
that is both a background match and a followed company's new ad has therefore been two notifications for two reasons: two rows, read and removed one at a time, and counted twice
in the badge ("by design", 0162 s4). Four facts shape this decision.

- **The second notification usually comes later.** In the common order the match is opened within minutes and the follow hit is born at night (the oracle's comment; the premise
  of Klas's question 3). Under the (source, ad) unit the night's hit rang again for an ad the user had already opened. Derived from the old unit, not observed.
- **Klas's rule of 2026-10-11 binds the fix** (0168 Context). The bell decides only what reaches the bell and never changes what Översikt, `/matchningar` or
  `/foretag/bevakade/nya` shows, so the de-duplication cannot live in `NewHits`, `NewMatchSet` or the definitions those surfaces share.
- **Match rows are produced under a consent and outlive it** (0162 D6; #2138, open with Klas). A follow source that reads match rows must stop reading them when the consent is
  withdrawn, and must not widen #2138 (0168 D9).
- **#1663 adds a third source, and Klas put this first.** The source contract (0162 D1) names no order between sources, and a smart-watch criterion hit can name an ad that is
  also a match or a followed company's ad. Klas ordered #2158 before #1663 (answer 2), so the rule is stated once, here and in the contract, for #1663 to inherit.

## Decision

**An ad is one notification in the bell. While match notifications reach the bell, an ad the user has a match row for is that match row's notification and the follow source
leaves its hits out. A click or an X on the ad reaches the ad's rows in every source, each write under its own gate. The token, the merge, the sets Översikt and the lists share,
the wire and the schema do not move.**

### D1 — The unit is the ad, and a match takes precedence (Klas, answers 1 and 3). Changes 0162 title, D2, s4; 0168 Alternative A

- **Held.** While `NotificationFeedGate.Matches` holds (the background-match consent AND the bell's match switch), an ad the user has ANY match row for is *held* by the match
  source: unread, read, dismissed or behind the match watermark alike. The set is `MatchFeedSource.HeldAdIds`, built on `NewMatchSet`, so it names only the user's match rows on
  Active ads.
- A held ad is the match row's notification and **not** a follow notification: the follow source does not count it, list it or name it in a fallback (D2). **Held depends on the
  gate and never on the row's own state**, so the night's follow hit never rings for an ad the user has already opened, or removed with the X, as a match. The match row keeps
  its grade and its reason; the badge counts the ad once.
- With the gate off (the consent withdrawn, or the bell's match switch off) nothing is held and the follow source speaks for the ad again, by its own read state. Klas accepted
  that in answer 3 (R2).
- An ad with no match row is untouched, and the follow source is still read only while the user's own switch for follows is on.

### D2 — Where it is decided: the follow source, in four statements, in SQL (the session's mechanism). Extends 0168 D1

- The follow source's first statement already reads the user's switch for the type and the watermark. It now also projects `MatchesReachBell`, whether the match gate holds for
  the user, and the held set is built only when it does. With the gate off, no later statement names a match row.
- **Four statements** leave the hits of held ads out, in SQL and before any `Take`: the count input (`NewFollowedCompanyAdSet.LoadCountInputAsync`), the window, and the two
  fallbacks in `ReadUnreadBeyondWindowAsync` (the common-path grouped read and the grade-path display read). A held hit therefore takes no window slot, moves no horizon, does
  not count toward the grade path's bound (`The_count_bound_counts_no_held_hit`) and is never sent to the grader.
- **Not** the token, not `Merge`, and not `NewHits`, `NewMatchSet` or the definition of the set (Klas, 2026-10-11). Plain core LINQ over `IAppDbContext`: no provider member and
  no port (AGENTS.md §2.1).
- **No statement is added.** The predicate sits inside the existing statements, so the base case for the two sources stays at nine, test-asserted with a held ad
  (`A_held_ad_costs_no_statement`, `Held_hits_take_no_window_slot_and_move_no_horizon`). ADR 0168 D6's eleven stands: its pin is in a test file this change leaves alone, and the
  two fallbacks gain a predicate, not a statement (derived, not re-run).
- `LoadCountInputAsync` takes one bell-only `narrowing` expression where it took a boolean for the read flag. The bell passes "not read in the bell and not held"; Översikt
  passes `null`. The method no longer names the flag, which changes 0168 D1's reader clause to "the bell passes the predicate".

### D3 — The numbers: I6' (Klas, answer 1). Changes 0162 D3's effect and Positive; 0168 D2

- **The match source's I6 stands** (0168 D2): its unread is the new-match count (ADR 0080 Beslut 6) less the matches read in the bell. **I6' for the follow source:** its unread is
  Översikt's new-ads count less every ad that is held (only while the gate holds) or whose included new hits are all read or dismissed in the bell, each ad once. So it is at most
  Översikt's, and equal to it while nothing is read in the bell and no ad is held. The bell's follow count is below Översikt's by the held ads, by design; Klas's question 1 named
  this consequence.
- **Översikt's followed-ads count, `/foretag/bevakade/nya` and `/matchningar` do not change.** The narrowing is the bell's alone, and
  `Oversikts_followed_count_and_the_followed_list_never_read_a_match_row` pins on the statement text that their SQL never names `user_job_ad_matches` while Översikt still counts
  the ad the bell shows as a match. 0168's pin that they never name the bell's flags (`Oversikt_and_the_lists_never_read_the_bells_flag`) stands.
- **The badge counts distinct unread ads,** because each ad is counted by one source. The not-in-list line (the sources' counts less the unread rows shown) therefore stays
  exact, apart from R4's one poll; the oracle asserts at every read that the two agree (all its scenarios fit the 20-row panel).

### D4 — The token covers held hits. Extends 0168 D2

The follow token stays the newest `CreatedAt` of ALL new hits, with no read-flag predicate and no held predicate, read first (0162 D3, 0168 D2). It is pinned twice: the unit test
asserts the token is "the newest new hit, held or not", and `The_follow_source_reads_a_match_row_only_while_the_match_gate_holds` asserts that no statement containing `max(` names a
match row. A token narrowed to the ads the follow source shows would fall short of a held hit newer than all of them and strand it above "Markera alla"'s bound.

So **"Markera alla" also reads the held hits** through the follow token. With the gate on this changes nothing the user sees; if the gate later goes off, the ad does not come back
as an unread follow notification for hits the button read. The match side of "Markera alla" is unchanged and has R7.

### D5 — The marks: a click and an X reach every source's rows of the ad (Klas, answer 1). Changes 0168 D3; 0169 Context, D4', D5'

- `MarkNotificationReadCommandHandler` and `DismissNotificationCommandHandler` loop over `NotificationFeedSources.Markers` for the ad, the match source first. **Each write keeps
  its own gate:** the match write is one `ExecuteUpdate` that carries the consent `EXISTS` in its statement (0168 D3), so no match row is written while the consent is withdrawn;
  the follow write loads the owner's hits of the ad and marks them through the aggregate, with no consent gate (0162 D6); and the bell's switch for a type gates no write (0168 D3).
- So with the match switch off and the consent held, a click or an X on a follow row reaches the ad's match row too (`With_the_match_switch_off_a_click_or_an_X_on_the_follow_row_reaches_the_match_row`),
  and with the follow switch off a click on the match row still reads the ad's hits (`With_the_follow_switch_off_a_click_on_the_match_row_still_reads_its_hits`). With the consent
  withdrawn the same click or X writes the hits and never the match row (R6).
- **Still one POST per click or X,** so the mark bucket's figures stand (60 per 60 s; the burst of 42 in 0169 D4' is derived, and unchanged because the call count per row is).
  `Kind` leaves `MarkNotificationReadCommand`, `DismissNotificationCommand` and their validators (`IsInEnum` is gone): a validated field that selects nothing would be a false claim.
  `{source}` on the two row routes stays a closed set with its 400 before the command is sent, and is otherwise a synonym kept for the wire. The routes, bodies, DTO and Server
  Actions are unchanged.
- **The commit is not atomic.** The match `ExecuteUpdate` commits at once; the follow aggregate commits when the unit of work saves after the handler returns (R5). Both writes are
  set-only and idempotent, and no member clears a flag (0169 D3').
- **0169 V20 holds.** The only writer of either flag is still the user's click through the two source classes; nothing writes a match row as a consequence of a withdrawn consent.
  0169 D5' clarified that the user's own X on a match row, under the consent gate, is not the "hiding of match rows" that 0168 D9 forbids. It now also covers an X on a follow
  row that dismisses the ad's match row: user-initiated, bell-only, and only while the consent is held.

### D6 — The legal position (written for `security-auditor`'s review in the PR). Changes 0162 D6

- The follow source reads the **existence** of a match row, never a column of it, for the bell's own purpose: to decide that the match row is the ad's notification.
- Whether `NotificationFeedGate.Matches` holds is read in the follow source's first statement. With it off the held set is never built and no statement of the follow source names
  `user_job_ad_matches`: test-asserted for the withdrawn consent and for the match switch off, where the first statement still returns a row, and for the follow switch off,
  where it returns none (`The_follow_source_reads_a_match_row_only_while_the_match_gate_holds`).
- With the gate on, every statement that uses the held set re-checks the gate inside it, so a withdrawal between two statements stops the reads at the next one
  (`The_match_gate_is_checked_in_each_statement_that_reads_a_match_row`), and the set is the user's own through `NewMatchSet.Matches(db, userId, …)`
  (`Another_users_match_row_holds_nothing_for_this_user`).
- Nothing is projected from a match row into the follow source's output. `HeldAdIds` selects the ad id and is used only as an exclusion; the follow source's items, counts and
  token are built from hit columns.
- The follow source's own rows are still not consent-gated (0162 D6: produced for every active follow, the follow consent governs e-mail only). 0162 D6's "The follow source is
  not gated" stays true of its rows and is no longer true of its reads. The gate it reads is the bell's, the consent and the match switch, with no mail pause (0164 D1).
- 0168 D9's list holds: no store copies a match row, no surface reads match rows without the gate, nothing is deleted, thinned or hidden on another surface, and no write reaches
  a match row while the consent is withdrawn (D5). #2138 is not widened, and whatever Klas answers there the held set stores nothing: it is a read-time exclusion.
- The diff changes no processing register or policy text; whether the review finds one owed is for `security-auditor` to say in the PR.

### D7 — Precedence, for #1663 (Klas, answer 2). Extends 0162 D1

The contract in the `INotificationFeedSource` docblock now states the rule: an ad is ONE notification; where two sources can name one ad, the source without precedence leaves out
every ad the other holds, under the other's gate, inside its own count and list statements and before any `Take`, never in its token. **A match outranks a followed company's ad.**
A third source's place in that order, and whether its rows join the per-ad mark of D5 (`NotificationFeedSources.Markers` is the list a click and an X walk), are decided by its own
ADR, with `security-auditor` as 0162 D1 already requires. `NotificationFeedSourcesTests` makes a third source be listed once and have its marker; no test of the source list checks
the precedence rule, so for #1663 it is carried by the docblock and the review.

### D8 — What this is not

- **No merged row.** The ad is one row, the match row, with its grade and its reason. No row carries two reasons (Alternative B).
- **No change** to the wire (the DTO, the routes, the `{source}` set and its 400), the UI, the copy, the schema (no migration), the producers (`BackgroundMatchingJob`,
  `CompanyWatchScanJob`), or to Översikt, `/matchningar` and `/foretag/bevakade/nya`. The frontend diff is comments, and the X's accessible name is unchanged.
- **No change to the e-mail channel.** The mail duplicate, an ad mailed as a Toppmatch that the next follow digest mails again, was measured on Testcontainers and is tracked in
  #2163; it is not fixed here. ADR 0161 ("Out of scope") had put that de-duplication with #1664 and #1665.

## Alternatives considered

- **A — the match row is the ad's notification.** Chosen (D1; Klas, answer 1). *For:* one row, one count, one X and one read per ad, with nothing new on the wire, in the copy or
  in the design; the row keeps the match's grade; a follow hit born after the match was read never rings. *Against:* the follow count is below Översikt's by the held ads (D3); a
  matched ad has no "Bevakar" notification while the gate holds; R1–R10.
- **B — one row carrying both reasons.** *Against:* a wire field for the second reason, a second label, new copy, a design review, and an inclusion rule that needs grading before
  the second reason could be labelled honestly. Klas chose A.
- **C — the follow row wins.** *Against:* it rings again at night after the match was read, and the row loses the match's grade.
- **D — de-duplicate in `Merge`.** *Against:* the window and the counts would still carry the held hits, so slots are wasted and counts doubled; and in a one-poll race both rows
  are true, so a merge-time de-duplication would make the not-in-list line false (R4).
- **E — "held" means what the bell lists.** *Against:* the ad would ring again as a follow row once its match ages out of the 20 rows or is dismissed.
- **F — the producer skips a hit for a matched ad.** *Against:* it couples the follow producer to the match context and puts a bell rule into a producer, which would lapse the
  producer premises of 0168 D1 ("no producer change") and 0169 s1; and the follow notification would be gone, not only left out, once matches are switched off.
- **Narrower rejections:** the client sending two POSTs per row (it doubles the row calls in the burst of 0169 D4', 2 × 40 + 2 = 82 against 60, derived); a transaction port on
  `IAppDbContext` (ADR 0009 deliberately exposes no `Database`, and both writes are set-only and idempotent); keeping `Kind` on the row commands (a validated field that selects
  nothing is a false claim); an `IQueryable<JobAdId>? leaveOut` parameter beside the boolean (two bell-only parameters in `CompanyWatches` instead of the one `narrowing`); bounding
  "held" by the match token (it couples the two sources' state); materialising "held" as an id list (an added statement, and the `Contains` translation trap over the strongly typed
  id that `NewFollowedCompanyAdSet` already names).
- **0169's Alternative G, answered.** 0169 rejected an ad-level keying for "a correlated NOT EXISTS in every statement". The same shape is accepted here and is bounded: four
  statements, each an anti-join (`NOT EXISTS`, derived from `!held.Any(...)`) against the user's rows in `user_job_ad_matches`, which the unique index
  `ux_user_job_ad_matches_user_jobad` on `(user_id, job_ad_id)` can serve, and no added statement. The cost is derived from the schema and the LINQ and not measured; no plan was
  read (lapse trigger 9).

## Consequences

### Positive

- The bell cannot ring twice for one ad: one row, one count, one X and one read per ad while match notifications reach it. The rule is pinned by the oracle
  `NotificationFeedOneNotificationPerAdTests` (Testcontainers Postgres and the production endpoints; the rule is a model in the test and no expected value is written by hand; it
  covers the common and the grade path, a click, an X, "Markera alla", both bell switches, the consent through its own endpoint and the list visits) and by the InMemory theory
  `FollowedCompanyAdFeedSourceHeldAdTests` (every combination of the match row's state, the gate's state and the path).
- Held ads are never graded: the grade path's candidates are the narrowed hits, so no held ad is sent to the grader
  (`On_the_grade_path_a_held_ad_is_neither_counted_graded_nor_listed_and_never_crowds_the_display_read`, and the theory's grader-call assertion).
- Counts stay exact or absent (ADR 0120): the narrowing sits in the statements that count and list, so a row and the count beside it are decided by one predicate (0168 I3).
- No schema or producer change; Klas's rule of 2026-10-11 stays a dependency direction, since the narrowing is the bell's alone. #1663 inherits a stated rule and does not choose one.

### Negative and risks

- **Cost and coupling.** Four anti-joins, derived and not measured (D2, Alternatives). The follow source now calls the match source's `HeldAdIds`, a one-way dependency from the
  source without precedence to the one with it (D7).
- **R1. A hit read first and a match born later returns the ad as an unread match.** In the ordinary order the match row is born within minutes of the ad's ingest (ADR 0161), before
  the hit. The reverse needs the matching run to reach back over ads it had not matched, which happens after a consent grant: the scan reaches back at most seven days
  (`BackgroundMatchingJob.ColdStartDays`; the session's reading). Then the ad is held, its read hit is invisible and the new match row is unread.
  `A_followed_ad_read_first_returns_as_an_unread_match_when_its_match_row_is_born_later`.
- **R2. With the match gate off, held hits that are new and unread show as unread follow notifications.** The common case: matches switched off in the bell, or the consent
  withdrawn, and the night's hit speaks for the ad by its own state. Klas accepted this (answer 3).
  `A_hit_born_after_its_match_was_read_never_rings_while_matches_reach_the_bell` (its second half switches matches off and on).
- **R3. Matches switched back on absorb a period.** A match row born while the switch was off is marked read by the switch-on backlog mark (0168 D5) and its ad is held again, so
  that period's unread follow notification goes with it. `Switching_matches_back_on_takes_the_ad_as_a_read_match`.
- **R4. One-poll races.** The two sources, and the follow source's four statements, are separate statements on one context with no transaction across them, and the held set is
  evaluated in each. A match row born, read, dismissed or gated off between two of them can leave an ad absent, listed twice or counted ±1 for one poll; the next poll is
  consistent. `Merge` does not de-duplicate (Alternative D), and the not-in-list line can be off by one for that poll. The per-statement gate re-check is pinned
  (`The_match_gate_is_checked_in_each_statement_that_reads_a_match_row`); the race itself is not.
- **R5. The two writes commit separately, the match first** (D5). A failure between them leaves the match row marked and the ad's hits not. While the gate holds the ad is held and
  nothing shows; if the gate goes off, the follow notification rings as unread. A repeated request completes the other half. Not pinned.
- **R6. Consent withdrawn, then a click or an X on the follow row** writes the hits and not the match row (0168 D3, B1). After a new consent the ad is an unread match again. It is
  the class of 0169 s2, and #2138 is the open question about those rows. `With_the_consent_withdrawn_the_follow_rows_speak_and_no_write_reaches_a_match_row`.
- **R7. "Markera alla" while matches are off sends no match token** (the gated source returns none: 0162 D6), so after the switch-on or a new consent the match row rings.
  `Mark_all_while_matches_are_off_leaves_the_match_row_unread_for_when_they_are_back_on`.
- **R8. A behaviour change at merge, for Klas's information.** Today's doubles collapse into the match row, so the badge drops by one for each ad that was counted twice, and an
  unread "Bevakar" notification for an ad whose match is read or removed disappears. How many such ads exist on the box was not measured for this ADR.
- **R9. Consent withdrawn with more than 10 000 new hits on the grade path.** The held hits rejoin the probe, so it can pass `MaxCountedHits` (10 000, 0162 D7) and the follow count
  turns refused: an empty pill, ADR 0120 clause 5. It needs a grade-path account with that many unacknowledged hits. Derived; not pinned.
- **R10. A confirmed click patches by (kind, ad).** `withRowRead` and `withRowDismissed` find the row by kind and ad in the answer the client holds. If the next answer lists the ad
  under the other kind (R1, R4), the patch finds no row and the dot stays until the following read, by which time the server has written both sources' rows. The by-kind rule is
  pinned by `use-notification-feed.patch.test.ts`; the cross-kind case is not.

**Decided, not residual:** the follow count below Översikt's by the held ads (D3); an already-matched ad never gets a "Bevakar" notification while the gate holds (D1); `{source}`
on the row routes is a synonym kept for the wire (D5); and a click or an X on a follow row reaches the ad's match row while the consent is held, whatever the match switch says (D5).

**Lapse triggers.** Single named home: this ADR. Named reader: Klas. This decision is re-examined when:
1. #1663 arrives, or any third source: its place in the order and whether its rows join the per-ad mark (D7);
2. `NewMatchSet` changes, or the match source filters its rows beyond its gate and the X, since the held set is built on that definition and holds an ad for any row (D1);
3. #2138 is answered, in either direction (D6);
4. a follow or match backfill, or a change of scan cadence, which changes the order in which a hit and a match row are born (R1, 0169 s1);
5. a partial match switch, since "held" rests on the whole gate (D1);
6. more than one POST per row, or the mark bucket is re-derived (D5);
7. a "Visa alla" page (0168 D9);
8. a transaction port on `IAppDbContext` (D5, R5);
9. a measurement against ADR 0045, which replaces the derived cost of the anti-joins (D2);
10. the mail de-duplication is decided: #1664, #1665 or #2163 (D8).

The lapse triggers of 0162, 0168 and 0169 stand.

## Implementation

One PR on `fix/bell-one-row-per-ad` (code commit `0f8dbb51b`, rebased onto `154212486`), with no migration, so no migration GO is owed. The ADR is promoted with `git add -f`
(precedent 0168–0170) and its README row is the driving session's to add. 0162, 0168 and 0169 each get one header line that points here and nothing else. The number follows 0171
(on `origin/main`) and 0172 (open as PR #2162), so the README rows of 0172 and 0173 may need ordering when both merge.

- **Application.** `MatchFeedSource.HeldAdIds`; `FollowedCompanyAdFeedSource` (the first statement, `CountedInTheBell`, `NotHeld`, `ReadUnreadBeyondWindowAsync`);
  `NewFollowedCompanyAdSet.LoadCountInputAsync` (`narrowing`) and `GetNewFollowedCompanyAdCountQueryHandler` (passes `null`); `NotificationFeed/Commands/{MarkNotificationRead,DismissNotification}/*`
  (the handler loop; `Kind` gone from commands and validators); the contract text of `INotificationFeedSource`, and the docblocks of `INotificationFeedReadMarker`,
  `NotificationFeedSources` and the feed DTOs.
- **Api.** `MeNotificationFeedEndpoints`: the two row routes check the segment against the closed set and send the command without a kind; the through-token route is unchanged.
- **Frontend.** Comments only (`notifications-bell.tsx`, `lib/dto/notification-feed.ts`, two tests). DESIGN.md §6 loses the parenthetical "the reason tells two notifications of one
  ad apart" from the X's description.
- **Tests.** New: `NotificationFeedOneNotificationPerAdTests` (Api.IntegrationTests) and `FollowedCompanyAdFeedSourceHeldAdTests` (Application.UnitTests). Edited for the new unit:
  `FollowedCompanyAdFeedSourceReadStateTests`, `FollowedCompanyAdFeedSourceTests`, `GetMyNotificationFeedQueryHandlerTests` (the merge test no longer expects two rows for one ad),
  `NotificationFeedMarkerTests` (the row commands lose their kind; the signed-in half moved to the oracle, because the X now reaches the match source's `ExecuteUpdate`, which
  InMemory cannot run), and the lifecycle registry text for `NewMatchSet.Matches` in the Architecture tests (ADR 0113; no entry is added, since the held set composes that one call).
- **Owed outside the diff.** The §9.2 panel reviews the implementation in the PR, with D6 written for `security-auditor`; its verdict table and the hand mutations are in the PR body.

## Klas's decisions, verbatim

Klas answered three questions in the session on 2026-10-17. They are quoted in Swedish, as asked and answered, without edits (compared character for character with the session's
record of the questions and answers).

**1. The form of the one row** (applied in D1 and D3):

> **Q:** "Ska den ihopslagna raden vara matchraden, så att bevakar-träffen på samma annons inte syns alls i klockan? Eller en rad som bär båda skälen? Det avgör hur talet förhåller sig till Översikt."
>
> **A:** "Matchraden (Rek.)"

**2. The order of work** (applied in the Context and D7):

> **Q:** "Spåraren säger att din prompt 2 (bakgrundsmatchning utan samtycke) är nästa steg, och att epikens ordning sedan är #1663 → #1665 → #1664. Ska #2158 gå före #1663?"
>
> **A:** "Ja, #2158 först (Rek.)"

**3. The return of the follow notification when matches stop reaching the bell** (applied in D1 and R2):

> **Q:** "Med Matchraden gäller detta: om du stänger av matchnotiserna i klockan, eller drar tillbaka samtycket till bakgrundsmatchning, visar klockan bevakar-notiserna igen för annonser du redan har sett som matchning. Bevakningsträffen kommer oftast in på natten efter att du öppnat matchningen, så den visas då som oläst. Är det okej?"
>
> **A:** "Ja, det är okej (Rek.)"

## References

- Code: `src/Jobbliggaren.Application/NotificationFeed/Sources/` (`MatchFeedSource`, `FollowedCompanyAdFeedSource`, `INotificationFeedSource`, `INotificationFeedReadMarker`,
  `NotificationFeedSources`) · `src/Jobbliggaren.Application/CompanyWatches/Queries/NewFollowedCompanyAdSet.cs` · `src/Jobbliggaren.Application/NotificationFeed/Commands/` ·
  `src/Jobbliggaren.Api/Endpoints/MeNotificationFeedEndpoints.cs`
- Tests: `tests/Jobbliggaren.Api.IntegrationTests/NotificationFeed/NotificationFeedOneNotificationPerAdTests.cs` ·
  `tests/Jobbliggaren.Application.UnitTests/NotificationFeed/FollowedCompanyAdFeedSourceHeldAdTests.cs`
- ADR 0009, 0045, 0080, 0113, 0120, 0161, 0162, 0164, 0168, 0169 · AGENTS.md §2.1, §3.6, §5 (Tests) · CLAUDE.md §9.2, §13
