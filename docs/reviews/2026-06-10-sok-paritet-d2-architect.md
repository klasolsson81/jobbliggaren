# dotnet-architect — Platsbanken sök-paritet Fas D2 (ISearchQueryParser)

**Datum:** 2026-06-10
**Scope:** ADR 0067 Beslut 5c — `ISearchQueryParser` för residual-fritext. Design-grind INNAN kod.
**Roll:** Clean Arch/DDD-inramning + identifiera kontrakts-spänning + ge options till senior-cto-advisor (decision-maker, §9.6). Ingen egen rekommendation.

## Kärnspänning identifierad

ADR 0067 Beslut 5c skrevs FÖRE Fas C2 och namnger `ParsedSearchQuery(SsykConceptIds, RegionConceptIds, EmploymentTypeConceptIds, ResidualQ)`. C2-implementerings-notatet (ADR 0067 rad 117) avvecklade `Ssyk` ur sök-identiteten. Post-C2-SPOT = `JobAdFilterCriteria(OccupationGroup, Municipality, Region, Q)`. Kontraktet matchar inte verkligheten → reconciliation krävs.

Djupare designfråga: givet chip-driven UX (5b: FE strukturerar dimensioner; "disambiguering vid input snarare än gissande backend") — extraherar parsern dimensioner ur residual alls, eller normaliserar den bara residual → säker ResidualQ?

## Lager-placering

Port `ISearchQueryParser` + `ParsedSearchQuery`-DTO i Application/JobAds/Abstractions. **Load-bearing avvikelse från IOccupationSynonymExpander-precedensen:** synonymExpander ligger i Infrastructure ENBART för att den binder `IOptions<SearchSynonymsOptions>`. Om parsern är ren ResidualQ-normalisering (ingen config/taxonomi) har den INGET Infrastructure-beroende → per Dependency Rule (Martin 2017 kap. 22) kan `internal sealed`-impl bo HELT i Application. Renare än synonymExpander-splitten, bättre testbarhet (§2.4). Om dimension-extraherande → behöver taxonomi-lookup → Infra-split, men det är "gissande backend" som 5c avråder.

## Reconciliation-tabell (post-C2)

| ADR 5c-fält | Post-C2 | Reconcilerat | Motiv |
|---|---|---|---|
| SsykConceptIds | Ssyk avvecklad | OccupationGroupConceptIds (om dim behålls) | SPOT:ens occupation-dim är OccupationGroup (ssyk-level-4) |
| (saknas) | Municipality finns | MunicipalityConceptIds | ADR glömde Municipality |
| RegionConceptIds | Region finns | RegionConceptIds | Direkt |
| EmploymentTypeConceptIds | NULL-data tills re-ingest | **UTESLUTS** | Falsk-klar-gate (D1-disciplin) |
| ResidualQ | Q | ResidualQ | Direkt — möter q-grenen |

## Multi-approach-options (för CTO)

**Fråga 1 (parser-form):** A = ren ResidualQ-normalisering (HONEST, ingen dimension-gissning, impl helt i Application). B = minimal 1:1 token-lyft (GRÄNSFALL, dubblerar FE-chip-ansvar, kräver Infra). C = full fuzzy (GISSANDE BACKEND, avrått av 5c).
**Fråga 2 (kontrakt-dims):** A = bara ResidualQ. B = ResidualQ + post-C2-dims (utan EmploymentType). C = ADR verbatim (avrådes — avvecklad Ssyk + NULL-gated + saknar Municipality).
Koppling: form-A→dim-A, form-B→dim-B.

## Reconciliation-natur (architect-bedömning)

BLANDAD: (a) Ssyk→OccupationGroup/−EmploymentType/+Municipality = mekanik-konkretisering av redan-Accepted-beslut → implementerings-notat (C2-precedens). (b) Om-parsern-extraherar-dimensioner = substantiell, ej avgjord av tidigare ADR → CTO-beslut; om vald väg tömmer kontraktet kan det kräva Klas-STOPP.

## Residual-Q-inkopplingspunkt

Kedjan: `ListJobAdsQuery.Q → ListJobAdsQueryHandler → JobAdFilterCriteria.Q → ApplyCriteria q-gren (JobAdSearchQuery.cs:189-227, rent OR-additivt)`. Designkrav: ResidualQ får ENDAST nå sök-kompositionen via `JobAdFilterCriteria.Q`, ALDRIG eget `.Where` AND-villkor → kraschsäkerheten blir kompilator-garanterad (olika fält på samma DTO).

## DoS/injection-yta (för security-auditor)

Längd-cap (referera SearchCriteria.QMaxLength=100, duplicera ej), kontrolltecken-strip, whitespace-kollaps. websearch_to_tsquery robust → ingen tsquery-escape behövs; EF parametriserar → ingen SQL-injection. Primär yta = resurs-DoS, inte injection.

## Testbarhet

Parser ren CPU → Application.UnitTests utan DB (kräver InternalsVisibleTo). ResidualQ→FTS rör shadow-props/Npgsql → Testcontainers, EJ InMemory (memory `feedback_ef_strongly_typed_vo_contains_translation`).

**Referenser:** Martin 2017 kap. 7/22, Evans 2003 kap. 14, Fowler 2018 (Speculative Generality, YAGNI).
