# senior-cto-advisor — Platsbanken sök-paritet Fas E1 (split + radius-mekanik + E1b-entanglement-återtriage)

**Datum:** 2026-06-10
**Agent:** senior-cto-advisor (decision-maker)
**Scope:** Fas E-uppdelning, ADR 0052 radius-mekanik, E1b kontrakts-migrations-entanglement

---

## Beslut 1 — Radius 12px→6px på /jobb-hero

**Dom: ren cleanup. Ingen formell ADR 0052-amendment. Klas-promptens "fixa 12px-radie regel-1-brott" ÄR GO. CC kör direkt.**

ADR 0052 Beslut 4 ("12px `--jp-r-xl` ENDAST hero") är en **permissiv tak-regel** (tillät 12px för hero, mandaterade det aldrig). Att sluta använda `--jp-r-xl` på /jobb-hero bryter inte regeln — det gör tokenet oanvänt där, ett tillåtet utfall. OCP/token-indirektion (Martin 2017 kap. 8): tokenet bevaras i token-lagret (landing-hero). YAGNI (Fowler 2018 kap. 3): att tvinga en amendment för att *sluta använda* ett permissivt värde är ceremoni utan substans. Triage mot `feedback_adr_mechanism_vs_env_phase_triage`: utfall = mekanik-not, ej amendment (ADR-substans orörd). `feedback_v3_designspec_veto_scope`: Klas 2026-06-10-direktivet har företräde oavsett. Dokumenteras i ADR 0067 implementerings-notat.

## Beslut 2 — E1 split (E1a/E1b)

**Dom: SPLITTA. E1b (kontrakts-migration, code-reviewer-gated) FÖRST → E1a (design-grind, design-reviewer VETO + Klas-GO) ANDRA.**

SRP-på-PR-nivå (Martin 2017 kap. 7): block 1+2+4 (hero-omskinn + microcopy + docs-drift) ändras av design-skäl; block 3 (kontrakts-plumbing) av kontrakts-skäl. Olika change-reasons + olika gater (design-reviewer VETO vs code-reviewer) → två PR:er. Små kohesiva diffar (SWE@Google 2020 kap. 9): en 24-fil-migration + full hero-redesign i en diff är ogranskbar. Plumbing under design (Stable Dependencies, Martin kap. 14): bygg det stabila lagret först. Per Klas non-stop-direktiv körs de i följd utan mellan-stopp — splitten kostar noll extra Klas-gates. Klas-GO-fönstret landar på E1a:s design-reviewer-veto-rapport.

## Beslut 3 — E1b-entanglement-återtriage (NYTT FAKTUM)

**Dom: Approach A — E1b = `/suggest`→SuggestionDto[]-migration ENDAST. Param-rename + recent-shim + picker-nivå-skifte → ALLT till E2.**

Nytt on-disk-faktum efter Beslut 2: FE:s taxonomy-DTO är stale (modellerar occupation-name, ej yrkesgrupp/kommun som backend `TaxonomyTreeDto` redan exponerar). `buildJobbHref` delas mellan recent-search (bär C2-migrerade KORREKTA yrkesgrupp-ids) och live-pickern (matar occupation-name-ids → `?ssyk=` som backend IGNORERAR = tyst no-op idag). Döper man om paramen `?ssyk=`→`?occupationGroup=` utan picker-skiftet → backend filtrerar `occupation_group_concept_id IN (occupation-name-ids)` → **noll träffar = regression**.

- **Mot (B) (minimal picker-skifte i E1b):** avvisat — skeppar halv E2 (yrkesgrupp-nivå syns men TD-100-paritets-acceptance ej uppfylld = falsk-klar, `project_platsbanken_parity_baseline`); tvingar rendered ändring genom code-reviewer-gate (gate-semantik-brott); blandar två change-reasons (SRP). "Korrekt Yrke-filter nu" är ingen legitim drivkraft — filtret är en befintlig no-op, ingen regression förhindras (YAGNI).
- **Mot (C) (feature-flag):** avvisat — komplexitet för noll akut värde.
- **E2-sekvenskrav:** DTO-utökning + picker-datakälla + `buildJobbHref`-param byts i SAMMA commit-batch (`feedback_di_with_handlers_same_commit`) — annars broken intermediate state.

**KRÄVER KLAS-GO (CTO-flaggat):** Approach A flyttar 2/3 av Klas-promptens E1-listade migrationer till E2. Det är en scope-omförhandling av ett Klas-formulerat uppdrag (CLAUDE.md §9.6 punkt 5 / Regel 5), inte in-block-triage. Klas ska se trade-offen (smalare E1b vs orörd E1-prompt) explicit och kan avvika (t.ex. tidigarelägga E2 istället).

## Referenser

Martin, *Clean Architecture* (2017) kap. 7/8/13/14; Fowler, *Refactoring* 2e (2018) kap. 2/3; Winters/Manshreck/Wright, *SWE@Google* (2020) kap. 9; Beck/Hunt-Thomas YAGNI/KISS; CLAUDE.md §9.6/§9.7; memory `feedback_adr_mechanism_vs_env_phase_triage`, `feedback_v3_designspec_veto_scope`, `project_platsbanken_parity_baseline`, `feedback_di_with_handlers_same_commit`. On-disk: ADR 0052 Beslut 4; ADR 0067 Beslut 7 rad 104 + C2-notat; `TaxonomyTreeDto.cs`; `taxonomy.ts`; `search-params.ts`; `jobb-hero-filters.tsx`.
