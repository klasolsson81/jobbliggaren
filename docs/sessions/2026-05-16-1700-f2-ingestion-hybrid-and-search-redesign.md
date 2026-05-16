---
session: F2 samlad — ingestion payload-trunkerings-fix (hybrid) + sök-yta-omdesign B–E
datum: 2026-05-16
slug: f2-ingestion-hybrid-and-search-redesign
status: PÅGÅENDE — Batch 1 Part 1 + ADR-amendment klart, pending deploy (STOPP 3)
commits:
  - "feat(jobads): Batch 1 Part 1 — snapshot-trunkerings-resiliens (denna session)"
  - "docs(ADR 0032): amendment 2026-05-16 hybrid + Batch 1 docs-synk (denna session)"
---

# F2 samlad session — ingestion-hybrid-fix + sök-omdesign

6 linjära batchar, 7 Klas-STOPP. Denna logg uppdateras löpande per batch
(§1.5) — överlever kompaktering.

## Mål

Stäng F2-milstolpen: (1) fixa ingestion payload-trunkering så korpus
konvergerar mot ~40k+, (2) sök-yta-omdesign B–E (SearchCriteria multi,
relevans, IsNew, typeahead) + frontend.

## Batch 0 — Discovery (ingen kod)

CloudWatch `/aws/ecs/jobbpilot-dev/worker` (dev `v0.2.8-dev`, 48h) verifierade
rotorsak: `/v2/snapshot` >364 MB singel-GET termineras **icke-deterministiskt**
mid-stream (START→TRUNC 87–442 s, bytePos 21–364 MB) → ofångad
`System.Text.Json.JsonException` vid enumeration i `PlatsbankenJobSource`
(saknade try/catch runt `await foreach`) → propagerade förbi
`SyncPlatsbankenSnapshotJob`s per-item-upsert-catch → `Hangfire.AutomaticRetry`-
storm (60 starts/0 completes). Hypoteser **motbevisade**: HttpClient.Timeout=5min
(ingen tidsvägg), MaxResponseContentBufferSize=500MB (364<500 + streaming
bypassar), Polly (completar vid headers-read). Sök-kod kartlagd för Batch 3–5.

## Batch 0→1 — CTO + architect

- **senior-cto-advisor** (`a237dfe175089fb7d` → forts. `ad8564aafc29be5a0`):
  först A2 (stream-cursor), sedan **omvägd till hybrid** efter web-verify
  (JobTech GettingStartedJobStreamSE.md 2026-05-16: full-korpus-pattern är
  snapshot-först + stream; **ingen dokumenterad stream-only-backfill**;
  retention-djup okänt; rate-limit 1/min, granularitet ospecificerad). A2:s
  premiss rev → §9.5. MA-triage: 1.1=A (stateless, ingen cursor), 2.1=A
  (behåll job/id, ändra internals), 3.1=A (enumeration-catch + bounded retry),
  4.1=A (delad limiter). Snapshot-paus rekommenderad (prio-1).
- **dotnet-architect** (`a6a02546f13bd5236`): design-skiss INNAN kod,
  bekräftade lager-placering + ACL-bevarat kontrakt; surfade 4 MA-punkter +
  drift-konvergens-STOPP-kandidat.

## Klas-STOPP 1 (GO)

A2-inriktning (sedermera hybrid via web-verify), snapshot-paus nu
(CC levererade operatörsprocedur: Worker ECS desired-count→0, ej CC-verkställt),
non-stop-arbetsflöde bekräftat.

## Batch 1 Part 1 — root-cause-fix (levererad)

`PlatsbankenJobSource.FetchSnapshotAsync`: resilient enumeration (manuell
enumerator, enumeration-boundary-catch `JsonException`/`IOException`/
`HttpRequestException` **skild** från per-item-upsert-catch, `OCE` rethrow,
`MaxSnapshotAttempts=3` bounded retry idempotent via UNIQUE-index, graceful
`yield break` → ingen storm). LoggerMessage 5004/5005. Regressionstest FÖRST
(WireMock trunkerad-body, reproducerar exakt rotorsaken). Build 0/0; svit
**1043 grön** (Domain 293/App 398/Arch 51/Api.Int 269 (+1)/Worker 26/Migrate 6).
**code-reviewer** `ab3fefc83d7e4f22a` GO (0 Block/0 Major/1 Minor — redundant
`using` fixad in-block §9.6).

## Klas-STOPP 2 (GO)

Drift = **recurring inkrementell, ingen timeout-höjning** (CTO-lutning).
Amendment-hantering = **CC drafter** (medvetet Klas-override av §9.4
verbatim-text-källa — dokumenterat i amendment-Status). ADR 0032-amendment
2026-05-16 **Accepted** (hybrid + MA + drift + konvergens-risk). README-rad
uppdaterad.

**Syntes:** hybrid + alla MA=A + stream "oförändrat mönster" ⟹ Part 1 ÄR hela
Batch 1-kodändringen. Ingen separat Part 2 (windowed-stream-katch-up tillhörde
förkastad ren A2; bevaras som framtida skala-trigger, ej TD — §9.6/§9.7).

## Beslut & avvägningar

- Konvergens-risk medvetet accepterad (Klas 2026-05-16): ~40k+ tar dygn ej
  timmar; STOPP 3 mäter korpus-**tillväxt över tid**, ej omedelbar ~40k.
- §3 förtydligas, supersederas ej (hybrid bevarar overlap-window-mönstret).
- §9 X4 410 + `JobType:"snapshot"`-literal + ADR 0036-metric oförändrade
  (MA 2.1=A).

## STOPP 3 — deploy + gate-def-konflikt löst

`v0.2.9-dev` tag-pushad (CC på Klas-GO), deploy run `25970027351` in_progress.
**Gate-def-konflikt:** LÅST PLAN sa "Batch 3 ej innan korpus ~40k+"; accepterad
ADR 0032-amendment sa "~40k+ tar dygn". Klas-beslut 2026-05-16: **grön =
storm-borta + korpus-tillväxt-trajektoria** (ej literal ~40k+) → Batch 2–6
non-stop, ~40k+ konvergerar i bakgrunden. Worker-rescale (om paus kördes) =
Klas AWS-op. Cron-grön verifieras async.

## Batch 2 — ADR 0042 + 0039-supersession (noll kod, parallellt m/ deploy)

adr-keeper `ad3fa8dc6d921d8a3`: ADR 0042 (sök-yta-IA A–F: A kollaps-filter,
B SearchCriteria single→multi + 4 DDD-invarianter, C C1-typeahead, D D2-ILIKE
+ D1-skala-trigger-ej-TD, E Since/IsNew runtime-ej-VO, F CV-out korsref 0040).
ADR 0039 Beslut 3 additivt supersession-blockquote (brödtext orörd, Nygard) +
header partial-flagga. README-index. CC rättade adr-keeper-batch-nr-fel
(B Batch 4→3 per LÅST PLAN). **Klas-STOPP 4 GO** — Accepted.

## Batch 3 — B SearchCriteria single→multi (KLAR)

architect `aeb84989ef8c96f70` INNAN kod → CTO `a3f867af2b57df564` **Yta A3**
(property-level HasConversion + System.Text.Json tolerant converter i
Infrastructure; web-verifierat `OwnsOne().ToJson()`+converter instabilt via
Npgsql #3129; ingen data-migration, lazy on-read). test-writer
`ac943915d61d386ed` FÖRST (röda: equality+4 invarianter+jsonb-roundtrip+
default-deny+regression). Impl: `SearchCriteria` IReadOnlyList Ssyk/Region +
sorterad+distinct-normalisering + maxantal-cap MaxConceptIds=10 + generaliserad
tom-invariant + per-element-regex + explicit Equals/GetHashCode (SequenceEqual
ordinal — SavedSearch jsonb-dedupe-grund). `SearchCriteriaConverters.cs`
(tolerant default-deny, Domain orört). `JobAdSearch.ApplyCriteria` list→IN(...).
Validator + alla konsumenter (commands/DTO/endpoints) propagerade. db-migration-
writer `aef17608f4c9e63d7`: migration `F2SearchCriteriaMultiValue` tom Up/Down
(A3 no-op — kolumn redan jsonb; Klas-beslut: behåll). security-auditor
`a7a056bb9566e86fd` **PASS** 0 Crit/High/GDPR (7/7); **M1** (SavedSearch
Create/Update saknade pre-handler cap-paritet) → **fixad in-block** §9.6
(speglar ListJobAdsQueryValidator). code-reviewer `a2536dc814ec4e1b7` **GO**
0 Block/0 Major/1 Minor FYI (test-only). Svit **1069 grön**, build 0/0.
**STOPP 5 (migration) + STOPP 6 (security-auditor) bundlat → Klas-GO**
(behåll tom migration; commit GO).

## Batch 4 — E (Since/IsNew) + D (relevans D2) (KLAR)

Inga reviewers utöver code-reviewer per plan (ingen extern-input-yta/migration).
E: `ListJobAdsQuery.Since` (DateTimeOffset?) + `JobAdDto.IsNew` (runtime-kontext,
EJ i SearchCriteria — analog Page/PageSize; RunSavedSearch/GetJobAd IsNew=false).
D: `JobAdSortBy.Relevance=4`, D2 ILIKE-heuristik (titel exakt 3/prefix 2/
contains 1/0 → PublishedAt desc) via EF.Functions.Like+ToLower (provider-
agnostiskt, ej Npgsql ILike — Clean Arch); `ApplySort(source,sortBy,q)`-signatur
(SPOT, båda konsumenter); invariant Relevance-kräver-q i SearchCriteria.Create
+ ListJobAdsQueryValidator. Tester: SearchCriteriaTests/ListJobAdsQuery
ValidatorTests (invariant ×2+2) + ListJobAdsMultiFilterTests (Relevance-ordning
+ IsNew, riktig Postgres). code-reviewer `a87dc6553605f8f0d` **GO** 0/0/1 Minor
FYI (pre-existing LIKE-wildcard-konvention, ej regression/in-block §9.6). Svit
**1074 grön**, build 0/0. Ingen Klas-STOPP (plan). Non-stop → Batch 5.

## Batch 5 — C typeahead C1 (KLAR)

architect `acb691cd15766291f` INNAN kod → CTO `afba3c7659c086817`: **Variant A**
btree functional partial-index `lower(title) text_pattern_ops WHERE
status='Active' AND deleted_at IS NULL` (ingen extension → ingen pg_trgm-
STOPP); **dedikerad SuggestPolicy** per-user FixedWindow 30/10s IOptions-bound
(least common mechanism, ej ListRead-återanvändning); Active-only-filter
bekräftat; LIKE-escape in-block §9.6. Impl: `SuggestJobAdTermsQuery`+Handler+
Validator+`LikePattern` (escape `\`→`\\` först sedan `%`/`_`; explicit 3-arg
`EF.Functions.Like(...,ESCAPE '\')` — implicit-default-bugg fångad av
integrationstest, fixad). Endpoint `GET /job-ads/suggest` auth-gated. Migration
`F2SuggestTitlePrefixIndex` (db-migration-writer `a33a51afa440702a9`, raw-SQL
F2P9-mönster, snapshot oförändrad). Tester: validator-unit (6) + integration
riktig Postgres (3: case-insensitiv/Distinct/Take-cap/escape-left-anchor).
security-auditor `a7cbb04ead2c402e4` **PASS** 8/8 0 Crit/High/GDPR (rate-limit
30/10s BEKRÄFTAT BLOCKING-mandat; Title=publik annons-metadata ADR 0032 §8 ej
PII). code-reviewer `a06d038db1e7121b0` **GO** 0/0/1 Minor FYI. Svit **1083
grön**, build 0/0. **STOPP 5+6 GO** → commit.

## Batch 6 — frontend B–E (KLAR, committad 5110b45)

nextjs-ui-engineer `ae8c96441b94d87ca`. A disclosure-kollaps-filteryta
(resultat-först, civic regel 3/7; sökfält+typeahead alltid synligt, taxonomi/
sort bakom aria-expanded-disclosure). B multi-select-chips (max 10, URL-driven
repeated query-params, ersätter concept-id-fritext). C live-typeahead — CTO
`a377901ce353b58e7` **Variant A** (self-contained debounce-hook ≥300ms/min 2/
AbortController; EJ TanStack — YAGNI+§9.1+§9.2; §4.3 reglerar mutations/pollar
ej typeahead-read; CC-auto-följ). In-block: abort-on-unmount + header-CTO-ref.
D snabbsortering inkl Relevance (disabled+förklarande copy utan söktext). E
Ny-badge (isNew, fast rullande 7-dygnsfönster, civic pill, ingen extra UI-
kontroll). **F HÅRT OUT** (ingen impl/placeholder/copy/tag). vitest 357/357,
tsc clean, lint 0 err/3 pre-existing. i18n: ingen messages/sv.json/next-intl i
repot (discovery-verifierat) → literala svenska strängar = on-disk-konvention
(§9.1); promptens messages/sv.json-krav ej applicerbart.

## Nästa — STOPP 7 + Fas 2 formell stängning

- **STOPP 7 (bundlat):** backend tag-push `v0.2.10-dev` (Batch 1–5 +
  migrations F2SearchCriteriaMultiValue [no-op] + F2SuggestTitlePrefixIndex
  [index], STOPP-5-godkända) + frontend Vercel (main-push auto) → auth-gated
  `pnpm visual-verify` mot FULL korpus → design-reviewer VETO mot bilderna →
  Klas approve + since-fönster-bekräftelse (fast 7d vs användarstyrt).
- **Fas 2 FORMELL STÄNGNING** vid B–E live-verifierat (steg-tracker Fas 2 →
  "Klar 2026-05-16", current-work + docs-keeper).
- Session-end: ADR 0042 impl-notat (adr-keeper — index-val Variant A),
  cluster-namn-drift-fix (`jobbpilot-dev`→`jobbpilot-dev-cluster` i
  aws-rds-migration-apply.md + paus-procedur-erratum), cron-grön-followup
  (snapshot-graceful @02:00 UTC + korpus-trajektoria).
- Async kvar: cron-grön-verifiering (storm-borta CONFIRMED; snapshot-graceful
  + korpus-tillväxt verifieras vid/efter 02:00 UTC).
