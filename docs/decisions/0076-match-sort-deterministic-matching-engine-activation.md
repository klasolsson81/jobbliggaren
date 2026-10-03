# ADR 0076 — Match-sort: activate the deterministic matching engine (CV→ad match tag + sort)

**Date:** 2026-06-19
**Status:** Accepted
**Decision-makers:** Klas Olsson (product pivot 2026-06-19, AskUserQuestion GO);
senior-cto-advisor (mechanism rulings, Goodhart guard, surface wiring,
preference SSOT scoping — 2026-06-19)
**Related:** ADR 0040 (smart CV-derived saved search — superseded by this ADR;
see below), ADR 0042 Beslut F (multi-value criteria), ADR 0045 (performance
budgets — Klass (a) 300 ms p95; inherited by F4-13/14),
ADR 0053 Beslut 5 (match presentation in job modal — amended by this ADR;
see below), ADR 0060 (SavedSearch hidden on FE — explains why smart-filter-as-
SavedSearch had no FE home), ADR 0063 Beslut a/b (batch-overlay pattern;
F4-13 reuses this pattern), ADR 0071 (no AI/LLM; Goodhart guard origin),
ADR 0074 (Fas 4 design & sequence — sequence-amended by this ADR; see below),
ADR 0075 (ExtractedTerms + skill index; F4-15 skill-resolver reuses inverted
skill index), BUILD §8 (deterministic CV and matching engines),
CLAUDE.md §5 (deterministic engines anti-pattern block)

> **Lifecycle note:** Written 2026-06-19 by Claude Code (adr-keeper) on
> explicit Klas directive and AskUserQuestion GO. Decision substance
> transcribed from senior-cto-advisor rulings (2026-06-19) + Klas product
> pivot — no decisions constructed. Accepted 2026-06-19 (Klas explicit GO).
> Sanctions CC authoring the ADR prose per memory
> `feedback_klas_can_override_adr_verbatim_source` (Klas override of CLAUDE.md
> §9.4 webb-Claude-verbatim convention; grounded in senior-CTO-advisor verdict).

---

## Context

The CV→job matching engine (F4-5 Fast, F4-6 Full) was delivered but DARK —
port-only, zero production consumers, `CandidateMatchProfile` built only in
tests. The engine was sequenced last in Fas 4 (F4-5/F4-6) precisely to give
the CV-review foundations time to mature, but it was never wired to any surface
that a user could see.

The planned feature that would have consumed matching was ADR 0040's
"smart CV-derived saved search" — derive SSYK from CV → confirm → create
`SavedSearch`. However, ADR 0060 hid the `SavedSearch` surface on the FE, and
the Worker notification consumer for saved searches is also dormant. The
smart-filter concept therefore had no live FE home.

**Klas product pivot 2026-06-19:** "light up matching" directly — a user's
primary CV + job preferences → a per-ad MATCH TAG shown on the job list +
a "sort by match" option. This delivers the same core user value (CV→relevant
jobs) without requiring a `SavedSearch` surface. The smart-CV-filter approach
(ADR 0040) is superseded by this pivot: the feature boundary has changed
fundamentally (derive→confirm→SavedSearch → per-ad match-tag + sort).

The existing matching engine, `ExtractedTerms` index (ADR 0075), and
batch-overlay pattern (ADR 0063) are the implementation substrate; this ADR
defines how to wire them into a visible product surface across five STEGs.

---

## Decision

Activate the deterministic matching engine as a per-ad MATCH TAG on the job
list and a "sort by match" ordering, implemented across five sequenced STEGs
(F4-12 to F4-16) inserted into the Fas 4 sequence per the ADR 0074 sequence-
amendment rule (see Amendment section below). No AI/LLM (ADR 0071/0074).

### Bound design decisions (CTO rulings 2026-06-19)

**Decision 1 — Primary CV, single-track.**
Match sorts against `JobSeeker.PrimaryResumeId`. There is one job list and
one ordering — no per-CV track switching in v1. Users with multiple CVs see
matching for their designated primary CV only.

**Decision 2 — SSYK is compute-on-demand from the primary CV.**
Confirmed SSYK is computed on demand from the primary CV (mirroring the
F4-9 compute-on-demand precedent from ADR 0074 Amendment 2026-06-15). No
new SSYK-persistence column is added now. A future LRM may introduce persisted
`ConfirmedOccupation` and a re-derivation-on-CV-change trigger — this is a
forward-note, not a TD.

**Decision 3 — Job preferences: new SSOT required in F4-12.**
Region and employment-type preferences need a new job-preference SSOT. The
existing `Preferences` value object on `JobSeeker` carries no region or
employment fields. A dedicated preferences extension (scoped to F4-12) is
a prerequisite for F4-13/14, not a later enrichment. Empty preferences →
those dimensions are honestly reported as NotAssessed, never faked.

**Decision 4 — Goodhart guard end-to-end.**
The sort-key computed in F4-14 lives ONLY in the Infrastructure sort path:
- It is never returned in any DTO.
- It is never persisted.
- It is never rendered to the user.
- An architecture test pins that no numeric total field exists on
  `MatchScore`/`FullMatchScore`.

The user always sees WHY: matched/missing keywords per dimension.
This mirrors the load-bearing declaration-order ranking in F4-3's
`OccupationMatchKind` — opaque scores are excluded by design.

**Decision 5 — Separate batch-overlay, never DTO enrichment.**
The per-ad match tag is delivered via a dedicated batch-overlay port (parity
with `isSaved`/`isApplied` per ADR 0063 Beslut b, which already names
"match-score per annons Fas 4"). `IJobAdSearchQuery` (ADR 0039 Beslut 1,
shared with `RunSavedSearch`) is NOT enriched — it must remain per-user-clean
and anonymously cacheable (SPOT).

**Decision 6 — Skill gap: Full tier deferred to F4-15 behind a skill-resolver port.**
CV skills are free-text (`Resume.Skill`); ad skills are JobTech concept-ids
(`ExtractedTerm.ConceptId`). Full tier (F4-15) is built last, behind a
dedicated skill-resolver port that reuses the existing inverted skill index
(ADR 0075). No parallel resolver is created.

**Decision 7 — Broad/unsure searchers get honest NotAssessed.**
No occupation set → honest NotAssessed for the SSYK dimension; falls back to
existing sort. Matching is never faked for users without a confirmed occupation.

---

## The five-STEG sequence: F4-12 to F4-16

These STEGs are inserted into the Fas 4 sequence immediately after F4-11
(per the ADR 0074 sequence-amendment rule).

| STEG | Name | Size | Gate / acceptance criteria |
|------|------|------|---------------------------|
| **F4-12** | Match-preference SSOT + profile-builder port (+onboarding for region/employment) | Medium | A logged-in user with a primary CV + confirmed preferences can produce a `CandidateMatchProfile` via an Application port, without reading raw CV-PII outside the DEK pipeline. |
| **F4-13** | Page-scoped Fast match-tag via batch-overlay | Medium | /jobb list shows an explainable category-primary match tag per card; ONE batch call per page; zero N+1; `JobAdDto` untouched. |
| **F4-14** | "Sortera efter matchning" via internal sort-key | Small | User can sort /jobb by match; order derived from dimension verdicts; no 0–100 score exposed or persisted; architecture test confirms no numeric total on `MatchScore`. |
| **F4-15** | Skill-resolver port + Full-tier upgrade | Medium | Tag/sort include skill-overlap + must-have/nice-to-have for CVs with resolved skill concept-ids; CVs without resolved skills degrade to Fast (NotAssessed), never NoMatch. |
| **F4-16** | ADR 0053 Beslut 5 redesign — match presentation in job modal | Small | Modal shows category-primary match (verdict + matched/missing per dimension); never "92% match"; never a percentage ring. |

---

## Alternatives considered

### Alt A — Deliver smart-CV-filter-as-SavedSearch (ADR 0040 original plan)

**For:** Already designed; derives occupation from CV → confirm → creates a
`SavedSearch` with SSYK filter.
**Against:** ADR 0060 hid the `SavedSearch` surface on the FE. The Worker
notification consumer for `SavedSearch` is dormant. The feature would land with
no visible FE home and no end-to-end value path for the user. The "CV →
relevant jobs" thesis is not served.
**Verdict:** Rejected. Feature boundary change is fundamental — supersession
is the correct instrument (Nygard 2011).

### Alt B — Expose a 0–100 numeric match score in the DTO

**For:** Familiar pattern from job-board products; easy to understand for users.
**Against:** Violates the Goodhart guard (ADR 0071) — an opaque aggregate score
becomes the target rather than genuine qualification signal. Users optimise
CVs for the score, not for the job. Explicitly named as a forbidden pattern
in CLAUDE.md §5 (deterministic engines block) and ADR 0071. ADR 0053 original
Beslut 5 also forbids the percentage ring.
**Verdict:** Rejected. The Goodhart guard is non-negotiable.

### Alt C — Enrich `IJobAdSearchQuery` with match data

**For:** Single query path; no separate batch call; simpler FE integration.
**Against:** `IJobAdSearchQuery` is the SPOT shared with `RunSavedSearch`
(ADR 0039 Beslut 1) and must remain anonymously cacheable. Enriching it with
per-user match data breaks the cache-isolation invariant and couples two
orthogonal read concerns. ADR 0063 Beslut b already established the batch-overlay
pattern for exactly this scenario.
**Verdict:** Rejected. Batch-overlay (Decision 5) is the correct pattern.

### Alt D — Persist the sort-key in the database

**For:** Cheaper re-query; simpler sort implementation.
**Against:** A persisted sort-key is a Goodhart-guard violation (Decision 4):
it would be visible in migrations, accessible in future queries, and could leak
into a DTO. Compute-on-demand is consistent with the F4-9 precedent (ADR 0074
Amendment 2026-06-15).
**Verdict:** Rejected. Compute-on-demand is the correct model.

---

## Consequences

### Positive

- The matching engine (F4-5/F4-6) finally has a production consumer — five
  independently-shippable STEGs that progressively light up the surface.
- Users see Fast match (SSYK + title + region + employment) for weeks before
  skill-overlap arrives (honest NotAssessed in the meantime — correct behaviour,
  not a degraded mode).
- The Goodhart guard (Decision 4) is enforced structurally via an architecture
  test — no numeric total can accidentally leak into a DTO or response.
- The batch-overlay pattern (ADR 0063) is reused — no new architectural
  precedent required.
- ADR 0053 Beslut 5 (match presentation) is finally realised in F4-16 — the
  Fas-4-gated deferral is lifted.

### Negative

- F4-12 introduces a genuine domain extension (job preferences SSOT). This
  is a real product need, not speculative generalization — but it adds a schema
  migration and a domain model change before any match tag is visible.
- Five STEGs after the current sequence (F4-11) extend Fas 4's total length.
  The tradeoff is independently shippable increments vs. a single large delivery.
- Users without a confirmed SSYK or without set preferences see NotAssessed
  for those dimensions — honest but potentially confusing without clear UX copy.
  UX copy is F4-16's responsibility.

---

## Supersession and amendments

### ADR 0040 → Superseded by ADR 0076

ADR 0040 ("Smart CV-derived saved search") is superseded by this ADR. The
feature boundary has changed: CV→filter-as-SavedSearch becomes CV→match-tag-
and-sort. Per Nygard 2011, supersession is the correct instrument when the
feature boundary (not merely the mechanism) changes. This consciously
reverses the earlier OQ2 "amendment" instinct, which held only while the
feature boundary remained the ADR 0040 SavedSearch path.

ADR 0040's transparency/user-confirmation principle (Beslut 4) survives
conceptually in F4-16: the match modal shows matched/missing per dimension,
never an opaque score.

The disposition of the `ConfirmDerivedSearch` code (retire vs. leave inactive)
is a discovery decision for F4-12, not decided by this ADR.

ADR 0040's original 2026-05-16 body and its 2026-06-14 amendment are preserved
verbatim for historical context (ADR immutability per Nygard 2011).

### ADR 0074 → Sequence amendment (F4-12..F4-16 inserted)

ADR 0074 § "The Fas 4 sequence" ends at F4-11. Per ADR 0074's own rule:
"sequence changes after Accepted require an amendment." F4-12 through F4-16
are inserted immediately after F4-11. See ADR 0074 Amendment 2026-06-19 for
the formal amendment record.

### ADR 0053 Beslut 5 → Amended

ADR 0053 Beslut 5 deferred match-presentation to Fas 4. F4-16 lifts that
deferral: the job modal will show category-primary match (verdict +
matched/missing per dimension), never "92% match", never a percentage ring.
The amendment is recorded in ADR 0053 Amendment 2026-06-19.

---

## References

- [ADR 0040](./0040-smart-cv-derived-saved-search.md) — superseded; original
  smart CV-filter feature; Beslut 4 transparency principle survives in F4-16
- [ADR 0042](./0042-search-surface-information-architecture.md) — Beslut F
  multi-value criteria
- [ADR 0045](./0045-performance-budget-and-fitness-functions.md) — Klass (a)
  300 ms p95; F4-13/14 inherit
- [ADR 0053](./0053-detail-modal-intercepting-parallel-route.md) — Beslut 5
  amended by F4-16
- [ADR 0060](./0060-recent-job-searches-auto-capture.md) — SavedSearch hidden
  on FE; explains why smart-filter had no FE home
- [ADR 0063](./0063-per-user-overlay-status-batch-port.md) — batch-overlay
  pattern; Beslut b names match-score per Fas 4
- [ADR 0071](./0071-fas4-without-ai-deterministic-cv-and-matching-engines.md)
  — no AI/LLM; Goodhart guard origin
- [ADR 0074](./0074-fas4-design-and-sequence.md) — Fas 4 umbrella; sequence-
  amended by this ADR
- [ADR 0075](./0075-f4-4-jobad-extraction-and-oq4-coverage.md) — ExtractedTerms
  + skill index; reused by F4-15 skill-resolver
- `docs/steg-tracker.md` §4.6 — living Fas 4 plan (mutable; this ADR is the
  frozen counterpart for F4-12..F4-16)
- BUILD §8 — deterministic CV and matching engines (authoritative spec)
- CLAUDE.md §5 — deterministic engines anti-pattern block

---

## Amendment 2026-06-19 — Occupation becomes a stated, persisted preference (Decision 2 reversed)

**Date:** 2026-06-19
**Instrument:** amendment-note. The feature boundary "match-tag + sort" is intact;
this changes the source of the occupation dimension only. Supersession is not
the correct instrument (Nygard 2011).
**Driver:** Klas product decision 2026-06-19; mechanism rulings bound by
senior-cto-advisor 2026-06-19.

### Rationale

Decision 2 (as written above) assumes that a user's CV is a faithful proxy for
their desired occupation. It is not, in at least three cases:

1. A career-changer whose CV records "industriarbetare" history but who wants
   "systemutvecklare" jobs. CV history ≠ desired occupation.
2. A user with no CV at all — nothing to derive from.
3. A multi-track user who is exploring roles across two occupations simultaneously.

Stated preference is the correct source of truth for the occupation dimension of
matching.

### Decision 2 — REVERSED

**Was:** "SSYK is compute-on-demand from the primary CV. No new SSYK-persistence
column is added now. A future LRM may introduce persisted `ConfirmedOccupation`
and a re-derivation-on-CV-change trigger."

**Now:** Occupation is a **stated, PERSISTED preference** on `MatchPreferences`.

- Field: `MatchPreferences.PreferredOccupationGroups` — a collection of ssyk-4
  GROUP concept-ids (the same SSYK taxonomy level used in `SearchCriteria`).
- Captured during onboarding / setup flow (see Decision 3 expansion below).
- Normalized identically to `SearchCriteria` field normalization: sorted,
  distinct, ordinal comparison, regex default-deny, cap = MaxConceptIds 400.
- Empty is explicitly allowed — an empty set means "not yet stated", not an error.
- The prior "future LRM persisted `ConfirmedOccupation`" forward-note is
  REALIZED by this amendment. It is not deleted from the original text above;
  it is superseded in-place by this amendment section.

**Scope of this reversal:** occupation derivation ONLY.
- Decision 4 (Goodhart-clean internal sort-key, compute-on-demand) is untouched.
- F4-9 CV-review compute-on-demand (ADR 0074 Amendment 2026-06-15) is untouched.

### Decision 3 — EXPANDED

Decision 3 originally scoped the new preferences SSOT to region and
employment-type only. It is now expanded:

`MatchPreferences` is the SSOT for:
- `PreferredOccupationGroups` (ssyk-4 GROUP concept-ids) — **added by this amendment**
- Preferred regions
- Preferred employment-types

All fields are optional, empty-allowed, and multi-value. All three are captured
in a **skippable** onboarding/setup flow.

**Nudge surface:** an incomplete-setup nudge is shown when
`PreferredOccupationGroups` is empty (e.g. on Översikt: "Du har inte angett
ditt drömjobb — komplettera din profil"). The backend signal for this nudge is a
derived bool `HasStatedDesiredOccupation` (computed from
`PreferredOccupationGroups.Any()` at the read side). No stored flag, no distinct
"skipped" state — a deliberate simplification.

### Derive→confirm UX — REPURPOSED (ADR 0040 Beslut 4)

ADR 0040's derive→confirm pattern (suggest occupations from the CV → user
confirms, edits, adds, or clears) is **repurposed inside onboarding** as a
convenience seed for the stated occupation preference. This is not a new use of
ADR 0040 — it is a continuation of its transparency/user-confirmation principle
(Beslut 4), which ADR 0076's Supersession section already noted "survives
conceptually in F4-16". That principle now also applies at the F4-12 onboarding
capture step.

The `ConfirmDerivedSearch` code path (formerly the mechanism of ADR 0040) may be
re-pointed at the `MatchPreferences` write-path rather than retired. This is a
discovery decision for the F4-12 implementation, not decided here.

### Decision 7 — REAFFIRMED (unchanged)

Decision 7 ("Broad/unsure searchers get honest NotAssessed") is reaffirmed and
its scope extended: an empty `PreferredOccupationGroups` is treated identically
to an unconfirmed occupation — honest NotAssessed for the SSYK dimension, never
faked. The matching engine never invents an occupation the user has not stated.

### F4-12 acceptance criterion — UPDATED

The F4-12 row in the STEG table above read:

> "A logged-in user with a primary CV + confirmed preferences can produce a
> `CandidateMatchProfile` via an Application port, without reading raw CV-PII
> outside the DEK pipeline."

That criterion is updated by this amendment to:

> **F4-12 — Match-preference SSOT incl. occupation-groups + profile-builder,
> preference-driven, no CV read required.**
> A logged-in user can state/update `MatchPreferences` (occupation-groups +
> regions + employment-types) via a skippable onboarding/setup flow.
> `CandidateMatchProfile` is built from stored preferences only — no CV content
> is read, no DEK / `IRequiresFieldEncryptionKey` is required at profile-build
> time. The derive→confirm UX may seed the preference capture from the CV
> (ADR 0040 Beslut 4 pattern) but the persistence target is `MatchPreferences`,
> not `SavedSearch`.

The other STEGs (F4-13..F4-16) are unchanged.

### Cross-reference note — ADR 0074 Amendment 2026-06-19

ADR 0074's Amendment 2026-06-19 (sequence insertion) describes F4-12 as:
"Primary CV + confirmed preferences → `CandidateMatchProfile` via Application
port; no CV-PII outside DEK pipeline."

That description was accurate at the time of writing. **This amendment reverses
it:** F4-12 is now purely preference-driven and requires no CV read and no DEK
pipeline at profile-build time. Treat ADR 0076's 2026-06-19 amendment as the
authoritative F4-12 definition; ADR 0074's F4-12 summary is superseded in
that specific respect.

---

## Amendment 2026-06-19 (b) — F4-13 graded match-tag: the ladder, the gates, and the visual form

**Date:** 2026-06-19
**Instrument:** amendment-note. The feature boundary "match-tag + sort" is intact;
this refines the F4-13 tag presentation (binary → graded category) and the
visual form of the grade indicator on the card. Supersession is not the correct
instrument (Nygard 2011).
**Driver:** Klas product decision 2026-06-19 (graded, positive-only ladder;
AskUserQuestion GO); mechanism rulings bound by senior-cto-advisor 2026-06-19
(`docs/reviews/2026-06-19-f4-13-match-tag-cto-rebind.md` RB1–RB5 +
`docs/reviews/2026-06-19-f4-13-match-visual-cto.md` V1–V8; initial mechanism
rulings: `docs/reviews/2026-06-19-f4-13-match-tag-cto.md` A1/B2/C2/D/E).

### 1. The F4-13 grade ladder (Fast tier scope)

The F4-13 match tag is a **graded, positive-only ordinal category** — not a
binary "good match". Klas product decision 2026-06-19.

F4-13 operates on the Fast tier. The preference-built `CandidateMatchProfile`
always carries `Title = ""`, so `TitleSimilarity` is always `NotAssessed` and
is **not consulted** by the F4-13 grade rule. The three live dimensions are
`SsykOverlap`, `RegionFit`, and `EmploymentFit`.

Three named grades ship in F4-13:

| Grade | Meaning |
|---|---|
| **`Strong`** | Occupation + region + employment all confirmed `Match` |
| **`Good`** | Occupation confirmed; at least one of region/employment also `Match`, none contradicted |
| **`Basic`** | Occupation confirmed; region and employment both `NotAssessed` (open / not stated) |

These three are the **bottom rungs** of Klas's eventual ladder. F4-15 (skill
resolver + Full tier) **extends** the same enum upward with skill / must-have /
nice-to-have rungs (the "golden match" rungs), never rewriting these three. A
profile with no resolved skills tops out at `Strong` — honest and correct.
`NotAssessed` F4-15 dimensions neither confirm nor cap (parity region/employment
below), so the extension is rework-free.

### 2. The deterministic rule (`MatchGradeCalculator`)

The grade derivation is a **pure, total function** over the `MatchScore`
verdict-tuple, implemented as a single tested .NET SSOT
(`MatchGradeCalculator`) in the Application layer. It is reused by the F4-14
sort path (no duplication).

**Gate:** if `SsykOverlap` is NOT `Match` (`NoMatch` or `NotAssessed`) → **NO
tag**. The ad is omitted from the batch overlay (parity the
`JobAdStatusBatchDto` "present means true" model). An empty
`PreferredOccupationGroups` yields `SsykOverlap = NotAssessed` → no tag.
The Översikt incomplete-setup nudge (`HasStatedDesiredOccupation`) owns the
"no occupation stated" case; a discouraging per-card tag does not.

**Region/employment cap:** a stated region or employment preference that the
ad **contradicts** (`NoMatch`) floors the grade. The rule: start from the rung
implied by the count of confirming `Match` dimensions among {`RegionFit`,
`EmploymentFit`}; then — if either is `NoMatch`, cap the grade at `Good` and
that dimension contributes no confirming rung; if both are `NoMatch`, cap at
`Basic`. A `NotAssessed` dimension (user stated "open" or did not state a
preference) neither confirms nor caps.

Klas (2026-06-19, RB1): "a deselected location/employment form must not read
as a strong match — 'det sämsta är ... inte rätt ort'." A confirmed mismatch
on a STATED preference forces the lowest positive grade, not merely a cap
below top.

**Total mapping** (`SsykOverlap = Match` assumed throughout):

| `RegionFit` | `EmploymentFit` | Grade |
|---|---|---|
| `Match` | `Match` | **Strong** |
| `Match` | `NotAssessed` | **Good** |
| `NotAssessed` | `Match` | **Good** |
| `NotAssessed` | `NotAssessed` | **Basic** |
| `Match` | `NoMatch` | **Good** *(employment caps below top; region still confirms)* |
| `NoMatch` | `Match` | **Good** *(region caps below top; employment still confirms)* |
| `NoMatch` | `NotAssessed` | **Basic** *(region caps; employment contributes nothing)* |
| `NotAssessed` | `NoMatch` | **Basic** *(employment caps; region contributes nothing)* |
| `NoMatch` | `NoMatch` | **Basic** *(both contradicted; occupation holds the floor)* |

This mapping is **total** (all 9 reachable verdict-tuples covered; `Partial`
cannot occur — `ScoreMembership` is binary) and **deterministic** (pure, no
I/O, no clock). It is unit-tested RED-first with the full 9-tuple table as
the oracle.

### 3. C2a — backend emits the named grade enum, not a number

The batch DTO carries, per matched ad:
- `MatchGrade Grade` — the ordinal category enum (`Strong`/`Good`/`Basic`),
  serialized by **name** (string), never an ordinal integer.
- The four `MatchDimensionVerdict`s (`SsykOverlap`, `TitleSimilarity`,
  `RegionFit`, `EmploymentFit`) — for F4-16 modal forward-compatibility and
  F4-14 sort reuse.

DTO shape (names indicative; the arch test pins the property set):
`JobAdMatchTagBatchDto(IReadOnlyList<JobAdMatchTag> Tags)` where
`JobAdMatchTag(Guid JobAdId, MatchGrade Grade, MatchDimensionVerdict
SsykOverlap, MatchDimensionVerdict TitleSimilarity, MatchDimensionVerdict
RegionFit, MatchDimensionVerdict EmploymentFit)`.

An architecture test (companion to `MatchScorerLayerTests`) pins `JobAdMatchTag`'s
public instance properties to exactly `{ JobAdId, Grade, SsykOverlap,
TitleSimilarity, RegionFit, EmploymentFit }` and forbids any numeric field
(`Score`/`Value`/`Total`/`Percent`/`SortKey`/`Rank`/`Points`, or any
`int`/`double`/`decimal`/`float` scored property). A companion assertion pins
`MatchGrade`'s members to exactly `{ Strong, Good, Basic }` at F4-13 (the guard
travels with the F4-15 extension when that test table is amended).

The matched/missing STRING evidence (raw concept-ids and Snowball stems at Fast
tier) does **not** travel on the list DTO. That is detail-altitude, owned by
the F4-16 modal.

### 4. Goodhart re-affirmation — a named ordinal category enum is NOT the forbidden opaque total

Decision 4 (Goodhart guard end-to-end) is reaffirmed and its scope clarified:

The guard forbids three things: an **opaque numeric total** (`0–100`,
percentage, ring, summable scalar), that total **persisted**, that total
**leaking into a DTO or response**. A named, ordinal, three-valued category
enum is none of those:

- It is **not opaque** — each grade is defined by the verdict-tuple that
  produces it, and the four verdicts travel on the same DTO. F4-16's modal
  shows matched/missing per dimension. The user always sees WHY.
- It is **not a number** — `Strong`/`Good`/`Basic` cannot be "optimized to
  91→92"; they are categories with a deterministic, fully-disclosed boundary.

The direct precedent is F4-3's `OccupationMatchKind` — an ordinal category
enum that drives ranking and is exposed — which Decision 4 itself cites:
"opaque scores are excluded by design." `MatchGrade` is its sibling. The
guard was authored to exclude opaque scores and explicitly tolerates
load-bearing ordinal category kinds.

The **F4-14 sort-key** (an internal ordering scalar used to `ORDER BY`) is a
distinct object from `MatchGrade`: it remains Infra-only, never serialized,
never returned in any DTO. Decision 4's "lives ONLY in the Infrastructure sort
path" clause applies to that scalar, not to the user-facing category.

### 5. The visual form (Klas decision via AskUserQuestion 2026-06-19)

The card renders the pre-staged `.jp-matchchip` — a **green dot + a terse
named grade word** — a DISCRETE, bounded, named indicator. This is the visual
twin of the `MatchGrade` enum: exactly as many states as the enum has members.

**Goodhart line, drawn by the CTO (V1):**

> A match indicator may render the grade as **one of N discrete, bounded,
> named-category states** (clean). It may NOT render the grade as a
> **position or fill on a continuum** (thermometer, fill-meter, progress bar,
> ring, gauge) — that is the opaque-magnitude family, the percentage ring's
> sibling, forbidden by ADR 0053 Beslut 5 and by Decision 4 of this ADR.

A discrete bounded indicator (e.g. a 3-step segmented glyph) is the 1:1
visual encoding of the enum and is **Goodhart-clean** — it cannot imply a
hidden continuum. A continuous fill-meter encodes magnitude on a continuum;
even without a printed number it communicates "how full" on a 0–100 scale,
which is the opaque aggregate signal the guard forbids. ADR 0053 Beslut 5
already bans the percentage ring (a circular fill-meter); a vertical fill-meter
is the same family — a linear ring.

**Red-as-best is independently rejected.** In the JobbPilot token system,
red is locked as `danger` (`--jp-danger`, "endast för status, aldrig
dekoration"). Positive match signal lives in the green success family
(`--jp-success`), never in danger-red.

**The card carries a self-describing named word per shown grade** (V3,
explainability floor): the match is "explainable by design" at card altitude
too. A wordless glyph is rejected — a bare fill or segmented pip with no
label makes the grade infer-from-fill rather than read, which is the Goodhart
failure mode in visual clothing.

The pip (if any) renders from the **enum** in the FE (`Strong`/`Good`/`Basic`
→ 3/2/1 steps as a presentation detail), never from a backend `int`/`Score`/
`Level` field. The Goodhart arch test (no numeric field on `JobAdMatchTag`)
already prevents any "filled-steps count" from reaching the DTO.

The full match **visual vocabulary** — the exact glyph, per-grade copy, and
the modal's matched/missing layout — is settled in **F4-16** with
design-reviewer. F4-13 ships the named chip (`.jp-matchchip`); the copy
strings and any richer treatment are F4-16's owned concern
(FAS-DEFERRAL-MANIFEST; `docs/reviews/2026-06-19-f4-13-match-visual-cto.md`
V2 scope seam).

### 6. Batch-overlay mechanism (Decision 5 reaffirmed)

Decision 5 is reaffirmed: the grade rides the dedicated batch-overlay port
`POST /api/v1/me/job-ad-match-tags` (parity `isSaved`/`isApplied`, ADR 0063),
never `IJobAdSearchQuery` enrichment.

Port and collaboration shape (all in-block, F4-13 PR):
- **`IMatchScorer.ScoreBatchAsync`** (A1): a second method on the existing
  port — `ScoreBatchAsync(IReadOnlyList<JobAdId>, CandidateMatchProfile,
  CancellationToken) → ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>>`.
  Loads ALL shadow rows in ONE query; reuses the four existing private helpers
  in-memory. Zero N+1. The CV-title lexeme computation is hoisted out of the
  per-ad loop (the preference-built profile has `Title = ""` at F4-13, so this
  is near-free now but structurally required before F4-15).
- **`IMatchProfileBuilder`** (B2): a shared Application collaborator consumed
  by both `BuildMatchProfileFromPreferencesQueryHandler` and the new batch
  handler. No handler-calls-handler (CQRS anti-pattern), no
  `IAuthenticatedRequest` trap. Anonymous callers receive an honest empty
  profile → all dimensions `NotAssessed` → no tags in the overlay.
- **Route:** `POST /api/v1/me/job-ad-match-tags`. Anonymous-tolerant (NOT
  `.RequireAuthorization()`). New dual-partition rate-limit policy
  `JobAdMatchTagBatchPolicy` (60/min, `user:`/`ip:` partitions), mirroring
  `JobAdStatusBatchPolicy` — the two overlay calls fire on the same page and
  MUST NOT share a budget. Validator caps IDs at 100 (parity
  `GetJobAdStatusBatchQueryValidator.MaxJobAdIdsPerCall`).

### 7. Scope

F4-13 ships the occupation + region + employment rungs ONLY. Skills,
must-haves, and nice-to-haves remain F4-15 (a planned sequence dependency
behind the skill-resolver port, Decision 6). This is NOT a TD and MUST NOT be
pulled forward: pulling skill scoring into F4-13 would introduce CV influence
at Fast tier, which ADR 0076 and CLAUDE.md §5 forbid, and would duplicate the
F4-6 Full path. F4-15 is a named sequenced STEG tracked by the steg-tracker,
not a gap in the TD register.

### F4-13 acceptance criterion — UPDATED

The F4-13 row in the STEG table above read:

> "/jobb list shows an explainable category-primary match tag per card; ONE
> batch call per page; zero N+1; `JobAdDto` untouched."

That criterion is updated by this amendment to:

> **F4-13 — Page-scoped Fast match-tag via batch-overlay, graded.**
> `/jobb` list shows an explainable, **graded** (`Strong`/`Good`/`Basic`),
> positive-only category-primary match tag per card, rendered as a discrete
> named chip (`.jp-matchchip`). The tag is gated on `SsykOverlap = Match`
> (absent otherwise); a stated `RegionFit` or `EmploymentFit` `NoMatch` caps
> the grade. ONE batch call per page (`POST /api/v1/me/job-ad-match-tags`);
> zero N+1; `JobAdDto`/`IJobAdSearchQuery` untouched. The batch DTO carries
> `MatchGrade` (named enum, serialized by name) + the four
> `MatchDimensionVerdict`s (forward-compat with F4-16 modal). An architecture
> test pins no numeric field on the wire DTO. The grade derivation is a single
> tested .NET SSOT (`MatchGradeCalculator`) reused by the F4-14 sort path.
> Anonymous-tolerant (honest empty). Per-grade copy and the full visual
> vocabulary are finalized in **F4-16** with design-reviewer.

---

## Amendment 2026-06-20 — Requirement-aware match grade (ladder re-bind; reverses Amendment (b) §1's "rework-free / never caps" clause)

**Date:** 2026-06-20
**Instrument:** first-class dated amendment — this REVERSES a frozen clause of
Amendment (b) §1. The feature boundary "match-tag + sort" is intact;
supersession is not the correct instrument (Nygard 2011). Amendment is, because
a frozen promise inside an Accepted section is being consciously overturned.
**Driver:** Klas product decision 2026-06-20 (the grade must mean "you meet the
ad's binding requirements", not merely "your preferences fit"); mechanism
rulings bound by senior-cto-advisor 2026-06-20 (`docs/reviews/2026-06-20-matchning-grade-rebind-cto.md`, G1–G5).
**Implemented in:** Fas 4 Matchning-UX STEG, PR-B1.
**Decision-makers:** Klas Olsson (product direction; Reading 1 for vacuous ads;
G3-OPT-A for sort coherence — AskUserQuestion GO); senior-cto-advisor
(mechanism rulings G1–G5, this report constitutes the binding CTO re-bind).

### The reversal — Amendment (b) §1 "rework-free / never caps" clause SUPERSEDED

Amendment (b) §1 (above) froze the following promise:

> *"skills extend the ladder UPWARD; `NotAssessed` F4-15 dimensions neither
> confirm nor cap (rework-free)"*

**That promise is superseded by this amendment.** Must-have coverage (`MustHaveCoverage`)
now GATES the upper rungs `Strong` and `Top`: a `NotAssessed`, `Partial`, or
`NoMatch` must-have verdict cannot reach `Strong` or `Top`. This is a real
modification of `MatchGradeCalculator.Grade(FullMatchScore)` — not a note.

The `MatchScore`/`FullMatchScore` *data shapes* remain frozen; the grade-derivation
*rule* changes. This is a behaviour correction (the calculator was ignoring the
domain's own binding requirement signal — `MustHaveCoverage`), not an OCP violation.
Per senior-cto-advisor G1: "The rebind aligns the grade vocabulary with the
extraction vocabulary." (Evans 2003 ubiquitous language.)

### 1. The new requirement-aware ladder (SSOT — replaces the F4-13 mapping table above in Amendment (b) §2)

`MatchGradeCalculator.Grade(FullMatchScore)` is a pure, total function. Read
top-down; first matching row wins. `SSYK = Match` is the gate throughout.

| Step | Condition (`SsykOverlap = Match` assumed; evaluated top-down) | Grade |
|---|---|---|
| **Gate** | `SsykOverlap ≠ Match` | **null** (no tag) |
| **Floor** | `RegionFit = NoMatch` OR `EmploymentFit = NoMatch` (a STATED preference contradicted) | **Basic** |
| **Top** | `MustHaveCoverage = Match` AND region & employment both `Match` AND (`SkillOverlap ∈ {Match,Partial}` OR `NiceToHaveCoverage ∈ {Match,Partial}`) | **Top** |
| **Strong** | `MustHaveCoverage = Match` AND region & employment both `Match` | **Strong** |
| **Strong (open-secondary)** | `MustHaveCoverage = Match` AND at least one of region/employment is `Match`, the other `NotAssessed` (none contradicted) | **Strong** |
| **Good** | NOT (`MustHaveCoverage = Match`) AND both region & employment `Match` | **Good** |
| **Good** | NOT (`MustHaveCoverage = Match`) AND exactly one of region/employment is `Match` | **Good** |
| **Basic** | otherwise (SSYK `Match`, no confirmed secondary, must-have not met) | **Basic** |

The table is **total**: the final `Basic` row catches all remaining reachable
tuples. `Partial` cannot occur on Fast dimensions (binary membership); `Partial`
CAN occur on must-have/skill/nice-to-have. The oracle test re-proves totality
over the expanded tuple space.

The four Swedish chip words (default set, subject to design-reviewer veto in
PR-B2): `Top` = "Toppmatch", `Strong` = "Stark match", `Good` = "Matchar dina
önskemål", `Basic` = "Grundmatch". `Good`/`Basic` are preference-framed (not
candidate-qualification claims); `Strong`/`Top` are requirement-met (must-have
gate confirmed).

### 2. The four hard-case rulings (binding)

**(a) No CV / no resolved CV skills — ceiling = `Good`, never `Strong`/`Top`.**
No CV ⇒ `MustHaveCoverage = NotAssessed`. `NotAssessed ≠ Match`, so the
must-have gate is NOT satisfied. The honest ceiling for a user without a CV is
`Good` ("Matchar dina önskemål") — a preference-fit grade, never a
qualification claim. Uploading a CV is the only path to `Strong`/`Top`. The
`NotAssessed` verdict is shown neutrally in the F4-16 modal ("Ej bedömt") with
a "ladda upp CV" call to action — it is not rendered as a penalty.

**(b) Ad has NO extracted must-haves — vacuous gate, `Vacuous` verdict, gate-open
(Klas Reading 1).**
`ScoreConceptCoverage` previously returned `NotAssessed` both when the CV had no
resolved skills AND when the ad stated no must-have terms. These two causes are
now distinguished by a new `MatchDimensionVerdict` member:

- **`Vacuous`** — the ad's partition is empty AND the CV is non-empty ("nothing
  required, and we did look"). Gate-open: the `MustHaveCoverage = Vacuous` branch
  satisfies the must-have gate exactly as `Match` does. A fully preference-matching
  + skill-confirming ad with no stated requirements CAN reach `Strong`/`Top`.
  Copy constraint: the chip/modal must NOT say "du uppfyller alla krav" for a
  vacuous-gate Top — the modal shows the empty must-have row honestly ("inga
  specifika skallkrav angivna").
- **`NotAssessed`** — retains its prior meaning: "we could not assess" (CV has
  no resolved skills, or assessment was skipped for another reason). Does NOT
  satisfy the gate.

Klas product decision 2026-06-20 (AskUserQuestion GO): Reading 1 with `Vacuous`
distinction is adopted. Per the CTO: "an ad with no stated must-haves has 'all
(zero) must-haves met' trivially … the chip copy for a vacuous-gate Top must
NOT say 'du uppfyller alla krav'."

Arch impact: `MatchDimensionVerdict` now has **five** members
(`Match`, `Partial`, `NoMatch`, `NotAssessed`, `Vacuous`). The arch pin
`MatchDimensionVerdict_still_is_the_locked_N_member_set` is updated to five.
All `MatchDimensionVerdict` consumers and the F4-16 modal copy are updated in
PR-B1/PR-B2.

**(c) `MustHaveCoverage = NoMatch` (CV present, none of the must-haves covered) —
capped at preference rungs (`Good`/`Basic`), never `Strong`/`Top`.**
`NoMatch` is the strongest possible failure signal (both sides non-empty,
disjoint). It must not claim candidate qualification. The user still receives a
preference-fit grade (`Good` if secondaries confirm, `Basic` otherwise) because
occupation/region/employment fit is real and worth surfacing. The modal's
`missing[]` list shows exactly which must-haves are unmet.

**(d) `MustHaveCoverage = Partial` (some but not all must-haves covered) —
capped at `Good`, never `Strong`.**
Klas (2026-06-20): "man måste såklart matcha **alla** must-have" for Stark.
`Partial ≠ Match`, so the gate is not satisfied. `Partial` lands on the
preference rungs. No new rung between `Good` and `Strong` is introduced —
the four-member enum is the Goodhart-minimal set. The modal shows which
must-haves are still missing. A future extension to a "nästan där" rung is a
later product call, not this STEG.

**The RB1 contradiction floor is preserved and evaluated FIRST** (before the
must-have rows): a deselected region or employment that the ad contradicts
(`NoMatch`) floors the grade to `Basic` regardless of must-have coverage.
Even perfect must-have coverage cannot rescue a confirmed place/role mismatch.
(Klas 2026-06-19/2026-06-20: "det sämsta är … inte rätt ort.")

### 3. The new `Vacuous` verdict (fifth `MatchDimensionVerdict` member)

`Vacuous` is a categorical verdict, not a number. It is Goodhart-safe.
It is per-dimension (an ad can be vacuous on must-haves while having explicit
skill terms). Its semantics: "the ad's partition for this dimension is
intentionally empty, and the CV is present — we looked, nothing was required."

Distinct from `NotAssessed` ("we could not assess, typically no CV or no
resolved CV skills"). The two are not interchangeable.

`ScoreConceptCoverage` emits `Vacuous` when `adTerms.Count == 0` AND the CV
is non-empty (resolved skills present). It emits `NotAssessed` when the CV has
no resolved skills (regardless of ad terms).

For the grade rule: `MustHaveCoverage ∈ {Match, Vacuous}` satisfies the
must-have gate. This is the `mustHaveMet` predicate used throughout the ladder.

### 4. Sort vs. grade coherence — G3-OPT-A adopted (Klas GO)

**The F4-14 sort and the visible grade are intentionally, honestly different axes.**

The `MatchSortedJobAdSearchQuery` sort path uses `BuildFullFromTopSkillsAsync`
(top-5 **plaintext** skills, no DEK, R5-REBIND Option H) with a binary GIN
overlap (`extracted_lexemes ?| @cvSkillIds`). This path:

- has no requirement partition (cannot distinguish must-have from nice-to-have);
- cannot compute `MustHaveCoverage = Match` ("all must-haves covered" requires
  a set-difference, not a `?|` any-overlap);
- has no DEK (deliberately, for the hot anonymous-cacheable path).

**Therefore the sort cannot compute the requirement-aware grade.**

The adopted resolution is **G3-OPT-A** (Klas product decision 2026-06-20,
AskUserQuestion GO): the sort ranks by the Fast grade band + binary skill
`?|` overlap (its current behaviour), and the visible per-ad grade is computed
by the full requirement-aware `MatchGradeCalculator` on the DEK-bearing page-TAG
path. These are **coherent but different axes**:

- **Sort** = "best preference + any-skill fit first" (fast, cacheable, DEK-free).
- **Grade chip** = "do you meet this ad's requirements" (per-ad, DEK-warmed, full CV).

The narrow divergence — a `Strong`/`Top` grade-chip ad (must-haves met) may
sort below a `Good`/`Basic` chip ad (must-haves not met, but skill overlap on
the top-5 plaintext set) — is **accepted, documented, and oracle-pinned**, not
silent. The UI copy says "Sortera efter matchning" (preference + skill relevance),
never "sort by grade exactly."

G3-OPT-B (promote the sort to the DEK-bearing verdict path, sort == grade
exactly) was **rejected** by Klas to preserve the R5-REBIND Option H DEK-free
hot path and ADR 0045 300 ms p95 budget. The cacheability loss and DEK
warmup on the sort hot path were the deciding costs.

**Arch pins updated:**
The `MatchSortedJobAdSearchQuery` Testcontainers oracle is re-derived to pin
the **sort-band contract** (a documented strict coarsening of the visible
grade), not the full requirement-aware grade. The oracle explicitly asserts
that the sort-band is monotone with respect to the visible grade's band ordering
EXCEPT in the documented must-have-within-Strong band where they may differ.

### 5. Goodhart reaffirmed (unchanged)

`MatchGrade` remains a named ordinal category with exactly four members:
`{Basic, Good, Strong, Top}`. No number, no percentage, no ring, no summable
scalar anywhere on the wire. The seven per-dimension verdicts + matched/missing
still explain WHY. The `OccupationMatchKind` precedent (Decision 4 of this ADR)
governs: an ordinal category that gates ranking is explicitly tolerated; only
opaque magnitudes are forbidden. The ladder gained a requirement *gate* (a
categorical condition on a categorical verdict), not an opaque magnitude.

The `Vacuous` verdict addition is a categorical refinement; it adds no number.
The existing arch pins (`MatchTagBatchLayerTests`) continue to hold on the
numeric-field prohibition; the member-count pin is updated to five for
`MatchDimensionVerdict`.

### 6. Cross-reference updates

- **ADR 0053 Beslut 5 (modal, no percentage ring):** the modal's must-have row
  is now grade-load-bearing. Modal copy gains a must-have reason line: when
  must-have is `Match`/`Vacuous` → "Du uppfyller alla skallkrav" (or "inga
  specifika skallkrav angivna" for vacuous); when `Partial`/`NoMatch` → "Du
  saknar X av Y skallkrav" with the `missing[]` list; when no CV →
  "Ladda upp CV för att se om du uppfyller kraven." Structural/DTO shape
  unchanged; copy + grade-reason line only. design-reviewer veto
  (FAS-DEFERRAL-MANIFEST) in PR-B2.
- **ADR 0045 (performance budget — 300 ms p95):** preserved. G3-OPT-A keeps the
  sort on the DEK-free plaintext path (R5-REBIND Option H). G3-OPT-B (DEK on
  sort hot path + ADR 0045 re-measure) was explicitly rejected.
- **ADR 0071 (no AI/LLM; Goodhart guard origin):** reaffirmed. Deterministic
  throughout. `Vacuous` is a categorical rule, not an inference.
- **ADR 0076 Decision 4/6 + Amendment (b) §1:** Decision 4 (Goodhart guard —
  sort-key never in DTO) is reaffirmed. Decision 6 (skill-resolver port,
  R5-REBIND Option H DEK-free hot path) is preserved and is the explicit reason
  G3-OPT-B was rejected. Amendment (b) §1's "rework-free / never caps" clause
  is superseded by this amendment (§ "The reversal" above).

### F4-15/F4-16 acceptance criteria — UPDATED

The F4-15 row in the STEG table (Amendment (b) §7) read:

> "Tag/sort include skill-overlap + must-have/nice-to-have for CVs with resolved
> skill concept-ids; CVs without resolved skills degrade to Fast (NotAssessed),
> never NoMatch."

Updated by this amendment:

> **F4-15 — Skill-resolver port + Full-tier upgrade, requirement-aware.**
> Tag/grade includes skill-overlap + must-have/nice-to-have for CVs with resolved
> skill concept-ids (full requirement-aware ladder per Amendment 2026-06-20). CVs
> without resolved skills degrade to Fast (all Full dimensions `NotAssessed`),
> which caps at `Good` under the new ladder — not a penalty, but not `Strong`/`Top`.
> The sort remains on the G3-OPT-A DEK-free path (Amendment 2026-06-20 §4).

The F4-16 row is unchanged in its acceptance criterion text but carries
additional copy obligations per §6 cross-reference (must-have reason line in
modal + "ladda upp CV" affordance).

---

## Amendment 2026-06-27 — Vacuous-with-signal gate (grade-precision audit F1; narrows Reading-1 for no-requirement ads)

**Date:** 2026-06-27
**Instrument:** first-class dated amendment — this NARROWS a frozen clause of
Amendment 2026-06-20 §2(b) (Reading 1 / `Vacuous` gate-open). It does NOT
reverse the five-member `MatchDimensionVerdict` enum, the `Vacuous` verdict
concept, or the rest of the requirement-aware ladder. The narrowing is
product-motivated, not a mechanism correction; supersession would obscure
Reading-1 context still valid for three of four Vacuous sub-cases.
**Driver:** grade-precision audit of the live 73 711-ad corpus (2026-06-27),
finding F1 — `docs/reviews/2026-06-27-matching-grade-precision-audit-268.md`.
~98 % of ads carry zero structured must-have terms and therefore yield
`MustHaveCoverage = Vacuous` for any CV-bearing user. Under the prior Reading-1
rule (`Vacuous` = gate-open unconditionally), Strong/Top were awarded with
ZERO must-have AND zero skill/nice-to-have evidence for ~98 % of ads —
contradicting the badges' documented "requirement-backed" promise and the
Reading-1 spirit itself (which assumed skill/preference confirmation would
accompany a vacuous gate).
**Implemented in:** `MatchGradeCalculator.Grade(FullMatchScore)` +
`MatchGradeCalculatorTests` oracle; nightly scan refreshes persisted
`UserJobAdMatch` grades (pre-prod, no migration needed).
**Decision-maker:** Klas Olsson (product direction, 2026-06-27 GO).

### The narrowing — Reading-1 `Vacuous` gate conditioned on positive signal

Amendment 2026-06-20 §2(b) stated, in full:

> `Vacuous` — the ad's partition is empty AND the CV is non-empty ("nothing
> required, and we did look"). **Gate-open**: the `MustHaveCoverage = Vacuous`
> branch satisfies the must-have gate exactly as `Match` does.

and §3:

> For the grade rule: `MustHaveCoverage ∈ {Match, Vacuous}` satisfies the
> must-have gate. This is the `mustHaveMet` predicate used throughout the ladder.

**The `mustHaveMet` predicate above is superseded by `requirementBacked`.**
`Vacuous` remains in the enum and retains its semantics. What changes is that
an UNCONDITIONAL `Vacuous` gate-open is no longer sufficient for Strong/Top:
a `Vacuous` must-have now requires a companion positive signal to reach the
upper rungs.

Define:

```
HasSkillOrNiceSignal =
    SkillOverlap ∈ {Match, Partial}
    OR NiceToHaveCoverage ∈ {Match, Partial}

requirementBacked =
    MustHaveCoverage == Match
    OR (MustHaveCoverage == Vacuous AND HasSkillOrNiceSignal)
```

`requirementBacked` replaces `mustHaveMet` everywhere in the ladder. The old
`mustHaveMet` (`MustHaveCoverage ∈ {Match, Vacuous}`) is retired as a named
predicate.

**Reading-1 spirit survives intact.** A candidate who genuinely shares a skill
or nice-to-have with a no-requirement ad (`Vacuous AND HasSkillOrNiceSignal`)
still reaches Strong/Top. Only the entirely-evidence-less preference-fit case —
`Vacuous AND NOT HasSkillOrNiceSignal` — is demoted. That case had no positive
evidence for the badge; the audit confirmed it affected ~98 % of graded ads.

### 1. The revised ladder (SSOT — replaces Amendment 2026-06-20 §1)

`MatchGradeCalculator.Grade(FullMatchScore)` is a pure, total function.
Read top-down; first matching row wins. `SSYK = Match` is the gate throughout.
`requirementBacked` is defined above.

| Step | Condition (`SsykOverlap = Match` assumed; evaluated top-down) | Grade |
|---|---|---|
| **Gate** | `SsykOverlap ≠ Match` | **null** (no tag) |
| **Floor** | `RegionFit = NoMatch` OR `EmploymentFit = NoMatch` (a STATED preference contradicted) | **Basic** |
| **Top** | `requirementBacked` AND region & employment both `Match` AND `HasSkillOrNiceSignal` | **Top** |
| **Strong** | `requirementBacked` AND region & employment both `Match` | **Strong** |
| **Strong (open-secondary)** | `requirementBacked` AND at least one of region/employment is `Match`, the other `NotAssessed` (none contradicted) | **Strong** |
| **Good** | NOT `requirementBacked` AND at least one of region/employment is `Match` | **Good** |
| **Basic** | otherwise (SSYK `Match`, no confirmed secondary, `requirementBacked` not satisfied) | **Basic** |

The table is **total**: the final `Basic` row catches all remaining reachable
tuples. The `Good` row collapses the two prior `Good` rows (both `Match`, or
exactly one `Match`) into one: `NOT requirementBacked AND ≥1 confirmed
secondary`. This is an editorial simplification; the set of reachable tuples
is identical.

**Changed cells vs. Amendment 2026-06-20 §1 ladder:**
Exactly one cell changes in reachable behaviour. The prior **Strong** (open-
or closed-secondary) row matched `MustHaveCoverage = Vacuous` with NO
HasSkillOrNiceSignal: that tuple now lands on **Good** (≥1 confirmed secondary)
or **Basic** (no confirmed secondary). All other cells are identical.

The four Swedish chip words are unchanged: `Top` = "Toppmatch",
`Strong` = "Stark match", `Good` = "Matchar dina önskemål",
`Basic` = "Grundmatch". `Strong`/`Top` now ALWAYS carry either a covered
must-have (`Match`) or a shared skill/nice-to-have signal with a no-requirement
ad (`Vacuous + HasSkillOrNiceSignal`). FE copy `uploadCvFoot` ("Det krävs för
Stark match och Toppmatch") remains accurate — in fact more so, because a
bare `Vacuous` (no CV skill overlap) no longer reaches Strong/Top.

### 2. Hard-case ruling update — Vacuous sub-cases (amends §2(b) of Amendment 2026-06-20)

**(b′) Ad has NO extracted must-haves — `Vacuous`, gate conditional on
`HasSkillOrNiceSignal` (Reading 1 narrowed; amends Reading 1 from
Amendment 2026-06-20 §2(b)).**

The `Vacuous` verdict and its five-member enum position are unchanged. The
gate-open condition is narrowed from "unconditional" to
"conditional on `HasSkillOrNiceSignal`":

- **`Vacuous` AND `HasSkillOrNiceSignal`** — `requirementBacked = true`.
  The candidate has no formal requirement to meet AND shares a skill/nice-to-have
  signal. CAN reach Strong/Top. Copy constraint from Amendment 2026-06-20
  preserved: modal must NOT say "du uppfyller alla krav" — show
  "inga specifika skallkrav angivna."
- **`Vacuous` AND NOT `HasSkillOrNiceSignal`** — `requirementBacked = false`.
  No must-have, no skill overlap, no nice-to-have overlap. Caps at `Good`
  (≥1 confirmed secondary) or `Basic`. This is a pure preference-fit match on a
  no-requirement ad; the chip is preference-framed ("Matchar dina önskemål"),
  not qualification-framed. No user-visible demotion copy is required — the
  modal's empty must-have row ("inga specifika skallkrav angivna") and empty
  skill section are self-explanatory.

Sub-cases (a), (c), and (d) of Amendment 2026-06-20 §2 are **unchanged**:
- (a) No CV / `NotAssessed` → ceiling `Good` (unaffected — `NotAssessed` was
  never `requirementBacked`).
- (c) `NoMatch` → capped at `Good`/`Basic` (unaffected).
- (d) `Partial` → capped at `Good` (unaffected).

### 3. `mustHaveMet` predicate retired — `requirementBacked` replaces it

The internal predicate name `mustHaveMet` (introduced in Amendment 2026-06-20
§3, last line) is retired. `requirementBacked` is the canonical name in code
and tests from this amendment forward. The semantics of `requirementBacked`
for `Match` are identical to the old `mustHaveMet` for `Match`; only the
`Vacuous` branch is narrowed.

`ScoreConceptCoverage`'s emission rules for `Vacuous` / `NotAssessed` are
**unchanged** — the change is entirely in `MatchGradeCalculator.Grade`, not in
the scoring layer.

### 4. Oracle and arch-pin updates

**`MatchGradeCalculatorTests`:** the oracle table is re-derived with
`requirementBacked` as the predicate column. New oracle rows are added for the
previously-untested `Vacuous + NOT HasSkillOrNiceSignal` tuples that now map to
`Good`/`Basic`. The totality proof is re-run over the full
`{Match,Partial,NoMatch,NotAssessed,Vacuous} × secondaries × signal` space.

**Sort oracle (`MatchSortedJobAdSearchQuery` Testcontainers):** unchanged.
The sort path has no concept of `requirementBacked`; it operates on Fast
dimensions + binary skill overlap (G3-OPT-A). The documented "sort-band is a
strict coarsening" invariant from Amendment 2026-06-20 §4 is preserved.

**Arch pin `MatchDimensionVerdict_still_is_the_locked_N_member_set`:** still
five members — no new verdict added. Pin unchanged.

### 5. Goodhart reaffirmed (unchanged)

`MatchGrade` remains a named four-member ordinal enum `{Basic,Good,Strong,Top}`.
No number, no percentage, no ring. `requirementBacked` is a categorical
boolean predicate on categorical verdicts — no magnitude introduced. The
narrowing tightens the qualification claim; it does not introduce a score.

### 6. Cross-reference updates

- **Amendment 2026-06-20 §2(b) (Reading 1 / `Vacuous` gate-open):** narrowed
  by this amendment. The unconditional gate-open sentence in §3
  ("For the grade rule: `MustHaveCoverage ∈ {Match, Vacuous}` satisfies the
  must-have gate. This is the `mustHaveMet` predicate used throughout the ladder.")
  is superseded by the `requirementBacked` definition above.
- **Amendment 2026-06-20 §1 (ladder table):** replaced by §1 of this amendment.
- **ADR 0053 modal copy:** no structural change. The `Vacuous` case copy
  ("inga specifika skallkrav angivna") already appears for both
  `Vacuous + HasSkillOrNiceSignal` (Strong/Top) and
  `Vacuous + NOT HasSkillOrNiceSignal` (Good/Basic). No new copy variant needed.
- **ADR 0045 (performance budget):** unchanged. `requirementBacked` is a single
  boolean expression evaluated in-process; no additional I/O.
- **ADR 0071 (no AI; Goodhart guard):** reaffirmed. Deterministic throughout.
- **`uploadCvFoot` FE copy:** remains accurate. "Det krävs för Stark match och
  Toppmatch" is now strictly true — uploading a CV is required, and the CV must
  produce a skill/nice signal (or cover a must-have) for Strong/Top to appear.
  No FE copy change required by this amendment.

**Persisted-grade note (no migration).** `UserJobAdMatch.Grade` rows written by a
prior background scan may now be stale (a `Vacuous`-without-signal match recorded as
`Strong` would compute `Good` under this amendment). The background job's
`UNIQUE(UserId, JobAdId)` dedup spine **skips already-persisted pairs**, so a re-scan
does NOT self-heal historical grades. This is harmless in pre-prod (no real persisted
matches at scale); no migration is taken. If pre-prod persisted data ever had to be
preserved correctly, a one-off re-grade backfill would be required — out of scope now.
(dotnet-architect F1(b) review, 2026-06-27.)

## Amendment 2026-06-29 — Unified Fast/Full divergence rule (formalizes G3-OPT-A across filter + sort + badge + count; issues #371, #382)

**Date:** 2026-06-29
**Instrument:** amendment-note. No code/behaviour change to the engine, the sort,
the filter, or the count (all stay byte-for-byte). This records, as one bound rule,
the divergence already adopted as G3-OPT-A (Amendment 2026-06-20 §4) — extending the
statement from the sort to ALL three quantitative list surfaces and naming its
relationship to the badge. No AI/LLM.
**Decision-maker:** senior-cto-advisor (2026-06-29), Option (a) — formalize the
deliberate divergence; (b)/(c)/(d) rejected (below). Klas may override per roadmap.
**Driver:** #382 (sort manifestation — "Bra match" interspersed among "Toppmatch")
and #371 (filter manifestation — `?matchGrades=Strong` can return a card whose badge
reads "Bra match"), found by the #268 grade-precision audit. Same G3-OPT-A root.

### The unified rule (SSOT statement)

> The /jobb list's three quantitative surfaces — **SORT, grade-FILTER and headline
> COUNT** — all rank/gate on the **Fast band** (`MatchGradeCalculator.Grade(MatchScore)`,
> mirrored in SQL by `PerUserJobAdSearchQuery.GradeRankExpression`). The card **BADGE**
> shows the **Full** requirement-aware grade (`MatchGradeCalculator.Grade(FullMatchScore)`,
> F1(b)-gated). Fast is an **honest coarsening** of Full: there is no Kind-separated
> must-have-lexeme column, so Fast can compute neither Top (upward) nor the F1(b)
> degrade (downward). The divergence is therefore **bidirectional and bound** — a
> Fast-Strong ad can badge Top (requirement-backed + skill signal) or Good (F1(b) not
> met; ~98% Vacuous ads without skill overlap, or no CV) — and is **documented +
> oracle-pinned**, never drift. Direction: **Fast ≤ Full-informed** (Fast never asserts
> more requirement evidence than the badge). Sort/filter/count order on **preference
> strength**; the badge expresses **requirement strength**. They are two questions.

### Oracle pins (anti-drift)

- **Sort side (#382):** `MatchSortOracleTests.SearchByMatch_DoesNotSeparateSameFastTupleByMustHave_WhileGradeWould_DivergenceG3OptA` — two same-Fast-tuple ads whose Full grade differs (Top vs Good) are NOT separated by the SQL sort.
- **Filter side (#371, added 2026-06-29):** `MatchSortGradeFilterOracleTests.GradeFilter_StrongBand_ContainsAds_WhoseFullBadgeDiffers_BoundDivergenceG3OptA` — the `{Strong}` filter bucket contains both a Full-Top and a Full-Good ad (filter gates on Fast; badge is Full). Count is the in-band Fast size.
- Both oracle files' `<summary>` doc-comments now state this unified rule and cross-reference each other + this amendment.

### Rejected alternatives

- **(b) Align sort/filter to the Full badge (materialize F1(b) + skill signal in SQL):**
  rejected. Requires a Kind-separated must-have-lexeme column + GIN on the EF-migration
  hotspot and the requirement logic in WHERE/ORDER BY — a structural perf/schema cost
  against the **ADR 0045** latency budget, to solve a *communicative*, not functional,
  problem. If full-grade list ranking ever becomes a stated requirement it is a separate
  phase decision with its own ADR + migration ownership.
- **(c) Show the Fast grade on the badge:** rejected — regressive; loses Top and the
  F1(b) precision win (Amendment 2026-06-27 / #268).
- **(d) Tie-break approximation of the badge in the sort:** rejected — a covert (b) that
  breaks the already-pinned one-directional sort invariant; the sort stays byte-for-byte.

### FE disclosure

The grade-filter help copy (`jobads.ui.gradeFilter.help`, sv + en) now states both
divergence directions (filter/sort = preference fit; badge can read Top or a level
below) without promising per-card exactness — the honest-by-design disclosure of this
rule. No design-token change.

**Cross-refs:** ADR 0084 (Related rung), ADR 0045 (latency budget — argues against (b)),
Amendment 2026-06-20 §4 (G3-OPT-A origin), Amendment 2026-06-27 / #268 (F1(b)), issues
#371, #382. (senior-cto-advisor ruling 2026-06-29; code-reviewer + design-reviewer APPROVE.)

---

## Amendment 2026-07-18 — RB1's NULL-facet gap closes: a stated ort/employment preference is contradicted by an absent ad facet, not only a mismatched one (#552)

**Date:** 2026-07-18
**Instrument:** first-class dated amendment. The five-STEG feature boundary
("match-tag + sort") and the requirement-aware ladder TABLE (Amendment
2026-06-27 §1) are both intact — the ladder's rows and grade names are
unchanged. What changes is upstream: which `MatchDimensionVerdict` the scorer
emits for `RegionFit`/`EmploymentFit` when the ad's own facet is absent. This
amends the RB1/Floor-row *semantics* (which situations reach the floor), not
its wording. Supersession is not the correct instrument (Nygard 2011).
**Driver:** Klas Olsson (AskUserQuestion 2026-07-17/18, Q1 — "ship now,
documented remote limitation, #551 re-admits when it lands"); senior-cto-advisor
(Approach A bind — close the hole in the emitted VERDICT, not in the calculator).
**Decision-makers:** Klas Olsson (product GO; the #551 forward-note below);
senior-cto-advisor (mechanism bind: which layer absorbs the fix).
**Implemented in:** `MatchScorer.cs` (`ScoreOrtUnion`; `ScoreMembership` renamed
`ScoreEmploymentMembership`), `PerUserJobAdSearchQuery.GradeRankExpression` (SQL
twin), oracle/integration test suites. Commits 8c3b911b (FE), 6cc4285c
(RED-first oracles), a06d2f05 (gate), branch `feat/grade-gate-ort-form-552`,
issue #552.

### Context — the hole the count closed but the grade did not

A prior step (issue #554, referenced but not itself amended here) made the
notis/setup facet-COUNT honest: an ad with a NULL ort or employment shadow no
longer counts toward a user's stated ort/employment preference. But the
GRADE (the ≥Good badge shown on `/jobb`, the follow-hub, the background scan)
still ranked such an ad as if the missing facet were merely unassessed:
`ScoreOrtUnion` and the pre-existing shared `ScoreMembership` returned
`NotAssessed` whenever the ad's own facet value was absent — the same rule
used for an UNSTATED user preference (the vacuous-gate doctrine, Amendment
2026-06-20 §2 Floor row). Under a both-stated profile (region AND employment
both preferred), one confirmed secondary plus one NotAssessed secondary still
reached `Good` — so a locationless or formless ad could badge Good/Strong for
a user who had explicitly constrained that dimension. The count (a
search-facet question) and the grade (a "does this ad fit you" question)
disagreed on the same ad, and the grade was the one being too generous.

### The widened rule (RB1 Floor row semantics — the ladder table is unchanged)

The Amendment 2026-06-27 §1 ladder table is **not edited by this amendment**.
Its Floor row still reads, verbatim:

> `RegionFit = NoMatch OR EmploymentFit = NoMatch (a STATED preference
> contradicted) → Basic`

What changes is which situations `MatchDimensionVerdict.NoMatch` is emitted
for, upstream in `MatchScorer` — the verdict *emission* widens, not the grade
*rule*:

- **`ScoreOrtUnion`:** a STATED ort preference against an ad whose region AND
  municipality shadows are BOTH NULL now returns `NoMatch` with EMPTY
  `Matched`/`Missing` (nothing to cite — the ad is silent), not `NotAssessed`.
  An UNSTATED preference is untouched — still `NotAssessed` (vacuous-gate
  doctrine). The pre-existing `#477` containment carve-out (a län-only ad
  whose län contains a preferred kommun) is untouched — it still reads
  `NotAssessed`, not `NoMatch`; the new both-NULL branch fires only when the ad
  states NEITHER ort value at all, so it never intercepts the containment case.
- **`ScoreMembership` renamed `ScoreEmploymentMembership`** (employment is its
  only remaining consumer — SSYK had already split into its own
  `ScoreSsykMembership` as part of ADR 0084 §F4's exact-vs-related split, prior
  to this amendment): a STATED employment preference against a NULL ad
  `EmploymentTypeConceptId` now returns `NoMatch` with empty evidence, not
  `NotAssessed`. An UNSTATED preference is unaffected.
- **SSYK is explicitly NOT gated this way.** `ScoreSsykMembership` keeps its
  original rule: an ad with no occupation group stays `NotAssessed`. SSYK is a
  hard MEMBERSHIP GATE (Amendment 2026-06-27 §1 "Gate" row), not a Floor-row
  secondary — widening it would change which ads receive a tag at all, a
  different and out-of-scope axis.
- **Title and skill dimensions are untouched** — they were never Floor-row
  secondaries.
- **`MatchGradeCalculator.Grade(FullMatchScore)` is UNCHANGED.** No new logic
  runs in the calculator. The existing Floor row consumes the new verdict
  automatically — the fix lives entirely in the verdict the calculator
  receives, per senior-cto-advisor's Approach A bind. The calculator's
  total-function proof over the five-member `MatchDimensionVerdict` space
  stays valid without re-derivation.

### The SQL twin — a three-valued-logic correction, not a new predicate

`PerUserJobAdSearchQuery.GradeRankExpression` mirrors the new verdict rule with
an EXPLICIT `== null` disjunct on the RegionFit/EmploymentFit floor branch —
never a bare `!list.Contains(nullableColumn)`. Under raw SQL three-valued
logic, `NOT (col = ANY(@list))` evaluates to `NULL` (never `TRUE`) when
`col IS NULL`, so a bare negated-Contains would silently fail to floor a
NULL-shadow ad in the database — diverging from the in-memory scorer without
any error. The explicit `IS NULL` branch is required precisely because
Postgres does not let a NULL column satisfy a `NOT IN`-style predicate. The
Testcontainers oracle suite (`MatchSortOracleTests`, `MatchSortGradeFilterOracleTests`,
`MatchSortGoldenRungOracleTests`, `MatchCountOracleTests`) pins that the SQL
rank expression and the in-memory `MatchScorer`/`MatchGradeCalculator` path
agree on every reachable verdict tuple — the same SQL≡C# parity discipline
established by Amendment 2026-06-20 §4 (G3-OPT-A) and extended by ADR 0084 §F4.

### The ripple — Good becomes containment-only under a both-stated profile

Before this amendment, a both-stated profile could reach `Good` via "one
secondary confirmed, the other silent on the ad → NotAssessed → not a
contradiction." That path closes by design: an ad silent on a STATED secondary
now floors to `Basic` via the (unchanged) Floor row. The one surviving route
to a NotAssessed secondary under a both-stated profile is the pre-existing
`#477` containment carve-out (a län-only ad whose län contains a preferred
kommun still reads `RegionFit = NotAssessed`, not `NoMatch`, because the ad DID
state a region — just not at kommun grain). The golden-rung oracle seed
(`MatchSortGoldenRungOracleTests`) was converted accordingly: the prior
"employment NULL → NotAssessed → Good" seed no longer expresses Good under a
both-stated profile, so the oracle now reaches Good via the containment route
(region via a län-only containment ad → `NotAssessed`; employment stated and
matched) — a seed that is stable in both the pre- and post-#552 epoch, rather
than one that silently changed meaning underneath the test.

This is an accepted, honest narrowing: it was never correct to badge Good for
"you told us where you want to work, and this ad won't say" — the gate makes
the ad's silence on a dimension the user constrained read exactly like any
other contradiction.

### One-off purge (accompanies the gate)

Because the fix widens the VERDICT rather than adding a post-hoc filter, ads
that would never have been persisted as a `UserJobAdMatch` under the new gate
(a NULL-facet ad against a user's CURRENT stated ort/employment preferences)
can still sit in the table from before the gate shipped. The background scan's
`UNIQUE(UserId, JobAdId)` dedup spine (ADR 0080 Beslut 1/2) never re-grades an
already-persisted pair, so these rows would otherwise remain as noise on
`/matchningar` and in digests indefinitely, with no natural self-heal — the
same structural reason ADR 0076 Amendment 2026-06-27 already recorded for the
F1(b) narrowing. A one-off purge deletes exactly the rows the gate would never
have created for the user's CURRENT stated preferences. Re-creation of a
purged row is impossible after the gate ships (the scorer will never again
emit the verdict that produced it) — a one-time cleanup, not a recurring job.
See ADR 0080's own cross-reference note (2026-07-18) for how the scan's
persist path absorbs this.

### Mutation-verification

The SQL-twin arms this amendment touches (the both-NULL region floor, the
both-NULL employment floor, the `#477` containment carve-out staying open, and
the unstated-preference vacuous case staying `NotAssessed`) are each
independently mutation-verified — flipping any one disjunct in
`GradeRankExpression` fails a corresponding Testcontainers oracle test. The 13
RED-first tests (commit 6cc4285c) encode the bound behaviour across all
surfaces this amendment touches: in-memory (`ScoreAsync`/`ScoreBatchAsync`/
`ScoreFullBatchAsync`), SQL (sort/filter/count oracles), and the background
scan (a gated ad is never persisted as `UserJobAdMatch`).

### #551 forward-note — the documented remote-work limitation this gate accepts

`JobAd` carries no remote/distans signal today. A truly locationless ad (BOTH
region and municipality NULL) is, under this gate, floored to `Basic` for any
user with a stated ort preference — even if the ad is in fact fully remote and
would in reality satisfy that user's ort preference. Klas accepted this as a
documented limitation (Q1, 2026-07-17/18) rather than blocking the gate on a
remote-signal feature that does not yet exist: closing the "silently too
generous" hole now is worth more than waiting for #551. When issue #551 lands
an explicit remote/distans signal on `JobAd`, that signal OVERRIDES the ort
gate — a remote ad is a location-match for everyone, stated preference or not.
This gate gates on KNOWN location; #551 adds the signal the gate then honors.
No code in this amendment anticipates #551's shape; this is a forward-note,
not a TD (the gap is a known, accepted, temporary product trade-off, not
neglected work).

### Cross-reference updates

- **ADR 0076 Amendment 2026-06-20 §1 (ladder table):** unchanged; the Floor
  row's wording still holds. This amendment changes only which verdicts feed it.
- **ADR 0076 Amendment 2026-06-27 (Vacuous-with-signal gate):** unaffected —
  that amendment narrows the must-have gate (`requirementBacked`); this
  amendment widens the RB1 Floor row. Independent axes.
- **ADR 0076 Amendment 2026-06-29 (Unified Fast/Full divergence rule,
  G3-OPT-A):** unaffected in principle — Fast and Full both consult the same
  widened `ScoreOrtUnion`/`ScoreEmploymentMembership` verdicts (both paths call
  the same scorer methods), so the bidirectional Fast/Full divergence bound is
  preserved; no new divergence is introduced.
- **ADR 0080 (background matching):** the scan's persist path inherits the
  gate automatically (same scorer, same calculator) — see ADR 0080's own
  2026-07-18 cross-reference note for the purge detail.
- **ADR 0079 (two-count model):** the notis/setup counts remain intentionally
  facet-hard and untouched by this amendment — see ADR 0079's own 2026-07-18
  amendment for why literal grade-collapse into the count was rejected again.
- **ADR 0084 (Related rung, F4 evaluation order):** untouched. The
  Related-cap is evaluated BEFORE the RB1 Floor row (ADR 0084 §F4), and this
  amendment changes only the Floor row's input verdicts, not the evaluation
  order or the Related-cap itself. A related-occupation ad with a NULL-facet
  location still reads `Related`, never `Basic` — see ADR 0084's own
  2026-07-18 verification note.
- **ADR 0071 (no AI; Goodhart guard):** reaffirmed. The widened rule is a
  categorical branch on existing categorical verdicts — no magnitude, no
  score, no new enum member.

## Amendment 2026-07-18 (b) — #551: a remote/distans signal OVERRIDES the ort gate

**Date:** 2026-07-18
**Instrument:** amendment realising the `#551 forward-note` above (the
documented remote-work limitation the #552 gate accepted, §"#551
forward-note"). No new ADR number — this discharges an existing forward-note,
exactly as #552 was itself an amendment.
**Driver:** Klas Olsson (AskUserQuestion 2026-07-18: source = AF's own
`remote=true` classification; scope = full bow). senior-cto-advisor bind
(D1–D9, `docs/reviews/2026-07-18-551-remote-distans-cto.md`).
**Related:** this amendment's own trigger is the 2026-07-18 (a) #552 gate above;
ADR 0067 amendment 2026-07-18 (the ingest source); ADR 0079 (facet vs
grade-override split); ADR 0071 (Goodhart — categorical, no score).

### The decision

`JobAd` now carries an explicit `remote` signal (a `bool NOT NULL` facet). Per
the forward-note, **that signal OVERRIDES the ort gate: a remote ad is a
location-match for everyone with a stated ort preference, regardless of which
ort they stated and regardless of whether the ad also names a (non-matching)
location.** This closes the "silently too generous"-in-reverse gap the #552
gate documented: a genuinely remote ad is no longer floored to Basic for a
location-scoped user.

### Mechanism (grade path ONLY — never the facet counters)

- **Scorer** (`MatchScorer.ScoreOrtUnion`): a new `adRemote` branch returns
  `RegionFit = Match` with EMPTY matched/missing evidence. Placement is
  load-bearing (CTO D4): **AFTER** the `!stated` early-return — an unstated-ort
  user has no gate to override, and lifting their `RegionFit` to `Match` would
  fabricate a confirmed secondary they never asked for (vacuous-gate doctrine;
  "for everyone" means regardless of WHICH ort, not "even for users who stated
  none") — and **BEFORE** the both-NULL #552 gate and the union logic (a remote
  ad is never floored, and remote wins over a non-matching tag). `Match` (not
  `NotAssessed`) is FORCED: a stated-single-ort user needs ≥Good, and
  `NotAssessed`+`NotAssessed` = 0 confirmed secondaries = Basic.
- **`MatchGradeCalculator` is UNTOUCHED** — a `Match` `RegionFit` both clears
  the RB1 floor and earns a confirmed secondary automatically (verdict-driven
  parity, exactly the #477 model).
- **SQL twin** (`GradeRankExpression`): two-valued welds mirroring the scorer —
  the RB1 ort-floor arm gains `&& !remote` (a remote ad is never ort-floored),
  and the Strong/Good secondary disjuncts gain `|| (ortStated && remote)` (a
  remote ad counts as a confirmed ort secondary, but only for a stated-ort user,
  mirroring the scorer's placement after `!stated`). Every non-remote path is
  **byte-identical** (`remote = false` ⇒ `!remote` true, `ortStated && false`
  false). `remote` is read as a non-null `bool`, so the three-valued-NULL trap
  the #552 code pays for does not reach this term.
- **Evidence:** `RegionFit = Match` with empty evidence (no region concept-id to
  cite — the ad has none or a non-matching one; a magic-string "distans"
  sentinel in `Matched` is a §5 anti-pattern that would break the taxonomy-label
  resolver). The human reason "Erbjuder distansarbete" rides a dedicated
  match-detail explainer flag delivered with its FE consumer (PR-C), not this
  shared `MatchDimension`.

### Ingest source (CTO D1) and its risk posture (CTO D2)

The signal is **AF's own `remote=true` classification**, harvested once per
nightly snapshot via a paginated `jobsearch.api.jobtechdev.se/search?remote=true`
query (the response schema carries no per-ad remote field — see the ADR 0067
amendment 2026-07-18). A failed harvest leaves the column untouched (never
flips the corpus to false; the transport facet is `bool?` with preserve
semantics). AF's classification is **derived, not ground truth**: a
false-positive-remote ad lifts a locationless ad off the #552 floor for a
location-scoped user. This residual is **accepted as a disclosed, bounded
trade-off** (parity with how #552 documented its own): it is (a) gated behind
occupation `Match` (only ads already passing the SSYK gate benefit), (b)
surfaced with a remote evidence marker (user-visible, PR-C), and (c) parity
with the source authority — the same classification that powers Platsbanken's
own remote filter. A precision guard was rejected: any honest narrowing would
suppress exactly the true-positive remote ads the feature exists for, trading a
small disclosed leak for a large silent recall loss (ADR 0071
explainable-by-design; CLAUDE.md §1/§5).

### Cross-reference updates

- **ADR 0076 Amendment 2026-07-18 (a) (#552 gate):** the trigger this amendment
  re-admits from. The #552 gate floors a stated-ort user's locationless ad; this
  amendment carves the remote-classified subset back out. The two are one
  coherent story: gate on KNOWN location, honor the remote signal that says
  location is irrelevant.
- **ADR 0079 (two-count model):** remote-the-**grade-override** (this amendment,
  ad-driven, grade path) is distinct from remote-the-**facet** (PR-B, user-driven
  `Distans` filter selection, the facet-hard counters). The scorer reads the
  ad's flag, NEVER the user's remote preference — the ADR 0079 "never
  grade-coupled" line. See ADR 0079's own 2026-07-18 cross-reference note.
- **ADR 0084 (Related-cap, F4 order):** untouched. The Related-cap is evaluated
  before the RB1 floor; the remote override changes only the ort verdict feeding
  the floor/secondary arithmetic, not the evaluation order. A related-occupation
  remote ad still reads `Related` (the cap wins).
- **ADR 0071 (no AI; Goodhart):** reaffirmed. The override is a categorical
  branch on a categorical `bool` — no magnitude, no score, no new enum member.

## Amendment 2026-08-31 — A membership dimension carries its own cause (the wire stops making the client guess)

**Status:** Accepted. **Beslutsfattare:** senior-cto-advisor (form bind 2026-08-31),
Klas (scope: all three membership dimensions, not only the two RegionFit findings).
**Föranleds av:** the follow-up the #1598 close-out routed, plus a fifth instance the
CTO measured during that bind.

### The defect class

Three membership dimensions have arms that return **empty evidence**, and two of them
reach the same verdict from two different reasons. `ScoreOrtUnion` alone has four
empty-evidence exits across three verdicts; `ScoreSsykMembership` folded two reasons
into a single `NotAssessed` guard.

The client therefore could not recover the reason from `(verdict, emptiness, dimension)`
— **that mapping is not injective** — so it derived one. Three measured consequences:

1. **Remote (`MatchScorer.ScoreOrtUnion`, the `adRemote` arm).** `Match` with both lists
   empty. Every span in `RegionFitEvidence` is guarded on `length > 0`, so the modal
   rendered **nothing** under the word "Matchar".
2. **#477 containment.** `NotAssessed`, which the modal captioned
   *"Du har inte angett någon region."* — to a user who had named a **kommun**.
3. **SSYK (the fifth instance).** A `NotAssessed` `ssykOverlap` made
   `JobAdMatchSection` replace the **entire** match section with a signpost about the
   user's own settings plus a link to a setting she had already filled in — when the
   silent side was the **ad's** occupation group.

Measured in the dev corpus 2026-08-31 (n = 106 071 ads): 30 092 ads (28.4 %) carry no
`occupation_group_concept_id`, and 790 are remote. The län-only shape the containment
arm needs was observed **zero** times, though `PlatsbankenJobSource` maps the two ort
facets independently from `workplace_address`, so the source can produce it. The box
itself is unmeasured.

### Decision

`MatchDimensionCause` — a bounded discriminator with four members
(`PreferenceUnstated`, `AdSilent`, `RemoteOverride`,
`RegionContainsPreferredMunicipality`), serialized by name, **nullable everywhere with
no `None` member**: absence means the evidence explains itself, the doctrine
`MatchRegisterConceptDto.Label` already carries.

It rides **beside** the score on `FullScoredMatch` (`MatchDimensionCauses`, one member
per membership dimension, named identically to the modal rows), and lands as one
nullable field on `MatchRegisterDimensionDetailDto` and `MatchCodedDimensionDetailDto`.
`MatchDimensionDetailDto` is untouched — its four dimensions have no arm whose reason is
unrecoverable.

**Rejected: a new `MatchDimensionVerdict` member.** That enum is the **grade**
vocabulary — `MatchGradeCalculator` reads it in nine places, it has a SQL twin in the
rank expression, it rides the batch wire, and it is arch-pinned to five members.
Widening it would give one type two change-reasons, and a `RemoteOverride` verdict would
have to be treated as `Match` at every one of those nine sites. `jobAdMatchBatchSchema`
also `.catch({})`s an unknown value, blanking every tag on the page.

**Rejected: a field on `MatchDimension`.** Seven dimensions share that type and four can
never bear a cause; `FullMatchScore` is shape-frozen by the Goodhart pin. Carrying a
scorer-computed signal beside the score is what the `FullScoredMatch` carrier already
exists for (CTO carrier-bind 2026-06-28).

### Copy ownership

The code is bounded; the **word** is the catalogue's, keyed on the pair
`matchCause.<Cause>.<dimensionKey>` — the same cause needs different words per
dimension. `adUnspecifiedReason.*` is **deleted** (replaced), and `notAssessedReason`
keeps only `titleSimilarity` / `skills` / `default`: the three dimensions that still have
exactly one way to be unassessed. A dimension with one cause needs no code.

The zod field is **strict, with no `.catch`** — a tolerant default would silently degrade
a cause back to the derived branch, reinstating the defect as silence. The price is
atomic shipping: an unknown value or a missing key takes the whole match section
(`getJobAdMatchDetail` degrades to `null`). Both are pinned by test rather than asserted.

### What this does NOT change

- No verdict moves. The SSYK guard was split into two arms with a byte-identical
  predicate and verdict, and `MatchGradeCalculator` is untouched.
- The Fast scorer paths discard the cause: there is no Fast carrier and no production
  grade caller for them (the Fast/Full asymmetry ADR 0084 already records).
- ADR 0071's Goodhart guard is reaffirmed — a named category, no magnitude, arch-pinned
  to a locked four-member set.

### Open, and deliberately not closed here

The rendered verification measured that
`.jp-modal__matchrow-evidence .jp-modal__matchrow-missing` sets `var(--jp-ink-1)`, the
same token the parent already carries, so the "neutral ink" several comments describe is
visually identical to ordinary evidence text. That is **pre-existing CSS**, not this
delta, and changing it is a design-token decision (DESIGN.md), not a comment fix. Two
comments that named `ink-2` were corrected by deletion; the design question is raised to
`design-reviewer` separately.

---

## Amendment 2026-10-03 — identity-preserving detail evidence and honest requirements (#1864/#1872)

This amendment supersedes the earlier detail bindings that rendered every empty
requirement row, described Vacuous as a requirement-free ad, repeated an all-requirements
summary, or made CV upload a prerequisite for Strong/Top. Historical grading decisions
remain intact; this change does not alter verdicts, grade calculation, extraction or storage.

The current profile builder reads confirmed PreferredSkills without a primary CV.
CV import can propose skills which the user confirms; only title similarity reads the
CV role. The empty confirmed-skill set produces NotAssessed in all three skill/requirement
dimensions. Vacuous means confirmed skills exist but this extracted ad partition is empty.
The extractor's structured requirements cover skills only, not all requirements in prose.

FullScoredMatch carries a required FullMatchConceptEvidence beside the frozen score:
each dimension partitions paired ConceptId/Display entries at the same point as its legacy
display arrays. The detail read projection uses the existing ISkillResolver.GroupConceptIds
separately for each dimension and matched/missing side. Every member survives, including
unknown ids with their original Display; grouping never changes the score or its evidence.
Display strings are not identities and may not be used to reconstruct ids or deduplicate
unrelated concepts. Cross-side and cross-dimension repetition remains meaningful.

The three dedicated skill row DTOs retain verdict/matched/missing exactly and add nullable
conceptEvidence with matched/missing groups (Display plus paired members). New clients
accept absent/null evidence from an older API and render every legacy display occurrence.
Present evidence is strict: empty is distinct from absent, and malformed/lossy partitions
fail parsing rather than silently falling back. Old clients continue to read legacy fields.
The recursive Goodhart wire guard covers groups and their members; no numeric total is added.

The shared full-page/modal view names separate matched and unmatched lists. It uses
body/ink-1 words, success Check for matched evidence, neutral Minus and dashed strong
border for unmatched evidence, without interaction affordances. Group words and distinct
original member words stay visible. Requirement labels are "Obligatoriska krav" and
"Meriterande" and refer only to skill requirements. Proven-empty Vacuous requirement rows
are omitted; NotAssessed stays. A single precise missing-data note directs the reader to
the ad text. No summary claims every employer requirement is met. Skill setup directs the
user to matching settings; it does not require CV upload.

The dated aggregate measurement and reproducible read-only SQL belong in
docs/research/issues/1864-match-detail-evidence.md. They are observations, not live constants.

---

## Amendment 2026-10-03 (b) — #1963: the job card presents the evidence as a checklist (Checklistan)

This amendment changes how the job-ad detail **presents** the evidence (the intercepted modal and
`/jobb/[id]`). It changes no verdict, grade, extraction, persistence or wire shape. The data rules of
the amendment above stand: concept identity on the wire, strict parsing, and display strings are not
identities. Klas approved the Claude Design handoff "Checklistan" on 2026-10-03 and answered two
planning questions the same day; both answers are quoted verbatim below.

It supersedes these presentation bindings, of the amendment above and earlier ones:

1. **Colour carries the outcome, beside an icon and a word** (DESIGN.md §3):
   - matched evidence and a Match dimension take the success family;
   - a Partial or NoMatch dimension and a missing skill take `--jp-warning`;
   - a missing obligatory requirement, and nothing else, takes `--jp-danger`.

   Unmatched evidence is no longer neutral ink with a dashed border.
2. **A dimension without an assessment (NotAssessed, Vacuous) is not rendered.** This retires "NotAssessed
   stays" and the neutral "Ej bedömt" rendering for the detail. An AdSilent NoMatch dimension (place,
   employment type) is still rendered, with the ad's reason as its value, because it lowers the grade.
   Klas: *"Visa som orange rad (Rekommenderas)"*. When the ad states no occupation group, the grade is
   null and no row explains why, so the ad's reason stands as one neutral line under the section's
   heading. A Titel row keeps the warning tone for Partial and NoMatch, although the title never moves
   the grade; with #1871 open it renders for almost no one.
3. **A chip shows its group's display only.** Distinct member words are no longer shown. The taxonomy's own
   qualifiers stay, because they tell distinct concepts apart. Klas: *"Behåll taxonomins parenteser
   (Rekommenderas)"*. Comma suffixes also stay until he decides otherwise.
4. **"Cross-dimension repetition remains meaningful" is narrowed.** A skill-overlap group that shares a concept id
   with a requirement chip on the same side is shown once, in the requirement group. Cross-side
   repetition is never merged, and display-only legacy evidence is never deduplicated.
5. **The counter beside the skills** counts distinct matched and not-in-profile skills over every rendered
   chip, collapsed ones included; overlapping id sets count once. Legacy evidence gets no counter. The
   counter counts the named chips shown beside it and enters no wire, so the Goodhart guard
   (Decision 4) is untouched. Percentages, ratios, "x av y", gauges and rings stay forbidden.
6. **The matching-settings link** is shown only in the notice for a user with no stated occupation. The
   notice for a user without confirmed skills names where the skills are chosen instead of linking there.

The following are unchanged:
- the five wire verdicts;
- the grade ladder;
- the requirement note's substance;
- the rule that empty extracted data never implies a requirement-free ad.

The producer facts this rests on were measured at `ad2ce55f9` on 2026-10-03:
- `MatchScorer` scores an AdSilent place or employment type as NoMatch, which floors the grade at Basic;
- 167 of the 20,679 v30 skill labels carry a parenthesis that belongs to the concept;
- stripping those parentheses made three sets of distinct concepts read identically.
