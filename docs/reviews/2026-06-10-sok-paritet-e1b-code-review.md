# code-reviewer — Platsbanken sök-paritet Fas E1b (suggest-kontrakt SuggestionDto[])

**Datum:** 2026-06-10
**Agent:** code-reviewer
**Branch:** `feat/sok-paritet-fe-suggest-e1b`
**Verdikt:** ✓ APPROVED — 0 Block / 0 Major / 1 Minor (åtgärdad in-block)

---

## Scope

FE-kontrakts-migration `GET /api/v1/job-ads/suggest` `string[]` → `SuggestionDto[]` (ADR 0067 Beslut 5a). 4 filer: `lib/dto/job-ads.ts`, `job-ad-typeahead.tsx`, + två testfiler.

## Fynd

**Kontrakts-korrekthet — verifierad.** `SuggestionKind.cs` deklarationsordning `Title=0, Region=1, Municipality=2, OccupationField=3, OccupationGroup=4` speglas exakt av `SUGGESTION_KIND_ORDER`. Enumen saknar `JsonStringEnumConverter` (global + attribut) → int-på-wire håller. Ordinal-tabell `as const`, transform `addIssue` + `z.NEVER` vid out-of-range (fail-stängt). Testtäckt 0–4 + array-mix.

**Defense-in-depth `int|string`-union — motiverad, ej spekulativ.** Strukturellt identisk med etablerad `sortByFromWire`-precedens (`recent-searches.ts`/`saved-searches.ts`). Följer kodifierad konvention, uppfinner inte nytt mönster.

**Render-säkerhet — ingen XSS-yta.** `{item.label}` som JSX-text (React auto-escape). `conceptId` endast i `key`, aldrig DOM. Inget `dangerouslySetInnerHTML`.

**Speculative Generality (skarpaste frågan) — inte YAGNI-brott.** `kind`/`conceptId` är det faktiska wire-kontraktet backend emitterar; FE måste parsa dem annars vore schemat en lögn. `kind` används dessutom i `key`. conceptId-konsumtion (chip-komposition) är explicit E2 per CTO-dom, dokumenterat i kommentar.

**Svenska (§10) — korrekt.** Saklig svenska, inga emoji/utropstecken.

### Minor (åtgärdad in-block)

1. **Kommentar rad 78** — "Samma int-konvention som JobAdSortBy" var imprecis (`jobAdSortBySchema` bär sträng-namn; int-konventionen tillhör `sortByFromWire`/`SAVED_SEARCH_SORT_ORDER`). Justerad till att referera `sortByFromWire` + `SAVED_SEARCH_SORT_ORDER` (kommentar-hygien, ingen kodpåverkan).

## Sammanfattning

0 blockers, 0 major, 1 minor (åtgärdad). Kontraktet korrekt mot verifierad backend-enum; defense-in-depth följer precedens; render-path säker; "oanvända" fält är legitim kontraktsvalidering. Mergeklar. `JobAdTypeahead` ej wirad live → noll UI-regression, visual-verify-grinden ej triggad. 45 vitest gröna, tsc/eslint rena, pnpm build grön.
