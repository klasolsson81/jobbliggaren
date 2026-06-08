# CTO-beslutsrapport — Platsbanken sök-paritet Fas B1 (data-layer, Klass 1)

**Datum:** 2026-06-08
**Agent:** senior-cto-advisor (agentId `aec40007a9f5aba41`) — decision-maker. CC ger ingen egen rek (`feedback_cto_decides_multi_approach`). Klas sista ordet på flaggat.
**Underlag:** ADR 0067 Beslut 7-tabell, ADR 0043 Beslut B + amendment, on-disk-verifiering 2026-06-08 (`TaxonomyReadModel.LoadAsync`, `TaxonomySnapshotSeeder.MapRows`, `TaxonomySnapshotFile`, `TaxonomyTreeDto`). Ingen committad generator existerar i repot (verifierat — endast FE-TS).

---

## BESLUT 1 — Snapshot-genererings-/GraphQL-hämtningsstrategi: **Variant C**

**Committat genererings-script, men `taxonomy-snapshot.json` förblir sanningskällan i repot.** Scriptet är dokumenterad, reproducerbar produktion av snapshotten — inte ett build- eller runtime-beroende. CC kör det en gång, committar både script och uppdaterad JSON (version 29→30). Konkret: litet off-build genererings-script (`tools/taxonomy-snapshot/`) som hämtar via GraphQL, transformerar till `TaxonomySnapshotFile`-shapen, sorterar deterministiskt, skriver JSON. Embedded resource + seeder oförändrade.

**Motivering:** Hermetisk build bevaras (ADR 0043 Beslut B — manuell körning, off-build, committad). Variant A (CC WebFetch:ar direkt in i JSON) lämnar inget reproducerbart spår — reproducerar exakt den brist Beslut B redan dokumenterar; vi kan rätta den kostnadsfritt. Reproducibility/hermeticity (Winters/Manshreck/Wright 2020 kap. 18). Determinism mot diff-brus (GraphQL har ingen garanterad ordning → kanonisk sortering måste kodifieras). DRY som knowledge piece (transformregeln på ETT ställe, Hunt/Thomas 1999 kap. 7). Dedup-disciplin (Evans 2003 kap. 14) — single-parent-regel maskinell, ej manuell.

**Avvisat:** Variant A (snabblösning, reproducerar Beslut B:s enda dokumenterade svaghet; "engångs" i Beslut B syftar på körningskadens, ej frånvaro av artefakt). Variant B läst som build-beroende (bryter hermeticity); läst som committat-script+committad-JSON = identisk med C.

**Klas-GO:** Nej för CTO-beslutet — design-delbeslut inom ADR 0067 Beslut 7:s GO:ade B1-mandat. Version-bump + JSON-commit ingår i B1-PR:n (redan GO:at).

---

## BESLUT 2 — B1/C1-gräns för LoadAsync + DTO-exponering: **Variant A**

**B1 seedar enbart de nya Kind-raderna (`Municipality`/`OccupationGroup`) i `taxonomy_concepts`. `LoadAsync` och `TaxonomyTreeDto` förblir orörda i B1. Hela DTO-exponeringen ligger i C1, exakt där ADR 0067 Beslut 7 placerar den.** Mekaniskt verifierat: `LoadAsync` filtrerar explicit på `Kind == Region/Occupation/OccupationField` → nya rader ignoreras tyst, ingen konsument bryts, inget test rödfärgas.

**Motivering:** Fas-disciplin är skriven ADR-dom (CLAUDE.md §9.6 + ADR 0067 Beslut 7 lägger DTO i C1). Startpromptens "LoadAsync blir Kind-medveten" är korrekt i minimal läsning (fortsätter fungera när tabellen har nya Kinds). YAGNI/Speculative Generality (Beck; Fowler 2018 kap. 3) — DTO-struktur ingen konsument läser förrän C1 = "falsk klar" på data-lagret, samma anti-pattern ADR 0067 själv avvisar (Beslut 1 Option B, Beslut 2 "Klass 1+2 i en migration"). SRP/change-reason (Martin 2017 kap. 7) — snapshot-shape (B1) vs DTO-form (C1) = två change-reasons → två faser. Renaste PR-gräns (Ford/Parsons/Kua 2017; ADR 0065 PR-per-fas).

**Avvisat:** Variant B (drar C1-DTO-arbete + handler/integration-tester in i B1, blandar change-reasons). Variant C (halv-exponerat DTO utan query-väg = värsta-av-två-världar).

**Trade-off accepterad — "död data" i B1:** Municipality/OccupationGroup-rader sitter i tabellen utan konsument förrän C1. Avsiktligt: ADR 0067 Beslut 7 säger "seeder" i B1 men "DTO" i C1 → författarna avsåg seed i B1, exponering i C1. Idempotent version-gated seeder → C1 blir ren läs-vägs-ändring utan ny seed/migration. Noll runtime-kostnad (LoadAsync filtrerar bort med samma Where-pass).

**Följd:** B1 utökar `TaxonomySnapshotFile` + `MapRows` + `TaxonomyConceptKind` (seeder-/snapshot-lagrets ansvar) men rör INTE `LoadAsync`/`TaxonomyTreeDto` (C1-domän). Om nested snapshot-shape tvingar en `TaxonomyTreeDto`-rörelse → STOPP (tecken på C1-scope-glidning).

**Klas-GO:** Nej utöver redan givet fas-GO. CC går direkt till impl.

---

## Sammanfattad dom

| Beslut | Vald variant | Klas-GO utöver fas-GO? |
|---|---|---|
| 1 — Snapshot-generering | **C** (committat script, JSON är källan) | Nej |
| 2 — B1/C1-gräns | **A** (seeda nya Kinds, LoadAsync/DTO orörda → C1) | Nej |

**Referenser:** Martin *Clean Architecture* (2017) kap. 7; Evans *DDD* (2003) kap. 14; Beck/Fowler *Refactoring* 2nd (2018) kap. 3; Hunt/Thomas (1999) kap. 7; Winters/Manshreck/Wright (2020) kap. 18; Ford/Parsons/Kua (2017); ADR 0043 Beslut B + amendment; ADR 0067 Beslut 7; CLAUDE.md §9.6/§2.4.
