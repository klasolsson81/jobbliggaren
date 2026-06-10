# senior-cto-advisor — Platsbanken sök-paritet Fas D1 (5 multi-approach-val)

**Datum:** 2026-06-10
**Agent:** senior-cto-advisor (agentId `a3e62a54498790c30`)
**Roll:** decision-maker (ej advisor). CC ger ingen egen rekommendation.
**HEAD vid analys:** `06b7840`

---

## Domar

| Val | Dom | Kärnmotivering | Källor |
|-----|-----|----------------|--------|
| **1 (A2)** FacetDimension-scope | **Variant A** — `{OccupationGroup, Municipality, Region}` | B2-dims utesluts tills re-ingest; enum-medlem utan data/GROUP BY-gren = kontrakt som ljuger (falsk-klar). Tillägg vid B2 är non-breaking enum-append. | Beck/Fowler YAGNI; Martin OCP (kap. 8); §9.6 + C1-CTO-dom c |
| **2 (A6)** Endpoint nu? | **Variant A** — bara port-metod + impl + Testcontainers-test | ADR 0067 Beslut 4 säger "metoden"; FE-konsumtion = Fas E. `CountAsync`-prejudikat (port-först). Facett-DTO-form designas rätt med FE-vyn framför sig (§9.3 gissa aldrig). | ADR 0067 Beslut 4; Martin SRP/SoC; §9.2/§9.3 |
| **3 (B1)** Kind-modell | **Variant B** — `public enum SuggestionKind` i Application.JobAds.Abstractions | §5.1 anti-magic-string explicit; `TaxonomyConceptKind` internal får ej korsa gränsen (DIP/ACL). `SuggestionKind` ≠ taxonomi-enum (egen `Title`-medlem). Bygg med exakt emitterade medlemmar (ingen Occupation). | Martin kap. 10–11; Evans ACL; ADR 0043; §5.1 |
| **4 (B2)** Occupation i suggest? | **Variant A** — uteslut | `JobAdFilterCriteria` har ingen Occupation-dimension → chip utan filter-mål = återvändsgränd. Recall bevaras ändå via q-FTS-synonym-grenen (`SsykConceptId`+`IOccupationSynonymExpander` orörda). | Falsk-klar (= VAL 1); YAGNI |
| **5 (B4)** Suggest-shim? | **Variant A** — inget shim (rent brott `string[]`→`SuggestionDto[]`) | Transient read-API utan persistens (≠ C2:s RecentJobSearchDto-shim som skyddade data-at-rest). Shim = dead code som rivs i Fas E. SRP: dubbelkontrakt = två change-reasons. | Martin Clean Code kap. 17 (Dead Code); Fowler YAGNI; §6.3 |

## Klas-STOPP-status

**Inget enskilt val kräver att CC pausar för Klas-GO** — samtliga entydiga mot principer + ADR-korpus (§9.6 p.5).

Enda Klas-interaktion (passiv): **VAL 5:s FE-kontraktsbrott SKA stå explicit i PR/STOPP-rapporten**
(medvetenhet, ej förhandsgodkännande). Wording: *"Suggest-kontraktet bryts `string[]`→`SuggestionDto[]`.
`web/.../job-ad-typeahead.tsx` blir inkompatibel tills Fas E migrerar den. Ingen runtime-overlap förväntas
eftersom FE inte deployas mot D1-backend före Fas E — bekräfta att ingen mellanliggande FE-deploy planeras."*
Om Klas signalerar planerad mellanliggande FE-deploy → shim (Variant B) omprövas.

## TD-bedömning

Ingen variant döljer en TD; de undviker aktivt TD-bloat. VAL 1/4/5 = samma princip ×3: bygg inte halv-wirade
kontrakt/chip/shims som blir uppskjuten skuld — additivt och verifierbart när rätt fas har data/konsument/mål.
Arbetet *finns inte än*, det är inte uppskjutet (≠ TD). VAL 2:s uteskjutna endpoint = ADR 0067:s egen fas-gräns,
ej glömd skuld. Att TD-posta någon vore precis tröskel-utlyftningen memory `feedback_td_lifting_discipline` varnar för.

## Reconcile-dom 2026-06-10 — NBomber-gate vs port-only (agentId `a4773166ed750ce7f`)

**Spänning:** VAL 2 (port-only, ingen endpoint i D1) gör Klas startprompts "NBomber BLOCKING i D1" omätbar — NBomber/NBomber.Http mäter HTTP-endpoints, ingen facet-endpoint finns.

**Dom: Väg B** — författa NBomber-scenariot som instrument, registrera det INTE i aktiv körning, bind gatens exekvering till Fas E.

**Motivering (ordagrann ADR-läsning):** ADR 0067 Beslut 4:s trigger-villkor är *"BLOCKING före per-option **går live**"* — inte "före D1-stängning". Port-only-VAL-2 lägger "live" i Fas E (ADR 0067 fas-tabell: Fas E = live-count). Gaten följer med "live" per sin egen ordalydelse. Dessutom: ADR 0045 Beslut 5/6 gör NBomber **observe-only, utanför `ci.needs`, ::warning::+exit 0** — "BLOCKING" i ADR 0067 är en **produkt-gate** (per-option får ej exponeras för användare innan p95 dokumenterad), ej CI-pipeline-gate. En produkt-gate mäts först när det finns en yta att exponera = Fas E.

**Avvisat:** Väg A (tunn mät-endpoint nu) — bryter VAL 2 på just den axel den skyddade (facett-DTO-form låses utan FE-vy: policy-val + route-kontrakt + Mediator-query-form hör till Fas E). Väg C (eskalera Klas) — ingen äkta principkonflikt när Beslut 4 läses ordagrant; "live" pekar redan på Fas E.

**Anti-falsk-klar (obligatoriskt):** D1 levererar instrument + dokumenterad defer-not (current-work + session-log + PR-body): ingen p95-dom i D1, ingen live-aktivering i D1, gate-exekvering = Fas E.

**Klas-STOPP:** Ingen beslutseskalering — CC kör Väg B direkt. En informations-flagga i PR-body: "Säg till om du vill ha tunn mät-endpoint nu (Väg A) istället — då låses facett-DTO-formen före FE-vyn."

## Referenser
Martin *Clean Architecture* (2017) kap. 7/8/10–11/13; *Clean Code* (2008) kap. 17; Evans *DDD* (2003) ACL;
Ford/Parsons/Kua (2017) kap. 2/4 (fitness function = instrument, dom faller i mätbart tillstånd);
Fowler "Yagni" (2015) + *Refactoring* 2nd; Beck XP; Dijkstra (1974) SoC. CLAUDE.md §2.1/§2.4/§5.1/§9.6;
ADR 0067 Beslut 1+4; ADR 0043; ADR 0042 Beslut C; ADR 0039 Beslut 1; ADR 0062.
Memory: `feedback_ef_strongly_typed_vo_contains_translation`, `feedback_td_lifting_discipline`, `feedback_automerge_all_own_prs`.
