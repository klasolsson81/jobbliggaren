# ADR 0187 — The junior list is read from a verdict stored per ad: a Worker sweep classifies the listed Data/IT ads by strict, measured rules, an admin's decision outranks the rules, and every read composes one membership under `IsListed(now)`

**Date:** 2026-11-20
**Status:** Accepted 2026-11-20 (Klas's answers of the same day, below, and the ruling of `senior-cto-advisor` on the storage). The ADR 0166 amendment of the same date, which carries the legal outcome, is **Proposed** until Klas approves its text and `security-auditor` signs it at her scoped re-check of the final head; the PR does not merge before then.
**Deciders:** Klas Olsson (controller and product owner). On 2026-11-20, by AskUserQuestion, he answered, verbatim: at the plan stage "En PR, allt (Rek.)" and "Appens inläsningskod lokalt (Rek.)"; "Egen PR efter denna (Rek.)" and "Ja, 3 totalt (Rek.)", after which the `/jobb` banner link moved out of this PR to #2265; at the phase-0 STOPP "A: ≥95 % och ≥10 fall (Rek.)"; and to `security-auditor`'s two questions "Bara junior-id:na (Rek.)" and "Ja, utanför repot (Rek.)". The question texts are quoted in the sign-off of the ADR 0166 amendment and are not repeated here. ·
`senior-cto-advisor` (plan read and ruling (A)–(D), report-only, `docs/reviews/2026-11-20-2264-plan-senior-cto-advisor.md`: the storage, the first-deploy gap, one PR) ·
`dotnet-architect` (plan read, report-only, `docs/reviews/2026-11-20-2264-plan-dotnet-architect.md`: Viktigt 1–5 and Nice-to-have 1–7, marked "(architect V1)", "(architect N3)" and so on below) ·
`security-auditor` (plan read, report-only, `docs/reviews/2026-11-20-2264-plan-security-auditor.md`: her M1–M4 and m1–m7, marked "(security-auditor m2)" and so on below)
**Supersedes:** ADR 0186 D1 (read-time membership), D2 (the predicate in Infrastructure), D3 (the curated file) and D4 (the regex pattern and the agreement test), and **decides** its D9 (option B, a review queue in `/admin`, with strict measured rules). ADR 0186 D5–D8 stand: the endpoint, the seven-member row, the bucket and the page.
**Amends:** ADR 0166, by its dated amendment "Amendment 2026-11-20 (#2264, ADR 0187)", which is the single home of the legal outcome (Context, last bullet). Nothing above that amendment is rewritten.
**Related:** [#2264](https://github.com/klasolsson81/jobbliggaren/issues/2264) (this change; the plan: P1, BE+FE, not `mvp`: a campaign page is none of the core features, CLAUDE.md §6.5) ·
[#2261](https://github.com/klasolsson81/jobbliggaren/issues/2261) (the junior filter on `/jobb`, which composes `JuniorMembership` and never copies it, D4) ·
[#2265](https://github.com/klasolsson81/jobbliggaren/issues/2265) (the `/jobb` banner link and its count endpoint, moved out of this PR by Klas) · ADR 0186 (superseded in part) · ADR 0166 · ADR 0071 Decision 1 (no AI/LLM in the product) · ADR 0160 (the publication predicate) ·
ADR 0113 (every `job_ads` read site carries a written lifecycle decision) · ADR 0106 (the tombstone and Art. 17) · ADR 0045 (budgets) · ADR 0154 (every merge is a deploy) · ADR 0065 (docs in the same PR)
**Measured against:** `feat/junior-live-classification` in `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobbliggaren-junior-classification-19b978`, cut from `5e16247dd` (ADR 0186's merge), at HEAD `eee1f3c9b` and its uncommitted tree, read on 2026-11-20.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D8 and the measurements are the brief's. The storage ruling, and its four answers to ADR 0186 D1, are `senior-cto-advisor`'s.
> Facts about the code were read on 2026-11-20 at the HEAD and in the uncommitted tree named above, and the public side's "unchanged" was read as a diff against `5e16247dd`. The numbers under *Measurements* are the driving session's of 2026-11-20, given in the brief; none was re-run for this ADR.
> What no source measures is listed under *What is not measured*. The mandatory agents review the delivered diff in the PR. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0186).
>
> **Numbering.** 0187 was measured free on 2026-11-20, before this file was written: in this worktree, in the main copy, in every other worktree and on `origin/main`.

---

## Context

`/junior`, shipped by ADR 0186 (#2262), lists the open junior IT jobs for anyone. Klas's LinkedIn post links to it. ADR 0186 left one question open, its D9: how new ads beyond the headline rule get labelled. Klas has now chosen option B. Eight facts shape the decision.

- **Where ADR 0186 left it.** Membership is a curated set of 143 Platsbanken ids or a headline pattern. The pattern is precise (69 of 69) and finds about half of the junior ads (51 %, ADR 0186 R1). The curated set ages as its ads close: the plan counts 87 of the reading's 254 ads ending in November and 85 in December. ADR 0186 D9 priced four options. Klas chose B, a review queue in `/admin`, with strict rules that are measured first.
- **What Klas required, 2026-11-20.** The approved plan records the requirements in Swedish; this is their rendering, and the one phrase marked as a quotation is his own words.
  - Every new or changed Data/IT ad is classified by the Worker as junior, not junior or unclear, with the rule that decided it saved.
  - `/junior` shows the junior ones. The unclear ones go to an admin queue, and his decision on an ad stands.
  - A change of the rules re-runs every open IT ad without overwriting his decisions.
  - It is built for more occupation fields, with only Data/IT switched on.
  - The rules are strict: a rule may show an ad automatically only if it is right in at least 95 of 100 cases against his reading. "Hellre oklar än fel."
  - Measure first, show him the result, and only then switch rules on. No deploy without his yes.
  - The limits that govern the whole change: the matching is unchanged; nothing from the reading is shown beyond what the ad already shows; no ad text leaves the app; the measurement prints counts only; the reading's own folder is never changed.
- **A model step is a stop.** ADR 0071 Decision 1 is "no AI/LLM of any kind in the product" (AGENTS.md §5). Options C and D of ADR 0186 D9 therefore stay closed: C is a stop that needs an ADR superseding ADR 0071, and D reads raw ad text, which holds recruiters' names, into an agent's transcript (CLAUDE.md §9.2 (c)).
- **The rules need the text.** A rule over the description, over Arbetsförmedlingen's own experience mark and over a stated years requirement cannot run per request without a regex over every listed ad's description, in a provider's dialect, which is the two-engine and database-locale problem ADR 0186 D4 had to engineer around. A verdict stored in advance takes the regex off the request path.
- **The experience mark was stored nowhere.** `experience_required` is in JobTech's response but was in neither `JobTechHit` nor `raw_payload`. It is on 43 860 of 43 860 snapshot ads and 894 of 894 stream upserts (R2).
- **ADR 0186 D1 rejected a column for four reasons,** and the plan's first form of this change was columns on `job_ads` with a revision counter. The plan reads and the CTO's ruling changed it (D1): a content fingerprint in place of the counter (architect V1), the timeout as a verdict (architect V3), a version that carries a content hash (architect V4), and the verdict and the decision on an aggregate of their own.
- **Phase 0 measured first.** The rules were run against Klas's reading on the app's own ad texts, counts only (R1–R3), before any rule was switched on, and he chose the criterion from the result.
- **The legal outcome is not this ADR's.** The stored verdict and decision, the widened selection, the reading's ids in the public repository and the admin queue fire ADR 0166 D10's triggers 22 and 23 for the junior list. The outcome has one home, the ADR 0166 amendment of 2026-11-20; this ADR points there and restates none of it. The PR may not merge, and so may not go live (ADR 0154), until Klas has approved that amendment's text and `security-auditor` has signed it at her re-check of the final head.

## Decision

**`/junior` still lists the open junior IT jobs for anyone, and what changes is how an ad becomes one. A Worker sweep, `ClassifyJuniorJobAdsJob`, classifies every listed ad in an enabled occupation field with deterministic rules from one versioned file, into Junior, NotJunior or Unclear, and stores the verdict, with the rule that decided it, in `job_ad_junior_assessments`, an aggregate of its own keyed by the ad. An admin's decision, made in the queue at `/admin/juniorjobb`, is stored on the same row and outranks every rule. Membership is `JuniorMembership.Of(assessments, groups)`: one EXISTS on the assessment's primary key, composed under `JobAdPublication.IsListed(now)` in every read, so an ad that closes, is removed, expires or is erased still leaves the rows, the total and the options on the next request. No regex runs on the request path. A rule may show or exclude an ad automatically only if it is right in at least 95 of 100 cases on at least 10 cases against Klas's reading; every other rule, and every candidate no rule decides, goes to the queue.**

**Scope.** One PR (Klas: "En PR, allt (Rek.)"). The `/jobb` banner link and its count endpoint left it for #2265 at his word ("Egen PR efter denna (Rek.)", "Ja, 3 totalt (Rek.)"). The public side of ADR 0186 D5–D8 (the endpoint, the seven-member row and its envelope, the page, its reader, its metadata and the bucket `public-job-ad-junior-it`) is byte-unchanged; only the membership predicate that the query composes changes.

### D1 — Storage: two ingest-written inputs on `job_ads`, and the verdict and the decision on an aggregate of their own (senior-cto-advisor (A); architect V1, V2; security-auditor m1)

`job_ads` gains exactly two nullable columns, both without a default, both written by the ingest funnel (`JobAd.SetSourcePayload`, the one funnel under `Import` and `UpdateFromSource`):

- **`experience_required`** (boolean): Arbetsförmedlingen's own mark. It travels `JobTechHit`, `MapFacets` and `JobAdFacets` as an optional member with **PRESERVE** semantics, like `Remote`: a payload that omits the key keeps the last value. It is a closed-domain boolean, kept on the `Erase()` tombstone like the facets.
- **`junior_input_fingerprint`** (`varchar(64)`): a lower-case-hex SHA-256 over the length-prefixed post-scrub title, post-scrub description, occupation group and mark (`JuniorInputFingerprint.Of`), written **last** in `SetSourcePayload`, after contact redaction and term extraction, so it is about what the row actually holds. `Erase()` sets it to null. It is an absolute content hash and not a counter: a counter loses increments between the stream and the snapshot, which lock on different resources and can overlap, and a timestamp misses a write that commits after the sweep's read (architect V1).

The verdict and the decision are **not** on `JobAd`. They are the new aggregate **`JobAdJuniorAssessment`**, table **`job_ad_junior_assessments`**:

- primary key `job_ad_id`, which is also a foreign key to `job_ads(id)` ON DELETE CASCADE;
- `verdict` (Junior, NotJunior or Unclear, stored by name), `rule_id` (the deciding rule's id), `rule_set_version`, `classified_fingerprint` and `classified_at`;
- `decision` (Junior or NotJunior, by name) and `decided_at`, with the check `(decision IS NULL) = (decided_at IS NULL)`;
- no index beyond the primary key (D8).

It has **two writers with disjoint members.** The Worker's sweep creates it and reclassifies it (`Classify`, `Reclassify`) and never touches the decision. The admin decide command writes the decision (`Decide(decision?, clock)`, where `null` hands the ad back to the rules, and which raises `JobAdJuniorDecidedDomainEvent`) and never touches the verdict. `JuniorClassification` makes the rule id and the version a closed domain by construction in Domain: lower-case kebab of at most 64 characters, and digits, dots, a plus and lower-case hex of at most 32, so neither can carry a span of ad text.

**The fingerprint is stamped, never recomputed.** `classified_fingerprint` is a copy of the input fingerprint the sweep read in the same statement as the text it classified, so the hash keeps one home. Null is a state: the staleness test (D3) is one expression in which EF's null semantics make null equal null, so an ad written before this change is classified once, and again after its first source write gives it a fingerprint. There is no `??=`, because the sweep cannot write `job_ads` at all.

**Why an aggregate and not columns on `JobAd`** (the plan's first form):

- *SRP.* `JobAd` mirrors what the source said; its writers answer to the source and to the lifecycle. The verdict answers to the rule file and the decision to Klas: two new actors on one aggregate.
- *Aggregate boundary.* The plan itself makes the verdict eventually consistent with the text, through a sweep and a fingerprint, so by its own design it lies outside `JobAd`'s transactional boundary, and a reference by identity is the DDD rule (Vernon 2013, ch. 10).
- *Least privilege (Art. 5(1)(c), 25(1)).* A domain method on `JobAd` needs a tracked entity, so the sweep would materialise `RawPayload`, `Contacts` and `OrganizationNumber` for every ad, only to write four values. As an aggregate of its own, the sweep reads an `AsNoTracking` projection of seven fields, and "never `raw_payload`" (security-auditor m4) holds by structure and not by restraint.
- *Structure instead of guard tests.* Architect V2's hazard, a falsely dirty `Contacts` written back onto an archived ad or text written back onto a tombstone, exists only because the sweep would track `JobAd`. The sweep never writes `job_ads`, so the hazard class is gone and V2's two guard tests are no longer owed.
- *Migration risk.* Two CHECKs on `job_ads` would validate by scanning the largest table under ACCESS EXCLUSIVE unless split into NOT VALID and VALIDATE. On a new, empty table they cost nothing, and only the decision pair keeps a CHECK.
- *The one cost* is a correlated EXISTS on a primary key in every membership read (D4), the shape `/jobb` already runs against `SavedJobAds` and `Applications` (`PerUserJobAdSearchQuery.cs:455-488`, as the CTO read it).

**ADR 0186 D1 gave four reasons against a column. Each is answered:**

1. *Overwrite* (`UpdateFromSource` overwrites a column filled at ingestion). The verdict is on another aggregate, which `UpdateFromSource` cannot reach. Ingest writes the two inputs and nothing else.
2. *Migration* (the single-owner hotspot, CLAUDE.md §6.5). Accepted. One migration, `AddJobAdJuniorAssessments`, with one owner, together with the `AppDbContext`/`IAppDbContext` hotspot: two `ADD COLUMN`s without a default, which are catalog-only on PostgreSQL 18, and one empty table. `SET LOCAL lock_timeout = '3s'` turns a queue behind a long transaction on `job_ads` into a loud deploy failure that the next reconcile retries, instead of a stall.
3. *Backfill.* The sweep is the backfill (D3, D6). The migration has no data step, and could not have one, since the rules are C#.
4. *Refill on a rule change.* The stored version carries a content hash of the rule file (D2), so any edit re-classifies every open ad in the field.

**Rejected:**

- **Columns on `JobAd`** for the verdict and the decision, as the plan first had them (the reasons above).
- **Columns on `JobAd` written by `ExecuteUpdate` from a projection.** It removes the materialisation but bypasses the aggregate (AGENTS.md §2.2) and adds a second `ExecuteUpdate` writer of `job_ads`, beside the one `JobAdRawPayloadDerivationGuardTests` admits.
- **Recompute the fingerprint and fill it with `??=`.** It makes the sweep a writer of a column that ingest owns and reopens the lost-update window: an ingest that commits new text and a new fingerprint after the sweep loaded a null row would have its fingerprint overwritten by the hash of the old text, and the stale verdict would look current until the next re-ingest.

### D2 — The classifier and the rules: deterministic, strict, and one file per occupation field (ADR 0071; architect V3, V4, N1; security-auditor m4, m6)

**`JuniorClassifier`** is pure Application code. It is deterministic, reads the post-scrub title and description, the experience mark and the Platsbanken id, and uses no model (ADR 0071 Decision 1, AGENTS.md §5). Patterns run under `RegexOptions.NonBacktracking | IgnoreCase | CultureInvariant` with a one-second match timeout, so a crafted description cannot cause catastrophic backtracking; the loader compiles every pattern under those options and fails loud at host build if one does not compile.

**The order of decision.** The first step that applies decides:

1. *The reading.* An ad whose Platsbanken id is one of the reading's junior ids is Junior, under `reading-2026-11-19`. The id is read only here; the sweep reads listed ads only and `IsListed` admits Platsbanken ads only, so the id is never another source's (architect N1).
2. *No candidate signal.* An ad with no candidate signal (no candidate pattern matches its title or text, and its mark is not "experience not required") is NotJunior, under `no-signal`.
3. *The rules, in file order.* The first rule whose conditions all hold decides. An automatic rule gives its verdict. A rule that is not automatic gives Unclear under its own id, so the queue says why.
4. *Candidate-only.* A candidate that no rule decides is Unclear, under `candidate-only`.

A match that runs out of time is a verdict and not an exception: Unclear under `match-timeout`, so the ad reaches the queue instead of being re-read on every run (architect V3, security-auditor m4). At read time an admin's decision comes before all of this (D4).

**The rules file.** `src/Jobbliggaren.Infrastructure/JobAds/Junior/junior-rules.data-it.v1.json` is an embedded resource, one per occupation field, and it replaces `junior-it-list.v1.json`.

- It has thirteen top-level keys, pinned by a test that fails on a fourteenth: `//` (maintainer documentation, not mapped), `ruleSetVersion`, `occupationFieldConceptId`, `enabled`, `reading` (exactly `ruleId` and `junior`, D7), `patterns` (13 named patterns), `candidatePatterns`, `candidateWhenNoExperienceRequired`, `years`, `rules` (12), `candidateFallbackRuleId`, `noSignalRuleId` and `timeoutRuleId`.
- Every word list, pattern and threshold is versioned data (AGENTS.md §5). No pattern may name an employer, a person or a place, or quote one ad (security-auditor m6).
- The loader fails loud at host build for: a pattern that does not compile under the linear-time engine; a rule id named twice or not in kebab form; an unknown verdict, condition target or pattern name; a rule that decides Unclear (a rule that only queues sets `auto` to false); a years pattern without its capture group; and a reading id that is not a Platsbanken id or is named twice.

**The version stored beside every verdict** is the file's declared `ruleSetVersion`, then `+`, then the first eight hex digits of the SHA-256 of the file's bytes (at most 32 characters). Any edit, a changed pattern or a flipped `auto` included, therefore re-classifies every open ad in the field, whether or not anyone remembered to raise the declared version (architect V4). "A rule change is a new version" is no longer a convention.

**Exactly one field is enabled, Data/IT (`apaJ_2ja_LuF`),** and a test pins that exactly one rule file ships and that it is that one (security-auditor m6). Another field is a new file, switched on one at a time: it is trigger 22 of the ADR 0166 amendment and an Art. 9 re-read for that field. The enabled field's groups come from the taxonomy through `IJuniorFieldCatalog` (`JuniorFieldResolver` over `ITaxonomyReadModel`). A switched-on field that the taxonomy no longer knows, or one without groups, **throws**, because it would empty the list and the queue without a word. The sweep and every read resolve fields through that one catalog, so "Data/IT" means the same groups when a verdict is written and when it is read.

**The criterion and the four automatic rules.** Klas chose "A: ≥95 % och ≥10 fall (Rek.)": a rule is automatic only when its point precision against his reading is at least 95 % on at least 10 cases. On 2026-11-20 four of the twelve rules meet it (R1), and they are the four with `auto: true`:

- `title-junior`: 70 of 70, shows;
- `text-seek-junior`: 42 of 44, shows;
- `title-senior`: 29 of 30, excludes;
- `text-mentor-only`: 34 of 35, excludes.

The other eight (`title-thesis`, `years-3-plus`, `text-junior-and-senior`, `text-new-graduate`, `text-early-career`, `text-no-experience-needed`, `title-student` and `flag-no-experience`) give Unclear under their own ids. A rule made automatic, or an automatic rule loosened, needs a new measurement against this criterion, recorded with its date (trigger 22 of the amendment).

### D3 — The sweep `ClassifyJuniorJobAdsJob`: level-triggered, bounded, and the only writer of a verdict (architect V2, V3, N6; security-auditor m4; the race `test-writer` proved)

- **Cadence and bounds.** The recurring job `classify-junior-job-ads` runs every two minutes (`*/2 * * * *`). `ClassifyJuniorJobAdsOptions` has `Enabled` (the kill-switch), `BatchSize` 200 and `TimeBudgetSeconds` 90, which is below the cadence. Hangfire stays in the Worker's wrapper `ClassifyJuniorJobAdsWorker`: `DisableConcurrentExecution` with a 20 s lock wait (shorter than the cadence, so a tick blocked by a running one fails fast) and `AutomaticRetry(Attempts = 0)`. A failed tick is benign, because the next one re-derives the same stale set from the committed state.
- **Selection.** Like the company-watch sweep, the committed state is the queue. An ad is selected while it is listed now (`JobAdPublication.IsListed(now)`), its occupation group is in an enabled field's groups, and there is **no assessment whose stamped fingerprint equals the ad's input fingerprint under a current rule-set version.** Newest first, then by id.
- **Rounds without OFFSET.** Each round re-selects the newest `BatchSize` stale ads, because the predicate shrinks by the sweep's own work, keeps an in-tick skip set, and repeats until nothing is stale or the budget is spent. A poison ad cannot livelock the sweep: one that fails is counted, skipped for the rest of the tick and retried on the next.
- **A seven-field `AsNoTracking` projection:** id, external id, post-scrub title, post-scrub description, occupation group, experience mark and input fingerprint. It never reads `raw_payload`, the contacts or the organisation number, **never writes `job_ads`, and never touches an admin's decision.** It classifies in Application, then loads or creates the ad's assessment in a scope of its own, stamps the fingerprint it read with the text, and saves.
- **Counts-only logs,** and only for a tick that did something: junior, not junior, unclear, refused, failed and purged. A failed ad logs its exception's type and nothing else, because a message or a stack could carry a fragment of the text. Never an ad id, a title, an employer or a matched span.
- **The purge of erased ads.** At the start and at the end of every tick, the sweep deletes the assessments of ads whose status is Erased. It reads first, so a quiet tick writes nothing. The erasure command deletes the ad's assessment in its own unit of work, and `Erase()` nulls the fingerprint, but a sweep that read the ad before the erasure committed can write its assessment after it. That is the race `test-writer` proved. This purge closes it: such a row cannot outlive the next tick. The registry's ground for `job_ad_junior_assessments.classified_fingerprint` says so.
- **ADR 0113.** Every read site has its row in `JobAdLifecycleReadRegistry`: the sweep's selection, its purge, both public reads, the queue's tabs and the decide handler's load.

### D4 — Membership: one EXISTS on the assessment's primary key, composed under `IsListed(now)` in every read, and no regex on the request path (supersedes ADR 0186 D1–D2)

`JuniorMembership.Of(assessments, groups)` (Application, beside `JobAdPublication`) says of an ad: its occupation group is among the enabled fields' groups, and an assessment exists whose decision is Junior, or whose decision is absent and whose verdict is Junior.

- **A decision outranks every rule, the reading included.** Decision NotJunior removes an ad the rules show; decision Junior shows one the rules left unclear or excluded; `null` hands the ad back. A decision survives every reclassification, because the sweep never writes it, and every `UpdateFromSource`, because the ingest funnel cannot reach the aggregate.
- **It does not filter on the rule-set version.** A verdict from the previous rules keeps showing until the sweep has re-classified the ad, so a rule deploy leaves no gap and never empties the list.
- **It says nothing about lifecycle.** Every read composes `.Where(JobAdPublication.IsListed(now))` as the outer conjunct, as ADR 0186 D1 did, so an ad that is removed by the stream, passes its deadline, is expired by `ExpireJobAdsJob` or is erased leaves the rows, the total and the kommun options in the same request.
- **The public read keeps its shape.** `IJuniorItJobAdQuery` keeps its two members, and `JuniorItJobAdQuery` composes `JuniorMembership.Of(db.JuniorAssessments, groups)` into the one base from which the rows, the total and the options are read at one `now`. The handler, the row, the envelope, the page, the reader, the metadata and the bucket are unchanged.
- **#2261 composes it and never copies it.** The `/jobb` filter composes `JuniorMembership` over the assessments queryable as an EXISTS, beside its other per-user EXISTS filters. This PR deletes `JuniorItPredicate`, `JuniorItSetResolver`, `junior-it-list.v1.json`, its loader and `JuniorItHeadlinePatternAgreementTests` (the ids moved into the rules file as the reading, D7), and with them the two-engine agreement, the database-ctype risk and a per-request regex over third-party titles. `JobAdSearchComposition.OrElse` is `private` again.
- **Cost.** One primary-key EXISTS per candidate, over about 2 850 listed Data/IT ads (R2). It is unmeasured; ADR 0045's hot-path budget is observe-only.

### D5 — The admin queue `/admin/juniorjobb` and the decision (security-auditor m2)

- **Three tabs.** *Att avgöra*: listed ads in an enabled field whose verdict is Unclear and which have no decision. *Visas på /junior*: the ads on `/junior` now, by a rule or by a decision, where a wrong one is vetoed. *Beslutade*: every ad with a decision, where it is changed or handed back. Each composes `IsListed(now)` and joins the assessment, so a closed or erased ad never reaches the page. The three counts and the page are read at one instant; a page is 25 rows.
- **Routes**, under the Admin authorization policy: `GET /api/v1/admin/junior-job-ads` (the tab and the page as query parameters) on `admin-read`, and `POST /api/v1/admin/junior-job-ads/{id}/decision` with the body `{decision}` (`null` hands the ad back) on `admin-write`. Every answer is `private, no-store`, and the web reader is `no-store`.
- **The decision.** `DecideJuniorJobAdCommand` is an `IAdminRequest` and an `IAuditableCommand`. The audit row is `Admin.JuniorDecisionRecorded`, aggregate `JobAd`, with the ad's id and no payload: no title, employer or external id. Who decided is in `audit_log` and nowhere else.
- **Eligibility is checked in the handler's load, not in the aggregate.** The ad must be listed now, in an enabled field and assessed; a closed, erased, manual or out-of-field ad answers not found. `IsListed(now)` is time-relative and lives in Application, so no aggregate can own it; the assessment still owns its own invariants (the decision pair and a closed decision type).
- **A row** shows the ad's title, employer, kommun, publication date and last day, and the rule's Swedish label; a decided row shows both *Reglerna* (the verdict and the rule) and *Ditt beslut*. Never ad text, an Arbetsförmedlingen id or an organisation number. A rule id with no label shows "Okänd regel", and a web test pins that every rule id the file can produce has a label in both languages.
- The queue is internal processing by the controller himself, with no recipient. CC never signs in to look at it (CLAUDE.md §9.2 (d)); Klas verifies it live.

### D6 — The cutover: the first deploy's gap is accepted, the fallback arm is rejected, and a later storage change is expand, migrate, contract (senior-cto-advisor (B))

- **The gap.** After the deploy the table is empty, so `/junior` reads nothing until the sweep has run: one tick, plus the drain of the field's backlog. The backlog is the 2 850 listed Data/IT ads of R2, which is at least 15 rounds of 200 across as many ticks as the 90 s budget needs; the time is unmeasured. The gap is accepted because the post is not out yet and nobody is linked to the page. Klas can start the job from admin/jobb right after the deploy (it is in `RecurringJobIds.All`), and the amendment's D11 (security-auditor M4 (c)) reads "listed Data/IT ads without an assessment" as 0 before he publishes.
- **The fallback regex arm is rejected.** An arm that lists unclassified rows by the old headline pattern, to keep the list populated during the window, would keep two definitions of "junior IT" alive, which is what this change retires. "Not yet classified" cannot be told apart from "the sweep fails on this ad forever", so it would hide a broken sweep. During the window the list would mix two definitions, since the rules can judge an ad Unclear or NotJunior even when its headline matches. And it is code for two minutes of one deploy, with no removal trigger. A backfill inside the migration is impossible, because the rules are C#.
- **A later rule deploy leaves no gap,** because reads ignore the version (D4).
- **The rule once the post is out.** Any later change to how membership is stored ships as expand, migrate, contract (Sato, "ParallelChange", martinfowler.com, 2014). It was considered and rejected for this one deploy: it would take two deploys, or a read flag, to save about two minutes on a page with no audience.
- **The first night's write wave.** The nightly snapshot re-ingests the listed ads and writes `junior_input_fingerprint` once on every one of them, where it was null: a one-time write wave on `job_ads` over the snapshot's 43 860 ads (R2). After it, each ad's input fingerprint differs from the null its assessment was stamped with, so the sweep reclassifies the field once more. Both are expected and not a fault.

### D7 — The reading and the facit (Klas: "Bara junior-id:na (Rek.)", "Ja, utanför repot (Rek.)"; security-auditor M3, m5, m7)

- **The reading in the file** is Klas's hand reading as Platsbanken ids, **junior ids only.** It holds 145 ids: ADR 0186's 143, and 2 junior-labelled ids from the reading's later additions. The id left out on 2026-11-20 (outside the Data/IT groups) stays out. The block holds exactly `ruleId` and `junior`, and no not-junior id, direction, label, year, place or note. An ad he read as not junior goes through the rules like any other (R3 prices that). The 145 ids were counted on the box before the first push (R4), and the file is never edited because of an erasure (ADR 0166 trigger 23).
- **The minimised facit copy** holds the id, the label, `arMin` and `arMax` and nothing else: no note, place or county. It is kept outside every repository, at `C:\DOTNET-UTB\Jobbliggaren-junior-facit\`, to re-measure the rules while the reading's ads are open, and **is deleted on 2027-05-18,** when the last of them closes. It is not personal data on the record, and the processing register is to state its content, place, deletion date and reader (security-auditor m7). The reading's own folder is never changed. Once the copy is gone, the living facit is Klas's decisions in the queue, stored beside the verdict.
- **The harness** is `tests/Jobbliggaren.QA.Corpus/Junior/JuniorFacitMeasurement.cs`, opt-in: it calls `Assert.Skip` before any file or network I/O unless `JBL_JUNIOR_FACIT_DIR` is set (Klas: "Appens inläsningskod lokalt (Rek.)"). It reads every ad through the app's own JobTech client, `PlatsbankenJobSource` and `JobAd.Import`, so the classifier sees the post-scrub text the app stores. It is one `[Fact]`, with null loggers (the source logs external ids), a per-ad failure counted by its error code or exception type, and the report written only to the scratch file named by `JBL_JUNIOR_MEASURE_OUT` or to the test output. **It prints counts only:** no title, text, employer, note or id, and no assertion over data, because an assertion would print it on failure.

### D8 — No new index on `job_ads`, and the §3.6 note for the new table (architect N3)

The sweep's selection is `IsListed ∧ group in groups ∧ stale`, ordered by `published_at DESC, id`, with a limit. The existing partial index on `occupation_group_concept_id` narrows it, a comparison between two columns of one row cannot use a btree index anyway, and no partial index on a verdict or a decision is added. `job_ad_junior_assessments` has only its primary key, which the membership EXISTS and the sweep's NOT EXISTS use.

AGENTS.md §3.6: the table's first drain bulk-loads an empty table, and then it is written continuously (the sweep every two minutes, decisions as they are made). Continuous DML excuses a table from a job-side `ANALYZE` only where autovacuum demonstrably re-arms, so `last_autoanalyze` for it is **read on the box** (CLAUDE.md §9.2 (a)) after the first drain, and not assumed.

## What is not measured, and the measurements owed

**Not measured, and not claimed:**

- **The precision of `no-signal`.** Klas's reading covers only the ads he read. The candidate filter caught 242 of the 250 measured reading ads, all 135 shown labels included (R1), but the listed ads outside the filter and outside the reading were never read, so what `no-signal` excludes among them is unmeasured.
- **The precision out of sample.** It is measured against the same reading the rules were shaped on, so it is optimistic. The ongoing check is the queue's decisions: every Unclear ad, and every automatic one Klas vetoes on the *Visas på /junior* tab, is a data point.
- **The cost of the EXISTS read** (D4). ADR 0045 is observe-only here.
- **The first deploy's drain time** (D6). This ADR records it after the deploy.
- **`last_autoanalyze` of the assessments table on the box** (D8).

**Owed by this PR's own tests, named here ahead of the panel (the PR holds them):** the Domain's `JobAdJuniorAssessmentTests`; the classifier's and the loader's tests, including the pins of the thirteen top-level keys, of exactly one rule file and of a reading of junior ids only; `ClassifyJuniorJobAdsJobStalenessTests` (Testcontainers; the staleness expression, an ad written before the migration included); `JuniorAssessmentErasureTests` and `JuniorAssessmentErasureRaceTests`; `JuniorItJobAdQueryReadTests`, rewritten, whose four exits (removal from the stream, a passed deadline, `ExpireJobAdsJob` and `Erase()`) are kept for the rows, the total and the options, and whose curated- and headline-arm assertions are replaced by verdict- and decision-arm assertions (declared in the PR body, security-auditor R7); the admin queue's endpoint, handler and rate-limit wiring tests; and the registry rows. Their premises come from production paths (AGENTS.md §5, Tests): rule sets are loaded through `JuniorRuleSetLoader.LoadFrom` and ads imported through `JobAd.Import`, and an assessment arises only through the sweep or the domain methods.

**The two guards the rulings name** (architect N7, senior-cto-advisor) are in the PR: `JuniorClassificationWriterGuardTests` pins the callers (only the sweep creates and re-classifies an assessment and builds the field classifier, only the decide handler decides, and only the field classifier runs `JuniorClassifier`, so no regex reaches a request path), and `JobAdRawPayloadDerivationGuardTests` admits no bulk `ExecuteUpdate` writer of `ExperienceRequired` or `JuniorInputFingerprint`.

## Alternatives considered

### Alt A — The chosen design (D1–D8)
**For:** a new ad that a strict rule decides reaches `/junior` within a sweep tick; an unclear ad reaches the queue instead of being guessed; Klas's decision stands across rule changes and source writes; a rule edit re-runs every open ad without anyone remembering; no regex on the request path; the sweep never writes `job_ads` or reads `raw_payload`; one definition of "junior" for #2261; the public side is byte-unchanged; more fields later are one rule file each.
**Against:** the trade-offs listed under Consequences.

### The forks
- **Option A of ADR 0186 D9, the headline rule alone.** Rejected by Klas's choice of B: it is precise and finds about half, and the curated set ages.
- **Options C and D of ADR 0186 D9.** C, a model in ingestion, is an AGENTS.md §5 / ADR 0071 stop and not an option to price. D, a recurring agent run, reads recruiters' names into an agent's transcript (CLAUDE.md §9.2 (c)).
- **Columns on `JobAd`, as the plan first had them.** Rejected by the CTO's ruling (D1): SRP, the aggregate boundary, a tracked whole-row load in the sweep, two CHECKs on the largest table.
- **Columns on `JobAd` written by `ExecuteUpdate` from a projection.** Rejected: it bypasses the aggregate and adds a second `ExecuteUpdate` writer of `job_ads` (D1).
- **A revision counter in place of the content fingerprint.** Rejected (architect V1): it loses increments between two ingest writers, and a mix of two writers can leave the verdict standing over wrong text until the next rule version.
- **Recompute the fingerprint and `??=` it.** Rejected: it makes the sweep a writer of a column ingest owns (D1).
- **A fallback regex arm for unclassified rows, or a backfill inside the migration.** Rejected (D6).
- **The rules in SQL at request time.** Rejected: two regex engines again, the database's locale back in the match, and a scan of the description on every request.
- **Wilson's lower bound as the criterion.** Not chosen: under it no rule qualifies, since `title-junior` at 70 of 70 gives 94.8 %, and the queue would be about 8 a day (R1). Klas chose point precision with a floor of ten cases.
- **Every rule automatic.** Fails the criterion: eight of the twelve rules are below 95 % or below ten cases (R1).
- **The reading in both directions in the file.** Rejected by Klas ("Bara junior-id:na (Rek.)"): "not junior" is a datum of his reading that neither the ad nor the list shows, and it would be public for good. Its cost, measured the same day, is R3.
- **The facit copy in the repository's gitignored `docs/research/junior-facit/`** (the plan's first form). Rejected on `security-auditor` m7: a gitignore is one `git add -f` away from public. Klas: "Ja, utanför repot (Rek.)".
- **Two PRs, or the banner link inside this one.** Klas: "En PR, allt (Rek.)", then, for the banner link, "Egen PR efter denna (Rek.)". The CTO concurs on principle, not by deference: the membership expression contains the decision arm, the decision lives in the same migration, and the re-take and the DPIA addendum must describe the processing as built; a split would describe a queue that does not exist yet or run `security-auditor`'s signature cycle twice. Scope size is not a criterion.

## Consequences

### Positive
- A new or changed ad that a strict rule decides reaches `/junior` within one sweep tick (two minutes) of its ingest write; an ad nothing decides reaches the queue and waits for Klas, and is never guessed.
- A rule change re-runs every open ad in the field by itself and leaves every decision of Klas untouched.
- The request path has no regex: the two-engine agreement, the database-ctype risk and the per-request regex over third-party titles are gone.
- The list is still live by construction: every read composes `IsListed(now)`, so closing, removal, expiry and erasure leave the rows, the total and the options at once, and an erased ad's assessment is deleted with it and purged on the next tick if a racing sweep wrote one.
- The sweep reads a seven-field projection and never writes `job_ads`, so the whole-row-load hazard class is gone.
- The public row, envelope, page, reader, metadata and bucket are unchanged, and `JuniorMembership` is the one definition #2261 and the queue share.
- The reading ids are the only thing from the reading in the public repository, and they are junior ids only.

### Negative and accepted trade-offs
- **A new aggregate, a table and a migration on the single-owner hotspot,** and a CTO-reviewed ingestion change (one facet with PRESERVE semantics and one SHA-256 per source write, inside the atomic funnel).
- **A gap on the first deploy** (D6), and a **one-time write wave** on `job_ads` the first night, after which the field reclassifies once more.
- **One primary-key EXISTS in every membership read.** Unmeasured (*What is not measured*).
- **The precision is in-sample** and so optimistic. The criterion is a point estimate on at least ten cases, not a lower bound. Two ads that Klas read as not junior are shown until he vetoes them (R3).
- **The queue is Klas's time.** On new ads it averages 4.0 a day over the last seven days, at most 9 (R2); that is about 28 a week, where ADR 0186 D9 estimated about 50 a week to read for option B. It is the price of "Hellre oklar än fel".
- **A decided ad keeps its decision after the employer rewrites the text.** The decision is about the ad, and no marking of a decided ad whose text changed is built.
- **The 145 reading ids are in a public repository for good** from the first push. They were counted first (R4); the file is never edited for an erasure; "not junior" is deliberately left out.
- **The legal outcome waits.** The merge, and so the deploy, waits for Klas's approval of the ADR 0166 amendment and `security-auditor`'s signature, and the deploy is followed by a reading on the box before the post (the amendment's D11, M4 (c)).

## What comes back

Nothing detects these automatically. Each is read when the event is proposed or reported.

- **A second occupation field switched on.** A new rule file, ADR 0166 amendment trigger 22 and an Art. 9 re-read for that field. The test that pins exactly one rule file turns red first.
- **A rule set to `auto`, or an automatic rule's pattern loosened, without a new measurement** against Klas's criterion (point precision of at least 95 % on at least 10 cases), recorded with its date. Narrowing a rule or turning its `auto` off is not this.
- **Any change to how membership is stored:** expand, migrate, contract (D6), and `senior-cto-advisor` reads it.
- **The banner link, #2265:** a fifth read in the `/jobb` hero's `Promise.all` is a plan on ADR 0045's hot path, so `senior-cto-advisor` reads #2265's plan before it is built, and `security-auditor`'s m3 holds for it (a per-user rate-limit policy with a wiring test, `private, no-store`, no Next cache, and the link hidden at 0 and on error).
- **The first deploy's drain time and `last_autoanalyze`,** which this ADR records once read (D6, D8).
- The legal triggers are ADR 0166 D10's and have their home in that amendment.

## Implementation

One PR on `feat/junior-live-classification`, lane BE+FE, #2264. It adds one migration, no package and no `deploy/` diff, and no edit to `proxy.ts`, `robots.ts` or the sitemap. **Hotspots touched** (CLAUDE.md §6.5; the borrow and its return are the driving session's): the EF migration, `AppDbContext`/`IAppDbContext` and `DependencyInjection.cs` (`hotspot:di`), `RecurringJobIds` and the registrar, and `messages/{sv,en}` (`admin.json`, `common.json`, `admin-preview.json`; `hotspot:i18n`).

- **Domain:** `JobAdJuniorAssessment`, `JuniorVerdict`, `JuniorDecision`, `JuniorClassification`, `JuniorInputFingerprint`, `JobAdJuniorDecidedDomainEvent`; `JobAd` (`ExperienceRequired`, `JuniorInputFingerprint`, `Erase()`) and `JobAdFacets` (`ExperienceRequired`).
- **Application:** `JobAds/Junior/` (`JuniorClassifier`, `JuniorRuleSet`, `JuniorYearsReader`, `JuniorFieldClassifier`, `IJuniorFieldCatalog`, `JuniorMembership`); `JobAds/Jobs/ClassifyJuniorJobAds/` (the job and its options); `Admin/JuniorJobAds/` (`DecideJuniorJobAdCommand`, `ListJuniorJobAdsQuery` with their handlers and validators); `IAppDbContext.JuniorAssessments`; the assessment's delete in `EraseRecruiterAdsCommandHandler`; the `ErasureCascadeRegistry` rows and grounds; `RecurringJobIds`.
- **Infrastructure:** `JobAds/Junior/` (`JuniorRuleSetLoader`, `JuniorFieldResolver`, `junior-rules.data-it.v1.json` and its `<EmbeddedResource>`); `JobAdJuniorAssessmentConfiguration` and the `JobAdConfiguration` columns; the migration `20261120103608_AddJobAdJuniorAssessments`; `JobTechHit` and `MapFacets` for the mark; `JuniorItJobAdQuery`; the registrations in `DependencyInjection.cs`. **Deleted:** `JobAds/JuniorIt/` (loader, predicate, set resolver, `junior-it-list.v1.json`) and their tests, including `JuniorItHeadlinePatternAgreementTests`.
- **Worker:** `ClassifyJuniorJobAdsWorker` and the `RecurringJobRegistrar` row.
- **Api:** `AdminJuniorJobAdsEndpoints` (D5).
- **Web:** `(admin)/admin/juniorjobb/`, the admin junior components, `lib/actions/admin-junior-job-ads.ts`, `lib/api/admin-junior-job-ads.ts`, `lib/dto/admin-junior-job-ads.ts`, `lib/admin/junior-jobs.ts`, the navigation entry, the `juniorJobs` block in `messages/{sv,en}/admin.json` and the admin-preview fixtures.
- **Architecture tests:** the lifecycle-registry rows (ADR 0113), the erasure registry, `JobAdPublicSurfaceGuardTests`, `TaxonomyAclLayerTests`, `AccountHardDeleteCascadeFitnessTests` and `MappedPlaintextExposureRegistry`.
- **Then the docs:** this ADR and its README row; the ADR 0166 amendment; the pointer in ADR 0186; the session docs per ADR 0065. Then the panel, and `security-auditor`'s signature before the merge.

**Outside the diff, owed by the driving session** (the legal ones are listed in the amendment; `security-auditor` M1, M2, M4): the sixth addendum to DPIA #456 §8-B (local, main copy), whose text Klas approves; the processing register's JobTech row and `docs/runbooks/recruiter-pii-erasure.md` §2.4a, and ADR 0106's amendment of 2026-11-19, where they still say that membership is read and classified per request; and the three readings of M4, (a) and (b) before the merge and (c) after the deploy and before Klas publishes the post.

## Measurements

Each reading below is dated and has its source. Nothing is estimated. All are the driving session's of 2026-11-20, given in the brief, and none was re-run for this ADR.

| Reading | What | Recorded |
|---|---|---|
| R1 | the rules against Klas's reading, phase 0 | 2026-11-20 09:55–10:00Z, the rule file of commit `1be6f9976` (before the `auto` flags were set), reading JobTech through the app's own client, `PlatsbankenJobSource` and `JobAd.Import`. The reading had 254 ads, of which 250 were measured and 4 were no longer retrievable. The candidate filter caught 242 of the 250, and all 135 shown labels. Per rule, standalone, see the table below. In sequence under the criterion: 82 ads shown automatically, 80 of them right (97.6 %), a coverage of 59.3 %; 48 excluded automatically, 47 of them right (97.9 %); 120 of the 250 to the queue. Under Wilson's lower bound no rule qualifies (`title-junior` at 70 of 70 gives 94.8 %), and the queue would be about 8 a day. |
| R2 | the population | 2026-11-20, the same harness and rule set. 2 850 listed Data/IT ads, 256 of them candidates. The queue on new ads averages 4.0 a day over the last 7 days, at most 9. `/junior` would show 144. Over the shown set: an organisation number of personnummer shape 0, without an organisation number 0, not ten characters 0; 34 kommuner, 20 of them with exactly one ad. The experience mark is carried by 43 860 of 43 860 snapshot ads and 894 of 894 stream upserts. |
| R3 | the reading's not-junior ads, with junior ids only in the file | 2026-11-20, the same run. 111 of the reading's not-junior ads are listed. Under the criterion, 63 go to the queue once, 46 are excluded, and 2 are shown until Klas vetoes them. |
| R4 | M3, the reading's ids counted before the first push | 2026-11-20 11:02:03Z, on the box, counts only, over the file's 145 ids and the Data/IT field's 12 occupation groups: found 145 (not found 0); listed 143; outside the Data/IT groups 0; erased 0; an organisation number of personnummer shape 0, not ten digits 0, without an organisation number 0. |

**R1, per rule, standalone:**

| Rule | Fires | Right | Precision |
|---|---|---|---|
| `title-junior` | 70 | 70 | 100 % |
| `text-seek-junior` | 44 | 42 | 95.5 % |
| `title-senior` | 30 | 29 | 96.7 % |
| `text-mentor-only` | 35 | 34 | 97.1 % |
| `years-3-plus` | 38 | 24 | 63.2 % |
| `text-junior-and-senior` | 36 | 16 | 44.4 % |
| `text-new-graduate` | 15 | 12 | 80.0 % |
| `text-early-career` | 54 | 37 | 68.5 % |
| `flag-no-experience` | 22 | 15 | 68.2 % |
| `title-student` | 4 | 4 | 100 %, below the ten-case floor |
| `title-thesis` | 1 | 1 | 100 %, below the ten-case floor |
| `text-no-experience-needed` | 1 | 1 | 100 %, below the ten-case floor |

## References

- Code, in the tree: `src/Jobbliggaren.Domain/JobAds/` (`JobAdJuniorAssessment.cs`, `JuniorClassification.cs`, `JuniorInputFingerprint.cs`, `JuniorVerdict.cs`, `JuniorDecision.cs`, `JobAd.cs`, `JobAdFacets.cs`) · `src/Jobbliggaren.Application/JobAds/Junior/` · `src/Jobbliggaren.Application/JobAds/Jobs/ClassifyJuniorJobAds/` · `src/Jobbliggaren.Application/Admin/JuniorJobAds/` · `src/Jobbliggaren.Application/JobAds/Commands/EraseRecruiterAds/` (`EraseRecruiterAdsCommandHandler.cs`, `ErasureCascadeRegistry.cs`) · `src/Jobbliggaren.Infrastructure/JobAds/Junior/` (`JuniorRuleSetLoader.cs`, `JuniorFieldResolver.cs`, `junior-rules.data-it.v1.json`) · `src/Jobbliggaren.Infrastructure/JobAds/JuniorItJobAdQuery.cs` · `src/Jobbliggaren.Infrastructure/Persistence/Migrations/20261120103608_AddJobAdJuniorAssessments.cs` · `src/Jobbliggaren.Worker/Hosting/ClassifyJuniorJobAdsWorker.cs` · `src/Jobbliggaren.Api/Endpoints/AdminJuniorJobAdsEndpoints.cs` · `web/jobbliggaren-web/src/app/(admin)/admin/juniorjobb/`
- Tests, in the tree: `tests/Jobbliggaren.QA.Corpus/Junior/JuniorFacitMeasurement.cs` · `tests/Jobbliggaren.Application.UnitTests/JobAds/Junior/` · `tests/Jobbliggaren.Domain.UnitTests/JobAds/JobAdJuniorAssessmentTests.cs` · `tests/Jobbliggaren.Api.IntegrationTests/JobAds/` (`JuniorItJobAdQueryReadTests.cs`, `ClassifyJuniorJobAdsJobStalenessTests.cs`, `JuniorAssessmentErasureTests.cs`, `JuniorAssessmentErasureRaceTests.cs`) and `Admin/` (`AdminJuniorJobAdsEndpointTests.cs`, `AdminJuniorQueueHandlerTests.cs`, `AdminJuniorJobAdsRateLimitWiringTests.cs`) · `tests/Jobbliggaren.Architecture.Tests/JobAdLifecycleReadRegistry.cs`
- ADR 0186 (D1–D4 superseded, D9 decided, D5–D8 stand) · ADR 0166 (the amendment of 2026-11-20, the single home of the legal outcome) · ADR 0071 (Decision 1) · ADR 0160 · ADR 0113 · ADR 0106 · ADR 0045 · ADR 0154 · ADR 0065 · ADR 0009 · AGENTS.md §2.1, §2.2, §3.6, §5, §8 · CLAUDE.md §6.5, §9.2, §9.6
- `docs/reviews/2026-11-20-2264-plan-dotnet-architect.md` · `docs/reviews/2026-11-20-2264-plan-security-auditor.md` · `docs/reviews/2026-11-20-2264-plan-senior-cto-advisor.md` (all local, transcribed by the driving session) · the approved plan, `C:/Users/zebac/.claude/plans/pasted-content-id-f745-g-r-juniorlistan-playful-starfish.md` (local)
- Vaughn Vernon, *Implementing Domain-Driven Design* (2013), ch. 10 · Robert C. Martin, *Clean Architecture* (2017), ch. 7 and 13 · Danilo Sato, "ParallelChange", martinfowler.com (2014) · PostgreSQL docs, `ALTER TABLE`: `ADD COLUMN` without a default (links may rot)
