---
session: E2h — chips-i-sökfältet
datum: 2026-06-11
slug: e2h-chips-i-sokfaltet
status: PR öppen (automerge)
commits:
  - feat(web): Platsbanken sök-paritet Fas E2h — chips-i-sökfältet
  - docs: ADR 0067 impl-notat E2h + agent-reviews + current-work + session-logg
---

# Session E2h — chips-i-sökfältet (Klas produktspec efter E2d-rendered-test)

## Bakgrund
Klas testade E2d (#51) renderat: val av förslag sökte direkt + tömde fältet —
fel flöde ("kan inte skriva klart systemutvecklare göteborg heltid").
AskUserQuestion-svar låste specen: val → tagg-chip I fältet; ALLT blir taggar
(även fritext); mellanslag/komma avgränsar; live-resultat; Tab väljer markerat.
Minus-operator (`-Deltid`, kompis-feedback) = ej i scope, Klas-pending.
Specen sparad i memory `project_e2h_chip_in_field_spec`. "Fortsätt" efter paus.

## Beslut (architect + CTO INLINE)
- VAL 1=A: chips deriveras HELT ur URL:en (q = en chip per ord, wire-ärligt);
  enda lokala staten = utkastet. E2d-buggklassen orepresenterbar.
- VAL 2=B: router.replace + {scroll:false} för fält-commits (mekanik-klassad,
  ej produktval); toolbar-× pushar — dokumenterad asymmetri.
- VAL 3=A: recent-capture-mellansteg accepteras + observeras (ej backend-fold).
- VAL 4=B: ny ChipSearchField komponerar JobAdTypeahead (OCP-additiva props).
- Architect-upptäckt: websearch_to_tsquery tolkar ledande `-` som NOT redan
  idag → tokenizern strippar `-` (håller Klas minus-beslut genuint öppet).
- CTO-addendum: utkast-sentinel-borttagningen ACK:ad (naiv sentinel kan inte
  skilja egen RSC-roundtrip från extern nav; destruktiv felmod förlorar).

## Levererat
- `lib/job-ads/tokenize.ts` (ren, 17 tester) + `chip-models.ts` (delad SPOT
  toolbar+fält) + `Q_MAX_LENGTH`-konstant + `ChipSearchField` +
  `JobbHeroSearch`-rewrite (useOptimistic, hydrated-flagga, no-JS-fallback,
  aria-live-annonser, q-max-guard) + typeahead-props (selectOnTab/
  onEmptyBackspace/inputRef, onMouseLeave-fantomvalskydd) + toolbar-refaktor
  (useState-kopior → useOptimistic) + chipfield-CSS (tema-stabila literaler).

## Reviews
- design-reviewer: VETO (B1 ×-fokus-ring WCAG 2.4.7; B2 fokusförlust 2.4.3;
  M1 tema-skiftande chips; M2 tyst q-max-swap; M3 kongruens "Lade till/Tog
  bort"; M4 mouseover+Tab-fantomval) → alla åtgärdade in-block → re-review
  **Approved**.
- code-reviewer: 0 Block / 2 Major (sentinel-avvikelse → CTO-ACK + PR-body;
  testgap → 5 nya edge-case-tester) / 4 Minor (q-dubblett-dedupe, no-op-guard
  i förslags-val, död selectRef, hårdkodad 100) — alla fixade.
- security-auditor ej triggad (ingen ny backend-yta).
- Gates: tsc/eslint/818 vitest (+39)/pnpm build gröna.

## Nästa session
- Klas rendered-test av chips-flödet (B1-fokus-ring + q-max-annons särskilt).
- Klas-dom: minus-operatorn (NOT — backend-fas, ADR 0062-notat vid GO).
- Pending sedan tidigare: spec-edit-hooken, de-grönings-domar, zod-drift-
  triage, re-ingest Klass 2.
