# #1682 — occupation × SNI-division profile: measurement on the box, 2026-09-14

**Purpose.** Issue #1682 says of its own table: *"Regenerate before building; the numbers above are a
dated sample by title, not by concept."* This is that regeneration, keyed on
`job_ads.occupation_group_concept_id` (JobTech `ssyk-level-4`, ADR 0067 Beslut 1) rather than on the
ad title. Every number below is a read-only measurement against the production database on
`dev.jobbliggaren.se`, taken 2026-09-14 between 09:40 and 10:05 UTC via
`ssh jp-vps 'sudo -n docker exec -i jobbliggaren-postgres psql -U postgres -d jobbliggaren'`. The
query files are reproduced at the end so the run is repeatable; the numbers decay with every ingest
run and are not to be copied into code (AGENTS.md §5 `Comments:`).

**Window.** `job_ads.status IN ('Active', 'Archived')` as an allow-list. `Erased` is the Art. 17
tombstone (#842) and is excluded by construction, never by a `<>` — JobAdSearchComposition's #864 D4
rule applies to this read too. On the measurement day the table held 0 `Erased` rows, so the
allow-list and `COUNT(*)` coincided; that coincidence is not a property to rely on.

## 1. Coverage

| Quantity | Value |
|---|---|
| `job_ads` rows, non-erased | **83 280** (40 833 Active + 42 447 Archived; 0 Erased) |
| … with `occupation_group_concept_id` | 83 280 (100 %) |
| … with `organization_number` | 82 551 |
| … whose org.nr is in `company_register` | **80 243** (96.4 % of 83 280), all with a non-empty `sni_codes` |
| … whose matched register row is `Deregistered` | 27 |
| … with no org.nr at all | 729 |
| … with an org.nr the register does not hold | 2 308 |
| **"Not in register" bucket** (729 + 2 308) | **3 037** (3.6 %) |

`company_register` on the day: 743 654 Active, 323 284 Deregistered.

## 2. Ads per occupation group

395 of the 400 `OccupationGroup` concepts have at least one ad.

| min | p10 | p25 | p50 | p75 | max |
|---|---|---|---|---|---|
| 1 | 9 | 25 | 72 | 218 | 4 040 |

Groups under a given count: **< 10: 44** · **< 20: 81** · **< 30: 119** · **< 50: 167** · **< 100: 232**.

## 3. Profile size and the effect of the threshold

| Shape | Rows | Groups |
|---|---|---|
| Full profile, group × division, not-in-register as its own bucket | **5 142** (254 of them the null-division bucket) | 395 |
| Divisions at ≥ 5 % of the group's ads | 1 471 | 393 |
| … and the group has ≥ 21 ads | 1 132 | 310 (85 refused) |
| Groups with ≥ 40 ads | — | 251 |

The largest number of divisions any one group keeps at ≥ 5 % is **12**.

## 4. Cost of the full aggregate

`EXPLAIN (ANALYZE, BUFFERS, TIMING OFF)` of the `LEFT JOIN … GROUP BY` over the whole window:

```
HashAggregate  (actual rows=5142)  Batches: 1  Memory Usage: 2329kB
  Buffers: shared hit=62358
  -> Nested Loop Left Join  (actual rows=83308)
       -> Seq Scan on job_ads j  (actual rows=83308)
            Filter: ((occupation_group_concept_id IS NOT NULL) AND ((status)::text = ANY ('{Active,Archived}'::text[])))
            Buffers: shared hit=19448
       -> Memoize  Cache Key: j.organization_number  Hits: 72505  Misses: 10803  Evictions: 0
            -> Index Scan using pk_company_register on company_register c  (actual rows=0.97 loops=10803)
Planning Time: 0.526 ms
Execution Time: 162.852 ms
```

A whole rebuild is **162.9 ms** and 62 358 shared-hit buffers on the day's corpus. The register side
is served entirely from `pk_company_register` through a Memoize node (10 803 distinct employers for
83 308 ads); the ad side is a sequential scan of `job_ads`, which is the right plan for a read that
touches every non-erased row.

## 5. The two worked examples, by concept

**Grundutbildade sjuksköterskor** (`Z8ci_bBE_tmx`), 3 317 ads:

| Division | Ads | Share |
|---|---|---|
| 86 Hälso- och sjukvård | 1 626 | 49.0 % |
| 78 Arbetsförmedling, bemanning | 1 146 | 34.5 % |
| 88 Öppna sociala insatser | 228 | 6.9 % |
| 87 Vård och omsorg med boende | 125 | 3.8 % |
| 70 Huvudkontor, konsulttjänster | 72 | 2.2 % |
| 85 Utbildning | 59 | 1.8 % |
| not in register | 10 | 0.3 % |
| (nine more divisions) | ≤ 17 each | ≤ 0.5 % |

**Mjukvaru- och systemutvecklare m.fl.** (`DJh5_yyF_hEM`), 2 249 ads:

| Division | Ads | Share |
|---|---|---|
| 78 Arbetsförmedling, bemanning | 615 | 27.3 % |
| not in register | 395 | 17.6 % |
| 62 Dataprogrammering, datakonsultverksamhet | 375 | 16.7 % |
| 70 Huvudkontor, konsulttjänster | 165 | 7.3 % |
| 84 Offentlig förvaltning | 108 | 4.8 % |
| 71 Arkitekt- och teknisk konsultverksamhet | 100 | 4.4 % |
| 46 Partihandel | 65 | 2.9 % |
| 30 Övriga transportmedel | 58 | 2.6 % |
| 64 Finansiella tjänster | 55 | 2.4 % |
| (twenty more divisions) | ≤ 39 each | ≤ 1.7 % |

The by-title sample in the issue (2026-09-06) had 84 Offentlig förvaltning at 25 % for
`systemutvecklare`; by concept it is 4.8 %. The title sample matched every ad whose *title* contained
the word, across all occupation groups; the concept sample is the one group the taxonomy files the
word under. That is why the issue asked for a regeneration.

## 6. Taxonomy facts that shape the match surface

- `OccupationGroup` labels are **plural** (*"Grundutbildade sjuksköterskor"*, *"Mjukvaru- och
  systemutvecklare m.fl."*); `Occupation` labels are singular (*"Sjuksköterska, grundutbildad"*).
- `Occupation.parent_concept_id` points to an **OccupationField**, never to an OccupationGroup. The
  only occupation-name → occupation-group link in the system is the frozen migration resource
  `occupation-name-to-ssyk-level-4.v30.json` (`OccupationGroupMappingLoader`).
- `label ILIKE '%systemutvecklare%'`: 1 Occupation + 1 OccupationGroup.
  `label ILIKE '%sjuksköterska%'`: 24 Occupation, **0 OccupationGroup**.
- `taxonomy_relations` holds one kind, `Substitutability` (444 rows).
- The three most recent `System.JobAdsSynced` snapshot audit rows: 2026-09-14 02:05:51Z,
  2026-09-13 02:05:57Z, 2026-09-12 02:06:15Z.

## 7. The deriver's answer for the two acceptance words

`senior-cto-advisor` (D2, `docs/reviews/2026-09-14-1682-form-cto.md`) bound the picker's match
surface to the delivered `IOccupationCodeDeriver` and made this measurement a condition: the probes
above used `ILIKE` over labels, the deriver does not. Measured 2026-09-14 by
`OccupationCodeDeriverIntegrationTests.DeriveAsync_BranschPickerAcceptanceWord_ResolvesToAtLeastOneGroup`
against the live seeded taxonomy snapshot (v30) in a Testcontainers Postgres:

| Word | Groups | Match |
|---|---|---|
| `systemutvecklare` | **1** — `DJh5_yyF_hEM` Mjukvaru- och systemutvecklare m.fl. | `StemmedTokenOverlap` on *Systemutvecklare/Programmerare* |
| `sjuksköterska` | **2** — `Z8ci_bBE_tmx` Grundutbildade sjuksköterskor; `6zAR_EHM_Kwj` Övriga specialistsjuksköterskor | `StemmedTokenOverlap` on *Sjuksköterska, grundutbildad* / *Medicinskt ansvarig sjuksköterska* |

So `systemutvecklare` renders its map directly, and `sjuksköterska` reaches the D2b chooser with two
names — not the ~10 the `ILIKE` count in §6 suggested. The spread gate (`MaxGroupSpread = 4`) is what
narrows it.

## 8. Query files

`probe1.sql` — coverage, status counts, ads-per-group distribution, profile row count.
`probe2.sql` / `probe4.sql` — the two worked examples, the threshold effect, the EXPLAIN.
`probe3.sql` — taxonomy parent kinds, relation kinds, audit rows, label hits.

The aggregate under measurement, verbatim:

```sql
SELECT j.occupation_group_concept_id,
       CASE WHEN c.organization_number IS NULL THEN NULL ELSE left(c.sni_codes[1], 2) END,
       count(*)
FROM job_ads j
LEFT JOIN company_register c ON c.organization_number = j.organization_number
WHERE j.status IN ('Active', 'Archived')
  AND j.occupation_group_concept_id IS NOT NULL
GROUP BY 1, 2;
```

`sni_codes[1]` is the primary SNI code by the register's own convention (SCB `Bransch1` first,
documented in `ScbCompanyRegisterEntry` and preserved by the upsert); no constraint enforces the
ordering, so the profile inherits that convention rather than proving it.
