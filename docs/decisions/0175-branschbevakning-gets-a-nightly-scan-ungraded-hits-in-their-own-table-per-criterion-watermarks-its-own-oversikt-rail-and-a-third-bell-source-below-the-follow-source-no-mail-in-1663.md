# ADR 0175 — Branschbevakning gets a nightly scan: ungraded hits in their own table, per-criterion watermarks, its own Översikt rail and a third bell source below the follow source; no mail in #1663

**Date:** 2026-10-18
**Status:** Accepted 2026-10-18 (the form decided by `senior-cto-advisor` under CLAUDE.md §9.2, and Klas's six answers of the same day, quoted under "Klas's decisions, verbatim").
The two privacy-policy texts (D12) are **not** decided by it: each is signed with its PR. The DPIA Part D addendum (§D9; D12) was signed by `security-auditor` and
approved by Klas on 2026-10-18, and its ruling (2) settles the third switch's default (D8). PR 1 of three shipped with it (#2173). PR 2 builds D2–D7 and D10–D12
and changes D7's count to its own route (see D7).
**Deciders:** Klas Olsson (six answers in chat on 2026-10-18) · `senior-cto-advisor`, "the CTO" below (decision-maker under §9.2; ruling on forks F1–F12 and an addendum that
answers an adversarial check of it, report-only; `docs/reviews/2026-10-18-1663-plan-cto.md` and `…-plan-cto-addendum.md`; **where the two differ, the addendum governs**) ·
`security-auditor` (plan read, report-only; `…-plan-security.md`: Blocker-class conditions B1–B9, Major M1–M12, Minor 1–9 and the escalations E1 and E2) · `dotnet-architect`
(form read, report-only; `…-plan-architect.md`) · the adversarial check (`…-plan-check.md`: twelve gaps hold and one holds in part; each is answered in the addendum). Klas's
standing directions are applied here, not re-decided: the notification doctrine of 2026-09-05 (epic #1662: all three sources notify), his answer of 2026-10-12 (no "Visa alla" and
nothing more in the bell until real users have given feedback) and his answer of 2026-10-17 that #2158 goes before #1663 (ADR 0173).
**Amends:** ADR 0117 D1 (superseded in part) and ADR 0105 (RF-9 superseded, RF-7 extended to criteria). ADR 0162 (D1, D11, s10), ADR 0168 (D1, D5, D9 and its lapse clause), ADR 0169
(D1', V20, lapse 1 and D4'), ADR 0173 (lapse 1, 4 and 9) and ADR 0125 (the restore-exposure grant, E2). In PR 2 also ADR 0018 (the `after()` residual list), ADR 0139 (the scan
is a reader of the members; grant 4 untouched, D7) and ADR 0140 (Beslut 5's card gains a per-criterion pill). Each stays as the record and carries one header line that points here;
the first seven are added with this ADR, the last three with PR 2's code. See "Amendments to other ADRs".
**Related:** [#1663](https://github.com/klasolsson81/jobbliggaren/issues/1663) (this decision ships under it; it closes after PR 3) · #1662 (the notification epic) · #1664 and #1665
(the other notification senders; D13) · #2138 (the open GDPR Blocker) · #2115 (the policy does not describe following companies) · #2127 (a frozen watermark) · #2163 (the mail
duplicate) · #1576 (the follow window rule) · ADR 0009 · 0018 · 0045 · 0080 · 0087 · 0113 · 0120 · 0139 · 0144 · 0146 · 0149 · 0154 · 0161 · 0164 · DPIA 560 Part D and Part E
(`docs/research/`, local) · the processing register (`docs/runbooks/gdpr-processing-register.md`, local)
**Measured against:** `origin/main` at `8ce73fa36`, read on 2026-10-18 by the form-round agents, and the worktree `C:/tmp/jbl-smart-watch-scan` (branch `feat/smart-watch-scan`)
for this ADR. Nothing here is merged. The only figure from the box is the count in D12, taken from an earlier session's log.

> **Provenance.** Written by `adr-keeper` for the driving session from the approved plan and the five form-round reports (CLAUDE.md §9.2, §13); it adds no decision of its own. The
> reports are local files, transcribed verbatim by the driving session. The `file:line` citations into `CompanyWatchBrowseQuery.cs`, `BackgroundMatchingJob.cs`,
> `CompanyWatchScanJob.cs`, `CompanyWatchScanWorker.cs`, `NotificationFeedSourceKind.cs`, `MatchFeedSource.cs`, `FollowedCompanyAdFeedSource.cs`, `RecurringJobRegistrar.cs`,
> `CompanyWatchCriterionMemberStore.cs`, `CompanyWatchCriterion.cs`, `AccountHardDeleter.cs`, `NewFollowedCompanyAdSet.cs`, `BackgroundMatchingWorker.cs`,
> `OrganizationNumberSurfacingGuardTests.cs`, `RateLimitingOptions.cs` and `pages.json` were re-read against `8ce73fa36` for this ADR; every other citation is the reports' and was not
> re-read. Costs are derived from the schema and the statements, not measured, unless a figure is dated. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a
> README row (precedent 0167–0174).

---

## Context

Issue #1663 asks that a new ad from a company that matches a user's Branschbevakning (`CompanyWatchCriterion`; the UI name is Branschbevakning) becomes a hit, shows in the bell and on
Översikt, and reaches the mail. Klas's prompt of 2026-10-18 defines a hit as a new ad from a company that matches the criterion. Eight facts shape the decision.

- **Nothing notifies from a criterion today.** Outside the Api's own create, update, delete, list and browse paths, the readers of `company_watch_criteria` are the membership
  materialiser (which maintains `company_watch_criterion_members`), the Art. 17 hard delete and the recruiter-erasure count (grep of `src/` at `8ce73fa36`). There is no scan, no
  hit table, no bell source and no mail.
- **The shipped copy promises the opposite.** ADR 0117 D1 (`0117:32-35`, written under the feature's earlier name) says the criteria surface sends "inga egna notiser", and
  `messages/sv/pages.json:367` ("…utan egna notiser.") and `en/pages.json:367` ("…with no alerts of their own.") say it to every user. ADR 0105 RF-9 (9C, Klas 2026-07-12) froze
  criterion notification: not v1, PR-4 frozen, "egen DPIA när den byggs". Klas's directive of 2026-09-05 (#1662: all three sources notify) is the unfreeze.
- **The purpose changes.** The register describes the deferred purpose as notifying about new matching *companies* (`gdpr-processing-register.md:1866`), designed in DPIA 560 Part D
  over the register's own `created_at`. #1663 notifies about new *ads*. That purpose never ran and appears in no public text, so this is a change to an Art. 30-documented purpose
  (D12).
- **The members are not Application's.** `company_watch_criterion_members` is Infrastructure-internal (ADR 0139), and the two existing ad-set readers are Api-only and refuse above
  2 000 ads (D2).
- **The follow scan reads without an upper bound**, so which of two scans bears a hit for the same ad first decides whether a notification the user has read rings again (D3; ADR
  0169 s1; ADR 0173 lapse 4).
- **Two bell sources exist and a rule for a third is already written.** ADR 0173 D7 states that a match outranks a followed company's ad, and leaves a third source's place and
  whether its rows join the per-ad mark to its own ADR, with `security-auditor`'s review (ADR 0162 D1).
- **A bell kind an open tab does not know fails the whole read** (D9).
- **Klas decided on 2026-10-12 that "Visa alla" is not built and not asked about again** before real users have given feedback, which ADR 0162 D11 and ADR 0168 D9 had tied to
  this issue.

## Decision

**Branschbevakning gets a nightly scan in the Worker. Each (user, criterion) is read in one statement through a new read-only port that returns ad ids, and every new Active ad of
a member company becomes a hit in a table of its own, ungraded, under Art. 6(1)(b). Four columns on the criterion carry what the scan has read, what its last outcome was, when
the criterion last changed and what the user has seen. Översikt shows a "N nya" pill per criterion and links to a page of its own; in PR 3 the bell gets a third source below the
follow source. #1663 sends no mail. It ships as three PRs, one session each.**

### D1 — Three PRs, one session each, and no mail (F1; Klas, answers 1 and 2)

- **PR 1: expand only, no visible change, no migration.** This ADR; the #1576 acknowledgement window lifted into a pure helper; the grade helper moved to a neutral home; the three
  NotificationFeed test seeders extracted; the web learns the third bell kind (D9).
- **PR 2: the producer, Översikt and the destination, with every transparency text.** Migration (Klas's GO), aggregate, port, scan job, the `newAds` count, `/nya`, copy, policy,
  register, registries and retention.
- **PR 3: the bell source.** The third switch with its own zero-DDL migration and its own GO, the `{source}` segment, the buckets, the statement pins, EXPLAIN (ANALYZE) and the bell
  sentence in the policy.
- **Why this split.** Policy and copy must land with the processing they describe (ADR 0164 D8; a merge is a deploy, ADR 0154), and a count must never ship before its destination
  (#1576, ADR 0120), so the producer, the count and the destination are one PR. The web kind goes first because tabs that are already open parse the feed with a closed enum (D9). Small changes
  are reviewed better (Winters et al. 2020, ch. 9) and the review cycle is capped (CLAUDE.md §9.6). #1663 closes after PR 3.
- **This departs from the prompt** in two places, and Klas answered both: the prompt's end state is delivery and closing the issue in one session, and it asks for a mail pass (D13).

### D2 — The scan: one read per (user, criterion) through a new Worker-facing port, ad ids only (F2)

- **The port.** `ScanNewActiveAdIdsAsync(criterionId, fingerprint, sinceExclusive, untilInclusive)` on its own Application interface, not on the Api-facing
  `ICompanyWatchBrowseQuery`, implemented by `CompanyWatchBrowseQuery`. It is built from `MaterialisedAdsFromWhere` and `MaterialisedGate` (`CompanyWatchBrowseQuery.cs:273-280`,
  `:328-330`), so one predicate authority decides it, and it is driven from the materialisation row the way `MaterialisedAdIdSetSql` is (`:390-410`): the gate, the members and
  `job_ads` are read in **one** statement.
- **A closed result:** `Materialised(ids)`, `TooBroad` or `NotMaterialised`. The gate's C#-arm rule (`:303-316`) applies: an empty set read under a failed gate as "no new ads"
  would advance the watermark and swallow the window for good (ADR 0120).
- **Ad ids only.** No org.nr leaves the port (ADR 0139's firewall). The hit's `UserId` comes from the loaded criterion row, never from a parameter:
  `CompanyWatchCriterionAdHit.Create(criterion, jobAdId, clock)` takes the criterion itself, so no caller can pass an owner (D5).
- **No shared materialisation lock.** `ReplaceAsync` is one transaction, so a single statement sees the old set or the new one. A lock would serialise the scan behind the
  every-minute sweep. The two-statement page form (`:412-418`, no gate) is not copied.
- **No `LIMIT` and no size refusal. Why AGENTS.md §5's ban on unpaginated list fetches does not apply:** the read is a Worker batch of ids and never the answer to a request, and it
  is bounded by what it reads: `since` is capped at seven days (D6), a criterion at 2 500 member companies (ADR 0139, amendment 2026-09-08), a user at 20 criteria
  (`CompanyWatchCriterion.MaxPerUser`). A `LIMIT` would be a refusal, and a refusal freezes the watermark.
- **No ort filter.** Kommun on a criterion is the SCB seat code and an ad carries taxonomy ids from another code system. Geography already lives in the membership, and the standing
  set has no ad-location predicate either (`:273-280`), so "nya" stays a subset of "annonser". The follow scan's `AdmitsLocation` (`CompanyWatchScanJob.cs:300-304`) is not ported.
- **Dedup before insert.** Rows that exist are skipped, as in the follow scan, so a re-run is idempotent by construction and not by window arithmetic. D5's unique key is the backstop.
- **An EXPLAIN pin** on the statement, as `CompanyWatchBrowseQueryPlanTests` pins its siblings. No index serves `job_ads.created_at`, so it is a residual filter and cost grows with
  the criterion's ad history: derived, acceptable at this size, and the pin is what keeps it visible.
- **Rejected:** iterating members in Application (org.nr would cross the 0139 firewall); reusing `CriterionMatchingAdSetResolver` or `ListActiveAdIdsAsync` (both Api-only, both
  refuse above 2 000, and "new = not yet a hit" floods on every re-materialisation); one global statement per run (it loses per-user isolation and needs an index on the hot ingest
  table; recorded as a lapse path if the measured cost demands it).

### D3 — The upper bound: the criterion scan never reads past what the follow scan has read (addendum 1, amending F2)

- **Base.** `until = run clock − BackgroundMatchingJob.IngestSettleWindow`: that constant (`BackgroundMatchingJob.cs:33,68,219`), never a second literal. It closes ADR 0169 s1's
  boundary race for this producer (`0169:178-185`).
- **With an active follow** (the follow scan's due-set condition): `until = min(base, the owner's JobSeeker.LastCompanyWatchScanAt)`. The watermark is read with `AsNoTracking` and
  never written; `job_seekers` carries the xmin token (ADR 0146).
- **With an active follow and no follow watermark yet:** the scan holds for that owner and advances nothing.
- **With no active follow:** the follow watermark adds no bound ("no bound" in the addendum), and `until` is the base.
- **Why.** The follow scan reads past its own `now`: `CreatedAt > since` with no upper bound (`CompanyWatchScanJob.cs:75,209,231-232`; `0169:178-181`). So the criterion scan,
  held under that watermark, never covers an ad the follow scan has not covered. A followed company's ad gets its follow hit first, and D8's state-independent `HeldAdIds` keeps it
  out of the criterion source from its first night. That closes "a read criterion notification rings again as Bevakar", the defect ADR 0173 exists to remove (`0173:34-35,54-56`).
  The cron slot in D11 is therefore a latency choice only.
- **Pins (PR 2).** An ad ingested between the follow run and the criterion run gets no criterion hit that night; the next night its follow hit is born first and the criterion row
  is never listed. An owner with no follow is not bound by the follow watermark. An owner with a follow and a null watermark holds.
- **Rejected:** cron order alone (the invariant would rest on two independently configurable values plus Hangfire's timing, and the reversed order would be the common case for every
  daytime ad); a residual only (it accepts nightly the very defect 0173 removes); the follow source reading criterion state (a dependency cycle against 0173 D7's one-way direction,
  `0173:133-136,185-186`; ADP, Martin 2017, ch. 14).
- **Why the follow scan is bounded and the match is not.** The follow scan shares the nightly cadence, so its reversal would be the common path every night. Matching runs every five
  minutes (`RecurringJobRegistrar.cs:81`), and its reversal needs a failure or a consent grant, which is the class 0173 R1 already records (R-b).

### D4 — Nothing grades inclusion; the ground is Art. 6(1)(b) (F3; `security-auditor` S1 and S2; Klas's prompt defines a hit)

- **Every new Active ad of a member company is a hit.** There is no criterion "endast matchade". The ground is **Art. 6(1)(b), parity with 7C** (ADR 0105 RF-7: hit creation and the
  in-app rail are the service the user asked for; e-mail is Art. 6(1)(a)). It covers hit creation, Översikt, the destination and the bell. `security-auditor` ruled it lawful and
  recommended it, and it does not contradict the July bind: every "6(1)(a) first" text of 2026-07-12 means the proactive *e-mail*.
- **Why no scan-time grade.** Without `NotificationConsent.BackgroundMatch` it has no legal basis (B2); RF-3 already rejected it (`0105:111`); it would freeze a mutable grade into a row
  (`DigestDispatchJob.cs:429-436`); and persisting only ≥Good hits is a store that copies match outcomes, which is a Blocker while #2138 is open (`0168:199-201`). A grade written as
  a second SQL predicate breaks the #1656(b) bind.
- **The "matches you" flag per row on `/nya` is computed at read time in the Api** (`security-auditor` P1): through `BuildFullForSortAsync` and then `FilterToMatchingAsync`, over
  the page window, never persisted or logged. PR 1 moves the helper (`NewFollowedCompanyAdSet.ResolveMatchingAsync` and `GradeResolution`, `:51,247-262` at `8ce73fa36`) to a
  neutral home. `security-auditor` confirms the flag's read in PR 2; the follow sibling is a precedent and not a ruling on this surface. Any later grade (mail, #1664 and #1665) goes
  only through `FilterToMatchingAsync`.
- **E3 is not triggered** (the scan does not grade), and Klas's undecided "prompt 2" (background matching without consent) is not assumed (lapse 4).

### D5 — The hit: a sibling aggregate with its own table (F4)

`CompanyWatchCriterionAdHit : AggregateRoot<…>` with:

- `UserId`; `JobAdId` by identity, no FK; `CompanyWatchCriterionId` with an **EF-modelled FK, no navigation, `ON DELETE CASCADE` to `company_watch_criteria`** (precedent
  `CompanyWatchCriterionMemberConfiguration.cs:78-82`); and `CreatedAt`, **read from the clock per hit and never the run's `now`**. A run-wide timestamp over more than 100 hits makes
  the window's whole-window tie a live, never-progressing state (`ListNewFollowedCompanyAdsQueryHandler.cs:99-102,120-125` at `8ce73fa36`).
- The bell's two flags, `read_in_notification_feed` and `dismissed_from_notification_feed`, `NOT NULL DEFAULT false`, with the CHECK `NOT dismissed OR read`, **all in the
  `CreateTable`** (`0169:50-54`).
- **Three indexes**, the follow table's count: `UNIQUE (company_watch_criterion_id, job_ad_id)`, which serves dedup and the cascade lookup and lets the database enforce the real key
  without relying on the copied owner id; `ix (user_id, job_ad_id)`, which serves the per-ad mark every click and X runs (its follow counterpart is
  `ux_…_user_jobad_watch`, `FollowedCompanyAdHitConfiguration.cs:100-102`); and `ix (user_id, created_at DESC)`. `db-migration-writer` confirms them against the plans of the scan, the
  count, the bell and the mark.
- **Names, as PR 2's migration `AddCompanyWatchCriterionAdHits` fixes them:** the table `company_watch_criterion_ad_hits`; the FK
  `fk_company_watch_criterion_ad_hits_company_watch_criterion_id`; `ux_company_watch_criterion_ad_hits_criterion_jobad`, `ix_company_watch_criterion_ad_hits_user_jobad` and
  `ix_company_watch_criterion_ad_hits_user_created_at` (DESC); the CHECK `ck_company_watch_criterion_ad_hits_dismissed_implies_read`.
- **The mixed-version window, accepted with Klas's migration GO.** Between `migrate` and the API's recreate the old API serves against the new schema (ADR 0149). Its criterion
  INSERT names no `criteria_changed_at`, which is NOT NULL with no default, so a create in that window fails with 23502. It is bounded by one apply, only the create path
  fails, it fails loud and nothing is persisted; expand/contract would cost a second serial migration through the most dangerous hotspot.
- **Left out:** no grade, no mail columns, no `SeenAt`, no domain events. #1664 and #1665 own the mail state, and a later `NotificationStatus` column must give existing rows a
  non-mailable status (M10; D13).
- **Rejected:** a nullable criterion id on `followed_company_ad_hits` (the follow digest's pending set has no watch join, `DigestDispatchJob.cs:322-328`, so it would drain criterion
  hits; the follow source's marks key on user and ad with no watch join, so "Markera alla" and the follow switch's backlog mark would write criterion rows; NULL rows are distinct and
  break the unique key; `CompanyWatchId` is required by the aggregate's invariant). A by-identity criterion id with no FK (rows orphan when a criterion is hard-deleted,
  `CompanyWatchCriterion.cs:62-73`, an Art. 5(1)(e) residue; and with a DB-only FK, `AccountHardDeleter`'s tracked `RemoveRange` of criteria and hits, `:249-250`, can order the
  principal first, so the database cascade deletes the hits, EF's own DELETE affects zero rows and `DbUpdateConcurrencyException` fails the Art. 17 job).

### D6 — Four watermark columns per criterion, a single definition of "new", and no backfill (F5, F6; addendum 8b)

| Column | Written by | Meaning |
|---|---|---|
| `ScannedThrough` | the scan only | how far the scan has read |
| `LastScanOutcome` | the scan only | `Scanned`, `TooBroad` or `NotMaterialised` |
| `CriteriaChangedAt` | `UpdateCriteria`, and only when the spec actually changes | the lower bound after an edit |
| `NewAdsSeenThrough` | the destination's seen command: one conditional `UPDATE` that only moves it forward, clamped to now | what the user has acknowledged |

- **`UpdatedAt` is bumped only by `UpdateCriteria` and `Rename`** (`CompanyWatchCriterion.cs:181,195`), never by the scan or the seen command: the materialisation sweep prefilters on
  `updated_at` (`CompanyWatchCriterionMemberStore.cs:128`), so a scan that bumped it would re-materialise every scanned criterion. `Rename` bumps it, so it cannot carry
  `CriteriaChangedAt` either.
- **Outcomes.** `Materialised`: advance `ScannedThrough` and persist `Scanned`. `TooBroad`: advance, and persist the outcome (so narrowing later does not burst). `NotMaterialised`:
  persist the outcome and hold `ScannedThrough`. A criterion whose read throws is held on its own, with no hits and no `RecordScan`, and the owner's other criteria still
  advance. Hits and the watermark go in one `SaveChanges` per user; a lost advance only re-scans, which the unique key makes idempotent.
- **`NewCriterionAdSet` is the one definition** behind the count, the destination and, in PR 3, the bell. Its lower bound is `max(NewAdsSeenThrough, CriteriaChangedAt)` and it takes
  only criteria whose `LastScanOutcome = Scanned`, so none of the three surfaces shows a criterion that is `TooBroad` or `NotMaterialised`. **Marks are not gated by it** (0168 D3: a
  hidden row's flags are kept). The same lower bound is the mechanism that keeps hits from a membership an edit replaced from surfacing as the edited criterion's (`security-auditor`
  Minor 3, Art. 5(1)(d)). `LastSeenFollowedAdsAt` is never shared: it would swallow criterion hits (`ListNewFollowedCompanyAdsQueryHandler.cs:116-125`).
- **First run.** `since = max(ScannedThrough ?? CriteriaChangedAt, CriteriaChangedAt, now − 7 d)`; seven days is `ColdStartDays` parity (`CompanyWatchScanJob.cs:70`). **New and edited
  criteria backfill nothing. Existing criteria get at most seven days on their first scan.** The migration sets `criteria_changed_at = created_at`. Catch-up after an outage is
  capped at seven days too (R-e). A 7-day backfill per new criterion is rejected: criteria overlap, so it would make ADR 0169 s1 (a dismissed ad returns through a new hit) the normal
  case. For the existing criteria s1 cannot fire, because their first window runs before any criterion hit or dismissal can exist (the table is created in the same PR).
- **#2127 cannot occur here by construction:** the watermarks are per criterion, and a criterion that fails is held alone (Outcomes above), so one criterion cannot freeze
  another.

### D7 — Översikt and the destination: an own rail, a pill per criterion, never a sum (F7; addendum 6, 7)

- **An own rail.** Joining `NewFollowedCompanyAdSet` is ruled out: it is keyed on `CompanyWatchId`, returns null when the user follows nothing (`NewFollowedCompanyAdSet.cs:37,43,84-85,
  128-136`), and ADR 0117 D3 forbids nesting.
- **A "N nya" pill per criterion, never a sum** (ADR 0140:78). With one criterion it goes in the card head's `aside`, where the follow card's pill sits, and never beside the
  *matching* number; with two or more, on each row's name line, because the head already holds the count of criteria. Its accessible name names its own axis, and the exact copy is
  `design-reviewer`'s. The shipped cards already put a graded number beside an ungraded one (`companies-card.tsx:23-27,61-87`, `criteria-card.tsx:114-134`); the pill's placement and
  name are what keep the two readable.
- **It is shown only when `LastScanOutcome = Scanned` and N > 0**, never for `TooBroad`, `NotMaterialised` or an unscanned criterion (ADR 0120).
- **The count is its own route — a decision change in PR 2** (CTO D-1; Klas 2026-10-18, "Egen räknefråga (Rek.)"). `GET /me/company-watch-criteria/new-ad-counts`
  returns `(criterionId, newAds)` per criterion from one grouped statement over `NewCriterionAdSet`; `newAds` is `null` unless the last outcome is `Scanned`, and a real 0 is
  sent as 0. Översikt fetches it in its fan-out beside the follow count, and a failed read renders the card with no pills, never a 0. The list endpoint, its handler, its bucket
  and Klas grant 4 are untouched, so grant 4's protocol is not re-run; the list bucket's half 1 names the Översikt ⇄ `/nya` round trip and `security-auditor` re-signs it.
  **Why it changed.** This decision first put `newAds` on the list DTO and gated it on grant 4's protocol against 381,5 ms. The only valid comparison is 381,5 × Σ_new/Σ_old in
  one process (`dotnet-architect`, `security-auditor` S-5), and under it any fourth term exceeds 381,5, so the gate could only STOPP. The catalogue page, which also calls the list,
  would pay for a number it never shows, and a cheap count does not belong behind a bucket priced on the list's gate statements. The count and the page are measured instead
  against ADR 0045 class (a), 300 ms p95 (`docs/reviews/2026-10-18-1663-pr2-latency.md`), and each has its own
  per-user bucket derived from that run (`RateLimitingOptions.CriterionNewAdCounts`, `CriterionNewAds`, `CriterionNewAdsSeen`).
- **The destination** `/foretag/branschbevakningar/[id]/nya`. Its window is cut oldest first by the PR 1 window helper (the #1576 rule lifted out of
  `ListNewFollowedCompanyAdsQueryHandler.cs:57-125` at `8ce73fa36`), and its rows are shown newest first, as on the follow page. A `TooBroad` or not-scanned criterion gets a
  closed state, never "nothing new". It carries the C-D2 attribution through the existing key `pages.foretag.criteria.ads.source` (CTO D-15). Its page GET and its seen POST
  each get a derived rate-limit policy, and both are owner-scoped with a 404 for a foreign id (B7; `CriterionOwnerScopedLoader.cs:50-88`). Rows carry the per-row "matches
  you" flag of D4.
- **The seen write happens at render through `after()`** (parity with `foretag/bevakade/nya/page.tsx:65-71`), which needs an amendment of ADR 0018's residual list: "any new write at
  render … reopens this amendment" (`0018:283-284`; M11). The amendment lands with PR 2's code.
- **`design-reviewer` reviews the card and `/nya` in PR 2,** including the zero, `TooBroad` and `NotMaterialised` states (AGENTS.md §8.4).
- **Rejected:** one number for all criteria (it needs an amendment of 0140 and a union destination, and breaks per-criterion honesty).

### D8 — The bell source, PR 3: precedence match > follow > criterion (F8; addendum 11, 12, 13)

- **Order.** The enum order is the merge tie-break, `All` and `Markers` follow the same order (`NotificationFeedSourceKind.cs:5-13`; `NotificationFeedSources.cs`), and the new kind is
  appended last, so all three equal the precedence. **The criterion rows join the per-ad mark** (the default: the match row is reached only through its own marker, which carries the
  consent `EXISTS`). This is the place and the mark membership that ADR 0173 D7 leaves to this ADR.
- **Held by follow is the SQL superset:** any live hit under an active watch while `NotificationFeedGate.FollowedAds` holds, whatever the row's state. It is a new static
  `FollowedCompanyAdFeedSource.HeldAdIds` built on `NewHits(db, userId, null)`, the pattern of `MatchFeedSource.HeldAdIds` (`MatchFeedSource.cs:119-125`). The follow source's own
  behaviour is untouched.
- **What the criterion source does.** Its first statement projects both higher gates (the `FollowedCompanyAdFeedSource.cs:83-91` pattern). **Each exclusion is its own `NOT EXISTS`,
  never `Concat` plus `Any`, with its gate inside every statement,** so with a gate off no later statement names that table, and with the match gate off no statement names
  `user_job_ad_matches` (B5). B5's per-statement gate test is extended to the new source. **EXPLAIN (ANALYZE) is measured** on Testcontainers and dated in `docs/reviews/`; that answers
  0173 lapse 9 for this source. The count is an exact SQL `COUNT(DISTINCT)` because nothing is graded, so no `MaxCountedHits` (0162 D7's common path).
- **The wire.** A closed `{source}` segment with a 400 otherwise; the DTO carries no criterion id, label, SNI code, kommun code or org.nr (`0162:221-222`); the link stays
  `/jobb/{id}`. The read bucket (12/60; "Re-derive when a source is added (#1663)", `RateLimitingOptions.cs:269`) and the mark bucket (60/60; "Markera alla" becomes three POSTs) are
  re-derived and signed by `security-auditor` (M8). Statement pins 9 and 11 are re-measured with both kinds of held ad present.
- **The third switch.** A `NotificationFeedGate` static with `consent: null` and an off-stamp `Preferences` key (ADR 0168 D5: the backlog mark on switch-on, the audited
  replay-marked PUT). It is **on from the start**: `security-auditor`'s ruling (2) in the DPIA Part D addendum §D9 (2026-10-18) reads C-D6
  ("notis = separat opt-in default OFF", `dpia-560:120`) as binding the proactive e-mail, not the in-app bell. Its migration is a zero-DDL snapshot migration with its own Klas GO.
- **No bell flag has a system writer.** The scan and the retention purge (D10) write none (B6); the only writer of either flag is the user's click (0169 V20: a system writer is
  Blocker class).
- **"Visa alla" is not built.** Klas's answer of 2026-10-12 is the standing basis and it answers the trigger "#1663 lands a third source" in ADR 0162 D11 (`0162:503-506`) and ADR
  0168 D9 (`0168:197`). ADR 0169's lapse 6 stays a future event. E3 is not asked.
- **Decided, not residual: the launch backlog.** At PR 3's launch the bell shows the criterion hits the user has not yet acknowledged, the same set Översikt's pill already counts.
  ADR 0168 D5's backlog mark exists to honour a switch-off period and no user has switched off at launch; a launch read-mark would be a system writer of the read flag (named in
  0168's lapse); a read-time "on since" floor was rejected (`0168:223`); the panel caps at 20 rows and the not-in-list line counts the rest (`0168:244`).
- **Decided, not residual: an ad the follow source grades out is in the bell through neither source.** A followed company with "endast matchade" whose ad does not match the user
  keeps the ad off every follow surface, and the superset holds it from the criterion source. It stays on Översikt and the destination (Klas was told, information item 9).
- **Rejected:** criterion above follow (it changes the four statements of the follow source that already ships, and a specific "Bevakar" reason would lose to the broad criterion
  reason for exactly the overlapping companies); routing held sets through `NotificationFeedReadContext` (YAGNI with one consumer; this ADR records the order once).

### D9 — Wire names, fixed here so the web can ship first (F8; addendum 2)

- **`NotificationFeedSourceKind` member `CompanyWatchCriterionAd`**, appended last (the enum order is the tie-break and the precedence of D8). **Route segment
  `company-watch-criterion-ads`** (beside `matches` and `followed-company-ads` in `MeNotificationFeedEndpoints.cs`).
- **The web learns both in PR 1** (ParallelChange, Fowler): the closed `z.enum(["Match","FollowedCompanyAd"])` in `lib/dto/notification-feed.ts:14`, `SOURCE_SEGMENT`, the
  "Markera alla" tokens, and the bell's chip, reason and mark-all as an **exhaustive switch with no "Bevakar" fallback** (`security-auditor` Minor 1: a fallback would render a
  criterion row as a followed company and break ADR 0117's verb-lock, "bevaka" being reserved for following one company). The copy says Branschbevakning; `design-reviewer` grades it.
- **The reason is open tabs, not the build.** ADR 0149 already prevents a mixed web and api release, and the CTO withdrew "the web build can fail independently". An open tab parses
  the feed with the closed enum, and an unknown kind fails the whole read (`use-notification-feed.ts:175-179`), so the bell goes dark until reload. Expanding before the API sends the
  kind is what keeps that from happening. Nothing in the API sends the new kind before PR 3.

### D10 — Retention, erasure and registries (F11; B8, M4, M5, M7)

- **Retention.** The scan's per-user unit purges first, before the follow-bound hold and every read, the user's hits whose ad is no longer Active: one set-based
  `ExecuteDelete` that commits on its own, whatever the outcome or hold (M-D9). It writes no flag. Ad lifecycle is
  monotone (`DigestDispatchJob.cs:434`; an ad only moves to Archived or Erased) and every reader already gates on Active (`NewFollowedCompanyAdSet.cs:134-135` parity), so the purge
  removes nothing a surface shows. E8(5)'s deferral does not carry over: its ground was the volume of single companies. The per-user bound is in D12.
- **Account deletion.** A `CascadeMap` entry, a `RemoveRange` in `HardDeleteAccountAsync`, a line in the hand-listed negative test (`AccountHardDeleteCascadeFitnessTests`), and the
  `HardDeleteAccountsJob` integration oracle (B8).
- **Registries.** `MappedPlaintextExposureRegistry.PersonGrainedTables` (needs E2's grant, D12); an `ErasureCascadeRegistryTests` ground in `NonRecruiterTables` (an opaque ad id,
  purged when the ad leaves Active); a `JobAdLifecycleReadRegistry` entry for every new `JobAds` read, and `KnownNonReaches` for the raw-SQL scan, which also closes the existing
  gap for `CompanyWatchBrowseQuery` in-block because PR 2 touches that class (ADR 0113). The release checklist's "both notification tables" (`release-checklist.md:1579-1581`) names
  the third table (Minor 4).
- **A concurrent criterion delete** mid-scan fails that user's unit on the FK violation; the unit is isolated and the next night is clean.
- **Statistics.** The job ANALYZEs `company_watch_criterion_ad_hits` once per completed run, through its own port `ICompanyWatchCriterionAdHitStatistics` (AGENTS.md §3.6:
  the table is written by one periodic job and its columns reach `WHERE` and `ORDER BY`, and that autovacuum re-arms between runs cannot be shown before the table exists on
  the box).

### D11 — The job: no retry, no overlap, a cron in options, a distinct due set (F12; addendum 8a)

- **`[AutomaticRetry(Attempts = 0)]`, stated explicitly** (precedent `BackgroundMatchingWorker.cs:29`, `CompanyWatchCriterionMaterialisationWorker.cs:153`), and
  **`[DisableConcurrentExecution]`**. The follow wrapper carries no retry attribute and runs on Hangfire's default (`CompanyWatchScanWorker.cs:17-20`), so a parity claim is not made.
  The scan is level-triggered by `ScannedThrough`, so the next night repairs a failed run, and a Failed state stays visible.
- **The cron is an option with a default, 03:55 UTC or later**, after the follow lock's 1 800 s (`CompanyWatchScanWorker.cs:19`; the follow scan runs 03:25) and before the 05:30
  materialisation. A required key triggers CLAUDE.md §11's dev-boot contract. The cadence is configurable so #1664 does not need a second scan.
- **The due set** is DISTINCT `UserId` over criteria with the `JobSeeker` existence checked through its query filter, so an account under deletion produces no hits (Minor 8; parity
  `CompanyWatchScanJob.cs:140-143`). There is no per-user Hangfire fan-out (Minor 9; `0161:246-247`, V7).
- Org.nr, a criterion label or code, and a `JobAdId` together with a `UserId` appear in no log, no bell DTO and no mail (B9; `0161:244-247`, V5 and V6; M-D10's scope).
  The owner's own `/nya` response carries the ads and the criterion's codes by design, and the count carries criterion ids and numbers only.
  The new scan and port files go in `RawOrgNrReadingSourcePaths` (`OrganizationNumberSurfacingGuardTests.cs:58`).
- **Worker wiring.** `AddCompanyWatchCriterionScan` registers the options (`CompanyWatchCriterionScan:CadenceCron`, default `55 3 * * *`), the job and its wrapper; both
  `Program.cs` and `WorkerTestFixture` call it, which a source assertion pins, and a Worker integration test resolves the wrapper through it (`ValidateOnBuild` is false).
  `RecurringJobIds.All` goes from 21 to 22.

### D12 — The privacy position, what must exist before each PR, and the restore-exposure grant (F10; `security-auditor` S1, S4, M1–M3; Klas, answers 4 and 5)

- **Ground.** Art. 6(1)(b) for hit creation and every in-app surface (D4); no re-consent for in-app. Mail has its own Art. 6(1)(a) consent (D13). The enskild firma is safe by
  construction under M6 and B9: the register excludes sole traders twice, and members drop personnummer-shaped org.nr at write and count them
  (`CompanyWatchCriterionMemberFilter.cs:70-106`), so a hit derived by `members ⋈ job_ads` can only point at an ad whose org.nr is not personnummer-shaped. That holds only while the
  scan reads the members alone, the hit has no org.nr column, the port returns ad ids and the logs carry counts (D2, D5, D11).
- **A DPIA Part D addendum, signed by `security-auditor` and approved by Klas before PR 2 is built** (M1). It is a short addendum and not a new DPIA, drafted by the driving
  session. It holds an Art. 6(4) compatibility record for criteria created under "utan egna notiser" (link: the same job-search service, and a "bevakning" implies news; context:
  user-created, on the user's own account; nature: low sensitivity, Art. 9 not applicable; consequence: an in-app signal the user can switch off; safeguards: the bell switch,
  deleting the criterion, no mail without consent), R-D7 and M-D7 (the reklamspärr, the DPIA's own E1-against-E2 posture and not the escalations of this ADR, judged on the concrete surfaces), the reading of C-D6 (D8), and retention and size.
  Klas approves the exact text. **Done 2026-10-18:** signed by `security-auditor` (CONDITIONAL GREEN RATIFIED, 0 Blocker, 0 Major; conditions C-D7 on PR 2, C-D8 on every
  Branschbevakning mail sender, C-D9 on PR 3) and approved by Klas (answer 6), appended as §D9 to `docs/research/2026-07-12-dpia-560-smart-bevakning.md` (local); her review is
  `docs/reviews/2026-10-18-1663-dpia-security.md`.
- **Copy and policy.** B1: `pages.json:367` (sv and en) is replaced in the same PR that first writes or surfaces hits (PR 2; a merge is a deploy). M2: PR 2 adds a policy paragraph
  under Art. 13(1)(c), 13(2)(a) and 13(3) (criterion storage, the scan, in-app notices, retention) and corrects the follow half (Minor 5 and 6). PR 2 is part of #2115, not its close: match-row retention, decided with #2138, and the follow rail's register entry stay
  there (`security-auditor` Major 1). PR 3 adds the bell
  sentence. `security-auditor` signs each text, it rides its PR, and `CurrentPrivacyPolicyVersion` is set to the merge date (`TermsAcceptance.cs:37`). Klas's one GO (answer 5)
  covers both texts. M3: PR 2 rewrites the register entry (`:1866`, `:1869-1871`, `:1886`, `:1923`), lists the new table in Datafält and in the manual Art. 15 extract, and adds the mail
  type to "Utgående transaktionell e-post" purpose 1 (`:569`) when a mail sender is built.
- **The restore-exposure grant (E2; Klas, answer 4).** The accepted restore-exposure list of ADR 0125 (Case 2, the 30-day bound) covers the Branschbevakning hit table
  (`company_watch_criterion_ad_hits` by its sibling's convention; PR 2's migration fixes the name), on the same 30-day bound. A row is a person-grained fact, which ads reached one
  user through their Branschbevakning, and a restored backup can show it.
- **The per-user magnitude, as the E2 text required.** A user's stock is at most the sum, over their 20 criteria at most, of the Active ads of each criterion's 2 500 member companies at
  most. The unique key makes one (criterion, ad) one row, and the purge of D10 bounds the stock to Active ads. **The ceiling is 20 × the Active ads**; the closest count measured is
  42 235 listed ads on the box at 07:09Z on 2026-10-18 (counts only; `docs/sessions/2026-10-17-1900-seo-s4-job-sitemap-2167.md`); **PR 2 measures the realistic figure** and
  records it in the register entry. Measured 2026-10-18T18:25Z on release `sha256:df4b0248…` (source `c14ac57a1`) with `security-auditor`'s S-2 statement: 42 302 Active ads,
  so the ceiling was 846 040 rows per user; the per-user stock is not stated, because fewer than 5 users contribute; the pre-fixed gate "every user within 50 000" read true.
  Her ruling that this discharges M-D9 is `docs/reviews/2026-10-18-1663-pr2-md9-ruling-security.md`; the row as printed is in the register entry (local).

### D13 — Mail is not in #1663; the conditions every criterion mail sender carries (F1; `security-auditor` S2, S3; Klas, answers 2 and 3)

- **Why no mail here.** A third independent pass in `DigestDispatchJob` contradicts the report shape the CTO decided on 2026-10-11 (the user keeps Daily or Weekly, one cadence for
  the one report; recorded on #1665), and #1665 would tear it down next. It would put up to three mails per period in front of the user, naming the same ad, which breaks "one ad, one
  notification" (ADR 0173 D1) in the mail channel and widens #2163. The consent, the signed copy, the policy text and the register entry would each be written and signed twice. Top
  mail for criterion hits is #1664's (ADR 0161:123-124), the report is #1665's.
- **These conditions bind #1664 and #1665 alike, and any later criterion mail sender,** and a comment on each issue points here (a comment files nothing):
  - **Its own consent (E1).** An own switch for mail about Branschbevakningar, **off from the start,** with an own text that `security-auditor` signs, built in the first of #1664 and
    #1665 that sends such mail. The follow pair is not reusable: its text names only followed companies and its timestamp is stamped once, so it cannot show who said yes to a wider
    text (Art. 7(1)), and one switch for two purposes of different scale is not freely given (Recital 43; Art. 7(2)). She blocks the shared form. The own form is a zero-DDL snapshot
    migration (ADR 0164 D2): `Preferences` keys for the switch (default false), a `…ConsentAt` stamped once and a `…ConsentWithdrawnAt`; a `NotificationConsent.<X>` spec and a
    `NotificationMail.<X>` gate; an audited command and endpoint; a settings switch under the Branschbevakning group; a new signed ADR 0144 row pinned in
    `legally-bound-copy.test.ts`; and no nudge (ADR 0080 B5 parity).
  - **B3.** No criterion mail under the follow consent, and no one switch for both.
  - **B4.** A criterion mail sender never lands without its own consent, policy, register entry and signed copy in the same PR (ADR 0164 D8: not before, not after).
  - **M9.** The mail carries the attribution "Källa: SCB / egen bearbetning" (DPIA M-D2).
  - **M10.** A late-opt-in bound: hits accumulated under Art. 6(1)(b) never mass-mail on opt-in, and a test pins it (R-E6 and M-E6 parity, C-E5).
  - **M12.** A mail pass that grades criterion hits in the Worker under a non-matching consent scopes `content-legal.json:91` first; otherwise it widens a false statement.
  - **S2 P3.** Top mail for criterion hits needs `NotificationConsent.BackgroundMatch` **and** the criterion-mail consent (the policy promises that withdrawing the matching consent
    ends matching against the profile). Grading at dispatch for a report (P2) is lawful only under the criterion-mail consent whose signed text names the matching.

## Alternatives considered

Each fork's reasoning is in the D-section named; the line gives the verdict.

- **Hit storage.** A sibling aggregate with its own table (chosen, D5) · a nullable criterion id on `followed_company_ad_hits` (rejected) · a by-identity criterion id with no FK
  (rejected).
- **The scan.** One statement per (user, criterion) through a Worker-facing port (chosen, D2) · iterating members in Application (rejected) · reusing the Api resolver or a diff against
  existing hits (rejected) · one global statement per run (a lapse path) · a per-user batched statement over an array of criterion ids (not without its own measurement; ADR 0139's
  batched lateral lost, `0139:192-199`).
- **The upper bound.** Held under the follow watermark (chosen, D3) · cron order alone · a residual only · the follow source reading criterion state (all rejected).
- **Grade.** Ungraded hits, grade at read time (chosen, D4) · grade at scan time and persist ≥Good (rejected) · the grade as a second SQL predicate (rejected).
- **Watermarks.** Four per-criterion columns (chosen, D6) · a per-user watermark on `job_seekers`, viable only if it advances when every criterion was answered with `TooBroad`
  counting as answered, the due set includes every user with a criterion and the writer gets an ADR 0146 replay (not chosen) · sharing `LastSeenFollowedAdsAt` (rejected: it swallows
  hits) · a 7-day backfill per new criterion (rejected).
- **Översikt.** A pill per criterion and an own destination (chosen, D7) · one distinct number for all criteria (rejected) · joining `NewFollowedCompanyAdSet` (rejected).
- **Bell precedence.** Match, then follow, then criterion (chosen, D8) · criterion above follow (rejected).
- **Delivery.** Three PRs (chosen, D1) · all of #1663 in one PR (the issue would close sooner, but tabs open at the release lose the bell until reload and the PR is reviewed worse) ·
  the producer without a reader (rejected) · producer and count in separate PRs (rejected).
- **Mail.** Left to #1664 and #1665 (chosen, D13) · a third mail pass inside #1663 (rejected) · the follow consent reused (blocked by `security-auditor`).

## Consequences

### Positive

- A Branschbevakning does what its name promises: a new ad from a member company is a hit, counted on Översikt and, in PR 3, rung in the bell once.
- Hits are ungraded facts under Art. 6(1)(b): no consent infrastructure for the in-app rail, no copy of match outcomes (#2138), and the grade stays a function of the profile at read
  time.
- One definition of "new" (`NewCriterionAdSet`), one predicate authority for membership, and counts that are exact or absent (ADR 0120).
- The ad-id port keeps org.nr behind the ADR 0139 firewall, and the owner is always the criterion row's.
- PR 1 changes nothing a user sees, so the web is ready for the new kind before any API can send it.
- The follow source's behaviour, Översikt's follow count and `/foretag/bevakade/nya` do not change.

### Negative and risks

- **Volume.** A broad Branschbevakning can give many notifications per night in the bell (Klas was told; the type gets its own off-switch under Notiser). The stock per user is
  bounded as in D12 and is large in the worst case.
- **A new person-grained table** enters the restore-exposure list (E2) and the Art. 17 cascade, and it carries three indexes, which cost write amplification on a table that takes bulk
  inserts nightly.
- **A second nightly job** (`RecurringJobIds.All` goes to 22), which depends one way on the follow scan's watermark (D3).
- **The criterion scan depends on `job_seekers.LastCompanyWatchScanAt`** keeping its meaning (lapse 2).
- **Hits from a replaced membership stay in the table** until their ad leaves Active; the lower bound of D6 hides them from every surface.
- **Pre-PR 3 accumulation.** PR 2 writes hits for weeks before the bell can show them; PR 3's launch shows the unacknowledged ones (decided, D8).
- **R-a. The follow scan reaches back on a first follow or a re-follow.** On a first follow it cold-starts seven days back (`CompanyWatchScanJob.cs:209`); on a re-follow it starts
  from a stale watermark, because the due set holds only active followers (`:83-90`). Either way it can bear a hit for an ad already read as a criterion row. The class of 0169 s1
  (`0169:184`).
- **R-b. A match born later.** A consent grant reaches seven days back (`BackgroundMatchingJob.cs:48-50,214`), or the user's matching can be failing. Then an ad already read as a
  criterion row returns as an unread match. ADR 0173 R1, inherited (`0173:187-190`).
- **R-c. A held ad stops being held.** After an unfollow, or with the follow switch off, the criterion row speaks by its own read state. ADR 0173 R2's class (`0173:191-193`).
- **R-d. A scan in flight while the user edits a criterion** can commit hits from the old membership. The window is milliseconds once a night, and it is **not** closed by a
  concurrency token, because that token would put a replay obligation on every edit.
- **R-e. Catch-up is capped at seven days** after an outage; hits beyond that are lost, and `MaxReadAgeHours` (72, `CompanyWatchMaterialisationOptions.cs`) means a longer
  materialisation outage holds every criterion's watermark and bursts later. The follow coupling (D3) adds two holds: an owner who follows a company but has no follow
  watermark yet is held whole, and with an active follow every window ends at the follow watermark, so a stalled follow scan stalls this one too. A criterion whose read fails night after night is
  held alone (D6) and, past seven days, loses that part of its window.
- Each of R-a to R-c is pinned by a test in PR 2 (the shape of 0173's R1 and R2 pins).

**Decided, not residual:** the launch backlog (D8); an ad the follow source grades out is in the bell through neither source (D8); the pill's placement beside a graded number (D7);
"Visa alla" is not built (D8).

**Lapse triggers.** Single named home: this ADR. Named reader: Klas. This decision is re-examined when:

1. either scan's cadence changes, #1664 included (D3; answers ADR 0173 lapse 4);
2. the follow watermark's meaning changes (`JobSeeker.LastCompanyWatchScanAt`; D3);
3. a follow backfill is built (R-a);
4. Klas's "prompt 2" (background matching without consent) is decided, or #2138 is answered, in either direction: P3 and P4 of `security-auditor`'s S2 are re-reviewed (D4);
5. a review trigger of the DPIA addendum §D9 fires (a hit graded, or filtered by grade, at scan time; hit rows produced under a consent, Klas's undecided "prompt 2" included;
   a hit gaining an org.nr, a grade or a score; a criterion notice leaving the account), which reopens the third switch's default (D8);
6. the measured cost of the per-(user, criterion) statement demands the global one (D2).

The lapse triggers of 0162, 0168, 0169 and 0173 stand except where "Amendments to other ADRs" says one is answered here.

## Amendments to other ADRs

Each target stays as the record and gets one dated header line that points here. Added with this ADR:

- **ADR 0117 D1** ("inga egna notiser"): superseded in part; the verb-lock, the never-nested taxonomy and the scope split stand.
- **ADR 0105:** RF-9 (9C, the criteria-notify freeze) superseded by Klas's directive of 2026-09-05; RF-7 (7C) extended to criterion hits; RF-3 stands.
- **ADR 0125:** the restore-exposure list covers the Branschbevakning hit table (E2).
- **ADR 0162:** D1 (a third source and its place), D11 (the "Visa alla" trigger is answered by Klas's 2026-10-12 answer), s10 (the buckets are re-derived when the source is built).
- **ADR 0168:** D1 ("the two source classes" becomes "the source classes"), D5 (a third switch, on D5's terms: D8), D9 (the "Visa alla" due-date is answered) and the lapse clause that names #1663, which has fired.
- **ADR 0169:** D1' and V20 and lapse 1 ("the two source classes" becomes "the source classes"); D4' is answered, since the criterion source implements `DismissRowAsync`.
- **ADR 0173:** lapse 1 (answered by D8's order), lapse 4 (answered by D3's bound) and lapse 9 (answered by PR 3's measurement).

Added in PR 2 with the code they describe: **ADR 0018** (the `after()` write at render joins the residual list, D7), **ADR 0139** (the scan is a reader of the members and takes no lock;
"nothing to hook" becomes false; grant 4 is untouched, because the list is, D7) and **ADR 0140** (Beslut 5's criteria card gains a per-criterion pill and is still never summed,
and its "inga per-användar-deltan" bullet no longer holds for that card, D7).

## Implementation

Three PRs on `feat/smart-watch-scan` and its successors, one session each; PR 1 is this one. The ADR is promoted with `git add -f` and its README row is in the same change.

- **PR 1 (no migration, no visible change).**
  - This ADR, its README row and the header lines above.
  - The #1576 window lifted from `ListNewFollowedCompanyAdsQueryHandler.cs:57-125` (the `MaxRows + 1` oldest-first cut, the boundary-tie trim and the degenerate null case) into a pure
    helper, proposed as `Application/Common/Notifications/AcknowledgementWindow.cs`; the follow handler moves onto it and its tests stay unchanged and green.
  - The grade helper to a neutral home that the follow and the Branschbevakning sides share (`Application/Matching/Queries/` proposed; `dotnet-architect` confirms the name), with
    behaviour unchanged.
  - The three NotificationFeed test seeders extracted as static helpers that take the factory or the client, with no shared base class; a fourth copy is a reject.
  - The web learns the kind and the segment (D9), behind the `hotspot:i18n` token (held by #2166 at the time of writing).
  - The two spellings of the old name in `CompanyWatchCriterionMaterialiser.cs:433` (an operational log) and `ListCompanyWatchCriteriaQuery.cs:9` (a comment) become Branschbevakning.
  - The DPIA Part D addendum is drafted and goes to `security-auditor` and then to Klas (D12).
- **PR 2 (Klas's migration GO: one new table and four columns).** Aggregate, EF configuration, migration (`db-migration-writer`), the port, the scan job with its wrapper, registrar
  entry and cron option, the registries and retention (D10), the pill, the count route, `/nya`, the seen command, the C-D2 attribution, the `after()` amendment, the latency measurement against ADR 0045,
  the copy correction, the policy paragraph, the register rewrite, and the pointers in 0018, 0139 and 0140. Owns `AppDbContext`, the migration token and the Worker composition root.
  Panel: `code-reviewer`, `dotnet-architect`, `db-migration-writer`, `test-writer`, `security-auditor` and `design-reviewer`.
- **PR 3 (its own zero-DDL migration and its own GO).** The source, the third switch, the `{source}` segment, the buckets, statement pins 9 and 11, EXPLAIN (ANALYZE) and the bell
  sentence in the policy.
- **Owed outside the diffs.** Each policy text's signature in its PR; `security-auditor`'s
  bucket signatures; the comments on #1664 and #1665 that point at D13.

## Klas's decisions, verbatim

Klas answered six questions in the session on 2026-10-18 (AskUserQuestion). The answers are quoted in Swedish as given. The question behind answers 1, 2 and 5 is the CTO's, as drafted
in the addendum's Klas list (`docs/reviews/2026-10-18-1663-plan-cto-addendum.md`, items 1, 2 and 5); the AskUserQuestion record of the session holds the exact wording asked and is not
reproduced here. E1 and E2 are `security-auditor`'s own text, put to Klas unedited and compared with the report by the driving session (IDENTICAL; 1 227 and 903 characters).

**1. The split into PRs** (applied in D1):

> **A:** "Tre PR:er (Rek.)"

**2. Where the mail is built** (applied in D1 and D13):

> **A:** "Mejl i #1664/#1665 (Rek.)"

**3. E1, the consent for mail about Branschbevakningar** (applied in D13). The question is `security-auditor`'s E1, "E1 — Samtycke för mejl om branschbevakningar", in
`docs/reviews/2026-10-18-1663-plan-security.md`:

> **A:** "Eget samtycke (Rek.)"

**4. E2, backups and the hits** (applied in D12). The question is `security-auditor`'s E2, "E2 — Säkerhetskopior och träffarna från branschbevakningar", in
`docs/reviews/2026-10-18-1663-plan-security.md`:

> **A:** "Ja (Rek.)"

**5. The privacy-policy texts** (applied in D12):

> **A:** "Ja, båda texterna (Rek.)"

**6. The DPIA Part D addendum §D9** (applied in D8 and D12). The question was whether Klas, as controller, approves its exact text with `security-auditor`'s signature:

> **A:** "Ja, godkänt (Rek.)"

## References

- Reviews (local): `docs/reviews/2026-10-18-1663-plan-cto.md`, `…-plan-cto-addendum.md`, `…-plan-security.md`, `…-plan-architect.md`, `…-plan-check.md`.
- Code at `8ce73fa36`: `src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyWatchBrowseQuery.cs` · `…/CompanyWatchCriterionMemberStore.cs` ·
  `src/Jobbliggaren.Application/CompanyWatches/Jobs/CompanyWatchScan/CompanyWatchScanJob.cs` · `src/Jobbliggaren.Application/Matching/Jobs/BackgroundMatching/BackgroundMatchingJob.cs` ·
  `src/Jobbliggaren.Application/NotificationFeed/Sources/` · `src/Jobbliggaren.Application/CompanyWatches/Queries/NewFollowedCompanyAdSet.cs` ·
  `src/Jobbliggaren.Domain/CompanyWatches/CompanyWatchCriterion.cs` · `src/Jobbliggaren.Worker/Hosting/RecurringJobRegistrar.cs` ·
  `src/Jobbliggaren.Api/RateLimiting/RateLimitingOptions.cs` · `web/jobbliggaren-web/messages/{sv,en}/pages.json`
- ADR 0009, 0018, 0045, 0080, 0087, 0105, 0113, 0117, 0120, 0125, 0139, 0140, 0144, 0146, 0149, 0154, 0161, 0162, 0164, 0168, 0169, 0173 · AGENTS.md §2.1, §5, §8 ·
  CLAUDE.md §9.2, §9.6, §11, §13
- Martin, *Clean Architecture* (2017), ch. 14 (ADP) · Fowler, ParallelChange · Winters, Manshreck and Wright, *Software Engineering at Google* (2020), ch. 9
