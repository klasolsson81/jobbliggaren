# Code-review: Fas E2b — Platsbanken sök-paritet (geo-union + Län→Kommun-kaskad + Ssyk-shim-borttagning)

**Status:** ⚠ Changes requested → **alla Blocker/Major åtgärdade in-block (se Åtgärds-trail nederst)**
**Granskat:** 2026-06-11
**Branch:** `feat/sok-paritet-fe-kommun-kaskad-e2b` (637f170, 597fd84, d931b2f mot main)
**Auktoritet:** CLAUDE.md §2.1/§2.3 (Clean Arch/CQRS), §3 (C#), §4 (TS strict), §5 (anti-patterns), §7 (test-krav); ADR 0067, ADR 0062 (SPOT), ADR 0042 Beslut B; CTO-dom 2026-06-11 (VAL 1–4 + commit-direktiv 1–5)

### Blockers (måste fixas innan merge)

1. **Commit 3 (shim-borttagningen) missade en wire-konsument — tre integration-tester går garanterat rött**
   Fil: `tests/JobbPilot.Api.IntegrationTests/RecentSearches/RecentSearchesTests.cs:90-91, 115`
   Testerna asserterar wire-JSON-fälten som d931b2f tog bort (`row.GetProperty("ssykList")...`). `JsonElement.GetProperty` kastar `KeyNotFoundException` när nyckeln saknas → `ci`-aggregatet blir rött → automerge-labeln får inte sättas förrän detta är fixat. Krävs: ta bort asserts:en + uppdatera stale fil-kommentar; komplettera gärna med negativ vakthund (`TryGetProperty(...).ShouldBeFalse()`) — symmetri med `RecentJobSearchDto_ShouldNotCarryDeprecatedSsykShim`. Motivering: CLAUDE.md §7 + §6.3 punkt 5. Exakt den konsument-miss CTO-direktiv 3 flaggade för.

### Major (måste fixas innan merge)

1. **CTO-direktiv 4 ej levererat: ADR 0067-implementerings-notatet E2b + ADR 0042-klargörande-raden saknas i diffen — koden refererar ett dokument som inte finns.** Krävs: skriv notatet + ADR 0042-raden som docs-commit i **samma PR** (CLAUDE.md §1.5 steg 4 / ADR 0065). VAL 4-facett-låsningen extra viktig på pränt: `ExcludeDimension(FacetDimension.Municipality)` tömmer idag bara municipality-listan (`JobAdSearchQuery.cs:126-134`) — under union-semantiken är det fel spec för ort-facetten. Latent (noll `FacetCountsAsync`-konsumenter utanför Infrastructure, grep-verifierat), men E2c bygger mot detta — odokumenterat blir det en falsk-klar-fälla.

### Minor (nice-to-fix, blockerar ej)

1. **Cap-aritmetik-kommentaren ej skriven** (CTO-direktiv 2 sista mening). Föreslagen plats: `selectedConceptIds` i `jobb-results.tsx`.
2. **`saved-searches.ts`-zod är drifted mot backend — pre-existing, EJ E2b-introducerat:** FE-schemat kräver `ssyk` (REQUIRED) + `ssykLabels`, men backend `SavedSearchDto` bär `OccupationGroup`/`Municipality`/`Region` + labels sedan C2 — första FE-konsument av sparade sökningar får hård zod-fail. Idag latent (enda konsumenter är schemafilen + dess test). Hör till Fas E-pariteten — lyft till Klas/CTO för egen touch innan saved-search-FE-ytan byggs; inte in-block i E2b.
3. **Stale "deprecated består i wire-formen"-kommentar** i `RecentSearchesTests.cs` — åtgärdas med Blocker 1.

### Bra gjort

- **Backend-grenen är exakt CTO VAL 1:** union ENDAST när båda listorna icke-tomma (`if`/`else if` — ensamma grenar bevarade bit-för-bit); lokala list-kopior före expression-trädet; AND mot yrke/q orört. SPOT intakt — `ApplyCriteria` är fortsatt enda filter-vägen (ADR 0039/0062).
- **Testcontainers-täckningen träffar alla fem CTO-kraven:** cross-län-union, intra-län, syntetisk region-only-annons (recall-beviset — fabricerad rad, inte antagen korpus-egenskap), enkel-gren-regression, ortogonal AND. GUID-unika concept-ids ger test-isolering; `IDateTimeProvider` injicerad; `TestContext.Current.CancellationToken` propagerad.
- **Kontraktstesterna skarpare efter shim-borttagningen:** `ShouldNotCarryDeprecatedSsykShim`-vakthund + kanoniskt positionslås.
- **FE atomisk batch komplett (alla sex architect-punkter verifierade i diff):** `buildJobbHref` (required `municipality` → tsc tvingar alla konsumenter), `buildPageHref`/`rawParams` (F3-felklassen täppt med kommentar), hidden inputs, Suspense-`municipalityKey`, `selectedConceptIds`, recent-row + hero-chip, chips-ordningen med delad `MapPin`.
- **Dual-axis-kontraktet följer VAL 3 troget:** `groupAxis?`-parameterisering; enkolumns-läget HELT borta (grep: noll rester); a11y bevarad; `key`-remount utan setState-i-effect.
- **`ort-selection.ts` är ren, testbar logik på rätt plats** — unit-tester inkl. cross-län-mix, stale-id-tolerans, 414-skyddet. Taxonomy-zod REQUIRED `municipalities` + fail-loud-test är rätt dom.

### Sammanfattning

1 blocker, 1 major, 3 minor. Arkitekturen, semantiken och FE-batchen är genomgående korrekta mot CTO-domen — fynden är leverans-kompletthet, inte design. Re-review behövs inte för B1/M1 (mekaniska åtgärder med tydlig spec) — men automerge-labeln ska inte sättas förrän B1 är fixad och integration-sviten verifierat grön.

---

## Åtgärds-trail (huvud-CC, 2026-06-11 — in-block samma PR)

| # | Fynd | Åtgärd | Commit |
|---|---|---|---|
| B1 | RecentSearchesTests röda asserts | Asserts inverterade till frånvaro-vakthund (`TryGetProperty → false`); kombinationstestet i `ListJobAdsOccupationGroupFilterTests` omskrivet till geo-union-semantiken (ViaKommun/ViaRegion/FelYrke). 15/15 gröna vid re-körning. | `bc0ead1` |
| M1 | ADR-docs saknades | ADR 0067 implementerings-notat E2b (geo-union + dimensions-läsning + promotion-avgränsning + Obestämd-ort-trigger som explicit rest + VAL 4-facett-låsning) + ADR 0042 klargörande-not skrivna; ingår i docs-committen i samma PR. | docs-commit |
| m1 | Cap-kommentar | Skriven vid `selectedConceptIds` (711 < 1600 — on-disk-capen är ×4, architect-rapportens 800 var stale). | `64e871a` |
| m2 | saved-searches.ts zod-drift | Pre-existing, latent — lyft till Klas-triage i morgonrapporten (egen touch innan saved-search-FE-ytan byggs). | — |
| m3 | Stale kommentarer | Fixade. | `64e871a` |
