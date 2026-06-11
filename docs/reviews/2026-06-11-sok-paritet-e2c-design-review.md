# Design-review: Fas E2c — per-option facet-counts + "Visa N annonser" (`feat/sok-paritet-facet-counts-e2c`)

**Status:** ⚠ Changes requested (1 Major, 1 Minor — inga Blockers, **inget VETO**) → **båda åtgärdade in-block (commit `85dee89`)**
**Granskat:** 2026-06-11
**Auktoritet:** DESIGN.md, jobbpilot-design-tokens/copy/a11y, ADR 0067 Beslut 4 + impl-notat, ADR 0068
**Scope:** kod/diff-review; rendered-verifiering deferred per FAS-DEFERRAL-MANIFEST (Klas granskar Vercel post-merge — E2a/E2b/E2e-prejudikat).

### Major (åtgärdat)

1. **"Visa 1 annonser" — singular-böjning saknades** (`jobb-hero-filters.tsx`). Träffräknaren i samma vy böjer korrekt ("träff"/"träffar") — intern inkonsistens + grammatiskt fel. Noll-fallet ("Visa 0 annonser") godkänns medvetet: ärlig för-kommunikation innan stängning. **Åtgärd:** `totalCount === 1 ? "annons" : "annonser"` + vitest-fall för 1/2. ✓

### Minor (åtgärdat)

1. **`font-size: 12.5px` off-scale** — konventionens faktiska värden är 13px (hero-chip-count) resp. 12px (pill-count); 12,5 var ett tredje fraktionellt mellanvärde. **Åtgärd:** 13px. ✓

### Domar per granskningspunkt

1. **Per-option-counts: GODKÄNT.** Mono+ink-2-mönstret korrekt (WCAG ~7,6:1 light, högt i dark, inkl. hover-ytan). **Tre-tillstånds-semantiken förebildlig:** känd nolla ("(0)" när counts laddade) skiljs från okänt (degraderad → inga tal) — att visa "(0)" är ärligt; att INTE visa "(0)" vid degradering lika viktigt (en "(0)" som betyder "vet ej" vore desinformation).
2. **"Hela länet"-count + Yrkes count-lösa "Välj alla": GODKÄNT.** Region-facettens count semantiskt koherent under VAL 4; Yrkes-summan rätt utelämnad (påhittat tal — civic-utility visar hellre inget tal än fel tal).
3. **"Visa N annonser"-knappen (CTO-flaggad risk): GODKÄNT.** "Visa" är det ärligaste verbet (alternativen "Använd filter"/"Tillämpa" skulle aktivt ljuga i live-commit-modellen; "Stäng" kastar bort count-feedbacken). **Felkostnad vid fel mental modell = noll** (användaren får exakt N resultat oavsett modell — ingen felväg). Talet i etiketten lär dessutom ut live-commit-modellen (N uppdateras medan man bockar). Platsbanken-paritet: målgruppens inlärda stäng-affordance. Primär-behandlingen rätt (ytans huvudhandling; accent-800-fill per knapp-kontraktet). Fallbacken "Visa annonser" = ärlig degradering. Observation utan åtgärd: N och per-option-counts har olika uppdateringslatens — samma som träffräknaren själv, acceptabelt; SPOT-valet (aldrig facett-summa) rätt.
4. **A11y: GODKÄNT.** Count i accessible name önskvärt (SR-användare får samma beslutsunderlag; WCAG 2.5.3 håller — synliga etiketten ingår). Footer efter kolumnerna = tab-ordning matchar visuell ordning. Ingen live-region för count-uppdateringar = rätt avvägning (hint, ej tillståndsskifte).
5. **Svenska + locale: GODKÄNT** (frånsett Major 1, åtgärdad). toLocaleString sv-SE (non-breaking space), inga utropstecken/emoji/placeholder.

### Bra gjort

- Tre-tillstånds-ärligheten genomförd konsekvent i alla fyra lager (route → hook → popover → CSS)
- total-count-store löser ö-delningen utan att röra streaming-arkitekturen; SPOT-disciplinen dokumenterad vid varje konsumtionspunkt
- Enabled-gating (ingen bakgrunds-poll) + behållna counts vid stängning (ingen flimmer-nollning)
- `footer?: ReactNode` håller popovern presentationsdum
- Testerna bevisar navigations-frihet vid stängning (pushMock ej anropad — knappen är inte en commit-knapp ens tekniskt)

### Sammanfattning

0 Blockers, 1 Major (åtgärdad), 1 Minor (åtgärdad). Inget VETO; CTO-flaggade knappen godkänd med explicit dom. Re-review ej nödvändig (mekaniska, entydigt specade fixar). **Mergeklar.**
