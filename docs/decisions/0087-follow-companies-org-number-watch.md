# ADR 0087 — Follow companies — org.nr watch, read-model employer identity, and a parallel follow-notification rail

**Datum:** 2026-06-30
**Status:** Accepted
**Decision-makers:** Klas Olsson (feature GO 2026-06-28 market analysis;
GO "kör direkt" for the D5-tillägg, issue #452, 2026-07-01);
senior-cto-advisor (mechanism bind 2026-06-30; D5-tillägg panel-triage bind
2026-07-01 with dotnet-architect + security-auditor)
**Related:** ADR 0080 (notification backbone — DigestDispatchJob reused, FollowedCompanyAdHit mirrors
state-machine shape); ADR 0067 (filter-SPOT and IJobAdSearchQuery extension pattern — D6 follows
this exact shape); ADR 0039 / 0062 (filter-SPOT / IJobAdSearchQuery / SavedSearch identity); ADR
0011 (strongly-typed IDs); ADR 0024 / Art. 17 cascade (two new user-owned aggregates must be
cascade-wired); ADR 0071 / 0076 (no-AI / Goodhart guard — inherited end-to-end); ADR 0032 (ingest
audit / event-on-ingest rejection transfers verbatim); ADR 0023 / 0010 (Worker HTTP-free composition
root — CompanyWatchScanJob lives here); ADR 0079 (Fast/Full grade SSOT and `GradeRankExpression` —
the D5-tillägg hub count reuses this exact authority, exactness proof relies on its Fast/Full
band equivalence at ≥Good) · ADR 0045 (hot-path latency budget — the ground for rejecting Approach 2's
read-time Full-band scoring)

> **Lifecycle note:** Authored 2026-06-30 by Claude Code (adr-keeper) on explicit Klas directive
> and senior-cto-advisor mechanism bind (2026-06-30). Decision substance transcribed from bound
> decisions supplied by Klas and senior-cto-advisor — no decisions constructed. Accepts CC
> authoring the ADR prose per memory `feedback_klas_can_override_adr_verbatim_source` (Klas
> override of CLAUDE.md §9.4 webb-Claude-verbatim convention; grounded in senior-cto-advisor
> verdict). Local-only file (ADR 0072 docs-privacy convention).

> **Lifecycle note — D5-tillägg (2026-07-01):** the D5-tillägg section was added by Claude Code
> (adr-keeper) to record a decision the 2026-07-01 multi-agent panel-triage (senior-cto-advisor +
> dotnet-architect + security-auditor, grounded in code) bound as part of the #311 Företagsbevakning
> pre-MVP epic, ahead of and confirmed for issue #452. Klas gave the GO ("kör direkt") for the
> session that implements it; the panel decided the substance, CC transcribes it — no decisions
> constructed. Same webb-Claude-verbatim override as the base ADR
> (`feedback_klas_can_override_adr_verbatim_source`) applies to this addendum.

> **Delivery note (2026-07-01):** PR-1 (#411), PR-2 (#416), PR-3 (#430) mergade. **PR-4 (D5) LEVERERAD → PR #436** (`feat/follow-companies-scan-311`): `FollowedCompanyAdHit`-aggregat + `CompanyWatchScanJob` + separat `FollowedCompanyNotificationsEnabled`-consent + `DigestDispatchJob`-follow-pass + Art.17-cascade (9/9) + D5 surfacing-guard-fitness + migration. **senior-cto-advisor-bind 2026-07-01 (D1–D5-implementation):** D1 = nytt `FollowedCompanyNotificationEmail`-kontrakt (företagsföljning har ingen grad → INTE en `MatchNotificationEmail`-overload); D2 = delad `DigestCadence` (två consent-flaggor grindar två källor); **D3 = write-API (consent-command + endpoint) UPPSKJUTET till #408-blockade FE-PR:en** (speglar PR-2b:s endpoint-deferral — PR-4 levererar bara domänmetoden `UpdateFollowedCompanyNotificationConsent` + persistens, läst av scan/dispatch); D4 = `JobSeeker.LastCompanyWatchScanAt`-watermark (speglar `LastMatchScanAt`); D5 = tvådelad fitness (fail-closed org.nr-DTO-partition + scan-logg-källscan, in-scope PR-4). **Schema-avvikelse (medveten, db-migration-writer-noterad):** `followed_company_ad_hits` saknar `created_by`/`updated_at`/`updated_by`/`row_version` — Worker-skapat batch-aggregat, single-writer (`DisableConcurrentExecution`), speglar `UserJobAdMatch`; optimistic concurrency ej krav. **PR-4-forward-note:** D5:s logg-guard täcker `CompanyWatchScanJob` (den enda vägen som läser rått org.nr); utöka den om en framtida dispatch-väg läser org.nr in i scope. Kvar: #415 PR-2b (surfacing-endpoint + write-API, blocked #408), FE, PR-5 (BrandGroup, optional). Security-auditor **APPROVE 0/0/0** (§12 PII-grind clearad).

> **Delivery note — D5-tillägg (2026-07-01, issue #452, PR pending on branch
> `feat/hub-match-label-452`):** the hub (`/foretag`, #448) per-watched-company
> "matchande annonser"-count shipped as a **new read-time projection**
> (`IPerUserJobAdSearchQuery.CountPerUserByEmployerAsync`), NOT the
> LEFT-JOIN/compute-on-demand approach the issue originally sketched. See the
> **D5-tillägg** section below for the full bind, the rejected alternative, and
> the exactness proof. The D5 scan/notification spine (`CompanyWatchScanJob`,
> `FollowedCompanyAdHit`) described above is untouched by this addendum — it
> remains scorer-free and grade-free.

---

## Kontext

The 2026-06-28 discovery market analysis ranked "follow employers and get notified on new ads" as
the #1 feature. Users want to watch specific employers and be alerted when a new job ad appears
from that employer, without manually re-running a search every day.

**The canonical key problem.** Employer identity in Swedish Platsbanken is an org.nr
(organisationsnummer) — a 10-digit string issued by Bolagsverket. The alternative, fuzzy name
matching, is the "Volvo×20" trap: "Volvo" is not one legal entity; it is Volvo Cars AB, Volvo
AB, Volvo Buses AB, Volvo Financial Services AB, and many more, each with its own org.nr and
independent hiring pipeline. Fuzzy name matching merges them silently or splits them
inconsistently. org.nr is the only correct key.

**Empirically confirmed facts (live-verified 2026-06-30):**
1. org.nr lives in the live JobTech/JobStream feed at `employer.organization_number`. The
   jobsearch.api.jobtechdev.se API returns 10-digit Swedish org numbers (e.g. 5592804784 was
   observed in a live response).
2. org.nr is **absent** from all 77,520 existing stored `job_ads.raw_payload` rows. Zero rows carry
   it. Root cause: `raw_payload = Sanitize(JsonSerializer.Serialize(JobTechHit))` and the
   `JobTechEmployer` POCO only deserializes `name` — it never had an
   `organization_number` field. The sanitizer allowlist already retains `organization_number`
   (dropping PII email/phone) — so the fix is a POCO field addition, not a sanitizer change. This
   is the same B2 root-cause pattern as employment_type/working_hours_type (ADR 0067) and
   occupation/workplace_address (F6 P4).
3. Because org.nr is B2 (POCO never carried the field), existing rows are NULL on the D1 generated
   column until they are re-ingested. New and re-synced ads will populate the column from the
   moment PR-1 ships. Full backfill completes on the next JobStream snapshot sync; this is an
   implementation gate, not an architecture choice.

**Design constraints:**
- Deterministic, no AI/LLM at any point (ADR 0071). No opaque scoring.
- Large feature, split BE/FE. FE (disambiguation UI + /jobb per-employer filter) is deferred
  behind issue #408's /jobb redesign.
- Notification rail reuses ADR 0080's DigestDispatchJob spine (EMAIL TRANSPORT only) and adds a
  new parallel aggregate and scan job. `UserJobAdMatch` is NOT reused as the spine.
- GDPR Art. 17 fail-closed cascade gate: any new user-owned AggregateRoot must be Art.17-wired or
  `AccountHardDeleteCascadeFitnessTests` fails the build.

---

## Beslut

Implement employer follow via a five-PR suite: promote org.nr from the live JobTech feed to a
STORED generated column on job_ads (PR-1); add employer facet to the search filter (PR-2); build
the `CompanyWatch` aggregate + commands + endpoints + Art.17 cascade wiring (PR-3); build a
dedicated `FollowedCompanyAdHit` aggregate + `CompanyWatchScanJob` + a separate GDPR consent flag
+ DigestDispatchJob email transport reuse + Art.17 cascade wiring (PR-4); optionally add a curated
BrandGroup table in a follow-up (PR-5, deferred). A persisted Company aggregate is explicitly NOT
built in v1 — employer identity resolves via read-model projection over job_ads.

### Bindande beslut

**D1 — org.nr promotion: STORED generated column + partial index.**

Add `organization_number` to `JobTechEmployer` POCO so new/re-synced ads carry the value through
deserialization. On `job_ads`, add a STORED generated column:

```
HasComputedColumnSql("raw_payload->'employer'->>'organization_number'", stored: true)
```

This mirrors the pattern of the 6 existing shadow columns in `JobAdConfiguration` (ADR 0067
Klass 1 dimensions). The column is drift-impossible: it is derived from `raw_payload` by
definition and cannot disagree with it. Add a partial index `WHERE organization_number IS NOT
NULL` to support the D6 employer-filter IN-equality and the D5 scan watermark query efficiently.

**Rejected alternatives:**
- **Variant B** — plain column written at ingest + C# backfill: reintroduces a write/read drift
  surface and a second source of truth. DRY violation — raw_payload is already the ground truth.
- **Variant C** — map onto a Company VO on JobAd: wrong cohesion axis (a VO would pre-commit D2's
  persisted Company design space) and adds no invariant that the generated column does not already
  provide at lower cost.

**D2 — Company aggregate: DEFER. Use a read-model projection over job_ads.**

Do NOT build a persisted `Company` aggregate in v1. org.nr + company_name already live on
`job_ads` after D1. Follow targets reference org.nr directly (D3) — there is no CompanyId.

A persisted Company aggregate would be a second employer source-of-truth protecting zero invariant:
`display_name` and `normalized_name` are derivable projections, not invariants (Fowler,
AnemicDomainModel). Building it now is YAGNI (Martin 2017, ch. 34). Evans (2003) prefers natural
identity where a stable natural key exists — org.nr is that key.

Disambiguation and the employer facet list use a read-model projection:
`SELECT DISTINCT organization_number, company_name, municipality, COUNT(*) FROM job_ads
WHERE organization_number IS NOT NULL GROUP BY ...`. This is a different read concern from the
filter path (D6) and must not overload `IJobAdSearchQuery`.

> **CONSCIOUS DIVERGENCE FROM BUILD.md §5.1:** BUILD.md §5.1 lists Company as an AR with a
> surrogate CompanyId. That AR was never built — the actual reality is a name-only owned VO on
> JobAd. The CTO binds the spec-amendment direction: BUILD.md §5.1 should be amended to match
> reality, not the code bent to a stale spec. This is flagged for Klas's post-merge review.

**D3 — `CompanyWatch` aggregate.**

A new Domain aggregate `CompanyWatch` watches an org.nr on behalf of a user.

Key design:
- **Identity key: `Guid UserId`** (not JobSeekerId). This mirrors `UserJobAdMatch` (ADR 0080
  Beslut 1) — the notification rail this aggregate feeds is keyed by UserId. Using UserId avoids a
  `JobSeeker.UserId` bridge hop on every notification read. Cohesion follows the consumer.
- **`target_type` discriminator: EMPLOYER only in v1.** The EMPLOYER|BRAND_GROUP discriminator is
  NOT modelled as a multi-member enum yet — a one-member enum is speculative generality (Fowler).
  BRAND_GROUP arrives with D4 (deferred, PR-5).
- **Soft-delete** (mirrors SavedSearch). A follow is consent-adjacent (it feeds notifications) and
  produces an audit trail for Art. 17 posture — unfollow/refollow history must be recoverable.
- **`company_name` resolved at READ** via the D2 projection — do NOT denormalize a snapshot name
  field. Denormalized snapshots go stale; there is no per-item verdict semantics on company_name
  (the denormalized-plaintext doctrine from ADR 0059/0049 does not apply).

Fields:
- `CompanyWatchId` — strongly-typed Id (ADR 0011)
- `UserId` (`Guid`, indexed)
- `OrganizationNumber` — 10-digit string VO, the watch key
- `TargetType` — single-member enum `EMPLOYER` in v1
- `CreatedAt` / `DeletedAt?` — soft-delete

**MANDATORY:** `CompanyWatch` is a new user-owned AggregateRoot. It MUST be added to:
- `CascadeMap` (classified as user-owned)
- `AccountHardDeleter.RemoveRange` (by UserId)
- `AppDbContext.CompanyWatches`

Failure to wire this causes `AccountHardDeleteCascadeFitnessTests` to fail the build (GDPR Art.
17 fail-closed gate). This wiring ships in the same PR as the aggregate — never split.

**D4 — Curated BrandGroup: DEFER to PR-5 (optional follow-up).**

A curated org.nr → group alias table is the only acceptable brand-grouping mechanism. Auto-merge
of entities named "Volvo" is categorically rejected — the Volvo×20 trap is the whole reason
org.nr is canonical. When built: manually curated, NO automatic merging, ever.

No consumer exists until D3's `target_type` discriminator is extended to BRAND_GROUP (deferred).
PR-5 is optional and unprioritized.

**D5 — Notification rail: dedicated `CompanyWatchScanJob` + new `FollowedCompanyAdHit` aggregate.**

The notification spine for company follows is a **new `FollowedCompanyAdHit` aggregate**, NOT a
reuse of `UserJobAdMatch` (ADR 0080).

**Why `UserJobAdMatch` cannot be reused (Variant X — Rejected):**
`UserJobAdMatch.Grade` is typed `NotifiableMatchGrade {Good, Strong, Top}`, and
`UserJobAdMatchGoodhartTests` pins that enum exhaustively. A company-follow hit has no skill grade
— it is not graded by a matching engine. Riding `UserJobAdMatch` would require either:
(a) A synthetic 4th grade (breaks the Goodhart pin — corrupts a deliberately sealed aggregate), or
(b) A `source` discriminator field (makes the aggregate's own invariant false — the aggregate can
no longer honestly say "this row represents a skill-graded match").
Both options corrupt a deliberately sealed aggregate to save a table. This is a SRP violation
(Martin, ch. 7).

**`FollowedCompanyAdHit` aggregate:**

Keyed by `(UserId, JobAdId, CompanyWatchId)`. Fields mirror `UserJobAdMatch`'s state-machine
SHAPE — `Pending → Queued → Sent` — but carry no `Grade` field (company follow has no grade
concept). The shared shape is a shared pattern, not shared knowledge: it is not a DRY violation to
duplicate a three-state machine between two aggregates that model different concepts.

Fields:
- `FollowedCompanyAdHitId` — strongly-typed Id (ADR 0011)
- `UserId` (`Guid`, indexed)
- `JobAdId` (VO, no FK — ADR 0058/0059 soft-delete isolation; retracted ads must not
  delete hit rows)
- `CompanyWatchId` — links the hit to the specific watch that generated it
- `NotificationStatus` — `Pending → Queued → Sent` state machine; same dedup
  invariant as `UserJobAdMatch`: no re-Sent
- `CreatedAt` / `SentAt?` — persistence timestamps. (`DeletedAt?` + its soft-delete filter **RETIRED 2026-07-17 (#868):** a writerless decoy, removed; Art. 17 is HARD delete — ADR 0111. `CompanyWatch`'s own soft-delete axis above is a LIVE mechanism and is untouched.)

UNIQUE(UserId, JobAdId, CompanyWatchId) — dedup spine.

**MANDATORY:** `FollowedCompanyAdHit` is also a user-owned AggregateRoot. Same Art.17 cascade
wiring requirements as D3 — in the same PR.

**Trigger: dedicated `CompanyWatchScanJob` (Variant B — Accepted).**

Runs in the same nightly Hangfire window as `BackgroundMatchingJob` (ADR 0080 Beslut 2), with its
own watermark. The scan does NOT invoke the skill scorer — a company hit requires only:
`organization_number IN (user's watched org.nrs) AND job_ad.CreatedAt > watermark`.

**Rejected triggers:**
- **Event-on-ingest:** ADR 0080 Beslut 2's N×M queue-spike-on-$16-VPS rejection transfers
  verbatim. ADR 0032 per-RUN audit invariant also applies.
- **Overloading `BackgroundMatchingJob`:** fuses two independent change reasons — skill-scoring
  vs watched-org.nr-matching change independently. SRP violation.

**Consent: SEPARATE flag.**

A separate `FollowedCompanyNotificationsEnabled` (bool, default false) + withdrawal timestamp is
required. Reusing `BackgroundMatchNotificationsEnabled` (ADR 0080 Beslut 5) would force one
consent toggle over two distinct GDPR Art. 6/7 processing purposes (skill-match notifications vs.
company-follow notifications). GDPR consent granularity requires separate flags.

**Email transport: DigestDispatchJob IS reused.**

The ADR 0080 `DigestDispatchJob` (claim-then-send spine, anti-spam cap, per-user failure
isolation) handles email delivery for company-follow hits. The job is extended to handle
`FollowedCompanyAdHit` rows alongside `UserJobAdMatch` rows — same transport, different source
aggregate. The existing cap and per-user failure isolation mechanics apply unchanged.

**D5-tillägg — Hub match-count is a read-time projection over the D5/ADR 0079
grade SSOT, not a scan-time or scored field (senior-cto-advisor panel-triage,
2026-07-01, issue #452).**

> **Provenance:** bound by the 2026-07-01 multi-agent panel-triage
> (senior-cto-advisor + dotnet-architect + security-auditor, grounded in code)
> as part of the #311 Företagsbevakning pre-MVP epic. Klas gave the GO ("kör
> direkt") for issue #452 on this basis. The panel decided the substance; CC
> authors the ADR prose (memory `feedback_klas_can_override_adr_verbatim_source`).

The hub (`/foretag`, #448) gains a per-watched-company "matchande annonser"
count: `CompanyWatchDto.MatchingAdCount` (`int?`), computed **at read time**,
never persisted and never computed by the scan.

**Mechanism.** A new bounded port method,
`IPerUserJobAdSearchQuery.CountPerUserByEmployerAsync(orgNrs, profile, grades,
ct) → IReadOnlyDictionary<string,int>`, runs one `GROUP BY
organization_number` over PUBLIC Active `job_ads`, reusing the **exact same**
shared `GradeRankExpression` SSOT that `/jobb` (`SearchPerUserAsync` /
`CountPerUserAsync`) already uses. This is deliberately **not** an inline
projection inside `ListCompanyWatchesQueryHandler` — rank logic stays
single-owner in Infrastructure (DRY/SRP). The handler injects
`IMatchProfileBuilder` plus the new port method, in parity with
`GetMyMatchCountQueryHandler`.

**The scan stays scorer-free — the D5 seal HOLDS.** `CompanyWatchScanJob` is
untouched by this addendum: still pure org.nr set-membership, no grade
concept. `FollowedCompanyAdHit` gains **no** grade/score column. The hub count
is a derived READ LABEL only, computed fresh on every page load from the same
grade SSOT `/jobb` uses — it is not a new stored fact about a match.

**sort==grade coherence (ADR 0079).** Because the hub count reuses the
identical `GradeRankExpression`, the hub can never disagree with `/jobb` about
which ads count as a match for the same user — there is exactly one grade
authority in the system, read twice.

**The exactness proof (the load-bearing fact making Fast-band counting
correct).** "Matching" is defined as grade ≥ Good, i.e. the set `{Good,
Strong, Top}`. In `MatchGradeCalculator`, CV-skills/must-have evidence can
only ever *elevate* a match that **already has ≥1 confirmed secondary**
(Good→Strong→Top); a match with 0 confirmed secondaries floors to Basic in
**both** the Fast and Full scoring bands regardless of skill evidence.
Consequently the set `{grade ≥ Good}` is defined **identically** in both
bands — exact SSYK match, no RB1 contradiction, ≥1 confirmed secondary — a
condition the cheap Fast-band SQL `GradeRankExpression` computes exactly, with
no CV/skill data needed. Therefore: **the Fast-band ≥Good count equals the
Full-band ≥Good count**, always. Top's SQL-incomputability (G3-OPT-A, ADR
0079) is irrelevant to a ≥Good threshold count — it only matters when Top must
be distinguished from Strong, which this count never needs to do. A
Testcontainers oracle test
(`CountPerUserByEmployer_EqualsFullBandGradeSsotCardinality...`) pins this
equivalence so a future change to either band cannot silently break it.

**Rejected: Approach 2 — LEFT JOIN `UserJobAdMatch` + `ScoreFullBatchAsync`
compute-on-demand (the issue's original sketch).** The issue as filed proposed
joining the persisted `UserJobAdMatch` table and/or running Full-band scoring
on demand at hub-read time. Rejected: this performs read-time N×M in-memory
Full scoring on every hub page load — an ADR 0045 hot-path budget / CLAUDE.md
§2.5 violation on a $16 VPS — for **zero accuracy gain** over the Fast-band
count, per the exactness proof above. Paying Full-scoring cost to compute a
number the cheap SQL projection already computes exactly is not a tradeoff,
it is waste.

**Not-assessed honesty (Q5).** `MatchingAdCount` is `int?`, not `int`. It is
`null` when the user has stated no occupation (empty SSYK profile) — the
handler skips the count call entirely, in parity with `GetMyMatchCount`'s
existing empty-profile short-circuit. A hard `0` would misreport "no matching
ads" when the true state is "you haven't told us what you do yet"; the FE
renders the profile-completion nudge on `null` instead. Any non-null value,
including `0`, means the count was actually assessed.

**GDPR (extends ADR 0087 D8).** Approach 1 (the accepted mechanism) dissolves
the cross-user JOIN leak the original issue worried about: it reads only
PUBLIC `job_ads` plus the **current** user's own Fast profile
(`BuildFullForSortAsync`, `ICurrentUser`-scoped) — no other user's data is
read at any point. org.nr is used purely as the `GROUP BY` key, server-side,
and is never surfaced to the client or logged (parity with #447's
active-ad-count feature). A cross-user isolation integration test
(`CompanyWatchesMatchCountCrossUserIsolationTests`) pins this. No new PII, no
migration, no AI involvement (ADR 0071).

**Goodhart guard (ADR 0071/§5).** `MatchingAdCount` is a **count of ads
crossing a named grade threshold**, not an opaque match score — the same
shape as #447's `ActiveAdCount`. No numeric match score is surfaced anywhere
in this feature.

**FE scope note.** The hub's "se alla annonser" toggle is purely a frontend
concern — both the matching count and the all-active count are already
present on the DTO; no additional backend work is required to support it.

**D5-addendum-2 — Cross-channel dedup: per-hit `SeenAt` suppresses the follow-email for an ad
already seen in-app (#453; senior-cto-advisor 2026-07-02).**

> **Provenance:** bound by senior-cto-advisor (2026-07-02, 7 executable verdicts) on Klas's
> lanserings-blockerande decision (2026-07-01) "aldrig mejla något jag sett i appen". Klas gave the
> GO for issue #453 on this basis. The advisor decided the substance; CC authors the ADR prose
> (memory `feedback_klas_can_override_adr_verbatim_source`). Does NOT touch ADR 0080 (the match rail
> aggregate is untouched — see the audit #26 note for the one shared job that is extended).

`FollowedCompanyAdHit` gains a nullable `SeenAt` (`DateTimeOffset?`). When the user opens an ad in-app
(the ad-detail page/modal, reached via any surface), a Pending-only `MarkFollowedCompanyAdSeen` command
stamps `SeenAt` on every still-Pending hit for `(currentUser, jobAdId)`. The `DigestDispatchJob`
follow-pass due-set gains `AND SeenAt == null` on BOTH the claim fetch and the displayRows fetch, so a
seen-in-app hit is neither claimed nor emailed.

**The precondition correction (the load-bearing finding).** The issue named an "in-app follow-hit read
surface (part of hub #448)" as a precondition. It does NOT exist — #448 shipped the watched-companies
list, not a per-hit notification surface; the only consumers of `FollowedCompanyAdHit` are the scan
(writer), the digest (email), and the Art.17 cascade. The advisor ruled the feature is still buildable
and meaningful today (Q1 = **A1**): the true in-app surface where a specific ad is "seen" is the
ad-detail page/modal, reached via search/match/saved/shared-link. That IS the cross-channel dedup — the
in-app channel (ad-detail, any route) suppresses the email channel. Rejected: A2 (build the hub hit-list
now — scope creep + makes the dedup same-channel) and A3 (block #453 — the precondition is wrong, not
the feature). **No `followState` gate** on the FE trigger: unfollow soft-deletes only the `CompanyWatch`,
never the hits, so a gate would skip stamping a still-Pending hit for an unfollowed-then-refollowed
employer and the digest would email a seen ad ("never miss" break). The handler's precise Pending-hit
query is the sole correctness authority.

**MarkSeen semantics (Q2).** Pending-only stamp on ALL Pending hits for `(currentUser, jobAdId)`
(the same ad matched via two follows is two hits — the user saw the AD, both are stamped); no-op Success
on Queued/Sent (post-claim: the email decision is already taken — the Sent-dedup prevents repetition);
idempotent first-seen-wins; benign no-op Success (never NotFound) when no hit exists (a NotFound would
leak follow-existence). Returns non-generic `Result` (mutates, creates no id — parity
`MarkMatchesSeenCommand`). Not audited (high-frequency behavioral signal — parity the match seen-mark).

**IDOR-safe shape (Q7).** `MarkFollowedCompanyAdSeenCommand(JobAdId)` — UserId resolves from
`ICurrentUser`, NEVER the wire (a userId parameter would be an IDOR-Major, §5/§12). Route
`POST /api/v1/me/company-watches/ad-hits/{jobAdId:guid}/seen`, auth-gated, 204 (also on no-op). JobAdId
on the wire is fine (public, non-PII — already in `/jobb/{id}` URLs); the raw org.nr is never touched
(SeenAt is a timestamp). A **dedicated `FollowSeenMarkPolicy`** (TokenBucket, UserId-partitioned,
~60/min) rate-limits the endpoint rather than reusing `MeWritePolicy`: the seen-mark auto-fires on every
ad-detail open, so a shared write-bucket would let it starve the user's deliberate Save/Follow (bulkhead
/ least-common-mechanism — the house pattern in `RateLimitingExtensions`; senior-cto-advisor 2026-07-02 (b),
security-auditor-ratified number).

**Rejected: the coarse `LastSeenFollowedAdsAt` watermark.** A time-cursor on the JobSeeker (the shape of
the existing `MarkJobsSeenCommand`/`LastSeenJobsAt`) would silently over-suppress ALL follow-hits older
than the timestamp, including unopened ones — a "never miss" violation. Per-hit `SeenAt` models the true
invariant: only the specific ad the user actually opened is suppressed.

**Concurrency (Q4).** `FollowedCompanyAdHit` maps no xmin token (parity `UserJobAdMatchConfiguration`).
The MarkSeen-vs-digest-claim lost-update race is accepted as a bounded residual: sub-second overlap
during the nightly window, impact = one redundant email, self-healing (the row ends correctly Sent). No
migration for a one-email race. No new index for `seen_at` (the dispatch hot path already scopes via
`ix_followed_company_ad_hits_user_status` to a tiny per-user Pending set; `seen_at IS NULL` is a cheap
residual predicate).

**Audit #26 in-block (stranded-Queued recovery — commit 2, senior-cto-advisor Q5).** The external audit
(2026-07-02, Medium #26, CONFIRMED) found the follow rail re-shipped the exact permanent-invisible-strand
failure class that `StrandedMatchReaperJob` (TD-114) closed on the match rail and that ADR 0080's
prod-Resend flip checklist makes flip-blocking: `DigestDispatchJob`'s follow pass claims Pending→Queued
before the send; on send failure rows stay Queued forever with no `Failed` state, no reaper arm, and the
scan never re-detects the triple. Fixed in-block (NOT a TD — it fails §9.6's phase/dependency test and is
flip-blocking for #183; NOT a separate issue — it self-collides on the shared enum + digest hotspot):
`FollowedCompanyAdHitStatus` gains a `Failed` member (stored by NAME in the existing `varchar(20)` — no
migration); `FollowedCompanyAdHit.MarkFailed()` (Queued→Failed, guarded, terminal); and the existing
`StrandedMatchReaperJob` is EXTENDED with a second arm (`ReapStrandedFollowHitsAsync`, DRY — one shared
mechanism, two aggregate sources; both arms committed by one atomic SaveChanges), NOT a parallel job.
The audit's inverted "aging by CreatedAt is conservative" comment (#37) is truth-fixed in the same file
(it is aggressive, not conservative — the reaper may act earlier than the true strand age, never later).
The match rail aggregate (`UserJobAdMatch`) and its state machine are untouched — only the shared reaper
job is extended, so ADR 0080's aggregate is not modified.

**Note-only (out of #453 scope, audit-tracked).** Retention-Low (`followed_company_ad_hits` +
`user_job_ad_matches` grow monotonically with no retention sweep) and the #37 no-token race remain
epic-checklist items in the external audit report; not raised as TDs here (different change reason /
phase).

**D6 — Employer facet in search.**

Add `Employer` (org.nr multi-value `IReadOnlyList<string>`) to:
- `JobAdFilterCriteria` (SPOT shared by all three consumers)
- `ListJobAdsQuery` / `IJobAdSearchQuery` / `IPerUserJobAdSearchQuery`
- The endpoint

The filter applies as an IN-equality dimension on the D1 `organization_number` column, AND'd
against all other active dimensions — exactly the pattern established in ADR 0067 Beslut 6 for
EmploymentType/WorktimeExtent. `Employer` IS a real persisted search-criteria dimension (part of
the recent-search hash and SavedSearch identity). It is NOT a runtime-context flag like
MatchGrades/SavedOnly (those are not search-criteria dimensions).

Named-argument construction stays mandatory throughout (the filter record now has ~6 list
dimensions — silent positional-mapping is a trap at this scale).

The disambiguation list (DISTINCT org.nr + company_name + municipality + COUNT(*)) is a
**separate** read concern implemented as a dedicated projection query — it must NOT be folded into
`IJobAdSearchQuery`. FE consumption deferred behind issue #408.

**D7 — PR-split (one change reason per PR — ADR 0065 / SoC).**

Five PRs with clean separation of change reasons. Each is an independently mergeable, testable
unit. See Implementation section for scope and gates.

---

## Alternativ som övervägdes

### Alt A — Persisted `Company` aggregate (BUILD.md §5.1 literal interpretation)

**För:** Matches the existing spec; enables richer employer profile data in future.
**Emot:** An anaemic data-holder protecting zero invariant (Fowler AnemicDomainModel). company_name
and normalized_name are derivable projections, not domain invariants. Building the aggregate now
is YAGNI. Evans (2003) prefers natural identity where a stable natural key exists — org.nr is that
key, not a surrogate CompanyId Guid. A persisted Company would be a second employer source-of-truth
(first: raw_payload→job_ads). Spec predates the decision to keep employer identity in job_ads;
reality never built this aggregate.
**Verdict:** Rejected. Read-model projection over job_ads is the correct v1 approach (D2). Flag
BUILD.md §5.1 for amendment.

### Alt B — org.nr as a plain column written at ingest + C# backfill

**För:** Simpler EF mapping; no generated column syntax.
**Emot:** Introduces a write/read drift surface — the plain column can disagree with raw_payload.
Creates a second source of truth for a value that already lives in raw_payload. DRY violation.
The generated column approach (D1 Variant A) is drift-impossible by construction and mirrors the
existing pattern of 6 shadow columns in `JobAdConfiguration`.
**Verdict:** Rejected. STORED generated column (D1 Variant A).

### Alt C — Surrogate CompanyId Guid as follow target key

**För:** Uniform surrogate-key strategy; avoids string-typed foreign references.
**Emot:** Requires a persisted Company aggregate (Alt A, rejected). org.nr is a stable natural
key issued by an authoritative external party (Bolagsverket) — Evans (2003) prefers natural
identity here. A surrogate Guid over a well-known natural key adds indirection without protecting
any additional invariant.
**Verdict:** Rejected. org.nr is the canonical key (D2, D3).

### Alt X — Reuse `UserJobAdMatch` as the notification spine for company follows

**För:** One aggregate; less schema; reuses the existing `DigestDispatchJob` plumbing.
**Emot:** `UserJobAdMatch.Grade` is typed `NotifiableMatchGrade` and pinned by
`UserJobAdMatchGoodhartTests`. A company-follow hit has no skill grade. Mixing concerns corrupts
a deliberately sealed aggregate (SRP, Martin ch. 7). Two paths out — synthetic 4th grade or
source discriminator — both make the aggregate's own invariant false.
**Verdict:** Rejected. New `FollowedCompanyAdHit` aggregate (D5).

### Alt Y — Trigger on ingest event (event-on-ingest)

**För:** Lower latency — notification could fire minutes after a matching ad is ingested.
**Emot:** ADR 0080 Beslut 2's rejection of event-on-ingest transfers verbatim: breaks ADR 0032
per-RUN audit aggregation invariant; risks unbounded N×M queue spike on the $16 VPS; YAGNI
(nightly scan is fast enough for a company-watch feature).
**Verdict:** Rejected. Dedicated `CompanyWatchScanJob` (D5).

### Alt Z — Overload `BackgroundMatchingJob` with company-watch scan logic

**För:** One job; simpler Hangfire registration.
**Emot:** Fuses two independent change reasons into one job. Skill-scoring thresholds and
watched-org.nr-matching change independently — they have different cadence, different failure
modes, and different team mental models. SRP violation.
**Verdict:** Rejected. Dedicated `CompanyWatchScanJob` (D5).

### Alt W — Approach 2: LEFT JOIN `UserJobAdMatch` + `ScoreFullBatchAsync` compute-on-demand
(D5-tillägg, issue #452, 2026-07-01)

**För:** Reuses persisted match rows where they exist; would, in principle, distinguish Top from
Strong (Full-band exact) if the hub ever needed that distinction.
**Emot:** Runs read-time N×M in-memory Full-band scoring on every hub page load — an ADR 0045
hot-path budget / CLAUDE.md §2.5 violation on a $16 VPS. The exactness proof (D5-tillägg) shows
this buys **zero** accuracy gain over the cheap Fast-band SQL count for a ≥Good threshold: CV/skill
evidence only ever elevates a match that already has ≥1 confirmed secondary, so `{grade ≥ Good}`
is identical in both scoring bands. Paying Full-scoring cost for a number the Fast-band SQL
projection already computes exactly is waste, not a tradeoff. Also reintroduces the cross-user
JOIN-shaped surface the issue originally worried about on the GDPR side.
**Verdict:** Rejected. `IPerUserJobAdSearchQuery.CountPerUserByEmployerAsync` — a bounded Fast-band
SQL `GROUP BY` reusing the shared `GradeRankExpression` SSOT (D5-tillägg, Approach 1).

---

## Konsekvenser

### Positiva

- **Natural key — no Volvo×20 trap.** org.nr as the canonical follow key means one legal entity
  = one watch. No fuzzy name matching, no silent merges, no scope explosion.
- **Drift-impossible org.nr column.** The STORED generated column (D1) is derived from
  raw_payload at write time — it cannot diverge from the ground truth. No backfill logic to
  maintain.
- **Clean separation of concerns.** `CompanyWatch` (the intent) and `FollowedCompanyAdHit` (the
  notification delivery record) are separate aggregates. `BackgroundMatchingJob` (skill scoring)
  and `CompanyWatchScanJob` (org.nr membership) have separate responsibilities. `DigestDispatchJob`
  handles email transport for both without conflating their semantics.
- **Goodhart guard preserved.** `FollowedCompanyAdHit` carries no score or grade — company-follow
  notifications are not grade-routed. No new Goodhart surface is introduced.
- **GDPR-compliant consent granularity.** Separate `FollowedCompanyNotificationsEnabled` flag
  means users control skill-match and company-follow notifications independently, as Art. 6/7
  granularity requires.
- **Art. 17 fail-closed from day one.** Both new aggregates are wired into `CascadeMap` and
  `AccountHardDeleter` in the same PRs that introduce them. The cascade fitness test enforces this
  at build time.
- **Incremental PR-split.** PR-1 (column foundation) unlocks PR-2 (facet filter) without waiting
  for the follow/notification work. The feature ships in observable increments.
- **D5-tillägg: one grade authority, read twice, at hot-path cost.** The hub match-count reuses
  the exact `GradeRankExpression` SQL SSOT `/jobb` already uses, so the D5-tillägg adds a hub
  feature with zero new scoring surface, zero new Goodhart risk, and a proven (Testcontainers-pinned)
  guarantee that the hub can never disagree with `/jobb` about what counts as a match — at Fast-band
  SQL cost, not Full-band in-memory scoring cost.

### Negativa

- **B2 backfill reality.** Existing 77,520 rows have NULL org.nr until re-ingest. The employer
  facet (D6) and company-follow scan (D5) work correctly on all rows that have been re-synced;
  older rows are simply excluded. The gap is not an architecture defect — it is the expected B2
  consequence — but it means the employer facet is initially incomplete.
- **Sole-proprietorship org.nr = personnummer (PII — highest priority guard).** Swedish enskild
  firma org.nr can equal the owner's personnummer (10-digit national identity number). The
  personnummer guard in CLAUDE.md §5 is the highest-priority PII rule. Implications:
  - The `CompanyWatchScanJob` and employer-facet query path MUST NOT log `organization_number`
    in plaintext without first checking whether it is a sole-prop org.nr.
  - Surfacing a sole-prop org.nr in the disambiguation list is a PII exposure question that
    requires a security-auditor STOPP before FE is built (deferred behind #408, but designed-in
    from the start).
  - The D1 column itself stores public Platsbanken data encrypted at rest — this does not block
    PR-1 — but the personnummer guard must be implemented in the scan and facet paths.
  - **Security-auditor gate is MANDATORY in PR-3 and PR-4.**
- **Two new aggregates = two Art.17 cascade fixtures.** Both `CompanyWatch` and
  `FollowedCompanyAdHit` must be cascade-wired and tested. The fitness test will catch omissions,
  but the wiring is implementation work that cannot be deferred.
- **DigestDispatchJob extension.** Extending the existing job to handle `FollowedCompanyAdHit`
  rows means the job now has two aggregate sources. Careful SoC within the job implementation is
  required — fetching and dispatching `UserJobAdMatch` rows must remain independent of fetching
  and dispatching `FollowedCompanyAdHit` rows (per-user failure isolation must apply to both).
- **Consent flag proliferation.** Two notification consent flags (`BackgroundMatchNotificationsEnabled`
  and `FollowedCompanyNotificationsEnabled`) in `Preferences` jsonb. This is the correct GDPR
  posture but increases the surface that PR-4's security-auditor gate must verify.
- **FE facet is deferred.** The employer search facet (D6) ships without a FE disambiguation UI
  until #408 is resolved. BE ships; FE blocks on the /jobb redesign.
- **BUILD.md §5.1 spec drift.** The Company AR in §5.1 never matched reality. Until §5.1 is
  amended post-merge, the spec and the code describe different designs. Klas owns this amendment.
- **D5-tillägg: the hub count is Fast-band-only by design, never Top-distinguishing beyond what
  the ≥Good threshold needs.** The exactness proof holds specifically for a ≥Good threshold count;
  it does not claim Fast-band and Full-band agree on Top-vs-Strong classification in general
  (ADR 0079 G3-OPT-A). If a future hub requirement needs to *display* the Top/Strong/Good split
  (not just count ≥Good), that is new scope requiring its own review — not covered by this
  addendum's exactness proof.

---

## Implementation

### Five-PR delivery sequence

| PR | Scope | Gate |
|----|-------|------|
| **PR-1** | D1: `organization_number` field on `JobTechEmployer` POCO + STORED generated column on `job_ads` + partial index WHERE NOT NULL + EF migration + backfill-verification note (existing rows NULL until re-ingest; new/re-synced ads populate immediately) | Migration clean; `dotnet test` green; architecture tests green; code-reviewer + dotnet-architect + db-migration-writer |
| **PR-2** | D6: `Employer` (`IReadOnlyList<string>`) added to `JobAdFilterCriteria` + `ListJobAdsQuery` + `IJobAdSearchQuery` + `IPerUserJobAdSearchQuery` + endpoint + disambiguation projection query (`DISTINCT organization_number + company_name + municipality + COUNT(*)`) as a separate read method | Named-argument construction throughout; search-parity tests green; facet integration test with known org.nr; code-reviewer + dotnet-architect |
| **PR-3** | D3: `CompanyWatch` aggregate + invariants + commands (`FollowCompanyCommand`, `UnfollowCompanyCommand`) + endpoints + Art.17 cascade wiring (`CascadeMap` + `AccountHardDeleter` + `AppDbContext.CompanyWatches`) | `AccountHardDeleteCascadeFitnessTests` green; soft-delete confirmed; security-auditor STOPP on sole-prop org.nr path before merge; code-reviewer + dotnet-architect + security-auditor + test-writer |
| **PR-4** | D5: `FollowedCompanyAdHit` aggregate + `CompanyWatchScanJob` (Hangfire, own watermark, org.nr IN-equality only, no skill scorer) + `FollowedCompanyNotificationsEnabled` consent flag (separate from ADR 0080's flag) + Art.17 cascade wiring for `FollowedCompanyAdHit` + DigestDispatchJob extension for `FollowedCompanyAdHit` rows | `AccountHardDeleteCascadeFitnessTests` green; scan produces `FollowedCompanyAdHit` rows; consent flag gates dispatch; security-auditor MANDATORY (personnummer guard in scan + consent granularity); code-reviewer + dotnet-architect + security-auditor + test-writer |
| **PR-5** | D4 (optional): curated BrandGroup table + `target_type` discriminator extension on `CompanyWatch` | Only if prioritized; no auto-merge logic ever; requires Klas GO |

### Mandatory agent invocations (per CLAUDE.md §9.2)

All PRs: code-reviewer + dotnet-architect (architectural scope).
PR-1: db-migration-writer (new migration).
PR-3: security-auditor (org.nr PII surface — sole-prop guard) + test-writer (new aggregate).
PR-4: security-auditor (MANDATORY — personnummer guard in scan path + two consent flags + DigestDispatchJob extension) + test-writer (new aggregate + scan job).

### D6 implementation split — PR-2 / PR-2b (senior-cto-advisor, 2026-06-30)

D6 was delivered in two sequenced PRs after two CTO binds during the PR-2 session.
D6's *substance* is unchanged — employer IS a persisted search-criteria dimension,
and the disambiguation list IS a separate projection never folded into
`IJobAdSearchQuery`. Only the *PR boundaries* were sequenced.

**PR-2 (shipped) — the live-search FILTER dimension only.** `JobAdFilterCriteria` +
`JobAdSearchComposition.ApplyFilter` IN-equality WHERE on `OrganizationNumber` +
`ListJobAdsQuery`/handler/validator (org.nr 10-digit format) + `?employer=` endpoint
binding. Both the anon and per-user paths inherit the WHERE via the shared
`ApplyFilter` SPOT. **Bind 1 — CONTAINED scope:** the Domain `SearchCriteria` VO,
`RecentJobSearch`, `SavedSearch`, `FilterHashCalculator`, and `ICapturesRecentSearch`
are NOT touched in PR-2; `RunSavedSearchQueryHandler` and `ListRecentSearchesQueryHandler`
pass `Employer: []`. Rationale: SoC/one-change-reason-per-PR (the persistence threading
needs a `recent_job_searches` migration + Domain-VO change — a distinct change reason
from the read-side filter), YAGNI (no consumer until #408 — the same axis D2 invokes
for the Company aggregate), and migration-hotspot discipline (PR-2 stays migration-free
and parallel-safe). **Bind 2 — no `FacetDimension.Employer`, surfacing deferred
(Variant B):** `FacetDimension.Employer` is NOT added — D6's "must NOT be folded into
`IJobAdSearchQuery`" is binding, and the facet port is taxonomy-label-unaware whereas
employer names live on `job_ads` (`JobAd.Company.Name`). PR-2 surfaces ZERO org.nr:
`JobAdDto` carries no org.nr (filter is input-only) and `LoggingBehavior` logs the
message type name only. The start-prompt's "(anon + FacetDimension)" is superseded by
this ADR (SSOT-over-prompt, CLAUDE.md §1.5).

**PR-2b (deferred, issue #415, blocked on #408)** — carries BOTH deferrals, landing
with the #408 FE consumer that exercises them: (1) the persisted search-identity
threading (SearchCriteria VO + `RecentJobSearch.employer_list text[]` migration +
FilterHash format-bump + SavedSearch jsonb back-compat), and (2) the org.nr-surfacing
disambiguation projection query + DTO + endpoint, the sole-prop **personnummer guard**
(CLAUDE.md §5, highest priority — a Swedish enskild-firma org.nr can equal a
personnummer), and the **MANDATORY security-auditor gate**. PR-2b therefore takes the
`hotspot:ef-migration` single-owner token and the §12 PII-surfacing class; PR-2 did not.

### D8 — org.nr at-rest posture (GDPR Art. 32) (senior-cto-advisor 2026-06-30; Klas Art. 32 risk-accept 2026-06-30)

**Trigger:** the PR-2 security-auditor escalated that `organization_number` is a plaintext
STORED generated column (`JobAdConfiguration.cs` `HasComputedColumnSql(..., stored: true)`
over plaintext `raw_payload`), so for a sole proprietorship (enskild firma) where org.nr ==
the owner's personnummer, this is **plaintext personnummer at rest** (Art. 32) — and the
inline comment falsely claimed "krypterad at-rest". This posture governs PR-1 (shipped), PR-3
(`company_watches`), and PR-2b (surfacing).

**Decision — split accept-risk, guard at the surfacing/logging boundary:**

- **(a) Public `job_ads.organization_number` (generated column, PR-1):** plaintext at rest,
  **accept-risk**. DEK is technically impossible on a generated column and pointless on
  already-public data (the employer published org.nr on the ad; same plaintext precedent as
  `raw_payload`, ADR 0032 §8 — PII *stripped* at ingest, public fields incl. org.nr retained).
  > ⚠ **AMENDED 2026-07-13 (#842) — the second supporting pillar of this bullet is WITHDRAWN.** The
  > clause *"same plaintext precedent as `raw_payload`, ADR 0032 §8 — PII stripped at ingest"* **is
  > false.** **D8(a)'s accept-risk stands** on its first pillar (org.nr is genuinely public employer
  > data), and is **not re-opened**. See the amendment immediately below D8.
- **(b) User-owned `company_watches.organization_number` (PR-3):** plaintext **because it
  must be equality/IN-queryable** by `CompanyWatchScanJob` (DEK breaks SQL IN — the
  `ef_strongly_typed_vo_contains` trap; blind-index/in-memory-scan cost is unjustified on
  public data + a $16 VPS). Its protection is **owner-scoped access + Art. 17 cascade
  erasability** (already mandated in D3), NOT encryption. The user-owned column carries a
  distinct processing context (it reveals *whom a user watches* — personal data about the
  user), which the access-control + erasability story covers.
- **(c) Surfacing/display/log paths (PR-2b/PR-3/PR-4):** the **personnummer guard lives here**
  — sole-prop (personnummer-shaped) org.nr is flagged/masked/excluded in any display
  projection, and org.nr is **never logged un-flagged** (CLAUDE.md §5). Safe default on
  detection-uncertainty: treat-as-sensitive (flag/mask), never surface. Verified by the
  MANDATORY security-auditor gate in PR-2b/PR-3/PR-4 (a sole-prop Blocker = §12 STOPP).

**Rejected:** Option B (exclude/transform personnummer-form org.nr from the queryable column —
feature gap: enskild-firma employers unfollowable/unfilterable; heuristic is imperfect so it
cannot be the primary guard; protects the wrong layer). DEK on (a) (impossible) or (b) (breaks
queryability; deterministic hash of a 10-digit space is weak). The DEK envelope (ADR 0049/0066)
stays reserved for high-sensitivity user-authored PII (CV/cover-letter content); public ingest
org.nr is not that class.

**Klas Art. 32 risk-accept (2026-06-30):** accepts that sole-prop org.nr (= personnummer) is
stored plaintext at rest in both columns with no at-rest encryption, justified by public-source
nature + the `raw_payload` plaintext precedent + queryability necessity, with the protection
concentrated at the display/log boundary.

**In-block comment corrections (factually-false, must be fixed):** `JobAdConfiguration.cs` has
TWO stale at-rest-encryption claims — the org.nr "krypterad at-rest" line, and the `raw_payload`
"Encryption-at-rest täcks av AWS RDS KMS" line (AWS retired, ADR 0066). Both corrected to state
plaintext-at-rest with ingest PII-stripping and the D8 surfacing-boundary guard; do not resurrect
TD-13 envelope-encryption for this public data class.

### D8(a) — amendment 2026-07-13 (#842): the "PII stripped at ingest" pillar is WITHDRAWN

**Scope of this amendment, stated first so it is not over-read: D8(a) is NOT re-opened and NOT
re-decided.** One of its two supporting pillars is false and is withdrawn from the reasoning; the
decision it supported survives on the other. This is the application of a withdrawal **already drafted
verbatim** in ADR 0032's amendment A3 (`0032:1119-1122`) and corroborated by security-auditor
(`docs/reviews/2026-07-12-824-dpia-archived-ad-security.md:180-183`). **Transcription, not judgement.**

**The withdrawal (verbatim from ADR 0032 A3):**

> **Knock-on:** ADR 0087 D8(a) justifies plaintext org.nr partly on *"raw_payload är redan plaintext,
> ADR 0032 §8 — PII stripped at ingest"*. **That supporting pillar is false.** D8(a)'s accept-risk
> survives on its own merits (org.nr is genuinely public employer data), but the false pillar is
> withdrawn from the reasoning rather than silently retained.

**Why the pillar is false.** D8(a) leaned on `raw_payload` as a *precedent*: PII is stripped at ingest,
so plaintext storage of the derived org.nr inherits an already-clean baseline. **The baseline was never
clean.** `JobTechPayloadSanitizer` is a **default-deny allowlist over jsonb KEYS that never examines a
value**, and it **deliberately retains every free-text key** (`description`, `description_text`, `text`,
`company_information`, `needs`, `requirements`, `salary_description`). **It strips the FIELD, not the
ADDRESS.** Recruiter contact PII in the ad body was never a gap in that design — **it IS the design**, and
`job_ads.description` stores the ad body verbatim in plaintext, full-text searchable, with no purge and
(until #842) no erasure path. *(Full evidence: `docs/research/2026-07-13-842-erasure-evidence-pack.md`;
contract: `docs/reviews/2026-07-13-842-erasure-contract-cto.md`; ADR 0106.)*

**Why D8(a)'s accept-risk nevertheless stands, and is not disturbed here.** The first pillar is
independent of the false one and carries the decision by itself: **the org.nr on a job ad is genuinely
public employer data** — the employer published it in the ad, and Arbetsförmedlingen publishes it. The two
operative constraints D8 rested on are also untouched by #842: **DEK remains technically impossible on a
STORED generated column**, and **the protection remains concentrated at the surfacing/logging boundary**
(D8(c) — the personnummer guard, which is where the sole-prop risk actually lives and which is unaffected
by anything in #842). Nothing in #842 makes org.nr *less* public or the surfacing guard *less* effective.
**Removing a false argument for a sound conclusion does not weaken the conclusion; it stops the conclusion
from being defended with a claim that will not survive an audit.**

**What this does NOT do:**
- It does **not** re-open D8(a), D8(b) or D8(c), and it does **not** disturb Klas's Art. 32 risk-accept of
  2026-06-30.
- It does **not** resurrect TD-13 / envelope encryption for this public data class.
- It does **not** touch ADR 0090's B-masked pnr posture (C1–C6 unchanged) or Part C's legal-entities-only
  register.

**One inherited clause to watch, flagged not fixed here.** D8's closing paragraph ("In-block comment
corrections") mandates that the in-code comments state *"plaintext-at-rest with **ingest PII-stripping**
and the D8 surfacing-boundary guard"*. **That phrasing carries the same withdrawn pillar** and must not be
read as an independent authority for it. Correcting the in-code comment is a truth-sync item for the #842
source-doc pass (tracked, PR-scoped); **it is flagged here, not silently re-decided.**

**Cross-ref:** ADR 0032 §8 + amendments A2/A3 · ADR 0106 (recruiter-PII erasure contract: ingest
minimisation + provable record removal) · DPIA #456 §8-B (risk register R-842-1…7) ·
`docs/runbooks/gdpr-processing-register.md` (Art. 30, JobTech entry) · issues #842 (launch-gate), #845,
#824, #841.

### D8(b) — amendment 2026-07-18 (#544): pnr-shaped `company_watches` org.nr moves to HMAC at rest

**Scope: D8(b) is superseded ONLY for the personnummer-shaped subset; D8(a) and D8(c) are untouched.**
ADR 0090 §D5 (Accepted 2026-07-03, Klas Art. 32 risk-accept) partially supersedes this D8(b): a
personnummer-shaped (enskild-firma) `company_watches.organization_number` — which *equals* the owner's
personnummer — is no longer stored plaintext but as a keyed **HMAC-SHA256(server-pepper) token** at
rest (a strict improvement, not a weakening: the plaintext-personnummer-at-rest exposure is removed). A
legal-entity (AB) org.nr stays plaintext (public data). **#544 is the implementation** of ADR 0090
D5's deferred migration + scan-matching fork — see ADR 0090 Implementation-(a) "RESOLVED 2026-07-18"
for the bound mechanism (Mechanism 1, hybrid pnr-only), the separate permanent (R1) pepper, the
guarded backfill, and the read-side resolution.

D8(b)'s original **queryability** rationale still holds and is exactly what the keyed HMAC preserves
(deterministic → equality/IN-matchable); its "deterministic hash of a 10-digit space is weak"
rejection concerned an *unkeyed* hash and is resolved by the server pepper (the ADR 0090 house
primitive). The in-code comment on `CompanyWatchConfiguration` is truth-synced in #544. This amendment
does **not** disturb Klas's 2026-06-30 Art. 32 accept for the AB subset, nor D8(a) (public `job_ads`
plaintext) or D8(c) (the surfacing/log guard — it still fires on a token via the length≠10 fail-safe).

**Cross-ref:** ADR 0090 §D5 + Implementation-(a) (governing supersession + the resolved fork); #544.

### D8(c) — amendment 2026-09-05 (#1471): `recent_job_searches.employer_list` moves from the EXCLUDED arm to the MASKED arm

**Scope: D8(c)'s three arms are unchanged and D8(a)/(b) are untouched; this records which arm ONE
surface uses.** D8(c) offers *flagged, masked OR excluded* for any display projection. For the
recent-search projection (`RecentJobSearchDto`, ADR 0060) the arm that shipped in #311 PR-2b was
**excluded** — the employer axis was persisted and counted but never projected — and
`RecentJobSearchProjectionParityTests.NotSurfaced["Employer"]` recorded the cost verbatim: *a captured
employer search replays broader than it was*. #1471 measured that cost as a user-facing defect (the
row's count is employer-filtered, the click is not; `recent-searches-hero-chip.tsx` on `/jobb` is one of
three consumers) and `senior-cto-advisor` chose the **masked** arm
(`docs/reviews/2026-09-05-1471-cto.md`, arm (B); arms (A) count-without-employer and (C) stop-capturing
rejected there).

**Decision.** `RecentJobSearchDto` carries `EmployerList` (org.nr per value). The handler builds ONE
`JobAdFilterCriteria` per row and every consumer — `CountAsync`, the projection the replay href is
built from, and the label — reads that object, so count, click and label rest on the same criterion
by construction. The employer values pass `EmployerAxisGate.Surfaceable`: a personnummer-shaped or
unparseable value is withheld from all three. That predicate is the SAME one the write side has
refused on since A2 (Klas-beslut 2026-08-19; the method moved from `RecentJobSearchCaptureBehavior`
into `EmployerAxisGate` — one home, #844), so what the capture gate refuses on the way in has no
second route to the wire on the way out, including rows written before A2, which can still carry such
a value (LRU-capped, never purged). The label names the axis value-free
(`RecentSearchLabelPartKind.Employer`, Klas-beslut 2026-08-23, delivered here because the replay now
carries the axis — #1469 arm (a) struck it until then) and
`Handle_NeverPutsTheEmployerOrgNumberInTheLabel` pins that the value never reaches it.

**Why this is not a new acceptance.** After the mask the wire carries only non-personnummer-shaped
org.nr, on the URL form `buildCompanyJobsHref`
has emitted since #1546/#1547 and under a key the edge log already scrubs (`edge-log-verdicts.ts`).
No data subject's position changes, no grade is withdrawn, and Klas's Art. 32 accept (2026-06-30) is
not touched. The masked arm's residual: a pre-A2 row whose only employer was personnummer-shaped counts
and replays as an employer-less search — count == replay still holds for it, and the withheld value has
no lawful presentation. Purging such rows is a separate change-reason and is not filed on speculation
(CLAUDE.md §9.6 filing discipline: a dated non-zero measurement by a stack-owning session is the ground
for that issue).

**Gate.** `security-auditor` sat on #1471's panel because org.nr reaches a new wire surface; her
verdict on that PR is the D8(c) gate this section names — recorded in `docs/reviews/2026-09-05-1471-security-auditor.md`
and in the PR's verdict table, not restated here. Pins: `RecentJobSearchProjectionParityTests` (every
dimension projected; the `NotSurfaced` list is retired), `ListRecentSearchesCountReplayParityTests`
(counted values == projected values, mask included), `RecentSearchesTests` (org.nr on the wire under
`employerList`, never inside `label`).

**Cross-ref:** #1471; #1469 (arm (a)); #1584 (employer as a list axis in FE); #1546/#1547
(`buildCompanyJobsHref`); #844; ADR 0060; ADR 0062.


### D5-tillägg implementation (issue #452, 2026-07-01)

| Scope | Detail | Gate |
|-------|--------|------|
| `IPerUserJobAdSearchQuery.CountPerUserByEmployerAsync` | New port method: `(orgNrs, profile, grades, ct) → IReadOnlyDictionary<string,int>`, one `GROUP BY organization_number` over PUBLIC Active `job_ads`, reusing the shared `GradeRankExpression` SSOT | Infrastructure-owned (`PerUserJobAdSearchQuery`), same SSOT as `SearchPerUserAsync`/`CountPerUserAsync` |
| `ListCompanyWatchesQueryHandler` | Injects `IMatchProfileBuilder` + the new port method, parity `GetMyMatchCountQueryHandler`; skips the count call on an empty SSYK profile (`MatchingAdCount = null`) | Handler unit tests updated |
| `CompanyWatchDto.MatchingAdCount` | New `int?` field — `null` = not assessed (no stated occupation), non-null (incl. `0`) = assessed | DTO/serialization test |
| Exactness oracle | `CompanyWatchMatchCountTests`: `CountPerUserByEmployer_EqualsFullBandGradeSsotCardinality...` — Testcontainers, proves Fast-band ≥Good count == Full-band ≥Good count | Must stay green; a change to either scoring band that breaks this pins a real regression |
| Cross-user isolation | `CompanyWatchesMatchCountCrossUserIsolationTests` — proves the count never leaks another user's profile/matches | security-auditor-relevant; no cross-user JOIN exists |
| API surface | `CompanyWatchesMatchCountApiTests` — endpoint-level parity check | code-reviewer + dotnet-architect |

No migration. No new aggregate. No change to `CompanyWatchScanJob` or `FollowedCompanyAdHit`.

### D4 — implementation amendment 2026-07-18 (#311 PR-5): curated BrandGroup shipped

> **Provenance:** bound by senior-cto-advisor (2026-07-18, binding, ahead of code) as part of the
> #311 Företagsbevakning epic's PR-5. Klas gave the GO for PR-5 on this basis. The advisor decided
> the substance; CC authors the ADR prose (memory `feedback_klas_can_override_adr_verbatim_source`).

**Scope: amend, not supersede.** D4's INTENT — a finite, manually curated org.nr → group mapping,
automatic name-matching categorically rejected, forever, the Volvo×20 trap held closed — is fully
honoured and NOT re-opened by this amendment. Only the MECHANISM is pinned below; D4's own prose
above stands unmodified.

**D1 — catalogue mechanism: embedded versioned JSON, not a relational DB table.** D4's "curated ...
table" is read as a lookup table in the data-structure sense, not a literal relational table.
Shipped as `brand-groups.v1.json` embedded in Infrastructure → a fail-loud `BrandGroupLoader` → an
immutable `BrandGroupCatalog` behind an `IBrandGroupProvider` port (Application) → an eager INSTANCE
singleton (a malformed asset fails host build, not a request). Grounds: the scan matches member
org.nrs via a SQL `IN` over a parameter list, no JOIN — a relational table buys nothing the
in-memory catalogue does not (YAGNI, Fowler speculative generality). Curation happens ONLY via a
reviewed PR — git history is the audit trail; there is no admin-CRUD runtime mutation surface, which
satisfies D4's "manually curated, NO automatic merging" more strongly than a form would. The shipped
catalogue ships EMPTY: an empty catalogue is a legal state, and the first real group is a separate
Klas-authored data-PR with §9.5-verified AB org.nrs.

**D2 — model: XOR on `CompanyWatch`, not a reused column, not a separate aggregate.**
`CompanyWatchTargetType` gains `BrandGroup` (stored by name, `varchar(20)`); `CompanyWatch` gains a
nullable `BrandGroupId` (a slug VO) and `OrganizationNumber` becomes nullable. A TargetType-
discriminated XOR is enforced in the factories (`Follow` / `FollowBrandGroup`) — house style:
aggregate invariant + two disjoint partial-unique indexes, not a DB CHECK. **Rejected:** reusing the
org.nr column for slugs (primitive obsession, §5 — the `IsPersonnummerShaped` fail-safe would
misclassify a slug as pnr-shaped); a separate `BrandGroupWatch` aggregate (duplicates the rail and
would need its own Art.17 cascade entry, where a group watch instead reuses the existing per-UserId
`CompanyWatch` cascade). Migration `AddCompanyWatchBrandGroupTarget` — additive/widening, no RAISE
guard.

**D3 — scan: app-side group expansion into a multimap.** The scan branches on `TargetType` first (a
group watch carries null org.nr); a group expands via the catalogue into the plaintext AB `IN`-list
only (members are all AB — see D4-pnr below). `abWatchByOrgNr` became a **multimap** (org.nr → list
of watches): a member followed both directly and via a group yields two honest hits under
`UNIQUE(UserId, JobAdId, CompanyWatchId)` — a single-valued dict would silently drop one.
**D3c (accepted trade-off — DISCHARGED 2026-09-01, see the amendment below):** an EMPLOYER + BRAND_GROUP collision on the same ad double-lists in the
follow-digest — two honest, independently-dispatched rows, the same policy the rail already applies
to employer+employer collisions. A digest-level group-by-JobAdId dedup is a future digest-rail
concern, not a TD.

**D3c — amendment 2026-09-01 (#1612): the deferred dedup is DISCHARGED.**

D3c deferred a digest-level collapse in its own last sentence ("a future digest-rail concern"). That
work is now delivered: the follow-digest collapses its display rows to distinct ads, so an ad reached
through two of a user's watches is listed once and counted once. **This is D3c maturing, not a
reversal** — the acceptance scheduled its own removal in the sentence that granted it.

Two measurements decided the timing rather than the principle:

- **D3c's stated ground had lapsed.** It rested on "the same policy the rail already applies", and
  #1576 (`268d5617`) changed that policy: `NewFollowedCompanyAdSet.CollapseToAds` made the /oversikt
  count and its destination count ADS, on the ground `MarkFollowedCompanyAdSeenCommandHandler` had
  already settled — the user-facing unit is the ad.
- **The delivered Swedish copy already asserted the ad unit.** `EmailTemplates.cs` renders
  "{TotalCount} nya annonser"; only the number was still counting hit rows.

**Unchanged by this:** the UNIQUE key, D3's multimap, and the claim spine — the dispatcher still
stamps `MarkQueued`/`MarkSent` on EVERY hit row of a collapsed ad, or the ad would resurface through
its other watch on the next run. The storage unit stays the hit row; only the DISPLAY unit changed.

⚠ **Measured 2026-09-01:** the shipped catalogue is empty (`brand-groups.v1.json` -> `"groups": []`)
and the two active-partial UNIQUEs on `company_watches` cap a user at one active watch per target, so
no user could reach the double-listing in delivered data. The defect was latent and would have armed
with the first curated group (D1). It is fixed ahead of that data-PR, not after it.


**D4-pnr — personnummer-shaped members are forbidden in a group (loader fail-loud).** A brand group
is a set of legal entities (AB); the loader rejects any `IsPersonnummerShaped()` member at host
build. This keeps the whole group path out of #544's HMAC/enskild complexity and makes it impossible
to commit a personnummer to the repo via the catalogue.

**D5 — BE-only.** Write path: `FollowBrandGroupCommand` (existence-in-catalogue → NotFound in the
handler; format → validator) + endpoint `POST /api/v1/me/company-watches/brand-group`; unfollow
rides the existing `DELETE /{id}`. The list is target-aware — the group name comes from the
CATALOGUE, never from `job_ads` — and #447/#452's counts sum over distinct members. **D5e:** a
latent #544 token-blindness in `GetCompanyWatchStatusBatchQueryHandler` was closed in-block: it now
tokenises the ad's org.nr and probes the enskild map, with direct-employer-beats-group precedence,
and was added to the org.nr log-boundary scan. FE (a `/foretag` group section + a public group-list
read endpoint) is a follow-up PR.

**Cross-ref:** senior-cto-advisor bind 2026-07-18; issue #311; D4/D5 above; D8(b) amendment (#544,
HMAC-at-rest — the reason group members must stay AB-only, D4-pnr).

---

## Referencer

- ADR 0080 — Background matching (notification backbone; DigestDispatchJob reused; event-on-ingest
  rejection transfers verbatim; Art.17 cascade pattern)
- ADR 0067 — Platsbanken search parity (STORED generated column pattern; IJobAdSearchQuery
  extension shape; Klass 1/B2 dimension promotion; employer-facet follows Beslut 6 exactly)
- ADR 0039 / 0062 — SavedSearch / filter-SPOT / IJobAdSearchQuery SPOT (filter-criteria SPOT
  shared by three consumers)
- ADR 0024 / 0374 — Art.17 hard-delete cascade (two new aggregates must be cascade-wired;
  fail-closed fitness test)
- ADR 0071 / 0076 — No-AI / Goodhart guard (inherited; `FollowedCompanyAdHit` carries no score;
  D5-tillägg's `MatchingAdCount` is a named-threshold count, not an opaque score)
- ADR 0079 — Fast/Full grade SSOT and `GradeRankExpression` (D5-tillägg's hub count reuses this
  exact SQL authority; the exactness proof rests on Fast/Full band equivalence at the ≥Good
  threshold; Top's SQL-incomputability, G3-OPT-A, is orthogonal to a ≥Good count)
- ADR 0045 — Hot-path latency budget (ground for rejecting Approach 2's read-time Full-band
  compute-on-demand scoring in the D5-tillägg)
- ADR 0032 — JobTech integration / ingest audit (per-RUN audit invariant; event-on-ingest
  rejected on this basis)
- ADR 0011 — Strongly-typed IDs (`CompanyWatchId`, `FollowedCompanyAdHitId`)
- ADR 0023 / 0010 — Worker HTTP-free composition root (`CompanyWatchScanJob` lives here)
- ADR 0058 / 0059 — Plaintext projections / soft-delete isolation (`JobAdId` no-FK rationale in
  `FollowedCompanyAdHit`)
- Evans, E. (2003). *Domain-Driven Design: Tackling Complexity in the Heart of Software.*
  (Natural identity preference where a stable natural key exists)
- Fowler, M. AnemicDomainModel. (anaemic aggregate rejection — D2)
- Martin, R. C. (2017). *Clean Architecture: A Craftsman's Guide to Software Structure and Design.*
  Ch. 7 (SRP — `UserJobAdMatch` reuse rejection); Ch. 34 (YAGNI — Company aggregate deferral)

## Amendment pointer 2026-10-10 (#2094) — the follow mail opens its new ads

D5's notification mail links `/foretag/bevakade/nya`, the page of new ads from followed companies, instead of
`/jobb`, which showed the whole market and, as a login `next`, resolved to `/oversikt`. That page acknowledges its
rail set on arrival, so a user who opened it after the scan sees its empty state, which names where the ads are.


## Amendment pointer 2026-10-11 (#2123) — D8: a validated personnummer inside `company_name` is masked at import

**What D8 refused.** D8 (Option B, Rejected) refused to exclude or reshape a value on `OrganizationNumber.IsPersonnummerShaped()`:
a shape test that is over-inclusive on purpose (`FromTrusted("Karlsson").IsPersonnummerShaped()` is `true`), imperfect as a primary
guard, and applied to the queryable `organization_number` column that sole-trader employers must stay followable through. D8(c)
put the personnummer guard at the surfacing boundary instead.

**What this does, and why it is a different act.** `JobAd.ApplyContactRedaction`, which every Import and UpdateFromSource reaches,
now runs `PersonnummerRedactor` over `Company.Name` as well as Title and Description. It acts on a **token that
`Personnummer.TryParse` validates** (month, day, Luhn), never on the name or on a shape test. The rest of the name is kept and the
mask preserves length. An ordinary name, an organisation number (its third and fourth digits form a number of 20 or more, so the
month check fails) and a Luhn-invalid run are left exactly as they were
(`A_company_name_without_a_personnummer_is_left_exactly_as_it_was`). It is a masking in the sense of D8(c), applied once at the
write instead of once per projection, because the public job page (ADR 0160, 0165) prints the stored value as it is. An enskild
firma's name is a person's name (`Company.Erased`), and a personnummer typed into it is that person's.

**What it does not touch.** The queryable `organization_number` column, D8(a)/(b)/(c) and Klas's Art. 32 accept are unchanged.
`url` is not masked: the link would stop working, and whether it needs anything else is `security-auditor`'s to decide on #2131.
`raw_payload` is not a read surface (ADR 0106 D5 amendment 2026-10-11). A manually created ad is user-authored text and is never
rewritten (CLAUDE.md §5).

**Stored rows heal without a backfill.** The nightly snapshot upserts every record through `UpdateFromSource`, which reaches
`ApplyContactRedaction`, so a name stored before this change is masked on the next run. That is pinned through the production
write path against Postgres by `A_company_name_stored_before_2123_is_masked_by_the_next_nightly_resync`, with the pre-change row
produced by the actor that wrote it (the importer before #2123, which stored `employer.name` verbatim). The one-off
`ApplyContactScrubBackfill` probe counts the name too, so probe and write path stay one detector.

**Cross-ref:** ADR 0134 amendment 2026-10-11 (2) (the separator forms the same detector now masks), ADR 0106 D4/D5, ADR 0160 and
0165 (the public page), #2123, #2131, #841.
