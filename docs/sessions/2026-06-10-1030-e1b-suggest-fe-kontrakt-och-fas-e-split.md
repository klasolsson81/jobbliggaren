---
session: Platsbanken sök-paritet Fas E — uppdelning + E1b suggest-kontrakt
datum: 2026-06-10
slug: e1b-suggest-fe-kontrakt-och-fas-e-split
status: E1b levererad (PR mot main); E1a + scope-omförhandling pending Klas-GO
commits:
  - 5fee02c feat(jobads) E1b typeahead-suggest FE-kontrakt SuggestionDto[]
  - (docs-commit) ADR 0067 impl-note + current-work + session-log + reviews
---

# Fas E — design-grind: uppdelning + E1b-leverans

## Mål (Klas-prompt)

Fas E (FE-sökyta) med låst design-riktning A "Papperskontoret" + varm papperston
`#FAF9F6`. Klas-prompten rekommenderade E1 (design-grind + microcopy +
kontrakts-migration + docs-drift) som första session, med förväntad sub-splitt.

## Vad som gjordes

### Discovery + agent-domar (före kod)

1. **Förkrav verifierade:** HEAD `13eb0af`, status clean, docker uppe, Api
   `/api/ready` 200, FE 3000 200.
2. **dotnet-architect — varm-canvas-token:** /jobb-scoped ny `--jp-hero-canvas`
   (rör ej app-wide `--jp-canvas`), dark ärver `--jp-canvas` #0B1525, behåll
   `.jp-hero`-klassnamn, 12px→6px. (`docs/reviews/2026-06-10-sok-paritet-e1-architect.md`)
3. **senior-cto-advisor — split + radius:** radius 12→6 = cleanup (ingen ADR
   0052-amendment, Klas-prompt är GO); SPLITTA E1→E1b (plumbing, code-reviewer)
   först → E1a (design, design-reviewer VETO + Klas-GO).
4. **Verifierat backend-kontrakt on-disk:** list-endpoint tar `?occupationGroup=`
   (`?ssyk=` borttagen C2); `SuggestionDto = {kind, conceptId, label}`, kind =
   native enum utan converter → HELTAL på wire; `RecentJobSearchDto` har nya
   `occupationGroupList`/`municipalityList`/-Labels + deprecated tomma
   `ssykList`/`ssykLabels`.
5. **KRITISKT fynd — entanglement:** FE:s `taxonomy.ts` är stale (modellerar
   occupation-name, ej yrkesgrupp/kommun som backend `TaxonomyTreeDto` redan
   exponerar). Live Yrke-picker matar occupation-name-ids → `?ssyk=` →
   backend ignorerar = tyst no-op idag. `?ssyk=`→`?occupationGroup=`-rename utan
   picker-nivå-skifte → Yrke-filter regresserar till **noll träffar**.
6. **senior-cto-advisor — återtriage (Approach A):** E1b = `/suggest`-migration
   ENDAST; param-rename + recent-shim + picker-skifte → E2 (atomiskt block).
   **CTO flaggade Klas-GO** — omförhandlar Klas-promptens E1-lista.
   (`docs/reviews/2026-06-10-sok-paritet-e1-cto.md`)

### E1b-implementation (suggest-kontrakt)

- `lib/dto/job-ads.ts`: `suggestionDtoSchema` + `suggestionKindFromWire`
  (wire-heltal → namn via `SUGGESTION_KIND_ORDER`, defensivt int|string-union
  speglar `sortByFromWire`).
- `job-ad-typeahead.tsx`: konsumerar `SuggestionDto[]`, renderar `item.label`,
  `choose(item.label)`. kind/conceptId = kontraktsfält, chip-komposition E2.
- Tester: +7 schema-fall i `job-ads.test.ts`; fixtures migrerade i
  `job-ad-typeahead.test.tsx`. 45 vitest gröna, tsc/eslint rena, pnpm build grön.
- Reviews: code-reviewer 0 Block/0 Major/1 Minor (kommentar-precision, in-block);
  security-auditor APPROVED.

## Beslut & avvägningar

- **E1 split + sekvens E1b→E1a:** SRP-på-PR-nivå, olika gater, granskbar diff.
- **Radius cleanup ej amendment:** ADR 0052 Beslut 4 är permissiv tak-regel.
- **E1b = suggest-only:** entanglement-fyndet gör param-rename till icke-plumbing
  (regression-risk). Approach A undviker falsk-klar mot TD-100.
- **`JobAdTypeahead` ej wirad live:** migrationen är noll-regression.

## Nästa session

1. **Klas-GO på scope-omförhandling** (E1b=suggest-only, resten → E2) — eller avvik.
2. **E1a design-grind** (oberoende av scope-frågan): hero varm canvas + regel-1-fixar
   + microcopy + docs-drift spec-edit (Klas kör `approve-spec-edit.sh`).
   design-reviewer VETO + Klas-GO.
3. **E2** (efter scope-GO): FE-taxonomy-DTO-utökning + picker-nivå-skifte +
   param-rename + recent-shim + Län→Kommun-kaskad + live-count + chip-komposition +
   TD-100-paritet. Atomisk commit-batch.

## Stack-status vid sessionsslut

Api 5049 / Worker / FE 3000 körande (ingen bygg-lås-stopp denna session — endast
FE-edits, ingen .NET-rebuild). `/api/ready` 200 verifierad vid start.
