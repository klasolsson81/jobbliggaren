# code-reviewer — Fas E2i spegel-sökfält (PR-förgranskning, working tree)

**Status:** ✓ Approved (re-review 2026-06-12 — se sektionen sist i filen; ursprunglig dom: ⚠ Changes requested, 3 Major / 0 Blocker / 4 Minor)
**Granskat:** 2026-06-12 00:20
**Diff:** working tree mot main `5f4e1cc` (branch `feat/sok-paritet-spegel-sokfalt-e2i`, ej committat)
**Auktoritet:** CLAUDE.md §2.4 (testbart först), §4 (TS-konventioner), §5.2 (FE-anti-patterns), §9.6 (multi-approach → CTO)
**Underlag:** `docs/reviews/2026-06-11-sok-paritet-e2i-architect.md` + `-e2i-cto.md` (CTO VAL 1=C′, VAL 2 greedy, VAL 3 caret-exkludering, VAL 4a–d)
**Scope:** FE-only — `tokenize.ts` (rewrite), `jobb-hero-search.tsx` (rewrite), `job-ad-typeahead.tsx`, `jobb-results-toolbar.tsx`, `chip-composition.ts`, `globals.css`; `chip-search-field.tsx` raderad
**Gates (omkörda av code-reviewer):** `tsc --noEmit` rent · vitest 75/75 i de fem berörda sviterna

---

## Major (blockerar merge)

### Major 1 — Egen-roundtrip-detektorn känner bara igen SENASTE committade staten → mellanliggande egen RSC-roundtrip mis-klassas som extern divergens och skriver om texten under pågående skrivning

Fil: `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:139–156`

`lastCommitted` ersätter useOptimistic (flaggad avvikelse från architect-skissen) och dubbelagerar som own-roundtrip-detektor via `sameUrlState(base, lastCommitted)`. Detektorn matchar bara EN state — den senaste. Race-fönstret:

1. Commit 1 ("göteborg ") → `router.replace`, `lastCommitted = S1`
2. Användaren fortsätter skriva; commit 2 ("volvo ") hinner före S1:s props → `lastCommitted = S2`
3. S1:s props landar: `base ≠ prevBase`, `!sameUrlState(S1, S2)` → behandlas som EXTERN → `updateTextForStateChange(text, prevBase, S1)` har additions → **full kanonisk `serializeSearchText(S1)`** → fältets text ersätts med "Göteborg" — pågående ord ("heltid…") raderas och casing/ordning hoppar under caret.

Detta är exakt E2d/E2h-felklassen ("texten han skrev ska stå kvar") som ren Variant A avvisades för, fast i ett race-fönster. Fönstret är realistiskt: commits sker per ord-boundary; vid RTT > tiden mellan två ordgränser (långsam uppkoppling, snabb skribent) är överlappet garanterat. Mitigering finns ENDAST om Next App Router bevisligen alltid kancellerar föregående in-flight-navigation så att mellanliggande props aldrig flushas — det är odokumenterat antagande, inte verifierad invariant, och inget i koden eller testerna adresserar det (hero-testet "Egen RSC-roundtrip landar" rerender:ar bara med exakt matchande props).

**Krävs:**
- Own-roundtrip-detektorn måste känna igen en KEDJA av egna in-flight-commits (t.ex. bounded kö av egna committade states; base som matchar någon i kön = own, trimma äldre), ELLER dokumenterat+testat bevis att mellanliggande navigations-props inte kan nå komponenten.
- **CTO-ack på avvikelsen:** `lastCommitted`-ersättningen av architect-skissens useOptimistic är ett multi-approach-val (CLAUDE.md §9.6 punkt 3 + Klas-direktiv "CC rekommenderar inte vid multi-approach") som implementerats utan senior-cto-advisor-dom. Själva resonemanget (overlay reverterar till stale base mellan transitions) är tekniskt rimligt — men valet och race-hanteringen ska till CTO.

Delegera till: senior-cto-advisor (beslut) → implementation.

### Major 2 — `onSelectSuggestion` sätter `prevClaims = parse(nextText)` i stället för faktiskt applicerade anspråk → persistent I1-brott vid Title-label med taxonomi-ord

Fil: `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:198–228` (särskilt rad 206–207, 224)

Kommentaren på rad 207 — "Title = fritext-ord; per-ord-representabilitet prövas nedan" — stämmer inte med koden: ingen per-ord-prövning sker. Title-labeln skrivs ALLTID in i texten och `prevClaims` sätts till en full `parseSearchText(nextText)`, oavsett vad som faktiskt applicerades.

Konkret felkedja (reachable — jobbannonstitlar innehåller ofta ortnamn, t.ex. "Säljare Göteborg"):

1. Title-val "Säljare Göteborg" → texten får orden; `composeSuggestionChip` lägger dem i **q** (fritext).
2. `parseSearchText(nextText)` claimar dock `Municipality:Göteborg` (unik label) → hamnar i `prevClaims.matches`.
3. I1 (`parse(text) ⊆ state`) är nu bruten: texten claimar en kommun-dimension som inte finns i state — och den självläker ALDRIG, eftersom adds-loopen i `applyClaimsDelta` skippar allt i `prevKeys` (tokenize.ts:243). Sökresultaten matchar inte det fältet visar.

Samma rot-orsak ger två följdfel: (a) Q_MAX-rejektioner vid Title-val markeras som applicerade (ordet står i texten, ingen retry, `setLimitNotice(false)` släcker notisen — kontrast mot `applyClaimsDelta`s genomtänkta rejected-ej-i-appliedClaims-semantik); (b) ett tidigare rejected q-ord i texten "amnesti-markeras" som applicerat av den fulla re-parsen.

**Krävs:** sätt `prevClaims` ur faktisk applicering (kör deltat genom `applyClaimsDelta`-vägen eller ekvivalent) i stället för optimistisk full-parse; gate:a Title-ordens text-insättning per ord med `isTextRepresentable(w, null, index)` så kommentaren blir sann; bevara limitNotice/rejected-semantiken. Saknad test: Title-label innehållande unik taxonomi-label.

Delegera till: implementation + test-writer (regressionstest).

### Major 3 — Ort-/yrkes-normaliseringens I1-hörn är INTE benignt i remove-riktningen (architect-domen täckte bara add-hörnet) → CTO-triage

Filer: `web/jobbpilot-web/src/lib/job-ads/tokenize.ts:241–251` (adds-loopens `prevKeys`-skip), `:308–322` (`removeMatch` OccupationField), `web/jobbpilot-web/src/lib/job-ads/chip-composition.ts:102–118` (per-län-normalisering)

Architect-rapporten dömde per-län-normaliseringen som "benign" — men analysen täckte add-ögonblicket. Verifieringen visar två icke-benigna kedjor:

- **(a) Tyst smalare sök än texten claimar (add-ordning):** texten "västra götalands län göteborg " → region adderas först, sedan släcker kommun-adden länets helläns-val (`applyMunicipalityChange`). Slutstate: enbart Göteborg — användaren skrev explicit hela länet men söket täcker en kommun. `appliedClaims.matches = next.matches` villkorslöst (tokenize.ts:281) → region-claimet bokförs som applicerat och re-adderas aldrig. (Omvänd ordning, "göteborg västra götalands län", ger superset-täckning — det fallet är benignt.)
- **(b) Permanent claim-förlust vid borttagning:** texten "Data/IT Systemutvecklare " → fältet materialiserar barnen; raderas sedan "Data/IT" ur texten släpper `removeMatch(OccupationField)` ALLA barn-grupper — inklusive `Systemutvecklare` som texten fortfarande claimar — och adds-loopen skippar den via `prevKeys`. Resultat: ordet står kvar i fältet, filtret är borta, för alltid. Samma mönster för region/kommun: text-claimad kommun som släckts av en region-add återuppstår inte när regionen raderas ur texten.

I bägge fallen visar filter-raden (total spegel) sanningen, vilket mildrar — men fältet visar ord som inte filtrerar, vilket är exakt den divergens C′-modellen byggdes för att utesluta. Per memory-regeln "agent-flaggad risk mot accepterad dom = CTO-triage, ej CC-omdöme" (TD-13 C1 J3-lärdomen): architectens benign-dom omfattade inte remove-kedjan → **senior-cto-advisor avgör** om detta fixas in-block (t.ex. adds-loopen re-adderar claims som saknas i state, med flip-flop-analys för ort-dimensionen; eller `removeMatch` undantar barn som `next.matches` claimar) eller accepteras som dokumenterad konsekvens.

Delegera till: senior-cto-advisor (triage) → implementation + test-writer.

---

## Minor (bör fixas, blockerar ej)

1. **Boundary tappas vid bar operator-token.** `tokenize.ts:144–145`: ett token som blir tomt efter operator-strip (`"+"`/`"-"` ensamt) `continue`:ar utan att bevara sin `boundary` — "stockholms, + län" bildar en run över kommat och matchar "Stockholms län", i strid med dokumenterade invarianten "n-gram spänner ALDRIG över komma" (rad 26). Fix: `pendingBoundary ||= boundary` före continue (gäller även caret-skippet, som redan sätter den — men ovillkorligt `= true`, vilket är korrekt där).
2. **`caret` nollställs inte vid extern resync.** `jobb-hero-search.tsx:141–156`: efter toolbar-×/Rensa pekar stale `caret` in i den NYA texten → `suggestQuery` kan bli ett godtyckligt ord → debounce-fetch + (eftersom typeaheaden aldrig stänger på blur) potentiellt öppnad förslagslista utan användar-input. Lägg `setCaret(null)` i extern-divergens-grenen.
3. **DRY + kommentar-glidning i Title-grenen.** `chip-composition.ts:50–57`: inline `q.split(/\s+/).filter(...)` duplicerar `splitQWords` (chip-models.ts:34); kommentaren "Överskrider appenden Q_MAX_LENGTH → no-op" beskriver per-ord-`break` som hel-no-op. Importera `splitQWords`, justera kommentaren.
4. **Rundtripps-testet är 5 fixa states, inte property-baserat.** `tokenize.test.ts:201–229`: CTO VAL 1 kräver "property-aktig" fitness function. Acceptabel start, men en genererande variant (kombinationer ur fixture-taxonomin) skulle vakta tokenizer-ändringar väsentligt bättre. FYI till test-writer vid nästa touch.

---

## Verifierat utan anmärkning

- **Rivningen komplett:** `ChipSearchField`/`onEmptyBackspace`/`inputRef`/`jp-chipfield`/`jp-filterchip--field` — noll kvarvarande referenser i `src/` (grep). CSS-ersättningen `.jp-hero__searchfield` är minimal och token-ren; dropdown-förankringen mot `.jp-hero__searchblock` består (static wrapper, dokumenterat).
- **parseSearchText i övrigt:** run-/boundary-logiken korrekt (pendingBoundary efter caret-skip bryter n-gram på båda sidor — testtäckt); greedy-degraderingen följer CTO VAL 2 exakt (unik vinner, ambiguös → kortare, längd 1 → fritext); operator-strip-positionen (efter caret-range-jämförelsen, på rå token) är konsistent med `getTokenRange`-konsumenterna.
- **applyClaimsDelta-grundflödet:** removes-före-adds frigör q-utrymme korrekt; rejected-q-semantiken (ej i appliedClaims → retry nästa commit-punkt) är genomtänkt och testtäckt; popover-valda dimensioner rörs inte (I1-testet).
- **serializeSearchText:** verify-funktionens `keys.size === items.length` är kollisionssäker (`q:`-prefix vs `Kind:`-namnrymd disjunkta; ci-dubblett-q-ord konvergerar deterministiskt via drop-loopen); drop-loopen är deterministisk (pop från slutet, q-ord först).
- **Toolbar:** `includeQ:true` + Search-ikon + "Ta bort sökordet X"-aria; Rensa-alla nollar q per Klas-beslut, E2e-testet uppdaterat med explicit ersätter-dom-kommentar; sortBy/pageSize bevaras; Relevance-gaten härleds fortsatt ur q-propen.
- **No-JS-kontraktet:** synlig input namnlös post-hydration, committad residual-q som hidden input, dimensions-hidden-inputs renderas även pre-hydration (filter bevaras vid native submit) — testtäckt.
- **Konventioner:** strict TS utan `any`/casts, inga console.log, ingen placeholder (Klas hård regel — hjälptexten bär instruktionen, testad), aria-live-annonser bevarade, kommentarer förklarar varje "use client"-mönster. `useEffect`-fetchen i typeaheaden är dokumenterat CTO-undantag (2026-05-16), oförändrad.
- **Title ERSÄTT→APPEND (CTO VAL 4b):** korrekt ci-dedupe + Q_MAX-guard, testtäckt, dokumenterad som kontraktsändring.

## Bra gjort

- C′-modellen är genomförd som CTO:n krävde: parse/delta/serialize/gate som separata rena, DOM-fria funktioner med var sin change-reason — testbara utan komponent (CLAUDE.md §2.4).
- `isTextRepresentable` som ETT predikat som styr både serialize och förslags-insättning är ren SPOT.
- Rejected-q-design i `applyClaimsDelta` (vägrade ord ej i appliedClaims → automatisk retry när utrymme frigörs) är en elegant detalj utöver spec.
- Test-sviten dokumenterar besluten i testnamnen (spårbart mot CTO-domen) och dödar E2d/E2h-felklassen explicit.

## Sammanfattning och delegationer

0 Blockers, **3 Major**, 4 Minor. Merge blockeras tills Major är adresserade.

| Fynd | Väg |
|---|---|
| Major 1 (roundtrip-race + avvikelse-ack) | senior-cto-advisor → implementation |
| Major 2 (prevClaims-optimism vid förslags-val) | implementation + test-writer |
| Major 3 (normaliserings-hörnet remove-riktning) | senior-cto-advisor-triage → implementation + test-writer |
| Minor 1–3 | trivial in-block |
| Minor 4 | test-writer, nästa touch |

Re-review efter Major-åtgärder. Design-reviewer-granskning av q-taggens copy/ikon (CTO VAL 4d-delegationen) är fortfarande utestående och ska ske i samma batch innan PR.

---

## Re-review 2026-06-12

**Status:** ✓ Approved
**Granskat:** 2026-06-12 (working tree mot main `5f4e1cc`, branch `feat/sok-paritet-spegel-sokfalt-e2i`)
**Underlag:** CTO-addendum 2026-06-12 (BESLUT 1–3) i `2026-06-11-sok-paritet-e2i-cto.md` + design-review-rapporten (`-e2i-design-review.md`, nu föreliggande — VAL 4d-utestående stängd)
**Gates (omkörda av code-reviewer):** `tsc --noEmit` rent · `eslint` 0 errors (5 warnings, samtliga pre-existerande i filer UTANFÖR E2i-diffen: `audit-log-table.test.tsx`, `delete-account-dialog.tsx`, `recent-searches.test.ts`, `saved-job-ads.test.ts`) · vitest **830/830** (80 filer) · production build grön

### Major 1 — ÅTGÄRDAD (CTO BESLUT 1 implementerad korrekt)

`jobb-hero-search.tsx:139–181`. Verifierat mot samtliga fyra implementations-direktiv:

1. ✓ `commit()` appendar till `recentCommits` med cap 10 (`[...prev, next].slice(-10)`, rad 176).
2. ✓ Sentinelen klassar base som EGEN vid `sameUrlState`-träff mot NÅGON post (`findIndex`, rad 148), prune t.o.m. träffen (`slice(hitIndex + 1)`), texten orörd; ingen träff = EXTERN → full resync + lista nollas + `lastCommitted = base`.
3. ✓ Icke-regress: `setLastCommitted(base)` körs ENBART i extern-grenen — vid egen träff rörs `lastCommitted` aldrig. Direktivets fall "listan tom efter prune → lastCommitted = base" är implementerat ekvivalent utan tilldelning: `commit()` sätter alltid `lastCommitted` och listans sista post till SAMMA värde, så träff på sista posten innebär att base redan är innehålls-lik `lastCommitted`. Ingen stale-bas möjlig.
4. ✓ Race-fitness-testet finns och passerar: "mellanliggande egen props-leverans serialiserar INTE om texten (två commits i flykt)" (`jobb-hero-search.test.tsx:186–221`) — S1-props efter S2-commit, texten orörd genom båda leveranserna.

### Major 2 — ÅTGÄRDAD (via delta-vägen per BESLUT 2-synergin)

`jobb-hero-search.tsx:211–254`. Insert-gaten är på plats: Title-label skrivs in ENDAST om `parseSearchText(label).matches.length === 0`; dimensions-label endast om `isTextRepresentable`. State går via `applyClaimsDelta` + garanterad `composeSuggestionChip` av själva valet + slutlig `enforceClaims` (compose-normaliseringen kan inte släcka text-claimade dimensioner). `prevClaims = delta.appliedClaims` — inte rå parse. Följdfelen läkta: `limitNotice` härleds ur `delta.rejectedQ` och vägrade ord ingår inte i `appliedClaims` (retry-semantiken bevarad). Regressionstestet "Title-label MED taxonomi-ord skrivs INTE in i texten" (`jobb-hero-search.test.tsx:303–326`) verifierar att q får orden, ingen municipality-param skapas och texten inte claimar labeln.

### Major 3 — ÅTGÄRDAD (CTO BESLUT 2 implementerad korrekt)

`tokenize.ts:280–340`. `enforceClaims` är exporterad och körs som SISTA pass i `applyClaimsDelta` (rad 285): varje next-claim re-assertas RÅTT per axel (ingen normalisering — flip-flop utesluten), `OccupationField` ⇒ samtliga barn-grupper. Enforcement-re-adds annonseras inte (`addedLabels` orörda — direktiv 2). Bokförings-lögnen läkt: `appliedClaims.matches = next.matches` är nu sann per konstruktion. Båda regressionskedjorna testtäckta och passerar: (a) "västra götalands län göteborg" → region + kommun BÅDA i staten (`tokenize.test.ts:164–175`); (b) field-removal släpper inte text-claimat barn (`tokenize.test.ts:177–185`). Rundtripps-testet passerar fortsatt.

### Minor 1–4 — ÅTGÄRDADE / ACK

1. ✓ `tokenize.ts:144–151`: `pendingBoundary = boundary` vid bar operator-token, med förklarande kommentar. *FYI:* ingen dedikerad test för "stockholms, + län"-fallet — acceptabelt för Minor-fix, tas vid nästa tokenize-touch.
2. ✓ `jobb-hero-search.tsx:166–167`: `setCaret(null)` + `setAnnouncement("")` i extern-grenen (täcker även design-Mi2 re-annonserings-hålet).
3. ✓ `chip-composition.ts:50–57`: `splitQWords` importerad och använd; kommentaren beskriver nu korrekt per-ord-break.
4. ✓ 5-state-rundtrippen består som dokumenterad acceptabel start — FYI till test-writer vid nästa touch kvarstår (ingen gate, per CTO-addendum).

### Design-fixar i batchen (FYI-verifiering, design-reviewers fynd)

Verifierade on-disk: ×-knappens hit-area fyller chip-höjden (`globals.css` `.jp-filterchip__rm`: `align-self: stretch; padding: 0 8px; margin-right: -10px`); "Rensa sökord och filter" + `role="group"` + `aria-label="Aktiva sökord och filter"` (`jobb-results-toolbar.tsx:171–215`); hjälptexten bär platsangivelsen ("Ord blir taggar i filterraden vid träffarna…"); announcement-reset (Minor 2 ovan). Design-Mi4 (fokus vid chip-borttagning) + rendered-flaggor ligger per FAS-DEFERRAL-MANIFEST hos Klas rendered-pass — utanför denna gate.

### NYTT FYND (re-review) — Minor: sortBy/pageSize-staleness vid egen-träff-klassning

Fil: `jobb-hero-search.tsx:147–172` + `tokenize.ts:535–542`.

`sameUrlState` jämför inte `sortBy`/`pageSize` (medvetet — de är inte sök-state). Konsekvens av non-regress-regeln: en EXTERN sort-/pageSize-ändring vars filter-state råkar matcha en in-flight-post (realistisk väg: toolbar-push kancellerar eget replace-echo → sort-leveransen möter icke-tom `recentCommits` med identiska filter) klassas som EGEN → `lastCommitted` behåller stale `sortBy`/`pageSize` → nästa text-commit `router.replace`:ar tillbaka det gamla värdet och reverterar tyst användarens sort-val. Före re-review-fixen self-läkte detta via det ovillkorliga `setLastCommitted(base)` — hålet är alltså en (oanalyserad) bieffekt av BESLUT 1.3, inte en miss mot direktivet.

Fönstret är smalt, harm är preferens-revert (inte text-förlust) och nästa externa leverans läker — **Minor, blockerar ej**. Föreslagen fix (bryter INTE BESLUT 1.3, vars non-regress avser de jämförda axlarna): vid EGEN träff, adoptera `base.sortBy`/`base.pageSize` in i `lastCommitted` (för äkta egna echos är värdet identiskt — no-op). En rad; rekommenderas in-block före PR, annars vid nästa hero-touch.

*FYI utanför scope:* den bredare cross-island-racen (toolbar-push byggd på stale props kan tappa en in-flight fält-commit helt) är arkitektur-inherent i URL-buss-modellen och pre-existerande — ej introducerad av E2i, ej E2i:s gate.

### Slutstatus

**✓ Approved.** 0 Blocker, 0 Major, 1 nytt Minor (+2 FYI). Alla tre Major åtgärdade exakt per CTO-direktiven med regressionstester; alla fyra Minor stängda; design-fixarna på plats; gates gröna (tsc · eslint 0 errors · vitest 830/830 · build). Mergeklar — det nya Minor-fyndet är en enrads-fix som med fördel tas in-block före PR men inte blockerar per severity-tabellen.
