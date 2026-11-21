# ADR 0188 — Quick links in the `/jobb` hero: at most three, a stamp for the junior link, a store-bounded slot on a recent-search row for a search, no eviction of a marked row, and the count a signed-in read on its own bucket

**Date:** 2026-11-20
**Status:** Accepted 2026-11-20 (Klas's answers of the same day, below, and the rulings of `senior-cto-advisor` B1–B8). The mandatory agents review the delivered diff in the PR.
**Deciders:** Klas Olsson (controller and product owner). On 2026-11-20 he answered, verbatim, "Egen PR efter denna (Rek.)" and "Ja, 3 totalt (Rek.)" (recorded in ADR 0187's Deciders line), after which the `/jobb` banner link moved out of #2264 to this change. ·
`senior-cto-advisor` (plan read and rulings B1–B8, report-only, `docs/reviews/2026-11-20-2265-plan-senior-cto-advisor.md`, marked "(CTO B1)" and so on below) ·
`dotnet-architect` (plan read and a follow-up on security-auditor's Major 1, report-only, `docs/reviews/2026-11-20-2265-plan-dotnet-architect.md`, marked "(architect Viktigt 1)", "(architect Q5)", "(architect N6)" and, for the follow-up, "(architect follow-up Q3)") ·
`security-auditor` (plan read, report-only, `docs/reviews/2026-11-20-2265-plan-security-auditor.md`: 0 Blocker, 2 Major, 6 Minor, marked "(security-auditor Major 1)", "(security-auditor Minor 3)" and her answers "(security-auditor Q2)"; and her m3 of the #2264 plan read, recorded on #2265, marked "(security-auditor m3)")
**Amends:** ADR 0060 (eviction, Beslut 5, and "no audit-trail dignity", Beslut 6), by its dated amendment "Amendment 2026-11-20 (#2265, ADR 0188)". Nothing above that amendment is rewritten.
**Related:** [#2265](https://github.com/klasolsson81/jobbliggaren/issues/2265) (this change) ·
ADR 0187 (the junior membership the count composes; its "What comes back" points here) · ADR 0186 (`/junior`) · ADR 0060 (the recent searches, amended) · ADR 0166 D10 (no trigger fires, D7) · ADR 0045 (budgets) · ADR 0164 and ADR 0168 (the stamp form) · ADR 0146 (replay of a lost update on `job_seekers`) · ADR 0039 (`SavedSearch`, not used by the web since ADR 0060) · ADR 0031 (cross-user access) · ADR 0022 (audit) · ADR 0142 D6 (the Art. 13 notice version) · ADR 0154 (every merge is a deploy) · ADR 0065 (docs in the same PR) · DESIGN.md §6
**Measured against:** `feat/jobb-quick-links` in `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-quick-links-2265-539069`, cut from `bc2235aba` (`origin/main`, the source of release 1681) and read in its uncommitted tree on 2026-11-20.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D9 are the brief's, and each carries the name of the person or agent whose ruling it is. Facts about the code were read on 2026-11-20 in the tree named above. R1 is the driving session's reading of the same day; its numbers were compared with `docs/reviews/2026-11-20-2265-latency.md` and none was re-run for this ADR. What no source measures is listed under *What is not measured*. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0187).
>
> **Numbering.** 0188 was measured free on 2026-11-20 at about 19:10Z by the driving session, in this worktree, the main copy, every other worktree and `origin/main`, and was re-checked by `adr-keeper` before this file was written (the same places and `C:/tmp/jbl-2199-pr3`; `origin/main` at `bc2235aba`).

---

## Context

- **What Klas required, 2026-11-20.** Quick links ("snabblänkar") in the `/jobb` hero for signed-in users: the junior link "Juniora IT-jobb · N just nu" to `/junior`, there by default and removable with × in the hero and in settings (a fifth section of Mina sidor, `/mina-sidor/snabblankar`), and links to the user's saved searches. At most three links in all, the junior link counted. The banner link left #2264 for this change at his word (ADR 0187, Scope).
- **A "saved search" in this app is a recent search.** The saved searches the web shows are the auto-captured *Senaste sökningar* (`RecentJobSearch`, cap 20, ADR 0060). `SavedSearch` (ADR 0039) has not been used by the web since ADR 0060's Amendment 2026-05-20.
- **A recent-search row is deleted on four paths** (CTO B1, B2): the user's delete, the capture's eviction, the account hard-delete, and the Art. 17 recruiter erasure (`EraseRecruiterAdsCommandHandler`), which deletes other users' rows. A link stored outside the row has to be cleaned by all four.
- **The 20-row bound is a promise to the user.** The public policy says "20 senaste sökningar" (ADR 0060 Beslut 5). A marked row is exempt from eviction (D5), so the number of marks has to be bounded by the store, or the promise can be broken by a race (security-auditor Major 1).
- **The count is a fifth read on ADR 0045's hot path.** The `/jobb` hero's `Promise.all` holds four reads; ADR 0045 sets class (a) at p95 300 ms. The count is `/junior`'s own total for a signed-in user. security-auditor's m3 puts five conditions on it, and ADR 0187's "What comes back" now points here, so this ADR is m3's only home (security-auditor Minor 3).
- **The legal outcome is split.** The mark is a new stored field of the existing processing "Senaste sökningar", so the public text and the register change (D9). The count fires none of ADR 0166 D10's triggers (D7).

## Decision

**A signed-in user's `/jobb` hero carries up to three quick links, the junior link counted. The junior link is on by default and is one stamp on the user's preferences, null meaning shown. A link to a search is a slot from 1 to 3 on that user's own `recent_job_searches` row, and a partial unique index lets the store admit each slot once per user, so no race marks a fourth search. The capture never evicts a marked row. The count behind the junior link is `/junior`'s own total, read per request on a bucket of its own, and a read that fails hides the link.**

**Scope.** One PR on `feat/jobb-quick-links`, #2265, separated from #2264 by Klas. One migration, no package, no change to `Program.cs`. ADR 0187's membership, row, page and bucket are unchanged: the count composes them and never copies them.

### D1 — Storage, the junior link: one stamp, null meaning shown (CTO B1; architect Q1)

- `Preferences` gains `JuniorQuickLinkOffAt`, a nullable stamp in the form of ADR 0164's and ADR 0168's switches. Null means the link is shown, so there is no backfill and every row written before the migration shows it. `JobSeeker.SetJuniorQuickLink(on, clock)` is its only writer; asking for the state the row already has changes nothing. The profile DTO carries `JuniorQuickLinkEnabled`, a bool, never the stamp.
- It needs no DDL: the key lives in the `job_seekers.preferences` jsonb and moves the model snapshot only.
- **No list of links is stored anywhere.**
  - A list of search ids on `JobSeeker` was rejected (CTO B1): each of the four delete paths would have to clean it, and the Art. 17 erasure would have to write other users' `JobSeeker` rows, which carry row-level concurrency checks (ADR 0146), inside its own transaction. The alternative is dangling ids that still count toward the cap (Vernon 2013, ch. 10). A collection in `OwnsOne().ToJson()` is also the mapping `JobSeekerConfiguration` records as unstable (Npgsql #3129).
  - A table of its own (the CTO's H2) was rejected for the same dangling ids, plus a table, a cascade row and a read.

### D2 — Storage, a search link: a slot on the search's own row, bounded by the store (CTO B2; mechanism by architect follow-up Q1, after security-auditor Major 1 and Minor 1)

- **The row holds the link.** `recent_job_searches.quick_link_slot smallint NULL`, a CHECK `quick_link_slot BETWEEN 1 AND 3` (interpolated from `QuickLinkCap.MaxTotal`) and a partial unique index on `(job_seeker_id, quick_link_slot) WHERE quick_link_slot IS NOT NULL`. The store therefore admits at most three marked searches per user whatever races occur.
- **The link goes with its row** on all four delete paths, so a link to a deleted search cannot exist. Label and href come from the read the hero already makes.
- **No timestamp beside the slot** (security-auditor Minor 1; architect follow-up Q1): a stamp would have no reader, and the slot's reader is the index. The DTO carries `IsQuickLink`, a bool, and the hero orders the search links in the list's order, because "lowest free slot" gives no order a user would recognise.
- **Rejected (CTO B2).** (b) A copy of the criteria in a quick-link store of its own: a second plaintext copy of `q` and `employer_list`, which for a sole trader is a personnummer, and a new sink for the erasure registry and its match query. (c) Reviving `SavedSearch`: what ADR 0060 retired, with run semantics, soft delete and routes without rate limits. The CTO's first form of B2, a nullable `QuickLinkAt` and a cap checked only in the handler, gave way to the slot on security-auditor's Major 1: the check reads first and writes after on a table with no lock or token, so concurrent marks on different rows all pass, and marked rows are never evicted, so the excess would not shrink back.

### D3 — The cap: a stateless domain rule, asked only when the add would change something (CTO B2; architect Viktigt 1)

- `QuickLinkCap` in `Jobbliggaren.Domain.JobSeekers` (a home that keeps Domain's one-way reference from `RecentJobSearches` to `JobSeekers`): `MaxTotal = 3`, `FreeSlot`, and `EnsureRoomForOne(juniorLinkOn, markedSearches)`, which fails with `DomainError.Conflict("QuickLink.MaxTotal")`, a 409.
- **The junior link counts while it is on, also when the hero hides it at N = 0,** so a link added at 0 cannot push the set past three when N returns (CTO B2).
- **Each "add" handler asks only when the add would change something,** so a repeated add is a no-op 204, and an ADR 0146 replay does not answer 409 (architect Viktigt 1).
- **What the store bounds and what it does not.** "At most three marked searches" is store-enforced (D2). "Three in all" spans a jsonb stamp and table rows, which no constraint can span, so it is checked per request, and a junior toggle racing a mark can show four links. Accepted (CTO B2; architect Viktigt 1 and follow-up Q4); the reads cope with four.

### D4 — The writes (architect Q5, Viktigt 2, N6 and follow-up Q2; security-auditor Q5, Q6)

- **`PUT /api/v1/me/junior-quick-link` `{ enabled }`:** 204, or 409 `QuickLink.MaxTotal` when turning on over the cap. `SetJuniorQuickLinkCommand` is `IAuthenticatedRequest`, `IAuditableCommand`, `IAuditPayloadCommand` and `IReplayOnConcurrencyConflict`; the audit event is `JobSeeker.JuniorQuickLinkUpdated` with the payload `{enabled}` and nothing else. The route carries its own `.RequireAuthorization()`, because `/api/v1/me` authorizes per route and `MeWritePolicy` gives an anonymous caller no limiter (architect Viktigt 2). It can answer two 409s, `QuickLink.MaxTotal` and `Concurrency.Conflict`, so the web branches on the problem title and not the status (architect N6).
- **`PUT` and `DELETE /api/v1/me/recent-searches/{id}/quick-link`:** the id only, never a body, so the capture gate stays the only way a search reaches the table and no link's href can carry a personnummer-shaped employer (security-auditor Q6, Praise).
  - **Mark** (`RecentJobSearch.QuickLinkMarked`, no payload) saves on its own, as `SaveJobAdCommandHandler` does, because a handler marked for replay may not save. A row deleted meanwhile answers 404. A slot taken by a racing mark answers 409 `QuickLink.SlotTaken`, a lost race and not the cap, since room may remain. Each failed write is detached first, or the unit of work's own save would send the failed UPDATE again outside the catch. The consequence: its audit row commits in the unit of work's save after the handler's own, the trade-off `SaveJobAdCommandHandler` already makes (architect follow-up Q2).
  - **Unmark** (`RecentJobSearch.QuickLinkUnmarked`, no payload) carries `IReplayOnConcurrencyConflict` and no save of its own. It writes NULL, which the partial index never covers, and a row deleted meanwhile re-reads as 404 (architect follow-up Q2).
  - Another user's row answers the same 404 and is logged (ADR 0031).
- **Audit never carries a criterion, a label, a href or a count** (security-auditor Q5): `employer_list` can hold a sole trader's personnummer, and `audit_log.payload` is registered as `Pseudonymised`, which the recruiter erasure's search does not reach.

### D5 — Capture eviction: the unmarked rows, the whole surplus, one conditional delete (architect follow-up Q3; security-auditor Major 1)

- `RecentJobSearchCapturer.EnforceCapAsync` reads the ids of the oldest UNMARKED rows, the whole surplus `count − 20 + 1` and not one row, deletes them with `ExecuteDeleteAsync` that re-checks `quick_link_slot IS NULL` in the same statement, and inserts only if every picked row was deleted.
- **A marked row is never evicted.** A row marked between the read and the delete survives, and that capture goes unstored: never a 21st row. The bound of 20 holds whatever the slots are, and an overshoot left by two concurrent captures shrinks back, which one-out-one-in never did.
- **No concurrency token on the slot** (architect follow-up Q3): it would let a user's click make an Art. 17 erasure or an account hard-delete fail, the contention the CTO rejected H1 for.
- ADR 0060 Beslut 5 and 6 are amended for a marked row in the same PR (CTO B8): Amendment 2026-11-20, appended to that ADR.

### D6 — The count and its read path (CTO B3, B4; security-auditor m3; architect Q7, N6)

- **The route.** `GET /api/v1/job-ads/junior-it-count`, in the signed-in `JobAdsEndpoints` group. `GetJuniorItJobAdCountQuery` (`IAuthenticatedRequest`) sums `IJuniorItJobAdQuery.CountByMunicipalityAsync(now)` at one instant, the null-kommun row included: `/junior`'s own unfiltered total (`GetPublicJuniorItJobAdsQueryHandler`). No shared helper is needed, because both call one port method, one statement read at one `now` (architect Q7).
- **security-auditor's m3, all five parts, held here:**
  1. *A per-user rate-limit policy with a wiring test.* Its own bucket `job-ad-junior-it-count`, `PerUserTokenBucket`, with `ListRead`'s numbers, 60 per 60 s. The count fires on exactly the events the list read does, every `/jobb` render including each word of live search, so a smaller bucket would hide the link while the list still loads. `facet-counts` is rejected by its own docblock (a read that fires once per render must not share a budget with the popovers' bursts), and `list-read` is excluded by m3 (CTO B4).
  2. *`Cache-Control: private, no-store`,* set inline as `/remote-count` does (CTO B4). A 401 or 429 written by the framework carries no count, RFC 6585 §4 forbids caching a 429, and `Program.cs`'s path list is for refusals that must not be cached in their own right, so `Program.cs` is not touched (security-auditor Q1).
  3. *No Next cache.* The web reader goes through `authedFetch`, which forces `no-store`, with no `cache()`, `unstable_cache` or `revalidate` (ADR 0166 trigger 22).
  4. *The handler logs nothing,* pinned by a source scan.
  5. *The link is hidden at 0 and on error,* and also on a profile-read error (CTO B3).
- **A fifth PARALLEL read in the hero's `Promise.all`** (CTO B3). Parallel reads cost as much as the slowest, so a cheap read changes nothing unless it becomes the slowest. Rejected: (ii) chaining it after the profile read, which runs the two one after the other on the default path; (iii) a streamed island, because the link's presence depends on the count, so streaming would insert or remove it late, a layout jump on mobile that DESIGN.md exists to prevent. The reader carries `AbortSignal.timeout` of 2 s, so a decoration cannot set the hero's worst case, and never throws.
- **The gate.** The CTO made the p95 a gate before the web read path is built, and above 300 ms it came back to him. R1 passes it. The architect (Viktigt 3) required the measurement to time the statement itself, kommun ids seeded, and the derivation to say that at 60 calls a minute the ~1 s anchor holds only up to about 16.7 ms p95, above which security-auditor re-derives the bucket whatever class (a) says.

### D7 — ADR 0166: no trigger fires (security-auditor Q2, Q3, Q4)

- **22:** the sum composes `IsListed(now)` and `JuniorMembership.Of` on every request and is uncached; sign-in decides who may read the number, not which ads are counted. **24:** every column it names is about the ad, and the stamp holds no ad id, verdict, count or matched span. **1 and 21:** the count is not public and is one total. ADR 0166's Amendment 2026-11-20 already places "a signed-in count or link on `/jobb` (#2265)" outside the public conclusion.
- **Her signature on that amendment stands on three conditions:** the handler reads only through the port, which keeps its two members (her R7); the ADR 0166 diff in this PR is exactly the parked `<D11>` and `<M4 (c)>` lines, because any other change to the amendment's text lapses the signature, and #2265's own reasoning therefore lives here; and the parked readings are 0 in the three organisation-number fields and both erased-ad counts (2026-11-20, 16:58Z), so nothing comes back to her before the post.
- **The junior preference needs no policy line** (Q3): ADR 0164's mail pauses and ADR 0168's bell switches have none, and the policy's "anpassa tjänsten efter dina val" covers it. Showing the link by default is not an Art. 25(2) opt-out default, since the "on" state stores nothing and reads a global number. There is no DPIA consequence (no profiling, no new data category, no new recipient) and no Art. 5(1)(e) concern once the store bound holds (Q4).

### D8 — DESIGN.md (CTO B5)

All three are written before the web build.

- **A `/jobb`-only clause in the band rule.** The line stays one plain sentence; the links are a `<nav>` beside it. Its scope is pinned in `band-lines.test.ts`, or nothing stops the exception from spreading to other bands. Putting the links outside the band was rejected, because the issue fixes the placement; Klas set the band rule in #1917, and #2265 is his later, specific instruction for this surface.
- **A fourth named direct-removal exception** to the destructive-action rule, under the criterion that rule already uses (WCAG 3.3.4): removing a quick link is undone where it came from, the junior link in Inställningar › Snabblänkar and a search link from its row in Senaste sökningar while the search exists. The rule's focus and status text applies as written. A confirmation dialog was rejected (friction on a reversible act Klas specified as ×), and so was "Ångra" (a timed control in a status region, duplicating two ways to restore).
- **`loading.tsx` reserves one row.** The link row changes the band's height only at ≤768 px, where the links can wrap onto more rows (R2). The "4-way `Promise.all`" comment in `loading.tsx` is wrong after this PR and is fixed in it.

### D9 — Legal text and register (CTO B2 binds; security-auditor Major 2, Minor 4, Minor 6)

- **`content-legal.json`, sv and en.** The sentence on where searches are shown gains the hero and the settings section. The list of what is kept about each search gains "Om du har gjort sökningen till snabblänk." / "Whether you have made the search a quick link." (her draft ended "och i så fall när" / "and if so when", dropped with Minor 1, since no timestamp is stored). The eviction sentences in the section and in the retention list gain the quick-link exception. The CTO: this is not Klas's question (same purpose, the user controls it, bounded), and security-auditor reviews the wording.
- **The version.** The policy version moves in all three places: `TermsAcceptance.CurrentPrivacyPolicyVersion` and both "Senast uppdaterad" lines. It was already `2026-11-20`, set by #2241 (security-auditor Minor 4). Release 1681 (source `bc2235aba`) carries the text without the quick-link sentences; the release that carries this PR's text is known only after the merge (*What is not measured*).
- **The processing register's "Senaste sökningar" row** (the main copy, gitignored) records `quick_link_slot`, the eviction exception, and that both new stored values, the slot and the junior-link stamp, go into the manual Art. 15 extract, since there is no self-service export (security-auditor Minor 6).

## What is not measured

- **The count's p95 on the production box,** and **the hero's added wall-clock in production.** R1 times the handler alone on a developer machine.
- **How often a junior toggle racing a mark shows four links** (D3).
- **How often a capture goes unstored** (D5).
- **Which release carries the new policy text,** known only after the merge (D9).

## Alternatives considered

### Alt A — The chosen design (D1–D9)
**For:** a link cannot outlive its search, and no copy of the criteria exists; the store bounds the marks and the capture keeps the 20-row bound whatever the slots are; the junior link needs no backfill; the count is `/junior`'s own number at 7.9 ms p95 and hides the link when it fails; the writes take an id only, so the capture gate stays the only way a search reaches the table.
**Against:** the trade-offs listed under Consequences.

### The forks
- **A list of search ids on `JobSeeker`, or a table of its own (H2).** Rejected (D1, CTO B1).
- **(b) A copy of the criteria in a quick-link store; (c) reviving `SavedSearch`.** Rejected (D2, CTO B2).
- **A nullable timestamp `QuickLinkAt` with a handler-checked cap.** Replaced by the slot (D2, security-auditor Major 1 and Minor 1). Her second option, a capture that never inserts while every row is marked, with a `Take(MaxPerSeeker)` on the list read, was not chosen: she preferred the store bound.
- **A database guard on "three in all".** Impossible: no constraint spans a jsonb key and table rows (D3, architect Viktigt 1).
- **A concurrency token on the slot.** Rejected (D5, architect follow-up Q3).
- **Evicting one row, as ADR 0060 Beslut 5 did.** Replaced by the whole surplus (D5).
- **The count chained after the profile read, or a streamed island.** Rejected (D6, CTO B3).
- **`facet-counts` or `list-read` for the count; the header through `Program.cs`'s path list.** Rejected (D6, CTO B4).
- **The links outside the band; a confirmation dialog or "Ångra" on ×.** Rejected (D8, CTO B5).
- **A block on `/sokningar` in place of a fifth settings section.** Rejected: `/mina-sidor` is what the app calls "Inställningar" (`common.json`), the issue says settings, and a block on `/sokningar` would put settings into a history list. Both "add" entry points call the same action (CTO B6).

## Consequences

### Positive
- A link lives on the user's own row and goes with it on every delete path. There is no copy of the criteria, no new erasure sink and no registry row (CTO B2; architect Q8).
- The store bounds the marked searches at three, and the capture keeps the 20-row bound whatever the slots are. An overshoot from concurrent captures shrinks back.
- The junior link needs no backfill: a profile with no key shows it.
- The count costs 7.9 ms p95 on a developer machine (R1), well inside the per-account anchor at 60 a minute, and a read that fails or times out hides the link and delays nothing beyond 2 s.
- The hero never inserts or removes the link late: the count is read in parallel and decides the link in the same render.

### Negative and accepted trade-offs
- **A fifth read on the hot path,** with a bucket of its own and a wiring test to keep right. The 2 s timeout bounds its worst case, not its added time in production (unmeasured).
- **"Three in all" is checked per request,** so a junior toggle racing a mark can show four links (D3). Unmeasured.
- **A capture can go unstored** (D5). Unmeasured.
- **A mark's audit row commits in a second transaction,** the unit of work's save after the handler's own (D4).
- **The cap's number lives in three places:** `QuickLinkCap.MaxTotal`, which the model's CHECK interpolates; the literal `3` in the migration, which records what ran; and the web's `QUICK_LINK_MAX`, pinned to the first by `QuickLinkCapWireContractTests`.
- **A marked row is no longer cleaned up by eviction.** Any future reset of recent searches or change to the FilterHash format must say what it does to quick links (ADR 0060, Amendment 2026-11-20).
- **The policy carries an exception to its own 20-search rule,** and two texts may sit under one version for a day (D9).
- **Two exceptions in DESIGN.md:** the one band that carries links, and a fourth direct removal (D8).
- **`Down` of the migration is destructive** by this repository's convention: it drops the column, and every mark with it.

## What comes back

Nothing detects these automatically. Each is read when the event is proposed or reported.

- **A change to the cap.** The web mirrors it as `QUICK_LINK_MAX`, pinned by `QuickLinkCapWireContractTests`; the CHECK needs a migration of its own.
- **A second consumer of the count route, or a field beyond Data/IT.** The bucket's recompute trigger (the `RateLimitingOptions` docblock); `security-auditor` re-derives it.
- **Any reset of recent searches, or a change to the FilterHash format.** It now deletes or orphans quick links; see the ADR 0060 amendment.
- **Quick links on a surface other than `/jobb`.** DESIGN.md's band clause and `band-lines.test.ts` turn red first.
- **A change to the policy text.** `security-auditor` reads it.

## Implementation

One PR on `feat/jobb-quick-links`, lane BE+FE, #2265. It adds one migration and no package. **Hotspots touched** (CLAUDE.md §6.5; the borrow and its return are the driving session's): the EF migration, `messages/{sv,en}` (`content-legal.json`, `jobads.json`, `pages.json`; `hotspot:i18n`), and `app/globals.css` (`hotspot:css`).

- **Domain:** `QuickLinkCap`; `Preferences.JuniorQuickLinkOffAt` and `JobSeeker.SetJuniorQuickLink`; `RecentJobSearch.QuickLinkSlot`, `MarkAsQuickLink` and `UnmarkQuickLink`, which raise `RecentJobSearchQuickLinkMarkedDomainEvent` and `RecentJobSearchQuickLinkUnmarkedDomainEvent` on actual transitions only (architect N3).
- **Application:** `SetJuniorQuickLinkCommand`, `MarkRecentSearchQuickLinkCommand` and `UnmarkRecentSearchQuickLinkCommand` with their handlers; `GetJuniorItJobAdCountQuery`; `JuniorQuickLinkEnabled` on the profile DTO and `IsQuickLink` on `RecentJobSearchDto`.
- **Infrastructure:** `RecentJobSearchConfiguration` (the CHECK and the index), `RecentJobSearchCapturer`, and the migration `20261120182624_AddRecentJobSearchQuickLinkSlot`, under `SET LOCAL lock_timeout = '3s'`: `ADD COLUMN` without a default changes the catalog only, but it takes ACCESS EXCLUSIVE and every committed search writes this table. No registry needs a row: a smallint is not text-bearing, `job_seekers.preferences` is classified and `RecentJobSearch` is in the cascade map.
- **Api:** the three routes of D4 and D6, `JobAdJuniorItCountPolicy` and `RateLimitingOptions.JobAdJuniorItCount`.
- **Web:** the hero's `jobb-quick-links` component and the fifth read in `jobb/page.tsx`; `loading.tsx`; `lib/api/junior-it-count.ts`, `lib/api/quick-links.ts` and `lib/actions/quick-links.ts`; the section `/mina-sidor/snabblankar` and its entry in the navigation; the mark control in Senaste sökningar; the DTO mirrors; `messages/{sv,en}`.
- **Tests, in the PR:** `QuickLinkCapTests` and `JobSeekerJuniorQuickLinkTests`; `RecentSearchQuickLinkStoreBoundTests` (a racing mark that takes the free slot is refused, and the store admits each slot once per seeker); `RecentJobSearchCapturerEvictionTests` (the oldest unmarked row goes and every mark stays, a mark committed between the eviction's read and its delete survives and the capture stores nothing, and an overshoot trims back to the cap); the quick-link endpoint and cross-user tests; the two rate-limit wiring tests; `JuniorItJobAdCountEndpointTests` (401 without a session, the total uncacheable, and a 429 with `Retry-After` and no count) and `JuniorItJobAdCountTests` (the count equals `/junior`'s total over every arm of the listed membership); `JuniorItJobAdCountLoggingSurfaceTests`; `QuickLinkCapWireContractTests`; the pre-migration stamp in `PreferencesConsentBackcompatTests`; `band-lines.test.ts`; `JuniorItCountLatencyMeasurement` (R1); and `tests/job-modal/quick-links.spec.ts`, the hero, `/sokningar` and the settings section in a real browser against the job-modal harness's fixture backend (R2). Their premises come from production paths (AGENTS.md §5, Tests).
- **Then the docs:** DESIGN.md (D8); `content-legal.json`; this ADR and its README row; the ADR 0060 amendment; ADR 0187's pointer to this ADR, its R6 and its narrowed lines, and ADR 0166's two parked lines (D7); the session docs per ADR 0065. Then the panel.

**Outside the diff, owed by the driving session:** the processing register's "Senaste sökningar" row (D9, security-auditor Minor 6).

## Measurements

Each reading below is dated and has its source. Nothing is estimated. R1 is the driving session's of 2026-11-20 and is recorded in `docs/reviews/2026-11-20-2265-latency.md`; it was not re-run for this ADR.

| Reading | What | Recorded |
|---|---|---|
| R1 | the count's latency, against ADR 0045 class (a), p95 300 ms (CTO B3's gate; architect Viktigt 3) | 2026-11-20, `JuniorItCountLatencyMeasurement` (`tests/Jobbliggaren.Api.IntegrationTests/JobAds`), run through the test project's exe with `-showLiveOutput` on a developer machine. 40 000 ads (28 844 listed, 2 526 junior), 290 kommuner plus ads without one, the Data/IT field's 12 groups, and the shipped rules' assessments on the 13 380 in-field ads. The handler over the real port and `JuniorFieldResolver`: **p50 7.1 ms, p95 7.9 ms**. The plan: a HashAggregate (291 groups) over a Hash Join of a bitmap scan on `ix_job_ads_occupation_group_concept_id` and a seq scan of the 3 449 Junior assessments; execution 6.4 ms. The gate passes. Against the house anchor of about 1 s of database time per account per minute: 60 a minute × 7.9 ms ≈ 0.47 s a minute, and the ceiling is ⌊1 000 ÷ 7.9⌋ = 126 a minute. A dated reading on a developer machine and not a live number: re-run the class to regenerate it. The full output is `docs/reviews/2026-11-20-2265-latency.md`. |
| R2 | the band's height with the link row (D8) | 2026-11-20, `tests/job-modal/quick-links.spec.ts` in the job-modal harness (a production webpack build, Chromium), `#main section.jp-hero` with no link, three links and a long label: 1280 px 394.4, 394.4 and 394.4 px; 800 px 482.7, 482.7 and 482.7 px; 390 px 563.9, 717.9 and 739.0 px. The loading skeleton's band at 1280 px is 304 px, a gap in the panel's skeleton that predates this change (#2271). |

## References

- Code, in the tree: `src/Jobbliggaren.Domain/JobSeekers/` (`QuickLinkCap.cs`, `Preferences.cs`, `JobSeeker.cs`) · `src/Jobbliggaren.Domain/RecentJobSearches/` (`RecentJobSearch.cs`, `Events/`) · `src/Jobbliggaren.Application/JobSeekers/Commands/SetJuniorQuickLink/` · `src/Jobbliggaren.Application/RecentJobSearches/Commands/MarkRecentSearchQuickLink/` and `UnmarkRecentSearchQuickLink/` · `src/Jobbliggaren.Application/JobAds/Queries/GetJuniorItJobAdCount/` · `src/Jobbliggaren.Infrastructure/RecentJobSearches/RecentJobSearchCapturer.cs` · `src/Jobbliggaren.Infrastructure/Persistence/Configurations/RecentJobSearchConfiguration.cs` · `src/Jobbliggaren.Infrastructure/Persistence/Migrations/20261120182624_AddRecentJobSearchQuickLinkSlot.cs` · `src/Jobbliggaren.Api/Endpoints/` (`MeEndpoints.cs`, `RecentSearchesEndpoints.cs`, `JobAdsEndpoints.cs`) · `src/Jobbliggaren.Api/RateLimiting/` · `web/jobbliggaren-web/src/components/job-ads/jobb-quick-links.tsx` · `web/jobbliggaren-web/src/app/(app)/mina-sidor/snabblankar/` · `web/jobbliggaren-web/src/lib/api/` (`junior-it-count.ts`, `quick-links.ts`)
- Tests, in the tree: `tests/Jobbliggaren.Domain.UnitTests/JobSeekers/` · `tests/Jobbliggaren.Api.IntegrationTests/RecentSearches/` · `tests/Jobbliggaren.Api.IntegrationTests/Me/` · `tests/Jobbliggaren.Api.IntegrationTests/JobAds/` (`JuniorItCountLatencyMeasurement.cs`, `JuniorItJobAdCount*Tests.cs`) · `tests/Jobbliggaren.Architecture.Tests/` (`QuickLinkCapWireContractTests.cs`, `JuniorItJobAdCountLoggingSurfaceTests.cs`) · `web/jobbliggaren-web/src/app/band-lines.test.ts`
- ADR 0187 · ADR 0186 · ADR 0166 (Amendment 2026-11-20, D10) · ADR 0060 (Beslut 4–6, Amendment 2026-11-20) · ADR 0045 (Beslut 1 and 5) · ADR 0164 · ADR 0168 · ADR 0146 · ADR 0142 D6 · ADR 0039 · ADR 0031 · ADR 0022 · ADR 0154 · ADR 0065 · AGENTS.md §2.1–2.3, §3, §5, §8 · CLAUDE.md §6.5, §9.2, §9.6 · DESIGN.md §6
- `docs/reviews/2026-11-20-2265-plan-senior-cto-advisor.md` · `docs/reviews/2026-11-20-2265-plan-dotnet-architect.md` · `docs/reviews/2026-11-20-2265-plan-security-auditor.md` · `docs/reviews/2026-11-20-2265-latency.md` · `docs/reviews/2026-11-20-2264-plan-security-auditor.md` (her m3) (all local, transcribed by the driving session)
- Vaughn Vernon, *Implementing Domain-Driven Design* (2013), ch. 10 · Eric Evans, *Domain-Driven Design* (2003) (Aggregates, Domain Services) · Robert C. Martin, *Clean Architecture* (2017), ch. 7 and 13 · Michael Nygard, *Release It!* (bulkhead) · RFC 6585 §4 · RFC 9110 §15.1 · WCAG 2.1, 3.3.4 · Npgsql issue #3129 (links may rot)

---

## Amendment 2026-11-21 — Anpassa in the hero, one tick-box list, and the junior link on `/jobb` (#2275)

**Why.** Klas tried release 1682 on 2026-11-21. He found the settings hard to find, and the junior link took him out of the app. Verbatim, in part: "det måste finnas en settings-knapp inne i bannern, där man kan lägga till snabblänkar lätt … super enkelt och bra UI/UX, utan brus" and "snabblänkarna, som inloggad, ska fortfarande vara på den kompletta jobb-sidan, men bara att t ex. juniorlistan då är 'filtret'-chippet". He approved the plan the same day. D1–D9 stand. This amendment changes the web only: no migration, endpoint, action or package.

- **A1 — Anpassa.** The hero's link row ends with a quiet button with a gear. It reads "Anpassa", with the name "Anpassa snabblänkar", or "Välj snabblänkar" when no link shows. It opens the list in the shared popover shell, anchored under the button, or as a sheet at ≤640 px. When the profile read fails there is no button, since the junior box's state is unknown. With no link as well, there is no row. D6's rules for hiding the junior link are unchanged.
- **A2 — One tick-box per possible link.**
  - The junior link comes first, then the searches that are links, then up to six of the others in the list's order. The settings page lists every search.
  - A tick or an untick writes through D4's endpoints, with no Save. At three, the unticked boxes are unavailable and a line says why. A refused write puts the box back and says why in the list. Only a successful write refreshes the page, as in #2272.
  - The rows are laid out once, when the list mounts. A tick re-renders the page, and a row that moved to another group or fell outside the six would take the focus with it.
  - Rejected: #2272's two lists, links and searches to add, inside the popover, where every change moves a row from one list to the other.
- **A3 — The settings page uses the same list,** in a card titled Snabblänkar. Its two lists, and their focus fallback to Mina sidor's h1, are removed.
- **A4 — The junior link opens `/jobb?junior=on`**, the junior filter from #2261, instead of `/junior`, whose public header looks signed out. The user keeps the whole search page: filters, sorting and matching. The count is still `/junior`'s total (D6). `/junior` is unchanged and reads no session (ADR 0186). (See ADR 0186, Amendment 2026-11-21, #2284.)
- **A5 — Every quick link is a `next/link` with `prefetch={false}`.** All of them now stay on `/jobb`. They navigate softly, and a link in view never reads the list in advance. #2272 used plain anchors so that a prefetch would not spend `/junior`'s own budget; with A4 that reason is gone, and so are the anchors.
- **A6 — An anchored panel follows its trigger.** `usePanelPosition`, which every popover shares, measures again after each commit while the panel is open; an unchanged placement keeps its state. At 1280 px, three links wrap the row onto two lines, so a tick moves the button from one line to the other. A panel left where it opened would cover the new line and its own button.
- **Docs in the same PR.**
  - DESIGN.md: the band clause now includes the button.
  - The fourth direct removal names the list behind Anpassa as a place where a link comes back.
  - The removal rule sends focus after the last link to Anpassa, as Matchning's chips do.
  - `content-legal.json`, sv and en: the sentence on where searches are shown names the list under Jobb as well as under Settings. The policy version names the day its text goes live. The file is shared with #1663 PR 3 on Klas's "Dela filen (Rek.)" of 2026-11-21, and the PR that merges last sets the date.
- **R3, the band's height** (2026-11-21, `tests/job-modal/quick-links.spec.ts`, the same harness as R2), with no link, three links and a long label:
  - 1280 px: 394.4, 394.4 and 394.4 px.
  - 800 px: 482.7, 482.7 and 482.7 px.
  - 390 px: 621.9, 717.9 and 739.0 px.

  At 390 px, the band with no link now carries the row with Välj snabblänkar.
- **Not built, on purpose.**
  - Sending a signed-in visitor of `/junior` to `/jobb`, since that page reads no session. (Built by #2284 as a branch of the proxy, not in the page; see ADR 0186, Amendment 2026-11-21, and ADR 0018, Amendment 2026-11-21.)
  - Quick links of any kind other than the junior link and a search.
  - Reordering.
