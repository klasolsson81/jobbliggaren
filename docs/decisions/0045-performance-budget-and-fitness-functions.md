# ADR 0045 — Performance-budgetar och fitness functions (latens/CWV/Worker-mem + mät-metod + observe-only-ratchet)

**Datum:** 2026-05-17
**Status:** Accepted
**Kontext:** Performance under-spec-mönster identifierat via roster-gap-CTO-genomgång. JobbPilot mäter latens men har ingen skriven budget eller dom. Denna ADR ger mätningen ett kontrakt och etablerar fitness functions (Ford/Parsons/Kua) som observe-only-mekanismer med inskriven ratchet-väg.
**Beslutsfattare:** senior-cto-advisor (B1–B7 — budget-tal, mät-metod-val, gate-policy, ratchet-väg, agentId 2026-05-17); Klas Olsson (Accepted-flip 2026-05-17 + lås av (a) 300 ms / (b) 150 ms p95; Klas-direktiv 2026-05-17: BenchmarkDotNet/micro-benchmark deferras — endast NBomber-load-test + Lighthouse-CI denna fas); Claude Code (implementation)
**Relaterad:** ADR 0032 (JobTech streaming/OOM/rate-limiter — perf-incident-evidens), ADR 0042 (sök ILIKE + functional partial-index, typeahead SuggestPolicy 30/10s — perf-incident-evidens), ADR 0043 (taxonomi-singleton-cache → CI-regression — perf-incident-evidens), ADR 0044 (coverage non-regression-ratchet, observe-only Worker Fas 1 — **bindande gate-mönster denna ADR efterliknar**), ADR 0019 (direct-push), `docs/reviews/2026-05-17-agent-roster-gap-cto.md` (roster-gap-CTO-beslut: hård sekvens ADR→CLAUDE.md→CI→perf-test-writer-agent — denna ADR är steg 1), CLAUDE.md §3.6/§8/§9.6

---

## Kontext

Före detta beslut har JobbPilot **ingen skriven performance-budget och ingen dynamisk perf-dom**.

- **BUILD.md rad 1649** — enda perf-spec idag är launch-item "Performance-pass (Lighthouse, profiling)", dvs ett engångsmoment utan budget eller löpande gate.
- **CLAUDE.md §8 (Definition of Done)** — "Lighthouse > 90" är manuell verifiering, ingen automatiserad gate.
- **CLAUDE.md §3.6** — statisk query-hygien (`AsNoTracking`, paginering, projektioner till DTO) är den befintliga *statiska* perf-regeln. Denna ADR är **dynamisk-mätnings-komplementet** — den ersätter inte §3.6 utan ger det körtidsmätta motstycket.
- **`LoggingBehavior.cs:18-29`** mäter redan `Stopwatch.ElapsedMilliseconds` per Mediator-meddelande och loggar latensen — men ingen budget jämförs och ingen dom fälls. Denna ADR ger den befintliga mätningen ett kontrakt.

Tre observerade perf-incidenter visar under-spec-mönstret konkret:

- **ADR 0032** — JobTech streaming/OOM/rate-limiter: minnesregression krävde streaming-fix utan att något minnestak fanns inskrivet.
- **ADR 0042** — sök ILIKE + functional partial-index, typeahead SuggestPolicy 30/10s: latens-känsliga ytor designade ad-hoc utan latens-budget.
- **ADR 0043** — taxonomi-singleton-cache → CI-regression: en cache-design som regresserade prestanda utan att en fitness function fångade det.

Dessa tre motiverar att perf-budgetar formaliseras som *fitness functions* (Ford/Parsons/Kua, *Building Evolutionary Architectures* 2017, kap. 2) snarare än som engångsmätning.

**ADR 0044 (coverage non-regression-ratchet, observe-only Worker Fas 1) är det bindande mönstret** denna ADR efterliknar för gate-konstruktionen: observe-only Fas 1, eget CI-job utanför `ci.needs`, manuell ratchet vid Klas-GO, non-regression snarare än måltavla (Goodhart/Fowler).

Roster-gap-CTO-beslutet (`docs/reviews/2026-05-17-agent-roster-gap-cto.md`) fastslog en hård sekvens: **ADR → CLAUDE.md → CI → perf-test-writer-agent**. Denna ADR är steg 1.

## Beslut

> Samtliga numeriska tal nedan är **VERBATIM CTO-låsta** (senior-cto-advisor 2026-05-17, B1–B7). De konstrueras eller justeras inte av CC.

### Beslut 1 — API-latens-budgetar per hot-path-klass

Mätpunkt = **server-side handler-latens** (det `LoggingBehavior` redan instrumenterar), **ej** edge-to-edge. p95 = primär dom; p99 = observe-only Fas 1 (ej gate).

| Klass | Endpoints | p95-budget | p99 (observe) | Lås-status |
|---|---|---|---|---|
| (a) read-query/list | `/jobb`-sök, list-endpoints (ILIKE + functional partial-index, ADR 0042) | 300 ms | 600 ms | Klas-låsbar (produkt/UX/kostnad) |
| (b) typeahead/suggest | ADR 0042 SuggestPolicy 30/10s | 150 ms | 300 ms | Klas-låsbar (produkt/UX/kostnad) |
| (c) command/write | CQRS-handlers (`Result<T>`, UnitOfWork-behavior) | 400 ms | 800 ms | CTO-tekniskt satt |
| (d) ingestion-throughput | JobTech-sync (`SyncPlatsbankenStream`/`Snapshot`) | ≥ 200 jobb/min sustained (throughput-golv) | — | CTO-tekniskt satt |

**Explicit lås-notis:** Klass (a) 300 ms och (b) 150 ms p95 är **Klas-låsta vid Accepted-flip 2026-05-17** — de bär produkt-, UX- och kostnadskonsekvens (CLAUDE.md §9.6 punkt 5: större produkt-/kostnadsfråga kräver Klas-godkännande). Klass (c) och (d) är CTO-tekniskt satta.

### Beslut 2 — Frontend Core Web Vitals

Trösklar = Google/web.dev officiella "good"-trösklar 2026, 75:e percentilen. Lighthouse composite perf-score gejtas **EJ** (dokumenterat flaky vid single-run). LHCI-assertions körs med `numberOfRuns: 3` + median.

| Metrik | Tal | CI-status Fas 1 |
|---|---|---|
| LCP | < 2,5 s | Gate (assert: error) |
| CLS | < 0,1 | Gate (assert: error) |
| INP | < 200 ms | Observe-only (assert: warn) — lab approximerar field-metrik dåligt |
| Total page weight (resourceSizes) | budget i `budget.json` | Gate (assert: error) |
| Lighthouse composite perf-score | — | Observe-only (loggas, ej assert) |

### Beslut 3 — Worker-minnestak

Soft cap **512 MiB working-set** per Worker-jobbinstans (`SyncPlatsbankenStream`/`Snapshot`) som backstop-invariant mot regression av ADR 0032:s streaming-fix. Observe-only Fas 1 (trend-logg + alarm-tröskel-förberedelse), **EJ** hård CI-gate. Ratchet till alarm/gate vid prod-stack (Fas 7-trigger).

### Beslut 4 — Mät-metod och verktyg (CTO-val, multi-approach avgjort)

- **Load-test API-latens:** **NBomber** (.NET-native, xUnit/MTP-stack-koherent, ren C#). k6 explicit avvisat (ny JS-toolchain bryter CLAUDE.md §1/§3.1 + Clean Architecture / *Clean Coder* Martin 2017 kap. 13 om toolchain-disciplin).
- **Micro-benchmark:** **DEFERRAD per Klas-direktiv 2026-05-17.** CTO:s B4.2-rekommendation var BenchmarkDotNet observe-only/lokal för hot paths (match-score, taxonomi-cache). Klas beslöt vid Accepted-flip att **inte** addera BenchmarkDotNet denna fas — micro-benchmark-spåret skjuts. **Trigger för omvärdering:** dedikerad/konsekvent benchmark-HW finns (Fas 7-prod-stack, samma fas-disciplin som p99/Worker-mem-defer). Tills dess bärs micro-perf-risk av statisk hygien (§3.6) + load-test-fitness-function (NBomber) + `LoggingBehavior`-trend. Skälet CTO angav (delade GitHub-runners = brusig baslinje → absoluta trösklar flaky) kvarstår giltigt och stärker deferral-beslutet.
- **Frontend:** `treosh/lighthouse-ci-action` + `lighthouserc.json` + `budget.json` (ingen ny frontend-app-dependency — GitHub-Action-isolerad).
- **Top-level-paket som tillkommer** (BUILD.md §3.1, Klas-godkänt 2026-05-17): `NBomber`, `NBomber.Http` (NuGet, load-test-projekt); `treosh/lighthouse-ci-action` (GitHub Action, ej paket). BenchmarkDotNet **ej** adderat (Klas-deferral ovan).

### Beslut 5 — Gate-blockerande-policy (KRITISK)

CTO-princip: **"flaky perf-gate sämre än ingen perf-gate"**. ADR 0044-precedens är bindande. ALLA perf-/audit-mekanismer Fas 1 = **observe-only, eget job, UTANFÖR `ci.needs`**, additiva. Coverage-gaten (`ci.needs: [backend, frontend, coverage]`) lämnas **orörd**.

| Mekanism | Fas 1-status |
|---|---|
| Lighthouse-CI (LCP/CLS/page-weight) | Eget job, observe-only, `if: always()`, ej i `ci.needs` |
| Load-test (NBomber) | Eget job, observe-only, budget-överskridande → `::warning::`, exit 0 |
| BenchmarkDotNet micro | **Deferrad** (Klas-direktiv 2026-05-17) — ej i denna fas; trigger = dedikerad benchmark-HW (Fas 7) |
| Dependabot | Config-only (öppnar PRs, gejtar ej per definition) |
| Audit-gate (`dotnet list package --vulnerable --include-transitive` / `pnpm audit --audit-level=high`) | Eget job, observe-only Fas 1, fail-signal vid High/Critical, exit 0 |

### Beslut 6 — Ratchet-väg (ADR 0044 Beslut 4-mönster)

Flip observe-only → blockerande (`ci.needs`-inläggning) sker när perf-mätningen visat **stabil distribution över N gröna runs på dedikerad/konsekvent HW**, vid **Klas-GO** — non-regression-ratchet, **EJ** target (Goodhart; Fowler *Refactoring* 2nd 2018; Ford/Parsons/Kua 2017). p99/INP/Worker-mem-gate + audit-blockering deferras till prod-trafik/dedikerad HW (**Fas 7-trigger** — samma fas-disciplin som roster-doc:ens SRE-defer).

## Konsekvenser

**Positiva:**

- Performance får en *skriven dom* — den befintliga `LoggingBehavior`-mätningen kopplas till ett kontrakt istället för att bara logga.
- Fitness functions per Ford/Parsons/Kua (2017) — perf blir en evolutionär arkitektur-egenskap, inte ett engångs-launch-item.
- MasterClass-höjning från "mäter men agerar ej" till "mäter, dömer observe-only, har inskriven ratchet till blockerande".
- Tre historiska incident-mönster (ADR 0032/0042/0043) får en strukturell motåtgärd.

**Negativa + mitigering:**

- Observe-only Fas 1 blockerar **inte** regression automatiskt än → mitigering: inskriven ratchet-väg (Beslut 6) + JSON-trend-artefakt (synlig drift) + Dependabot parallellt (supply-chain-yta täcks oberoende av perf-gaten).
- Ny tooling-yta (NBomber/lighthouse-ci-action) → minimerad: load-test-projekt-isolerad, Action-isolerad, ingen ny frontend-app-dependency, ingen ny JS-toolchain (k6 avvisat), BenchmarkDotNet ej adderat (Klas-deferral — ytterligare minskad tooling-yta denna fas).
- Lab-metriker (INP) approximerar field-metrik dåligt → INP medvetet observe-only Fas 1 tills field-data finns (prod-stack, Fas 7).

## Alternativ övervägda

1. **k6 över NBomber — avvisat:** ny JS-toolchain bryter CLAUDE.md §1 (.NET-stack-koherens) / §3.1 + Clean Coder (Martin 2017 kap. 13) toolchain-disciplin. NBomber är .NET-native och stack-koherent.
2. **Blockerande perf-gate i `ci.needs` Fas 1 — avvisat:** flaky gate sämre än ingen (CTO-princip); ADR 0044-precedens bindande; Goodhart — en gate som ger falsklarm ignoreras och förlorar värde.
3. **Lighthouse composite-score som gate — avvisat:** dokumenterat flaky vid single-run; LCP/CLS/page-weight som diskreta assertions med `numberOfRuns: 3` + median valt istället.
4. **Split-ADR 0045a/b/c (latens / CWV / Worker-mem separat) — avvisat:** REP/CCP-brott (release/common-closure — beslut med samma fas och samma ratchet-mekanik hör ihop); cross-ref-spindelnät mellan tre ADRs som alltid ändras tillsammans.
5. **Hård BenchmarkDotNet-gate på shared GitHub-runner — avvisat:** web-verifierad brusig baslinje på delade runners → absoluta micro-benchmark-trösklar inherent flaky. Observe-only relativ-baseline + trend-artefakt valt; absoluta trösklar deferras till dedikerad HW.

## Implementationsstatus

**ADR Accepted 2026-05-17** (Klas-flip; (a) 300 ms / (b) 150 ms p95 låsta).

**Klas-godkänt vid flip:**

- CLAUDE.md §2.5 (perf granskningsbar kärnprincip) + §9.2 dotnet-architect-obligatorisk-rad för Terraform-scope.
- BUILD.md §3.1: NBomber + NBomber.Http. **BenchmarkDotNet deferrad** (Klas-direktiv — micro-benchmark-spåret skjuts till Fas 7 / dedikerad benchmark-HW). **Fysiskt applicerat 2026-05-17** (människa-i-loopen: Klas körde `approve-spec-edit.sh` manuellt, `guard-spec-files` single-use-token konsumerad; auto-mode-klassificerarens block av agent-själv-godkännande bekräftat korrekt säkerhetsbeteende, ej bugg — sista roster-doc-loose-end stängd).

**Pending denna session (roster-doc-sekvens, efter denna flip):**

- CI-gates (observe-only, additiva, utanför `ci.needs`): Lighthouse-CI + NBomber-load-test + `dependabot.yml` + audit-gate.
- perf-test-writer-agent — **SIST** per roster-gap-CTO-sekvensen (ADR → CLAUDE.md → CI → agent). Mandat: NBomber-load-tester + Lighthouse-CI-config; **ej** BenchmarkDotNet (deferrad).

**Relaterade beslut:** ADR 0032/0042/0043 (perf-incident-evidens), ADR 0044 (bindande gate-mönster), ADR 0019 (direct-push), roster-gap-CTO-doc 2026-05-17.

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) kap. 13 & 22 — komponent-/toolchain-disciplin
- Ford, Parsons, Kua, *Building Evolutionary Architectures* (2017) kap. 2 — fitness functions
- Nygard, *Release It!* 2nd (2018) kap. 5 & 17 — stabilitets-/kapacitetsmönster
- Fowler, *Refactoring* 2nd (2018) + Goodharts lag — non-regression-ratchet, ej måltavla
- Poppendieck, *Lean Software Development* (2003) — Last Responsible Moment (defer av prod-gates)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — YAGNI (ingen hård micro-gate i förtid)
- Google / web.dev Core Web Vitals 2026 — "good"-trösklar, 75:e percentilen
- CLAUDE.md §3.6 (statisk query-hygien), §8 (DoD/Lighthouse), §9.6 (in-scope vs TD / Klas-STOPP)
- ADR 0042 (sök/typeahead), ADR 0044 (coverage gate-mönster)
- senior-cto-advisor-beslut 2026-05-17 (B1–B7)

---

*ADR-index underhålls av docs-keeper (index-uppdatering sker centralt vid session-end — 3-CC-koordinationsregel, ej denna leverans).*
