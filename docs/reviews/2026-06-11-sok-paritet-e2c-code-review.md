# Code-review: Fas E2c — live facet-counts (branch `feat/sok-paritet-facet-counts-e2c`)

**Status:** ⚠ Changes requested → **alla Major åtgärdade in-block (se Åtgärds-trail)**
**Granskat:** 2026-06-11
**Auktoritet:** CLAUDE.md §2.1/§2.3/§2.5/§3.5/§3.6/§4/§5/§7; E2c-architect §1–3/§6–7; E2c-CTO VAL 1–2 + NBomber-åtgärd (c); E2b-CTO VAL 4

### Blockers
Inga.

### Major

1. **Degraderings-kontraktet brutet — backend-fel renderades som "(0)" på varje popover-rad:** route-handlerns default-gren returnerade 200 + `{}` → hooken parsade tom dict → `counts[id] ?? 0` → "(0)" överallt. Aktiv desinformation ("Solna (0)" när backend är nere); tom dict är tvetydig (legitim tom korpus ≠ fel). Suggest-prejudikatets 200+[] är harmlöst för typeahead men fel här. Krav: non-2xx i default-grenen + doc-fix + FE-test.
2. **`use-facet-counts` effect-cleanup abortade inte in-flight fetch** — bara clearTimeout. Typeahead-prejudikatet (CTO-krav 2026-05-16) gör båda; utan abort kan ett gammalt svar för FEL filter landa transient + setState mot avmonterad komponent. Krav: `abortRef.current?.abort()` i cleanup.

### Minor

1. `as`-cast + JSON-rundtur i hooken — ersätt med ref-spegel av filter.
2. Endpoint-test saknades för helt utelämnad `dimension` (binding-fel → 400).
3. Route-handlerns doc-kommentar beskrev ett kontrakt koden inte höll (del av Major 1).

### Verifierat utan anmärkning

Clean Arch-placering (Application-query, inga EF-läckor); CQRS tunn adapter utan `Total` (SPOT); residual-konsistens via `ISearchQueryParser` (test-låst); `ICapturesRecentSearch` medvetet ej implementerat; `IsInEnum()`-skyddet med 400-inte-500-integ-test; validator-symmetri tecken-för-tecken; VAL 4-`ExcludeDimension` med 4 nya Testcontainers-tester inkl. geo-union-regressionsvakt; FacetCountsPolicy trogen Suggest-spegel; NBomber-aktivering per footer-receptet med omkalibrerade kommentarer och inget som påstår mer än observe-only; total-count-store korrekt useSyncExternalStore (primitivt snapshot, server-snapshot, no-op-guard); hookens useEffect-fetch är sanktionerat prejudikat (ADR 0042-notat — popover-read, ej mutation/poll); sv-SE-locale; hygien (CancellationToken-kedja, inga anti-patterns).

### Sammanfattning

0 blockers, 2 major, 3 minor — backend-delen mergeklar som den stod; Major-fynden låg i FE-kanternas degraderings-/livscykel-beteende. Re-review ej nödvändig (mekaniska, exakta rader).

---

## Åtgärds-trail (huvud-CC, 2026-06-11 — in-block samma PR, commit `85dee89`)

| # | Fynd | Åtgärd |
|---|---|---|
| M1 | 200+{} vid fel | default-gren → **502**; doc-kommentar omskriven; FE-test (502 → inga count-parenteser). |
| M2 | Cleanup utan abort | `clearTimeout + abortRef.current?.abort()` i cleanup, med prejudikat-kommentar. |
| m1 | JSON-rundtur/cast | Ref-spegel av filter (effect-uppdaterad per react-hooks/refs). |
| m2 | Utelämnad dimension otestad | Integ-test → 400; 6/6 gröna. |
| m3 | Stale doc | Med M1. |
