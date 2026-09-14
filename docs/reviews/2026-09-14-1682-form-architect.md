# #1682 — layer form advice (dotnet-architect, 2026-09-14)

> Transcribed verbatim by the driving session from the agent's returned report. Brief the agent
> answered: the session's scratch file `1682-form-brief.md` (D1–D7 with the box measurement of
> 2026-09-14, `docs/reviews/2026-09-14-1682-profile-measurement.md`). Facts the agent cites were
> verified by the session against the worktree at `6db47773` before the report was transcribed:
> `IOccupationCodeDeriver.DeriveAsync`, `MaxCandidates = 25`, `MaxGroupSpread = 4`,
> `OccupationCandidate(OccupationGroupConceptId, OccupationGroupLabel, MatchKind, MatchedOn)`, and
> `ScbCompanyRegisterClient.MapRow`'s blank-slot skip.

## Sammanfattning

The brief's hardest question is already answered by delivered code: **the word → occupation-group matcher exists** (`IOccupationCodeDeriver`), and it is already a ratified live-read-path consumer of the frozen crosswalk. That collapses D2's stated cost, removes the need for a second matching home, and makes D3(B) structurally unsafe. Two new ports, one new table pair, one new recurring job. One **Kritiskt** risk if a second matcher is written; four **Viktigt** shape risks in the options as drafted.

---

## 1. The Application-owned port(s) and their exact signatures

### 1a. The job side — a new port, `ICompanyRegister/Abstractions`

```csharp
namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

public interface IOccupationDivisionProfileBuilder
{
    Task<OccupationDivisionProfileResult> BuildAsync(CancellationToken cancellationToken);
}

public sealed record OccupationDivisionProfileResult(
    int OccupationGroupsProfiled,
    int RowsWritten,
    int AdsCounted,
    int AdsNotInRegister,
    int AdsInRegisterWithoutSni,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
```

**Recommendation:** a new port, not a third method on `ICompanyWatchCriterionMaterialiser`.

**Reason:** that port's own docblock binds its shape to a change-reason argument — *"Two actors, two reasons to change, so two methods; a single method with a selection flag would hide both inside one."* Its two actors are *the register moved* and *a predicate moved*. #1682's actor is a third and unrelated one: *our own ad corpus moved*. It resolves no user predicate, touches no criterion, and its output is a global corpus statistic. Adding it there would falsify the docblock that justifies the split.

Home is `Application/CompanyRegister/Abstractions` because the join against `company_register` is the thing that cannot be done anywhere else — ADR 0139's own placement rule.

Counts only, no rows, no org.nr — parity `CompanyWatchCriterionMaterialisationResult`, and for the same reason: the whole record must be safe to log (ADR 0087 D8(c)). `AdsNotInRegister` and `AdsInRegisterWithoutSni` are separate counters deliberately; see §5.

### 1b. The read side — a second new port, **not** a method on `ICompanyWatchBrowseQuery`

```csharp
namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

public interface IOccupationDivisionProfileQuery
{
    ValueTask<OccupationDivisionProfile> GetDivisionProfileAsync(
        IReadOnlyList<string> occupationGroupConceptIds,
        CancellationToken cancellationToken);
}

public sealed record OccupationDivisionProfile(
    OccupationDivisionProfileState State,
    int? TotalAds,
    IReadOnlyList<OccupationDivisionShare>? Divisions,
    int? NotInRegisterAdCount,
    DateTimeOffset? ProfiledAt);

public sealed record OccupationDivisionShare(string DivisionCode, int AdCount);

public enum OccupationDivisionProfileState
{
    Profiled = 0,       // a real answer; the only state in which a number may be rendered
    TooFewAds = 1,      // under the derived floor — an honest refusal, never a zero
    NotProfiled = 2,    // no run yet, or over-age — unknown, and not a zero either
}
```

**Recommendation:** a new port. Do not put this on `ICompanyWatchBrowseQuery`.

**Reason:** that port already carries two key types and spends a full docblock paragraph justifying it — *"the key type is not a stylistic difference between neighbouring methods; it is what each method is about."* Its keys are `CompanyWatchCriteriaSpec` (a live predicate) and `CompanyWatchCriterionId` + `CriteriaFingerprint` (a materialised saved criterion). A profile read is keyed on occupation-group concept ids, belongs to no criterion, no user and no fingerprint. A third key type there makes that paragraph false.

The two ports are separate from each other for the same reason `ICompanyWatchCriterionMaterialiser` and `ICompanyWatchBrowseQuery` are: one writes, one reads, and they change for different reasons.

**Three states, mirroring `CriterionMaterialisationState` exactly.** This is not decoration — the surface must not render "we have not built this yet" or "too few ads to say" as a zero or an empty list. `TotalAds`/`Divisions`/`NotInRegisterAdCount` are non-null exactly under `Profiled`, enforced in the constructor rather than left to reviewers (the `MaterialisedAdCount` idiom).

**`NotInRegisterAdCount` is a separate scalar, not a member of `Divisions`.** Structural, not policed: the not-in-register bucket is not a division and must never be checkable. Keeping it off the list makes "ticking ej i registret" unrepresentable rather than a rule someone remembers. It mirrors `MaterialisedAdIds.Refused` versus `TooBroad` — two facts that render similar sentences, kept apart in the type.

**Codes, never names.** `DivisionCode` only; the picker already holds every division name from `SniReferenceCatalog` via `/reference`. Shipping names would duplicate them and put Swedish proper nouns in the payload against ADR 0137's "names travel as codes".

### 1c. Word → groups: reuse `IOccupationCodeDeriver`; add nothing

`IOccupationCodeDeriver.DeriveAsync(title, ct)` already returns a ranked, deduplicated list of ssyk-4 occupation-group concept ids with explainable evidence, matched from free text via occupation-name labels and the frozen crosswalk. The read handler composes it with `IOccupationDivisionProfileQuery`. No new port. See §3.

---

## 2. Table, entity and EF configuration

### Tables

**`occupation_division_profiles`** — the profile.

| Column | Type | Note |
|---|---|---|
| `occupation_group_concept_id` | `text NOT NULL` | ssyk-4 concept id |
| `division_code` | `text NOT NULL` | 2-digit, or the sentinel |
| `ad_count` | `integer NOT NULL` | |

PK `(occupation_group_concept_id, division_code)` — composite natural key, parity `CompanyWatchCriterionMember`: the row *is* the pair, there is no identity to have, and a duplicate becomes unrepresentable rather than unlikely.

**`occupation_division_profile_state`** — the single-row state.

| Column | Type | Note |
|---|---|---|
| `profile_key` | `text NOT NULL` PK | one constant, declared once |
| `profiled_at` | `timestamptz NOT NULL` | age axis + read gate |
| `occupation_groups_profiled` | `integer NOT NULL` | |
| `rows_written` | `integer NOT NULL` | |
| `ads_not_in_register` | `integer NOT NULL` | audited counter |
| `ads_in_register_without_sni` | `integer NOT NULL` | audited counter |

**Reason for a state row at all:** without it, "no rows for this group" is one symbol for three facts — never built, built and under the floor, built and genuinely zero. That is the exact collapse `CompanyWatchCriterionMaterialisation` was built to prevent, and the repo has shipped the dishonest nought once already (`getRecentSearches`, #1656).

**Reason for a `text` PK on a single-row table:** it makes "one row" a primary key rather than a convention, is EF-mappable without a `CHECK`, and is greppable. A `smallint CHECK (id = 1)` expresses the same rule as a magic number.

**The two audited counters are on the state row on purpose.** A guard whose firings are not counted cannot be told from a guard that never ran — the argument `ExcludedPersonnummerShaped` is stored for. `ads_in_register_without_sni` is expected 0 today; storing it is what proves the bucket separation in §5 is live rather than decorative.

### The not-in-register sentinel

`division_code` is `NOT NULL`, so a sentinel is forced at storage level. Use **`"--"`**, declared once as an `internal const` in Infrastructure beside the entity.

**Reason:** SNI division codes are two digits, so a non-digit sentinel cannot collide with real data — unlike `"00"`, which looks like a division and would survive a careless `LEFT(code,2)`. The sentinel never crosses the Application boundary: the port maps it to the separate `NotInRegisterAdCount` scalar (§1b), so §5's magic-string ban is satisfied where it matters — at the layer boundary — while the storage key stays NOT NULL.

### Index needs

**None beyond the two PKs.** The read is `occupation_group_concept_id = ANY(@ids)` over ≤25 ids; the profile PK's leading column serves it.

**And unlike `company_watch_criterion_members`, that plan is stable by construction.** #1706's finding was that the member PK is taken only while `criterion_id` is selective, and selectivity there is a function of *how many users exist* — a table shape production leaves behind. Here `n_distinct` is ~395 occupation groups over ~5 142 rows (~13 rows per group), and that ratio is a property of the taxonomy, not of the user base. It cannot drift the way #1706's did. Worth writing into the configuration docblock, because the neighbouring table's docblock says the opposite about itself and the difference is the interesting part.

Do not add a speculative index on `division_code` or on `ad_count` — nothing queries by either, and an index nothing reads is write amplification on every run plus a second thing to keep correct.

### Entity and configuration shape

- `internal sealed class OccupationDivisionProfileRow` / `OccupationDivisionProfileState` in `Infrastructure/CompanyRegister/`
- `internal sealed class OccupationDivisionProfileConfiguration : IEntityTypeConfiguration<...>` in `Infrastructure/Persistence/Configurations/` — that namespace is what `AppDbContext.OnModelCreating` scans
- **Not a `DbSet` on `IAppDbContext`.** Reached only by raw SQL on the concrete `AppDbContext`, parity `ScbCompanyRegisterStore` and `CompanyWatchCriterionMemberStore`. The configurations exist for the migration schema, not for a read path.
- **No FK, no cascade, no `user_id`, no `criterion_id`, no org.nr.** The table is a corpus statistic. That means none of ADR 0139's Major 5a/5b/5c obligations arise — and the session must have `security-auditor` *confirm* that non-attributability rather than assume it, which the brief already binds.

---

## 3. Where the word → occupation-group matching lives

**It lives in Infrastructure, and it is already written.** `OccupationCodeDeriver` (Infrastructure/Taxonomy) behind `IOccupationCodeDeriver` (Application/JobAds/Abstractions):

- matches against the 2 323 singular occupation-**name** labels — exactly M2's surface
- rolls each hit up to its ssyk-4 group via `OccupationGroupMappingLoader` — exactly M2's crosswalk
- returns deduplicated group concept ids + labels + cited evidence (`MatchedOn`), ranked deterministically
- singleton lazy cache over the taxonomy snapshot + the frozen map — no DB hit per call
- bounded: `MaxCandidates = 25`, and a `MaxGroupSpread = 4` gate that drops low-precision cross-group fan-out

**The brief's characterisation of M2 as "a NEW consumer of a frozen artefact" is false, and this is the report's most consequential correction.** `OccupationCodeDeriver` is already that consumer, in the live read path, and the role-widening from "migration-owned" to "shared reference data" was **ratified by `senior-cto-advisor` Decision 2, 2026-06-14**, recorded as an ADR 0043 amendment-note. The loader's own docblock states it: *"F4-3 reads it from the live read path as a second, read-only consumer: the bytes are never regenerated, so migration immutability is fully preserved."* Reusing the deriver means #1682 adds **zero** new consumers. M2 is therefore cheaper than the brief prices it, and M1's only remaining advantage disappears.

**Guards this touches:** `OccupationCodeDeriverGroupLabelTiebreakTests` and `GroupSpreadGate_AnchorsBracketTheThreshold` (both existing, both pin the deriver against the live seeded snapshot); `TaxonomyAclLayerTests`' Application guard. **No new guard is owed** — which is itself the argument for reuse: a second matcher would need its own, plus a parity test between the two that nobody would maintain.

**Two properties to name rather than discover later, both consequences of reuse, neither a defect:**

1. **The deriver matches whole stemmed words, not prefixes.** A debounced type-ahead gets no block until the word is complete. That is arguably the *correct* behaviour — M1's stated defect is precisely a block that appears mid-word and vanishes on the last letter — but it is a product consequence for `design-reviewer`/CTO to rule on, not something to engineer around.
2. **`MaxGroupSpread = 4` means a generic word yields nothing.** "chef" (spread 8) and "operatör" (spread 7) produce no candidates. Honest, and consistent with the engine's transparency invariant; the surface falls back to its existing "inga träffar" for those.

**On M3 (`SearchSynonyms:Occupations`) — recommend against, on a ground the brief does not list.** Beyond the documented bounded-context line, the key exists in the **Api's `appsettings.json` only; the Worker has none**. Grounding a match surface in per-host configuration means the two hosts can disagree about what a word means, and nothing would surface the divergence. Keep the match surface in code and data, not config. Note that issue scope item 2 as written names M3 — that wording and the measurement ("adds nothing M2 does not already find") point in opposite directions, which is a CTO call, not mine to settle.

---

## 4. Job orchestration

**Recommend D1(a) — own cron, clock-padded. Reject (b) and (c).**

**Why not (c):** it is an edge-triggered handoff from an Application job to a Worker concern, which ADR 0139 clause (ii) already rejected on reasoning that transfers verbatim — a signal raised inside a handler must defeat `UnitOfWorkBehavior` committing *after* the handler returns. The house has adjudicated this shape; do not re-open it for a cheaper case.

**Why not (b), despite its literal fidelity:** the watermark reads another subsystem's `audit_log` payload shape (`payload->>'JobType' = 'snapshot'`, an ADR 0035 serialization detail). `JobAdSnapshotMissTracker` is the precedent *and* the warning: #510 was exactly a wrong payload-key read, and it silently paused miss-tracking for up to seven days. What that coupling buys is avoidance of one redundant 163 ms rebuild per day. It also gives the job a second reason to run, inside one method — the thing `ICompanyWatchCriterionMaterialiser`'s two-method split exists to prevent. Bad trade.

**Cadence: `"45 5 * * *"` (05:45 UTC).** Derived, not chosen: after the snapshot ingest (audit rows at 02:05–02:06Z) by a wide margin; after `company-watch-criterion-materialisation` (05:30); before the digest window (06:00); and outside the 02:00–05:00 DB-contention window the SCB cadence was deliberately moved out of (#708 PR 2). It extends the existing clock-padded chain 05:15 → 05:30 → **05:45** → 06:00.

⚠ **Name the lapse condition in the options docblock**, house discipline, because nothing detects it: *if the snapshot cadence moves or its runtime grows past this pad, this cron must be re-derived — do not simply nudge the number.*

### Exact obligations

| Obligation | Form |
|---|---|
| **`RecurringJobIds`** | add `BuildOccupationDivisionProfile = "build-occupation-division-profile"` **and add it to `All`** — the registrar-id-set == `All` parity test fails otherwise, and an unlisted id is untriggerable from the admin surface |
| **`RecurringJobRegistrar`** | `manager.AddOrUpdate<OccupationDivisionProfileWorker>(id, j => j.RunAsync(CancellationToken.None), options.Value.CadenceCron)` — registered **unconditionally**; the kill-switch lives in the job, so the schedule cannot drift from the allowlist |
| **`Worker/Program.cs`** | `builder.Services.AddScoped<OccupationDivisionProfileWorker>()` beside the sibling workers (~line 114) |
| **`WorkerTestFixture`** | `["OccupationDivisionProfile:CadenceCron"] = "<a value that is neither the shipped default nor any other section's>"`. This is a real obligation, not hygiene: the #1681 precedent makes the fixture's distinguishable value the thing that proves the **real** composition root binds from the right section — a test that rebuilds the binding itself cannot fail when `AddPersistence` is mutated |
| **ADR 0113 / `JobAdLifecycleReadRegistry`** | **one** `RawSqlNonReach` entry, same PR: `("Jobbliggaren.Infrastructure.CompanyRegister.OccupationDivisionProfileBuilder", "BuildAsync", "the status allow-list lives in the SQL string...")`. The IL scan emits no `get_JobAds` for raw SQL. The read side touches no `job_ads` and owes nothing |
| **`RawOrgNrReadingSourcePaths`** | **no entry — and keep it that way deliberately.** Doing the whole aggregate server-side (§5) means no org.nr ever reaches C# scope. Write that as the reason in the SQL's docblock; if any intermediate step ever reads one, the path must be added the same day |
| **ANALYZE** | once per **completed** run, after the write, on the two profile tables only, schema-qualified, plain `ANALYZE`. All three §3.6 conditions hold: one periodic writer, read-only between runs, `occupation_group_concept_id` reaches a `WHERE`. **Fail-loud** — §3.6's placement for a retry-bounded job. Never touch `job_ads` or `company_register`: different change-reason, and §3.6 scopes the rule to the table the job loaded |
| **`DisableConcurrentExecution`** | yes, with the **`(int)` per-method constructor** and a 15-minute wait, parity `MaterialisationWorker.RunAsync` (arrives once a day; a duplicate waiting that long is evidence something is genuinely wrong, and nothing queues behind it). **Do not share ADR 0139's lock resource** — that shared key exists because two writers touch the same two tables; this job shares no table with them, and widening the key would serialise unrelated work |
| **`AutomaticRetry`** | **keep Hangfire's default; do not suppress.** Same argument as `MaterialisationWorker.RunAsync` verbatim: no external call, costs ~163 ms, idempotent by construction (full replace), and the next scheduled attempt is 24 h away, so suppressing would turn a transient DB blip into a full day of stale profile |

**One simplification against #1681 worth stating:** `ThrowIfWhollyFailed` has no analogue here. That asymmetry exists because the materialiser loops per criterion and swallows individual failures, so a wholly-failed run would otherwise return success and the retained retry could never fire. This job is a **single statement** — a failure propagates naturally and the retry fires by default. Do not port the guard; port the property it protects.

**Options section** (`OccupationDivisionProfileOptions`, `SectionName = "OccupationDivisionProfile"`, `ValidateDataAnnotations().ValidateOnStart()`):
`Enabled` default **true** — the same inversion `CompanyWatchMaterialisationOptions.Enabled` argues: this guards a purely local recompute over tables we already hold, so defaulting it false would make an independent trigger independently switched off. `CadenceCron`. `MaxReadAgeHours = 72` — derived as three cadence periods, parity, and named as one decision with the cron. `MinimumSharePercent = 5`. See §6 for why the floor must **not** be a fourth setting.

---

## 5. The SQL shape

Compute and write the whole aggregate **in one server-side statement**; no rows cross into C# at all.

```sql
INSERT INTO occupation_division_profiles (occupation_group_concept_id, division_code, ad_count)
SELECT j.occupation_group_concept_id,
       CASE WHEN r.organization_number IS NULL          THEN @not_in_register
            WHEN COALESCE(array_length(r.sni_codes,1),0) = 0 THEN @no_sni
            ELSE LEFT(r.sni_codes[1], 2) END,
       count(*)
FROM job_ads j
LEFT JOIN company_register r ON r.organization_number = j.organization_number
WHERE j.status = ANY(@statuses)
  AND j.occupation_group_concept_id IS NOT NULL
GROUP BY 1, 2;
```

Preceded by an unconditional `DELETE FROM occupation_division_profiles;` and followed by the state upsert, **all in one transaction** — the same replace-never-supplement and one-transaction discipline as `ReplaceAsync`, and for the same reason: a crash between the two writes would leave a profile beside a state row that disagrees with it, silently.

### What to pin

1. **`LEFT JOIN`, never `INNER`.** The not-in-register bucket is 3 037 ads (3.6 %) and is issue scope item 1. An inner join drops it in silence. **Pin it with a test that seeds an ad whose org.nr is absent from the register** — this is the single most consequential line in the statement.
2. **Positive-polarity status allow-list**, `status = ANY(@statuses)` bound to `[Active, Archived]`, never `status <> 'Erased'`. `CompanyWatchBrowseQuery.FromWhere`'s docblock owns the argument: the negative form silently starts including a future third status member. Erased is an Art. 17 tombstone and must never be counted.
3. **`sni_codes[1]` is the primary code, with one subtlety to write down.** `ScbCompanyRegisterClient.MapRow` reads `Bransch_1..5` in order and **skips blank slots**, so `sni_codes[1]` is the first non-blank code — which is `Bransch_1` when present and the first populated slot otherwise. That is the honest "primary"; one sentence in the docblock stops the next reader assuming the array is dense.
4. **`LEFT(code, 2)` on text — never arithmetic.** SNI leaf codes are 5-digit strings with load-bearing leading zeros (`SniLeaf`, and `CompanyBrowseResult.SeatMunicipalityCode`'s explicit "never parse it to int"). `code::int / 1000` would eat a leading zero.
5. **Three buckets, not two.** An empty `sni_codes` array makes `sni_codes[1]` return NULL, and a bare `COALESCE` would fold "in the register with no SNI" into "not in the register" — two facts, one symbol, which is the collapse this whole feature's state machine is built against. Measured today it is 0 rows; keep the arms distinct anyway and count the second one on the state row, so a guard that starts firing is visible instead of silent.

### What to avoid

6. **No `ORDER BY`.** The insert is a set; an ordering would force a sort over 83 280 rows to establish an order no reader uses — `SelectCandidatesAsync`'s stated reasoning.
7. **Do not try to index away the Seq Scan.** 83 280 of 83 280 non-erased ads carry an `occupation_group_concept_id`, so the aggregate reads essentially the whole table and a Seq Scan is the *correct* plan; an index would be a slower random-access path. 62 358 shared-hit buffers, 163 ms, once a day.
8. **No EXPLAIN pin on this statement.** ADR 0139's EXPLAIN pins exist to protect *read-path latency budgets* (ADR 0045 class (a)); a once-daily 163 ms job has no budget to protect, and #1706 is the measured warning that a plan pinned in the wrong regime is worse than no pin. Memoize over `pk_company_register` is the planner's choice against a 1.07M-row register and an 83k-row corpus — stable against the real shapes, and nothing to bind.
9. **Set `CommandTimeout` explicitly — 120 s.** A raw `NpgsqlCommand` does **not** inherit EF's `SetCommandTimeout`; it silently takes the connection-string default. `CompanyWatchCriterionMemberStore.CommandTimeoutSeconds` documents the trap and the value: background job, nothing waiting, and a hung statement must still fail loud, so never 0.

The read query is a PK lookup: `SELECT occupation_group_concept_id, division_code, ad_count FROM occupation_division_profiles WHERE occupation_group_concept_id = ANY(@ids)`, ≤25 ids × ~13 rows ≈ 325 rows worst case, wrapped in the state gate (`profiled_at >= now() - @max_age`) the same way `MaterialisedAdsFromWhere` is — so an over-age profile costs no scan at all.

---

## 6. Clean Architecture and §5 risks in the brief's options

**[Kritiskt] Writing a second word → group matcher.** Two authorities for "which occupation groups does this word mean" is the #1407/#1471 divergence class ADR 0139 exists to close, re-entering through a new seam. Reuse `IOccupationCodeDeriver` (§3).

**[Kritiskt] D3(B) — bulk endpoint with matching in TypeScript.** It duplicates exact-normalised matching, Snowball stemming (`ITextAnalyzer.ToLexemes`, `to_tsvector('swedish')` parity) and the group-spread gate in a language with no faithful equivalent of the stemmer. The two implementations would diverge **by construction**, and the divergence would be invisible — every test on both sides stays green. It also ships 2 323 labels plus the crosswalk to every client. Reject; D3(A) keeps one authority server-side and costs ~1 kB.

**[Kritiskt] D1(c).** Edge-triggered Application → Worker handoff, already adjudicated (§4).

**[Kritiskt] Putting either table on `IAppDbContext`.** `ScbCompanyRegisterLayerTests.IAppDbContext_exposes_only_Domain_types` is fail-closed and would correctly red the build. Named because ADR 0139 records that the *alternative* form — a Domain-typed table — would have **passed** the guard while the property it documents went false.

**[Viktigt] The floor and the 5 % threshold as two independent numbers.** The brief derives 21 from 5 % (`1/20 = 5.0 %`, so 21 is the smallest N at which one ad cannot clear the threshold alone). That derivation makes them **one knowledge piece**, and two settings that must agree drift apart at the first edit. Single-source it exactly as `CompanyBrowseCriteria` does for its own pair — *"the page cap and the count cap are the same knowledge piece, so they are single-sourced"*:

```csharp
/// DERIVED from MinimumSharePercent, never configured beside it: below this N a single ad
/// clears the threshold alone, so the list is an enumeration of employers, not a distribution.
public int MinimumAdsPerGroup => (100 / MinimumSharePercent) + 1;   // 5 % -> 21
```

**[Viktigt] D2b and D4 are one decision, not two.** The floor's derivation is a property of *whichever denominator is rendered*. Under (iv) — one aggregated distribution over the union — the floor applies to the union total and almost always clears. Under (i) — one block per group — it applies per group and refuses 85 of 395. Choosing the render shape therefore chooses what the floor measures. The port in §1b serves (ii), (iii) and (iv) unchanged because the caller decides what to pass; (i) would need a per-group return and a per-group floor. Flagging the coupling for CTO, not pre-empting it.

**[Viktigt] Storing a thresholded profile.** Applying the ≥5 % cut or the floor at write time bakes product policy into storage: a threshold change becomes a rebuild rather than a deploy, and the floor needs the unthresholded per-group `SUM(ad_count)` anyway. Store the full 5 142 rows — it is nothing — and apply both thresholds at read time. Storage is a measurement; thresholds are policy; different change-reasons.

**[Viktigt] The sentinel as a bare string across the Application boundary.** §5 magic strings. Named `internal const` in Infrastructure, mapped to the typed `NotInRegisterAdCount` scalar at the port (§1b/§2).

**[Viktigt] M3's config asymmetry.** `SearchSynonyms:Occupations` exists in the Api's `appsettings.json` and not the Worker's. Grounding a match surface in per-host config lets two hosts disagree about what a word means with nothing to surface it (§3).

**[Nice-to-have] Naming.** `OccupationDivisionProfileBuilder`, not `*Service` (§5 generic suffix). The job takes `IDateTimeProvider` for `profiled_at` — never `DateTime.UtcNow` — and reads the stamp **where the data is read**, not at the write, so the conservative direction holds: an earlier stamp ages a row out of `MaxReadAgeHours` sooner, never later (the `ReplaceStampedAt` discipline; the window nearly collapses here since it is one statement, but the direction is free).

**On the read route's bucket, for `security-auditor` rather than me:** ADR 0139 separates *"the port is a layer boundary, the bucket a cost boundary"*. This read's cost class is a PK lookup over ~5 142 rows plus an in-memory deriver call with no DB hit — far closer to `/job-ads/taxonomy/skills/search` than to anything register-joining. `RateLimitingOptions.cs:250`'s *"Never folded into MeListRead"* still binds; the number is hers. Route family: `/api/v1/me/company-watch-criteria/...` is the coherent home — it is the picker's own family and `/reference` is precedent for global reference data living there — with `/api/v1/job-ads/taxonomy/...` the defensible alternative since the data is not user-scoped. A CTO call.

---

## References

- `CLAUDE.md` §2.1 (the three-axis EF dependency rule), §2.2, §3.6, §5
- `docs/decisions/0139-materialise-criterion-company-membership.md` — layer half, the Infrastructure-scoped table argument, the vacuous-guarantee class
- ADR 0043 amendment-note / `senior-cto-advisor` Decision 2, 2026-06-14 — the frozen crosswalk's ratified role-widening
- ADR 0113 — `JobAdLifecycleReadRegistry.KnownNonReaches`
- AGENTS.md §3.6 — the bulk-load ANALYZE rule and its three conditions
