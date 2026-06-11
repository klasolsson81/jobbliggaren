---
session: E2d — typeahead-chip-komponist
datum: 2026-06-11
slug: e2d-typeahead-chip-komponist
status: PR öppen (automerge), Klas-STOPP-fråga (VAL 3 auto-chip) i rapport
commits:
  - feat(web): Platsbanken sök-paritet Fas E2d — typeahead-chip-komponist
  - docs: ADR 0067 impl-notat E2d + current-work + session-logg
---

# Session E2d — typeahead-chip-komponist (ADR 0067 Beslut 5b)

## Mål
Wira `JobAdTypeahead` live i hero-sökrutan ("Volvo Göteborg Heltid"-visionen):
taxonomi-förslag → strukturerade chips per dimension; omatchad text →
filtrerande fritext (q). + ärvda E2d-Minors från E2f-review.

Klas-grind ifylld (CHIP/RESIDUAL BEKRÄFTAD: JA) → byggde. (Föregående prompt
hade tom grind → korrekt HALT med morgonrapport.)

## Vad gjordes
- **Discovery (§9.4):** hero-sökfältet var ett rent server-renderat `<input
  name="q">` i en GET-form — JobAdTypeahead var prod-owirad. Suggest-korpusen
  emitterar Title/Region/Municipality/OccupationField/OccupationGroup (Occupation
  uteslutet, VAL 4). URL-dimensioner: bara occupationGroup/region/municipality;
  OccupationField är suggest-bart men ingen dimension → öppen mappnings-fråga.
- **dotnet-architect INLINE:** ö-topologi (A/B/C), kind→dimension-mappning
  (OccupationField/Occupation/EmploymentType), auto-chip-vid-submit-frågan,
  a11y. Flaggade VAL 3 som produktval → Klas-STOPP.
- **senior-cto-advisor (decision-maker):** VAL 1=A (separata öar, URL-sanning,
  GET-fallback KRAV) · VAL 2a=materialisera OccupationField · VAL 2b=Occupation
  förbli ute · VAL 2c=GATED exhaustiv switch · VAL 3=Klas-STOPP bekräftad
  (blockerar EJ resten). CC gav ingen egen rekommendation
  (`feedback_cto_decides_multi_approach`).
- **Implementation (FE-only, ingen .NET-touch):**
  - `lib/job-ads/chip-composition.ts` — ren `composeSuggestionChip` + exhaustiv
    switch + `assertNever` (VAL 2c). VAL 2a materialisering. Återanvänder
    `applyMunicipalityChange` (SPOT).
  - `job-ad-typeahead.tsx` — refaktorerad till fullständig a11y-combobox;
    `onSelect(SuggestionDto)`; injicerbar styling.
  - `jobb-hero-search.tsx` (NY) — client-ö, form + typeahead + router.push;
    prev-prop-sentinel q-synk; no-JS hidden inputs.
  - `jobb-filter-popover.tsx` — F6-Minors (selectAllLabel-fn, dialogLabel,
    tri-state mixed) + hero-filters-callers.
  - `page.tsx` — inline-form ersatt med `<JobbHeroSearch>`.
  - `globals.css` — dropdown-overflow-escape via `.jp-hero__searchblock
    { position:relative }` + `.jp-hero__searchfield`.

## Beslut & detours
- **Ö-topologi A vald** framför sammanslagning: konformerar E2g (URL-sanning,
  useOptimistic), buildJobbHref-SPOT, medveten streaming-ö-split (F6 P4 B1).
- **prev-prop-sentinel** (setState under render) för q-synk — Reacts
  dokumenterade prop→state-synk, lint-säkert (ej effect), motgift mot E2g-bugg-
  klassen i sökfältet.
- **design Minor 1 in-block:** rateLimited-raden gjordes absolut-positionerad så
  den escapar hero-searchrowens overflow:hidden (läsbar på surface-primary).
- **VAL 3 EJ byggd** — Klas-STOPP-fråga (A/B/C) lyft i PR-rapporten; baslinjen
  (hela strängen → residual-q) är Accepted-default oavsett.

## Reviews
- code-reviewer: Approved, 0 Block / 0 Major / 2 Minor (CSS-escape framtids-
  vaksamhet; onåbar defensiv-gren).
- design-reviewer: Approved, 0 Blocker / 0 Major / 2 Minor (rateLimited-overflow
  → åtgärdad in-block; onMouseEnter FYI). Rendered-GO pending live-deploy.
- security-auditor: EJ triggad (ingen ny backend-input-yta).
- tsc / eslint / 779 vitest (+21 netto) / pnpm build: gröna.

## Nästa session
- **Klas-svar på VAL 3** (auto-chip-vid-submit A/B/C) → ev. additiv opt-in-PR.
- Rendered-granskning på Vercel av #46–#50 + E2d-ytan (live-deploy).
- Re-ingest Klass 2 (Klas-åtgärd) → låser upp Heltid-dimensionen + tredje
  Filter-knappen (VAL 2c blir wirad via compile-tvunget switch-tillägg).
- Spec-edit-hooken (DESIGN.md/tokens-skill) + de-grönings-domar + zod-drift-
  triage kvarstår.
