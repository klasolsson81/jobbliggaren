# CTO-rond — STEG 6 multi-approach (search-recall "systemutvecklare")

**Datum:** 2026-05-24
**Agent:** senior-cto-advisor (agentId `a3b55188be4e119ca`)
**Triggrad av:** CC inför STOPP A. Multi-approach-val A/B/C för MVP-demo måndag 25 maj.
**Källa-bilagor:** [`2026-05-24-steg6-fas-a-discovery.md`](./2026-05-24-steg6-fas-a-discovery.md)

---

## CTO-rekommendation

### Beslut

**Approach C — Hybrid B nu + A som dokumenterad backlog** med tre icke-förhandlingsbara villkor (se nedan). GO-typ: **Klas-låst**, inte CTO-låst — söndag→måndag-tidspressen är produkt-driven och bryter mot Klas-direktiv 2026-05-24 "inga quickfixes", vilket är scope-fråga utanför CTO:s mandat per CLAUDE.md §9.6 punkt 5.

### Motivering mot principer

**Last Responsible Moment (Poppendieck 2003, *Lean Software Development*, kap. 3):**
Detta är *exakt* den situation LRM-principen adresserar — ett beslut (full sync-rot-orsak-fix) som kräver verifiering (Hypotes 1) som inte kan slutföras inom tids-fönstret utan att riskera demo-leveransen. LRM säger: defer beslutet tills information är tillgänglig, men leverera värdet nu om reversibelt. Approach C respekterar detta; Approach A bryter det genom att tvinga ett irreversibelt verifierings-spelmoment söndag kväll.

**ACL — Anti-Corruption Layer (Evans 2003, kap. 14 "Bounded Contexts" + Vernon 2013 kap. 13):**
`IOccupationSynonymExpander` är legitim Application-port, inte premature abstraktion. Den översätter mellan **användarens fritext-domän** ("systemutvecklare") och **JobTech taxonomy-bounded context** (SSYK concept_ids). Detta är exakt vad ACL är till för. Att placera mappningen i `appsettings.json` via `IOptions<SearchSynonymsOptions>` är korrekt — det är konfiguration, inte domänregel, och kan utökas utan kod-deploy.

**SRP (Martin 2017, kap. 7):**
Synonym-expansion har en change-reason (taxonomi-mappnings-uppdateringar) skild från `JobAdSearchQuery`-handlerns change-reason (query-komposition). Port + impl är inte bloat — det är SRP.

**Evolutionary architecture — reversibility (Ford/Parsons/Kua 2017, kap. 2 "Fitness Functions"):**
Approach B är **reversibel**: portar går att ta bort, mapping går att tömma, Q-grenen återgår till FTS+title-LIKE utan datatap. Approach A:s backfill-job är också reversibel, men dess **verifieringsfas** (5-stickprov mot JobTech) är inte time-boxable mot demo-deadline. Reversibilitet är inte bara om koden — det är om beslutsvägen.

**YAGNI-test (Beck 1999, *XP Explained*):**
Kunde Q-grenen lösa detta med ett rakt `criteria.Q == "systemutvecklare" ? OR ssyk IN (...)` inline-hack utan port? Tekniskt ja. Men CLAUDE.md §1 "Mastercard-test" + §5.1 "Magic strings — alltid konstanter eller enums" + det faktum att vi vet redan att fler söktermer kommer (ML-engineer, dataingenjör, AI-utvecklare) gör att porten betalar sig inom 1-2 sessioner. Detta är inte spekulativ generalisering — det är dokumenterat kommande behov.

### Avvisade alternativ

**Approach A (sync-fix + backfill):**

Avvisad **inte för att den är fel approach** — den är den **rätta** rotsymptom-fixen. Avvisad för att **risk-investment-asymmetrin** är oacceptabel söndag kl 18 inför måndag-demo:

- Hypotes 1 (JobTech-källan saknar occupation för 76%) är prompt-författarens egen "troligaste rotsymptom"-bedömning
- Om Hypotes 1 bekräftas → A ger ~0 effekt och vi står utan reservplan kl 22 söndag
- 4-6h tidsfönster är inte kompatibelt med discovery-cap + verifierings-test + ev. sync-trigger-fix + idempotent Hangfire-job + körning + post-verifiering
- "Inga quickfixes"-direktivet är giltigt som princip men kan inte överrida fysiska tids-fönster när alternativet är demo-missfall

Approach A **ska** göras — men i egen session efter MVP-demo, när Hypotes 1 kan verifieras utan tids-press. Detta är inte att skjuta TD framför sig (CLAUDE.md §9.6 anti-pattern); det är att fas-allokera korrekt: rot-orsaks-arbete tillhör post-MVP-fas där JobTech-API-discovery får tas i sin egen tid.

**Approach B (ren, utan A-backlog-dokumentation):**

Avvisad. Ren B utan dokumenterad A-väg framåt **är** tyst TD-ackumulering — exakt det Klas-direktivet 2026-05-24 reagerar mot. Skillnaden mellan B och C är inte koden (identisk leverans nu) utan **disciplin-trailen**: C tvingar fram TD-lyft med konkret framtida session-plan, vilket gör söndag→måndag-defer:n **medveten och tidsbunden**, inte glömd.

### Trade-offs accepterade

1. **76% NULL-SSYK kvarstår** efter B-leverans. Synonym-expansion ovanpå NULL-data ger 0 träffar för de raderna — vi vinner endast på de 24% som har SSYK satt. Estimat 600-750 hits är kompatibelt med "minst 600"-kravet men har inte säkerhetsmarginal.

2. **TD-94 perf-interaktion är oklar.** Ny `ANY(@arr)` ovanpå FTS+title-LIKE i samma OR-uttryck kan ge marginell effekt (samma `ix_job_ads_ssyk_concept_id` partial-index serverar) eller signifikant (bitmap-OR mellan FTS-GIN + partial-btree kan trigga extra heap-scan). **Detta är en disciplin-miss om vi ignorerar §2.5.** Se villkor 3 nedan.

3. **Mappning inte uttömmande.** "systemutvecklare" → 9 ids täcker exakt match + närliggande utvecklarroller, men ML-engineer/dataingenjör/AI-utvecklare/embedded-utvecklare kräver egna mapping-poster i framtida sessioner. Acceptabelt eftersom `IOptions<SearchSynonymsOptions>` är konfiguration utan deploy.

4. **ADR-amendment behövs** för ADR 0062. Q-grenen får ny semantik-dimension (synonym-expansion). Detta är inte tyst arkitektur-glidning om vi dokumenterar — det är medveten utvidgning av FTS-hybriden.

### Icke-förhandlingsbara villkor för GO

Klas-GO på Approach C kräver att alla tre uppfylls **i samma commit-batch som B-implementationen**, inte som efterföljande TDs:

1. **TD-lyft skrivet och placerat i `docs/tech-debt.md` Severity Major × Fas Nu:**
   Titel: "Sync-rot-orsak: 76% NULL `ssyk_concept_id` blockerar fritext→taxonomi-expansion bortom synonym-mapping". Källa: STEG 6 CTO-multiapproach-triage 2026-05-24. Beroende: JobTech jobsearch-API-discovery (5 stickprov-verifiering av Hypotes 1). Trigger: post-MVP-demo, egen session. Föreslagen åtgärd: verifiera Hypotes 1; om falsifierad, idempotent Hangfire-backfill-job mot NULL-rader; om bekräftad, dokumentera som permanent korpus-begränsning och justera söktrösklar.

2. **ADR-amendment 0062** (eller ny ADR 0063 om amendment-format inte räcker) som dokumenterar `IOccupationSynonymExpander` som Application-port med ACL-motivering (Evans 2003 kap. 14), placering i Q-grenen, och relation till ADR 0045 perf-budgetar. Verbatim-källa: webb-Claude per CLAUDE.md §9.4.

3. **EXPLAIN ANALYZE-output bifogad STOPP-rapporten** före push, mot dev-RDS för:
   - Befintlig query (FTS+title-LIKE Q-gren) med Q="systemutvecklare"
   - Ny query (FTS+title-LIKE+synonym-ANY) med samma Q
   - Delta p50 och worst-case rader-scannade

   Om delta visar >+50ms p50-regression mot redan brännande TD-94-budget: **STOPP, tillbaka till CTO för re-triage**. CLAUDE.md §2.5: "perf-regression utan motivering när signalen finns är en disciplinmiss, inte en okänd". `LoggingBehavior` ger latensen — kör mätningen, undanta inte.

### In-block-fixar (samma commit-batch som B-implementation)

- `IOccupationSynonymExpander`-port i Application
- Infrastructure-impl med `IOptions<SearchSynonymsOptions>` bind mot `appsettings.json`
- Mapping `"systemutvecklare"` → 9 concept_ids (lista verifierad mot taxonomy.api.jobtechdev.se 2026-05-24); spel-programmerare-varianterna **exkluderas** i initial mapping (out-of-scope för "systemutvecklare"-semantik)
- `JobAdSearchQuery.ApplyCriteria` Q-gren-utvidgning med OR-clause mot expanderad `IReadOnlyList<string>`
- Unit-tester för expander (deterministisk; ingen DB)
- Integration-test mot Testcontainers som verifierar OR-grenen träffar SSYK-rader (även mot legacy-rader med satt SSYK)
- DI-registrering i samma commit som handlers (memory: `feedback_di_with_handlers_same_commit`)
- Verifierings-evidens i STOPP-rapport: `git diff` + EXPLAIN ANALYZE-bilagor

### Genuina TDs (lyfts som villkor 1)

- **TD-X** "Sync-rot-orsak: 76% NULL ssyk_concept_id" — kvalificerar §9.6 punkt 1 (annan fas: post-MVP-discovery-arbete kräver JobTech-API-stickprovsverifiering som inte är tids-kompatibelt med demo-deadline). Kvalificerar **inte** som §9.6 punkt 2 (ingen saknad funktion-dependency). Lyftning är legitim eftersom verifieringen själv är fas-avgränsad, inte arbetet.

### Klas-STOPP-flagga

**Denna rekommendation kräver Klas-GO innan implementation startar.** CTO kan inte själv-låsa Approach C eftersom:

- Söndag→måndag-tidspressen är produkt-/scope-fråga (Klas-domän)
- Direktivet "inga quickfixes" 2026-05-24 är från Klas; defer av rot-orsaks-fix till framtida session är medveten override av direktiv som bara Klas kan godkänna
- ADR-amendment 0062 är arkitektur-precedens som kräver Klas-medvetenhet, inte tyst CC-applicering

Förslagen Klas-fråga: *"Approach C: B-leverans nu med ACL-port + ADR 0062-amendment + TD-lyft för rot-orsaks-fix post-demo. Hypotes 1-verifiering deferred. EXPLAIN ANALYZE gate mot TD-94. GO eller override till A?"*

### Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP)
- Eric Evans, *Domain-Driven Design* (2003), kap. 14 "Bounded Contexts" (ACL-pattern)
- Vaughn Vernon, *Implementing Domain-Driven Design* (2013), kap. 13 (Context Mapping — ACL)
- Mary Poppendieck, *Lean Software Development* (2003), kap. 3 (Last Responsible Moment)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017), kap. 2 (Fitness Functions, reversibility)
- Kent Beck, *Extreme Programming Explained* (1999) (YAGNI)
- ADR 0045 (perf-budgetar p95 300ms hot-path)
- ADR 0062 (`IJobAdSearchQuery`-port + FTS-hybrid — kräver amendment)
- CLAUDE.md §2.5 (perf-disciplin), §9.4 (verbatim-källa), §9.6 (fas-regeln), §9.7 (TD-livscykel)
- JobTech taxonomy.api.jobtechdev.se (verifierat 2026-05-24)
