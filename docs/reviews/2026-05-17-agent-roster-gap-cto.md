# CTO-rekommendation — Agent-roster gap-analys (MasterClass-täckning)

**Datum:** 2026-05-17
**Beslutsfattare:** senior-cto-advisor (decision-maker per CLAUDE.md §9.6)
**Kontext:** Klas-fråga inför scoping av egen CC-session: "Har vi full agent/process-täckning för MasterClass-nivå, eller saknas specialist-agenter (perf, infra, SRE, m.fl.)?"
**Status:** Beslutsunderlag — ingen kod, inga agent-filer skapade. CTO-rek till Klas.
**Discovery-grund:** 12 agent-filer verifierade i `.claude/agents/`; code-reviewer/dotnet-architect/security-auditor-mandat lästa verbatim; LoggingBehavior.cs:18-29 verifierad; ADR 0036 läst i sin helhet.

---

## Helhets-verdikt (läs detta först)

**12 agenter är LAGOM-täckt — på gränsen till över-täckt — för JobbPilots faktiska ytor i nuvarande fas.** Roster-frågan är inte "vilka agenter saknas" utan "vilka *processartefakter* saknas". Det enda genuina agent-gapet är **ett (1)**: en perf-/load-test-builder — och den är **låst bakom en förutsättning** (perf-budget måste existera först, annars är agenten meningslös per Martin 2017 kap. 22 och Nygard 2018 kap. 5).

Resten av de föreslagna kandidaterna (infra/IaC-reviewer, SRE/observability-agent, dependency/supply-chain-agent, release-manager) avvisas som **agent-bloat** — de skulle duplicera mandat som code-reviewer, dotnet-architect, security-auditor och senior-cto-advisor redan bär, eller lösa problem som inte är agent-formade (de är ADR-/CI-/CLAUDE.md-formade).

Netto-rekommendation: **leverera processartefakter (perf-budget-ADR + CLAUDE.md §-tillägg + CI-gates), INTE nya review-agenter.** En enda ny builder-agent (perf-test-writer) tillkommer, och först efter att budgeten finns.

Detta är konsistent med kodbasens etos: ADR 0036 D1 avvisade prod-stack-bygge på YAGNI; samma disciplin gäller agent-rostern. **En review-agent utan en skriven standard att granska mot är en agent utan auktoritet** (code-reviewer.md:14 "Your authority is CLAUDE.md — not deadlines, not consensus"). Agenter får sin makt från skrivna regler. Inga skrivna perf/infra-SLO-regler → ingen meningsfull review-agent, bara teater.

---

## 1. Per kandidat-agent

### 1.1 Perf-/load-test-agent — **BEHÖVS: JA (villkorat, som BUILDER inte REVIEWER)**

**Verdikt:** JA till en `perf-test-writer` (builder), NEJ till en fristående "perf-reviewer".

**Motivering mot principer:**

- **Fitness functions (Ford/Parsons/Kua 2017, kap. 2):** Evolutionär arkitektur kräver *automatiserade, objektiva* fitness functions för icke-funktionella krav. Perf är den klassiska fitness-function-domänen. Idag fattas perf-beslut "ad hoc av CTO/architect/code-reviewer §3.6 (statiskt resonemang, ingen mätning)". Statiskt resonemang om perf är gissning — Martin 2017 kap. 22 ("The Humble Object") och Nygard 2018 kap. 5 ("Stability Patterns") är båda explicita: latens/throughput-beteende kan inte härledas från kodläsning, det måste mätas under last.
- **Konkret evidens i underlaget:** taxonomi-singleton-cache (perf-opt) orsakade en CI-regression; ADR 0032 OOM/streaming/rate-limiter-saga var perf under-spec; sök kör ILIKE + handtrimmade partial-index utan latens-mål. Tre reella incidenter där en perf-fitness-function hade gett regressionssignal. Detta är inte hypotetiskt gap — det är ett observerat mönster.
- **MasterClass-test:** En utomstående senior arkitekt som ser "Lighthouse>90 (manuell, ingen gate)" + "LoggingBehavior mäter `sw.ElapsedMilliseconds` men loggar bara, ingen budget/alarm" (verifierat: `LoggingBehavior.cs:18-29`) drar slutsatsen: *projektet mäter men agerar inte på mätningen.* Det är en MasterClass-brist.

**Exakt mandat (om/när den skapas):**
- **Roll:** Builder (som `test-writer`/`db-migration-writer`), INTE reviewer/gate. Skriver BenchmarkDotNet-microbenchmarks för hot paths (match-score, taxonomi-cache), NBomber/k6-scenarier för API-latens-budgetar, och Lighthouse-CI-config för frontend.
- **Gate:as INTE av agenten själv** — gaten är CI-jobbet agenten skapar. Agenten bygger fitness-function-infrastrukturen; CI exekverar den; code-reviewer läser regressionssignalen mot CLAUDE.md (precis som den idag läser test-coverage).
- **Anti-overlap:**
  - vs **code-reviewer:** code-reviewer äger *fortsatt* "är perf-budget-regressionen acceptabel?" mot CLAUDE.md. Perf-agenten skriver bara mätverktyget. Samma relation som code-reviewer↔test-writer idag (code-reviewer.md:297).
  - vs **dotnet-architect:** architect rådger *design* för perf (t.ex. "AsNoTracking, projektion till DTO"); perf-agenten *mäter* om designen håller budget. Advisor vs instrument — ingen överlapp.
  - vs **senior-cto-advisor:** CTO väljer *budget-nivåer* vid tradeoff (p95 200ms vs 500ms); agenten *implementerar* mätningen mot vald budget. Decision vs build.

**KRITISK FÖRUTSÄTTNING — se §2. Denna agent får INTE skapas före perf-budgeten är skriven.**

---

### 1.2 Infra/IaC-reviewer (DevOps) — **BEHÖVS: NEJ (agent-bloat)**

**Verdikt:** NEJ. Befintlig täckning räcker — beviset ligger i ADR 0036 självt.

**Motivering:**

- **ADR 0036 är counter-evidence mot gapet.** Underlaget hävdar "INGEN dedikerad infra/DevOps-reviewer". Korrekt — men ADR 0036 visar att 85 Terraform-filer + cloudwatch_ops_alarms-modulen redan granskades kompetent på MasterClass-nivå av **senior-cto-advisor (3 ronder, Q1c-korrigering + A3-rekommendation) + dotnet-architect (Terraform-design-validation, 4 Major + 2 Viktiga fynd)**, med citat ur Nygard *Release It!*, Humble/Farley *Continuous Delivery*, Poppendieck *Lean*, och AWS Well-Architected REL06-BP02. Det är inte ett gap — det är ett *fungerande* tandem-mönster.
- **YAGNI (Hunt/Thomas 1999) — samma logik som ADR 0036 D1.** ADR 0036 avvisade prod-stack-bygge för 0 användare. En dedikerad IaC-reviewer för en infra-yta som ändras i diskreta, sällsynta batchar (prod-stack är *deferrat till Fas 7*) är samma över-engineering ett lager upp. Drift-historiken (cluster-namn, OIDC-drift, RDS CA-bundle) löstes med `rds-ca-bundle-check`-workflow + `terraform plan`-diff + Klas-granskning — det är CI-/process-lösningar, inte agent-lösningar.
- **Conway-omvänt:** En solo-utvecklare (Klas) med CTO+architect-tandem på infra behöver inte en fjärde infra-röst. Fler granskningsröster på samma artefakt utan fler granskare = process-teater (SWE@Google kap. 9: specialist-review när teamet *inte är kvalificerat* — CTO+architect ÄR kvalificerade här, bevisat i ADR 0036).

**Vad som faktiskt behövs istället (ej agent):** ett CLAUDE.md §-tillägg som *kodifierar* när dotnet-architect ska invokeras för Terraform-scope (idag implicit per ADR 0036-precedens, inte skrivet). Det är en process-rad, inte en agent.

---

### 1.3 SRE/observability-agent — **BEHÖVS: NEJ (agent-bloat; gapet är fas-deferrat med avsikt)**

**Verdikt:** NEJ. Observability-arbetet är medvetet fas-skjutet, inte ouppmärksammat.

**Motivering:**

- **Gapet är redan triagerat och parkerat korrekt.** ADR 0036 Status: TD-77 (5xx-rate-alarm) + TD-78 (DB CPU >80%) är explicit deferrade till "Fas 8 Klass-launch — vid multi-user-volym". Det är CLAUDE.md §9.6-fas-regeln tillämpad rätt: observability-djup hör till en fas där trafik finns. Att skapa en SRE-agent nu vore att bemanna en roll för ett system utan användare — Nygard 2018 kap. 17 ("Transparency") förutsätter produktionstrafik att vara transparent *om*.
- **Ingen incident-volym att vara reaktiv mot.** SRE som distinkt roll (Beyer et al., *Site Reliability Engineering*, Google 2016, kap. 1) motiveras av error-budget-förvaltning över *reell* trafik. JobbPilot har 0 prod-användare. En SRE-agent vore en lösning som väntar på ett problem — anti-YAGNI.
- **Anti-overlap-omöjligt:** SRE-agentens naturliga mandat (alarm-design, incident-runbooks, error budgets) splittras redan rent: alarm-*design* → senior-cto-advisor (ADR 0036 D4 aggregate-thresholds, citerade AWS REL06-BP02); alarm-*säkerhetsdimension* → security-auditor (Minor-3 2026-05-12 log-pipeline-health-invariant); runbooks → `docs/runbooks/` + docs-keeper. En SRE-agent skulle överlappa alla tre utan att tillföra ett distinkt skrivet regelverk att vara auktoritet över.

**Trigger för omvärdering (skriv in i ADR):** Fas 7-förberedelse (prod-stack-session). Då — och först då — kan en incident-runbook-process (ej nödvändigtvis en agent) bli relevant.

---

### 1.4 Dependency/supply-chain-agent — **BEHÖVS: NEJ (agent-bloat; CI-formad, ej agent-formad)**

**Verdikt:** NEJ. Detta är ett CI/Dependabot-problem, uttryckligen redan så definierat.

**Motivering:**

- **security-auditor.md:292 säger det rakt ut:** "Scan for CVEs — that is Dependabot and CI's job" — explicit *icke*-mandat för en agent. Att skapa en agent för det security-auditor redan delegerat till maskinell automation är att åter-mänskliggöra ett medvetet automatiserat flöde. Supply-chain-scanning är en deterministisk, kontinuerlig, maskinell uppgift (SBOM, CVE-feeds) — inte ett omdömes-jobb en LLM-agent tillför värde till.
- **Rätt verktyg finns redan delvis:** gitleaks (pre-push, CLAUDE.md §6.3) + security-auditor (manuell secret/PII-Blocker). NuGet/npm CVE-täckning är en `dependabot.yml` + CI-`dotnet list package --vulnerable`/`pnpm audit`-gate — process, inte agent.
- **Fowler (Refactoring 2018) "preparatory refactoring"-logik omvänd:** lägg inte en agent där en CI-rad räcker. Agent-overhead (invocation, rapport-läsning, delegering) > värdet när uppgiften är binär (CVE finns/finns inte).

**Vad som behövs istället (ej agent):** `dependabot.yml` + CI-vuln-gate. Liten process-leverans, hör hemma i full-coverage-sessionen som CI-arbete.

---

### 1.5 Release-/deploy-manager-agent — **BEHÖVS: NEJ (agent-bloat; tag-disciplin är en runbook)**

**Verdikt:** NEJ. Deploy-disciplin är dokumenterad process + Klas-beslut, inte ett agent-omdöme.

**Motivering:**

- **ADR 0019 + ADR 0036 D1 definierar redan tag-semantiken** (`v*-dev`/`v*-rc*`/`v*` med manuell prod-approval). Deploy-besluten är *strategiska* (CLAUDE.md §9.2: "Deploya till staging eller prod utan Klas:s godkännande" är förbjudet för CC). En release-manager-agent skulle antingen (a) bara upprepa ADR-reglerna = noll tillfört värde, eller (b) fatta deploy-beslut = bryter §9.2 + ADR 0019:s Klas-sista-ordet-princip.
- **Last Responsible Moment (Poppendieck 2003), citerad i ADR 0036:** deploy-orkestrering blir värdefull att automatisera när release-kadensen är hög. JobbPilot deployar sällan, manuellt, med Klas-diff-granskning (CLAUDE.md §6.3 mekanism 4). Automatisera inte en lågfrekvent manuell grind innan kadensen motiverar det.
- **Anti-overlap-omöjligt:** release-beslut delas redan mellan Klas (godkännande) + CTO (strategiska fas-skiften, t.ex. ADR 0036 v0.2-tag-beslut) + CI (mekanisk deploy). Ingen lucka för en fjärde aktör.

**Vad som behövs istället (ej agent):** en `docs/runbooks/release-checklist.md` om en saknas. Runbook, inte agent.

---

### 1.6 Övriga bedömda (snabb-triage)

| Kandidat | Verdikt | Skäl |
|---|---|---|
| **A11y-agent** | NEJ — redan täckt | design-reviewer + `jobbpilot-design-a11y`-skill (WCAG 2.1 AA verbatim). Bekräftad icke-lucka. Ny agent = ren duplicering. |
| **API-contract/breaking-change-agent** | NEJ — för tidigt + delvis täckt | OpenAPI-export är post-Fas-0; inga externa API-konsumenter ännu (YAGNI). code-reviewer Area 3 fångar `IQueryable`-läckage. Omvärdera om/när publikt API + tredjepartskonsumenter finns. |
| **i18n-agent** | NEJ — YAGNI | Svenska-only civic utility (CLAUDE.md §10). Ingen multi-locale-yta. `jobbpilot-design-copy`-skill täcker locale-formatering. |
| **Concurrency/race-condition-agent** | NEJ — täckt | security-auditor Area 7 (race condition: row version/distributed lock) + dotnet-architect (async/threading §3.5). Distinkt agent skulle splittra ett redan delat mandat. |

---

## 2. Förutsättningar (sekvens — hård)

**Perf-agenten (§1.1) är den enda JA:n, och den är låst bakom budget-artefakter. Sekvensen är icke-förhandlingsbar:**

1. **FÖRST: Perf-budget-ADR.** En ADR (nästa lediga nummer) som låser:
   - API-latens-budgetar (p95/p99 per endpoint-klass: read-query vs command vs AI-call)
   - Frontend-budget (Lighthouse-score-tröskel som CI-*gate*, inte manuell DoD-rad; LCP/TBT/CLS-mål)
   - Backend hot-path-budgetar (match-score-beräkning, taxonomi-cache-lookup — de som redan orsakat regression)
   - Mät-metod-beslut (BenchmarkDotNet för micro, NBomber eller k6 för API-last — CTO-val vid den ADR:n)
   - **Detta är ett Klas-STOPP:** ny ADR + CLAUDE.md §-tillägg är strategiskt (CLAUDE.md §9.6 punkt 5). CTO kan rekommendera budget-nivåer men Klas låser dem (de har produkt-/kostnadskonsekvens).

2. **SEDAN: CLAUDE.md §-tillägg.** En ny §-rad (analog med §2.4 "Testbart först") som gör perf-budget till en granskningsbar konvention. Utan detta har en perf-agent ingen auktoritet — code-reviewer.md:14-princip: agent-auktoritet = skriven regel.

3. **SEDAN: CI-gate-infrastruktur.** Lighthouse-CI + (NBomber/k6)-jobb i `.github/workflows/`. Detta är CI-arbete, inte agent-arbete.

4. **SIST: `perf-test-writer`-agent-fil.** Builder som skriver benchmarks/scenarier mot den nu-existerande budgeten. Meningslös innan steg 1-3 (Martin 2017 kap. 22: instrumentet behöver en spec att mäta mot; ett benchmark utan budget är ett tal utan dom).

**Varför hård sekvens:** en perf-agent skapad steg-1-först skulle producera mätningar utan pass/fail-kriterium — exakt det LoggingBehavior redan gör (mäter `ElapsedMilliseconds`, loggar, ingen dom). Att lägga en agent ovanpå samma defekt vore att formalisera problemet, inte lösa det.

---

## 3. Prioritering + bunt-rekommendation för full-coverage-CC-sessionen

**Vad som SKA in i sessionen, i ordning:**

| # | Leverans | Typ | Klas-STOPP? |
|---|---|---|---|
| 1 | **Perf-budget-ADR** (latens/Lighthouse/hot-path-budgetar + mät-metod) | ADR | **JA** — strategisk, produkt-/kostnadskonsekvens. CTO rekommenderar nivåer, Klas låser. |
| 2 | **CLAUDE.md §-tillägg** (perf-budget som granskningsbar konvention; ny §) | CLAUDE.md-edit | **JA** — CLAUDE.md-edit kräver explicit Klas-instruktion (CLAUDE.md §9.2). |
| 3 | **CI-gates:** Lighthouse-CI + NBomber/k6-jobb + `dependabot.yml` + `pnpm audit`/`dotnet list package --vulnerable`-gate | CI/workflow | Nej (CC-arbete efter §1-2 låsta) |
| 4 | **`perf-test-writer`-agent-fil** (builder, mandat per §1.1) | Agent-fil | Nej (mekanisk efter 1-3) |
| 5 | **CLAUDE.md §-rad: dotnet-architect-invocation för Terraform-scope** (kodifierar ADR 0036-precedens) | CLAUDE.md-edit | **JA** — CLAUDE.md-edit. Liten men kräver Klas-GO. |
| 6 | **`docs/runbooks/release-checklist.md`** om saknas (annars skip) | Runbook | Nej |

**Sekvens-logik:** 1→2→3→4 är en strikt beroendekedja (agenten är värdelös utan budget+konvention+CI). 5 och 6 är oberoende, kan göras parallellt/när som helst i sessionen.

**Vad som INTE ska göras (explicit avvisat som bloat — skriv detta i sessionsplanen så det inte återuppstår):**

- ❌ Infra/IaC-reviewer-agent (§1.2) — CTO+architect-tandem bevisat i ADR 0036
- ❌ SRE/observability-agent (§1.3) — fas-deferrat med avsikt till Fas 7/8
- ❌ Dependency/supply-chain-agent (§1.4) — CI-formad, security-auditor.md:292 delegerar redan till Dependabot
- ❌ Release-/deploy-manager-agent (§1.5) — ADR 0019 + Klas-beslut täcker; §9.2 förbjuder agent-deploy-beslut
- ❌ A11y-, i18n-, API-contract-, concurrency-agenter (§1.6) — redan täckta eller YAGNI

**Netto agent-roster efter sessionen: 13 (12 + perf-test-writer), inte 17-18.** Fem avvisade kandidater är medveten anti-bloat, inte förbiseende.

---

## 4. Helhets-verdikt

**Är 12 agenter under-, lagom- eller över-täckt för MasterClass givet ytorna?**

**LAGOM, lutande mot välbalanserat.** Roster-strukturen (3 builders, 4 reviewers/veto, 2 advisors inkl. CTO-decision-maker, 3 docs/keeper) speglar SWE@Google kap. 9:s princip korrekt: specialist-review där generalisten ej är kvalificerad. Det enda stället där ingen agent har skriven auktoritet är perf — och det beror inte på en saknad agent utan på en **saknad skriven standard**. Saltzer & Schroeder (1975) "economy of mechanism": lägg inte till mekanism (agenter) där en regel (perf-budget) är den verkliga bristen.

**Netto-rekommendation till Klas:**

1. Den enda genuina agent-luckan är perf — och den löses **sist**, efter budget-ADR + CLAUDE.md-konvention + CI-gate. En perf-agent först vore formaliserad mätning-utan-dom (samma defekt som LoggingBehavior har idag).
2. De fyra andra kandidaterna (infra, SRE, dependency, release) är **agent-bloat**. Deras mandat ägs redan av code-reviewer/dotnet-architect/security-auditor/CTO eller är CI-/runbook-/ADR-formade. Att lägga agenter där vore att bryta samma YAGNI-disciplin ADR 0036 D1 just försvarade.
3. Buntningen för CC-sessionen är **process-tung, agent-lätt**: 1 ADR + 2 CLAUDE.md-§-tillägg + CI-gates + 1 builder-agent + ev. 1 runbook. Tre Klas-STOPP (ADR-nivåer, två CLAUDE.md-edits).

MasterClass-nivån höjs inte av fler granskningsröster på samma kod — den höjs av att de röster som finns har **skrivna, mätbara standarder** att vara auktoritet över. Perf är det enda området där en sådan standard saknas. Fixa standarden, lägg sedan agenten.

---

## Trade-offs accepterade

- **Perf-fitness-functions blir inte aktiva förrän hela sekvensen levererats** (ADR→CLAUDE.md→CI→agent). Accepteras: ett perf-benchmark utan låst budget är ett tal utan dom (Martin 2017 kap. 22). Hellre sen-men-auktoritativ än snabb-men-tandlös.
- **Drift-risk på Terraform fortsatt buren av CTO+architect ad hoc tills §5-raden skrivs.** Accepteras: ADR 0036 bevisar att tandemet fångar 4 Major + 2 Viktiga fynd utan dedikerad agent. Process-formaliseringen (§5) är polish, inte blocker.
- **13 agenter, inte 12.** Accepteras: perf-builder fyller en bevisat reell lucka (3 incidenter). Agent-antal är inte ett designvärde att minimera — auktoritets-täckning är.

## Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP), kap. 13 (Component Cohesion), kap. 22 (The Humble Object — mätning kräver instrumenterad gräns)
- Martin Fowler, *Refactoring* 2nd ed. (2018), kap. 3 (preparatory-refactoring-logik omvänd: ingen agent där en CI-rad räcker)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (O'Reilly 2017), kap. 2 (fitness functions för icke-funktionella krav)
- Michael Nygard, *Release It!* 2nd ed. (2018), kap. 5 (Stability Patterns), kap. 17 (Transparency — förutsätter prod-trafik)
- Beyer/Jones/Petoff/Murphy, *Site Reliability Engineering* (Google/O'Reilly 2016), kap. 1 (SRE-roll motiveras av error-budget över reell trafik)
- Winters/Manshreck/Wright, *Software Engineering at Google* (O'Reilly 2020), kap. 9 (specialist-review när generalist ej kvalificerad)
- Andrew Hunt & David Thomas, *The Pragmatic Programmer* (1999) — YAGNI
- Mary Poppendieck, *Lean Software Development* (2003) — Last Responsible Moment
- Saltzer & Schroeder, *The Protection of Information in Computer Systems* (CACM 1975) — economy of mechanism
- ADR 0019 (direct-push + tag-semantik), ADR 0032 (JobTech — perf under-spec-evidens), ADR 0036 (prod-stack defer + ops-alarms — CTO+architect-tandem-bevis)
- CLAUDE.md §2.4 (testbart först-precedens för perf-§), §6.3 (granskningsspärrar), §9.2 (CC-gränser/CLAUDE.md-edit), §9.6 (fas-regel)
- `src/JobbPilot.Application/Common/Behaviors/LoggingBehavior.cs:18-29` (mäter ElapsedMilliseconds, loggar, ingen budget/dom)
- security-auditor.md:292 (CVE-scanning explicit delegerat till Dependabot/CI — icke-agent-mandat)
- code-reviewer.md:14 (agent-auktoritet = skriven regel, ej konsensus)
