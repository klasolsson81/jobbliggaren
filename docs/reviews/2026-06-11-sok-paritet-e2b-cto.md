# senior-cto-advisor — E2b Ort-picker-semantik (natt-dom 2026-06-11)

**Datum:** 2026-06-11
**Roll:** decision-maker (CLAUDE.md §9.6). Underlag: `docs/reviews/2026-06-11-sok-paritet-e2b-architect.md` (variant-analys A/B/B′/C/D), ADR 0067, ADR 0042 Beslut B, `JobAdSearchQuery.ApplyCriteria` on-disk.
**Oberoende verifiering:** JobTech-geografi-semantiken raw-läst 2026-06-11 (samma källa + Haparanda/Norrbotten-exemplet ordagrant). ADR 0042 Beslut B textuellt verifierad on-disk: beslutssubstansen är list-form + fyra invarianter — **ordalydelsen "AND-mellan-dimensioner" finns inte i ADR 0042:s beslutstext**; den lever i ADR 0067 Beslut 5:s kombinationssemantik. Avgörande för amendment-triagen.

---

**Klas-STOPP-klassning: INGEN HALT.** Domen ryms inom redan-Accepted ADR 0067-mandat (motivering under VAL 1). CC bygger inatt. Ram-utvidgningen dokumenteras explicit i STOPP-rapport + PR-body för Klas post-merge-granskning (ADR 0065 automerge-policy).

## Beslut — VAL 1

**Variant D (backend geo-OR) som korrekthets-bärare + Variant A:s per-län-städning som UX-normalisering.** Backend-grenen döms som **(i) mekanik-konkretisering inom ADR 0067:s redan-Accepted TD-100-paritets-mandat** → ADR 0067-implementerings-notat räcker (plus additiv klargörande-rad i ADR 0042:s amendment-spår, samma klass som cap-amendmentet 2026-06-09). Ingen ADR 0042-amendment som kräver Klas. Ingen HALT.

### Motivering mot principer

**1. Amendment-triagen — varför (i), inte (ii):**

- **ADR 0042 Beslut B beslutade aldrig region×kommun-semantik.** On-disk-verifierat: Beslut B:s substans är single→multi + fyra invarianter. Kommun-dimensionen *existerade inte* i ADR 0042 (den tillkom via ADR 0067/ADR 0043-amendment 2026-06-08). En ADR kan inte ha låst semantiken för ett dimensionspar den inte kände till. Dagens backend-AND mellan `Region` och `Municipality` är en **emergent egenskap av sekventiell `.Where()`-komposition** i `ApplyCriteria` (rad 177–187), inte ett Klas-beslutat kontrakt.
- **"AND-mellan-dimensioner / OR-inom-dimension" lever i ADR 0067 Beslut 5** — och under den dimensionella läsning som architect korrekt etablerat (län ⊃ kommun = EN dimension i två granulariteter, inte ortogonala axlar) **konformerar D mot invarianten i stället för att bryta den**: union region∪kommun ÄR "OR-inom-dimensionen Ort". Det är dagens kod som bryter invariantens anda genom att AND:a två granulariteter av samma dimension. Evans 2003 kap. 2: ubiquitous language — "Ort" är begreppet användaren och Platsbanken delar; län/kommun är granulariteter av det.
- **ADR 0067:s Accepted-mandat ÄR semantiken.** Kontext-raden: "matcha Platsbankens sök/filter/sortering till 100%" + TD-100-acceptance. JobTech-semantiken är web-verifierad (architect 2026-06-11 + CTO oberoende, samma källa): inkluderande union, "most local promoted". D *implementerar* det Accepted-beslutet; A/B/B′ *avviker* från det. Att kräva Klas-amendment för att göra det Klas redan beslutat vore process-inversion.
- **Prejudikat:** D2-CTO VAL 3 (mekanik-konkretisering → implementerings-notat, ej amendment, ingen Klas-STOPP) och ADR 0042-amendmentet 2026-06-09 (cap 10→400, levererat additivt inom Accepted ADR 0067-fas utan separat Klas-STOPP). Memory-regeln `feedback_adr_mechanism_vs_env_phase_triage` kräver CTO-triage — det är exakt denna dom.
- **FE-only-ramen är fas-planering, inte arkitekturbeslut.** Beslut 7 rad 104 är en leverans-tabell. E1b-entanglement-prejudikatet krävde Klas-GO för fas-OMFLYTT; här är det ~10 rader inom samma fas i tjänst av fasens eget acceptance-kriterium. Klas natt-prompt delegerar dessutom explicit denna (i)/(ii)-dom till CTO med "CC bygger inatt"-utfall.

**2. Variantvalet i sak:**

- **Korrekthet (Martin 2017 kap. 22 — policy hör hemma i rätt lager):** B′ är "ACL upp-och-ner" precis som architect skriver — presentation-lagret kompenserar för en query-semantik som är fel mot domänens sanning. Sök-semantik är query-lagrets ansvar; D lägger korrektheten där den hör hemma. Evans 2003 kap. 14: ACL skyddar domänen mot externa modeller — inte tvärtom.
- **Recall (TD-100 + `project_platsbanken_parity_baseline`):** D är ensam recall-komplett. B/B′:s region-only-lucka är noll idag men ogaranterad (STORED-kolumnerna är oberoende jsonb-extraktioner; nästa snapshot-cron kan introducera region-only-annonser tyst). En semantik vars korrekthet vilar på en oövervakad korpus-egenskap är latent falsk-klar — exakt den felklass Klas-prompten förbjuder. För en jobbsökare är en tyst missad annons den värsta felklassen (CLAUDE.md §1).
- **Robusthet (Nygard, *Release It!* 2nd ed — kapacitets-antimönster):** B:s URL-materialisering bär en 414-corner vid Kestrels 8 KB request-line-gräns ("Välj alla län" ≈ 7,3 KB). En design vars korrekthet beror på att användaren inte väljer för mycket är skör by construction. D eliminerar hela klassen — param-kontraktet orört.
- **Enkelhet (KISS):** D är ~10 rader i den befintliga SPOT:en + tester. B′ är den mest komplexa FE-state-maskinen av alla varianter — för att uppnå något D ger gratis.
- **Per-län-städningen (A-delen):** ren UX-normalisering, ingen korrekthets-bärare. Under union är "hela län X" + "kommun i län X" redundant state (kommunen tillför inget) — FE normaliserar: kommun-val i ett län där "Hela länet" är valt ersätter region-valet för det länet; "Hela länet"-val rensar länets kommun-val. Ingen uttryckskraft förloras (unionen gör staten ekvivalent). Cross-län-mix förblir fritt giltig (D bär den). Redundant state som ändå anländer via URL-handredigering eller gamla recent-searches är **fortsatt korrekt** backend-side — normaliseringen är kosmetik, inte invariant.

### Avvisade alternativ

- **Variant A ensam:** cross-län-hålet (`region=Stockholm + municipality=Mölndal` → AND-∅) är ett dokumenterat korrekthetshål mot en vardaglig kombination. Att leverera den med "dokumenterad avvikelse" vore att medvetet skeppa en känd lögn om korpusen — civic-utility-brott (CLAUDE.md §1).
- **Variant B:** recall-lucka (latent falsk-klar) + 414-cornern + region-param som död FE-yta. Tre svagheter för att undvika 10 rader backend.
- **Variant B′:** korrekt men arkitektoniskt upp-och-ner (presentation skyddar query-lagret), högst state-komplexitet, ärver B:s recall-lucka i mixed mode. Att välja B′ vore att betala det dyraste priset för att bevara en fas-tabellrad — Mastercard-testet: en utomstående senior arkitekt imponeras av 10 rader i rätt lager, inte av en FE-invariant-maskin som kompenserar fel semantik.
- **Variant C:** avvisad, architects motivering fastställs (geometriskt omöjliga snitt som giltigt UI-state + felläsning av dimensions-ortogonalitet).

### Trade-offs accepterade

- E2b:s deklarerade FE-only-ram utvidgas med ~10 backend-rader + Testcontainers-tester. Accepteras eftersom alternativet är att skeppa fel semantik eller mer komplexitet — och utvidgningen dokumenteras explicit för Klas post-merge.
- "Most local promoted"-rankningen replikeras INTE (vi har explicita sort-ordningar, Relevance är q-driven ts_rank per ADR 0062). Paritet doms på resultat-MÄNGD, inte på JobTechs interna promotion-ranking. Noteras i implementerings-notatet som medveten avgränsning.

## Beslut — VAL 2: "Obestämd ort/Utomlands"

**Architects defer-med-payload-trigger-dom FASTSTÄLLS.** Klas-villkoret "om payload stödjer" är inte uppfyllt (snapshot saknar noderna; `IS NULL`-filterklass = ny backend-yta; de 1 293 är globalt ortlösa → per-län-rad vore död UI-yta = ADR 0042 Beslut F-brott; `country_concept_id` overifierad). Trigger-instrumentet (ej TD) är rätt per ADR 0043 Beslut E / ADR 0067 Beslut 3-precedens — overifierad extern data-dependency. Trigger: riktad raw_payload-discovery (country/ortlösa) + observation av region-only-annonser i snapshot-cron.

**Skärpning (falsk-klar-disciplin):** detta är en kvarstående avvikelse mot Beslut 7 rad 109:s uppräkning. E2b:s STOPP-rapport och implementerings-notatet ska explicit lista den som triggad rest — TD-100-stängning vid Fas E-slut får inte hävda 100% utan att denna rest är antingen löst eller Klas-accepterad. Recall-not: de 1 293 nås via sök utan ort-filter under tiden.

## Beslut — VAL 3: Popover-kontraktet

**Dual-axis-props — generaliserat axel-medvetet kontrakt, INTE en tredje mode.** `jobb-filter-popover.tsx` utökas till `selectedRegions`/`selectedMunicipalities` + axel-medveten `onChange`; "Hela länet"-raden skriver region-axeln, kommun-rader municipality-axeln. Yrke-instansen blir det degenererade fallet (icke-valbar vänsterkolumn, en aktiv axel) — parameterisering med data, inte med mode-flagga. En tredje mode är ett Flag Argument-smell (Fowler 2018; Martin 2008 om boolean/mode-parametrar) som förgrenar render-logiken internt och bjuder in Shotgun Surgery vid nästa picker. Key-remount-mönstret för `activeLeft`-reset (architect 4.3) ingår. Exakt prop-form inom detta kontrakt är nextjs-ui-engineers latitud.

## Beslut — VAL 4: E2c-facett-konsekvens (låser E2c-spec)

**Municipality-facetten exkluderar HELA ort-dimensionen (både region- och municipality-listan) ur WHERE.** Detsamma gäller en framtida region-facett. Motivering: facett-semantiken i ADR 0067 Beslut 4 är "dimension X:s counts reflekterar alla andra aktiva filter men inte X självt" — och premissen som legitimerar D är att ort ÄR en dimension. Att exkludera bara municipality-listan vore att behandla region som främmande dimension i facetten men samma dimension i WHERE — semantisk inkonsekvens (samma begrepp, två sanningar = ubiquitous-language-brott, Evans 2003 kap. 2). Det matchar också Platsbanken-referensen (kommun-counts per län givet övriga, icke-geografiska, filter) och gör countsen användbara: "Solna (12)" svarar på "vad får jag om jag väljer Solna, givet mina yrkes-/q-filter" — under union-semantik är det enda väldefinierade per-option-talet. `FacetDimension`-mappningen i `FacetCountsAsync` ska alltså exkludera båda listorna för ort-facetten. Detta är E2c:s spec inatt.

## In-block-direktiv till CC (samma PR, commit-struktur)

1. **Commit 1 — backend D-gren:** kombinerad OR-gren i `ApplyCriteria` (`src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs:171-187`) när BÅDA listorna icke-tomma; annars befintliga grenar orörda. SPOT bevarad — alla tre konsumenter ärver. **Testcontainers-tester obligatoriska** (STORED-kolumner + shadow-props; InMemory bevisar inget — ADR 0067 Beslut 2-precedens), inkl: cross-län-fallet (region X + kommun-i-Y → union), intra-län-fallet, och en **syntetisk region-only-annons** (kommun NULL) som bevisar recall — korpusen har noll sådana idag, så testet måste fabricera raden.
2. **Commit 2 — FE atomisk batch** (architect fråga 4, alla sex punkter): zod-DTO (REQUIRED, ej `.default([])`; fortsätt strippa `occupations`), `buildJobbHref` + `buildPageHref`/`rawParams` (paginering tappar annars kommun-filtret — F3 B-FIX-felklassen), `selectedConceptIds` (chip-labels), popover dual-axis per VAL 3, Suspense-`municipalityKey` i `page.tsx`, hidden inputs/GET-form, per-län-normaliserings-logiken med egna unit-tester (E2b:s enda riktiga FE-logik), recent-search-fixtures. Chips-ordning region → municipality → occupationGroup; samma `MapPin` (architect fråga 5 fastställs). Cap-aritmetik-kommentaren (711 < 800) skrivs nu.
3. **Commit 3 — `RecentJobSearchDto.SsykList/SsykLabels`-shim-borttagning** (`src/JobbPilot.Application/RecentJobSearches/Queries/RecentJobSearchDto.cs:25-27`): §9.6 in-block — samma fas (ADR 0067-notatet F5 säger "tas bort i Fas E"), dependency:n (FE-zod-frikoppling) levererades i E2a, och D öppnar redan backend-ytan. Egen commit (oberoende change-reason — Fowler 2018, atomicitet). Om dold konsument upptäcks: lyft ur, egen touch.
4. **ADR 0067-implementerings-notat** (E2b): geo-OR-mekaniken + dimensions-läsningen + promotion-avgränsningen + Obestämd-ort-triggern + VAL 4-facett-låsningen. Additiv klargörande-rad i ADR 0042:s amendment-spår (korsref, ej substans-ändring).
5. **Gater:** code-reviewer + dotnet-architect på backend-grenen (>arkitekturellt val), security-auditor ej obligatorisk (ingen PII/auth/secrets-yta; param-kontrakt orört). Automerge-label per policy om 0 oåtgärdade Blocker/Major.

## Genuina TDs (lyfts)

Inga. Obestämd ort/Utomlands är trigger-instrument (ej TD, per ADR 0043 Beslut E-precedens); allt annat är in-block.

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) kap. 22 (lager-ansvar — VAL 1 B′-avvisning); *Clean Code* (2008) (mode-parametrar — VAL 3)
- Eric Evans, *DDD* (2003) kap. 2 (ubiquitous language — VAL 1/VAL 4), kap. 14 (ACL-riktning — B′-avvisning)
- Martin Fowler, *Refactoring* 2nd ed (2018) — Flag Argument, Shotgun Surgery (VAL 3); atomicitet (commit-struktur)
- Michael Nygard, *Release It!* 2nd ed (2018) — kapacitets-corner som designfel (B:s 414-yta)
- Hunt/Thomas (1999) — SPOT (ApplyCriteria-grenen, facett-återanvändning)
- JobTech *GettingStartedJobSearchEN.md* (gitlab.com/arbetsformedlingen/job-ads/jobsearch-apis, raw-läst av CTO 2026-06-11): "If I make a query which includes 2 different geographical filters the most local one will be promoted" + Haparanda/Norrbotten-exemplet — inkluderande union bekräftad oberoende av architects läsning
- ADR 0067 (Beslut 4/5/7 + notat C2/D2/E2a), ADR 0042 Beslut B + amendment 2026-06-09 (prejudikat), ADR 0043 Beslut E, ADR 0062 (SPOT), CLAUDE.md §1/§2.5/§7/§9.5/§9.6
- `docs/reviews/2026-06-11-sok-paritet-e2b-architect.md` (variant-analys, fråga 2–5-underlag)
