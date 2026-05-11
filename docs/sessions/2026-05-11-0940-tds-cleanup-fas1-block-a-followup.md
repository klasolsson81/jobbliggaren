---
session: TDs-cleanup laptop-CC efter Fas 1 Block A (TD-44 + TD-45 + TD-46 + senior-cto-advisor)
datum: 2026-05-11
slug: tds-cleanup-fas1-block-a-followup
status: KLAR
commits:
  - b742e50 test(api): TD-44 — HSTS-header anti-regression-test
  - 994bd1a feat(web): TD-45 — LoginForm focus-flytt vid state.error (a11y)
  - c505be2 refactor(web): TD-46 — extrahera pathToElementId till lib/forms/ (per-domän)
  - 09ef399 chore(claude): senior-cto-advisor + 4h-TD-policy
---

# TDs-cleanup laptop-CC-session

## Mål

Köra TDs-cleanup på laptop-CC enligt STARTPROMPT-LAPTOP-2026-05-11.md som
stationär-CC förberedde i kvällsslutet 2026-05-10. Tre prio-TDs (TD-44, TD-45,
TD-46) — alla follow-ups från Fas 1 Block A reviews.

Under sessionen: Klas pivoterade workflow till ny `senior-cto-advisor`-agent
+ 4-timmarsregel för in-scope-fix vs TD-skapande.

## Sammanfattning

4 commits pushade. 3 TDs stängda. 1 ny TD lyft (TD-49 — blockerad av saknat
unit-test-projekt). 1 ny agent + CLAUDE.md-policy-tillägg. Backend 3 nya
`[Fact]`-tester. Frontend +1 test (TD-45) + 35 unit-tester (TD-46) + 11/11
regression-grön på komponent-tester. tsc 0 errors.

Senaste pushed HEAD: `09ef399`.

## Per-TD-genomgång

### TD-44 — HSTS-header anti-regression-test (`b742e50`)

**Scope:** Utöka `UseHttpsRedirectionGateTests` med 3 nya `[Fact]`-tester som
täcker `UseHsts()`-gaten på samma fixture-arv (Disabled-Production /
Enabled-Production / Development).

**Tekniskt fynd (subtilt):** ASP.NET-default `HstsOptions.ExcludedHosts`
inkluderar `"localhost"` / `"127.0.0.1"` / `"[::1]"` — HSTS-header sätts
ALDRIG på dessa hosts (Microsoft-skydd mot att lock:a dev-loop). TestServer-Host
är `localhost` → headern hade missats även när `UseHsts()` är registrerad.

**Diagnostiserat via:** temporär IStartupFilter+`Response.OnStarting` som
dumpade `Request.IsHttps` / `Scheme` / `RemoteIp` i response-headers.
Diagnostic-koden borttagen innan commit.

**Architect-review:**
- dotnet-architect APPROVED med Major-fynd: ursprunglig
  `PostConfigure<HstsOptions>(opts.ExcludedHosts.Clear())` overrider Microsoft-
  skydd globalt, gav falskt-positivt skydd i Development-test
- **Min Major-fix** (bättre än architect-förslag): byt till
  `request.Headers.Host = "dev.jobbpilot.se"` per HSTS-test-request → simulera
  prod-DNS istället för att override Microsoft-defense. Cleanare, noll
  konfig-asymmetri mellan fixtures
- Minor 2 (HstsOptions.EnsureSafeForEnvironment unit-test) → TD-49 (blockerad
  av saknat JobbPilot.Api.UnitTests-projekt)

**Resultat:** 6/6 grön HttpsRedirectionGate-suiten + 55/55 grön hela
Configuration-namespace.

### TD-45 — LoginForm focus-flytt vid `state.error` (`994bd1a`)

**Scope:** Vid `state.error` flytta fokus till email-fältet via
`useRef<HTMLInputElement>` + `useEffect`.

**Variant-val:** Variant A (focus till email-fältet) över Variant B (focus till
`<p role="alert">` med `tabIndex={-1}`). Per `jobbpilot-design-a11y` §10:
screen reader läser `role="alert"` automatiskt — focus-flytt är för
keyboard-användare som scrollat förbi felmeddelandet → email-focus ger
natural recovery-action.

**Pattern-skillnad mot TD-15:** singelpunkt-fokus (inte path-baserad)
eftersom LoginForm:s error är medvetet generisk av säkerhetsskäl ("Inloggningen
misslyckades. Kontrollera e-post och lösenord."). TD-15-pattern (`pathToElementId`)
inte tillämpligt.

**Reviews:**
- code-reviewer APPROVED (1 Minor: disabled-skydd onödigt — cargo-cult)
- design-reviewer APPROVED (1 Minor: touch-target ärvt från TD-42)

**Resultat:** 5/5 grön Vitest. tsc 0 errors.

### TD-46 — Extrahera pathToElementId per-domän (`c505be2`)

**Scope:** Extrahera `pathToElementId` från `me-profile-form.tsx` och
`resume-content-form.tsx` till separata utility-moduler. Bygger på TD-43:s
komponent-tests för focus-beteende — TD-46 isolerar path-routing för
testbarhets-skull (jsdom-quirks med HTML5-constraint-validation kringgås).

**Discovery-fynd:** Functions var INTE dubbletter — me-form har simple
switch-statement (4 cases), resume-form har regex-cascade för nested paths
(personalInfo.X, experiences.N.X, etc). Olika dataformer. Spec antog
deduplikation, verkligheten skiljde.

**Approach-val (efter Klas-fråga om Clean Arch / SOLID / SoC):** Approach B
(per-domän filer) över ursprungliga Approach A (1 fil). Approach A bröt mot:
- SRP (en modul, två change-reasons)
- REP/CCP/CRP (things-change-together-belong-together-bröt)
- ISP (konsumenter tvingas importera från modul de inte behöver)
- SoC (separata concerns blandade)

Approach B uppfyller alla principer + matchar existerande `lib/actions/me-schemas.ts`-mönster
(purpose-folder + domain-prefix file).

**Filer som skapats:**
- `src/lib/forms/me-path-routing.ts` (switch-statement)
- `src/lib/forms/me-path-routing.test.ts` (12 testfall via `it.each`)
- `src/lib/forms/resume-path-routing.ts` (regex-cascade)
- `src/lib/forms/resume-path-routing.test.ts` (23 testfall via `it.each`)

**Function-export-konvention:** båda heter `pathToElementId` (generisk inom
sin fil — domain-context encoded i filsökväg). Konsumenter importerar
utan alias.

**Reviews:**
- code-reviewer APPROVED (2 Minor: regex-duplikation acceptabel "explicit >
  clever"; `fieldA11y`-helper-duplikation noterad för framtida TD)
- design-reviewer APPROVED (pure refactor, ingen a11y-regression)
- Bonus: `src/lib/`-org-konvention (purpose-folder vs domain-folder) — kandidat
  för ADR

**Resultat:** 35/35 grön nya unit-tests + 11/11 grön befintliga komponent-tester
(regression-check). tsc 0 errors.

### Infra: senior-cto-advisor + 4h-TD-policy (`09ef399`)

**Klas:s workflow-pivot mid-session:** Efter min initial Approach A-rek (för
TD-46) frågade Klas om det var rätt enligt Clean Arch / SoC / SOLID. Min ärliga
analys: nej, A bröt mot principer. Korrigerade rek till Approach B.

Insikt: mina rek tenderar mot "snabblösningar" som bryter mot kodkvalitets-
principer. Klas ville ha en strikt CTO-rådgivare som beslutsfattare istället
för CC.

**Skapat:** `.claude/agents/senior-cto-advisor.md` (251 rader):
- Strategisk beslutsfattare (inte advisor) för multi-approach-val
- Auktoritet: branschens skrivna regler (Martin, Evans, Vernon, GoF, Fowler) +
  modern .NET (Microsoft Learn, Esposito 2025, ardalis-template) + evolutionary
  (Ford 2017, Winters 2020, Twelve-Factor)
- Read-only (Read/Grep/Glob/WebSearch/WebFetch)
- 5 beslutsregler (principer > pragmatism, Mastercard-test, 4h-regel,
  avvisa snabblösningar, Klas-sista-ordet med argumentation)
- 3 exempel-användningar (multi-approach, TD-skapande-validering,
  Klas-override-tolerans)

**CLAUDE.md uppdaterad:**
- §9.2: senior-cto-advisor tillagd i invocera-listan (CC går direkt till
  implementation efter CTO-beslut, ingen Klas-STOPP behövs)
- §9.6 (NY): 4-timmarsregeln för in-scope-fix vs TD-skapande. TD lyfts
  ENDAST om: (1) annan fas, (2) saknad funktion-dependency, (3) scope > 4h
  CC-tid. Default = fixa in-block.

**Anti-pattern dokumenterad:** "spara TD så scope inte växer" — vi måste
ändå fixa det förr eller senare. Kvalitet > tempo.

## Decisions sammanfattade

1. **Variant A för TD-45** (focus till email-fältet, inte alert-element)
   — natural recovery + screen-reader-redundans
2. **Approach B för TD-46** (per-domän filer) över Approach A (1 fil)
   — SOLID/SoC/Clean Arch principrenhet vinner över "minimum nya filer"
3. **Host-header-pattern för TD-44** (skickar `Host: dev.jobbpilot.se` per
   request) över `ExcludedHosts.Clear()` — simulerar prod-trafik, inte
   override Microsoft-defense
4. **TD-49 lyft som genuin TD** — blockerad av saknat
   `JobbPilot.Api.UnitTests`-projekt (kriterium 2 i 4h-regel: saknad
   funktion-dependency)
5. **senior-cto-advisor skapad** — strategisk beslutsfattare för
   framtida multi-approach-val. Klas har sista ordet, CTO argumenterar tydligt.

## Lärdomar

- **`HstsOptions.ExcludedHosts` default-list** inkluderar localhost-varianter —
  dev-loop-skydd som måste hanteras i HSTS-test (via Host-header-mock, inte
  `ExcludedHosts.Clear()`).
- **`UseForwardedHeaders` middleware** i TestServer accepterar `X-Forwarded-Proto`
  när `KnownNetworks__0=127.0.0.1/32` är satt + `Connection.RemoteIpAddress` är
  `IPAddress.Loopback` (verifierat via temp IStartupFilter+OnStarting).
- **xUnit v3 + MTP-runner** använder `-namespace`/`-class`/`-method`-flaggor
  (single-dash, native syntax) — inte `--filter "FullyQualifiedName~..."`
  som dotnet-test default.
- **shadcn `Input` är function-component, inte forwardRef** — React 19 stödjer
  ref-prop direkt på function-components utan `React.forwardRef`.
- **Spec-kunskap drift:** TD-46 spec antog `pathToElementId`-duplikation, men
  discovery visade olika dataformer. Lärdom: alltid discovery före plan-design
  vid refactor-TDs som påstår "duplikering" — kontrollera först.
- **Min rek-tendens mot snabblösningar:** Klas:s pivot till senior-cto-advisor
  är kalibrerings-svar. Behåller kommentaren även till framtida CC-instances:
  *Mastercard-nivå-granskning är default, file count är inte design-axel.*

## Pre-existing infra (oförändrat sedan Block A apply)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS, VerifyFull TLS) |
| API task-def | `jobbpilot-dev-api` (post-TD-38 apply) |
| Worker task-def | `jobbpilot-dev-worker` (post-TD-38 apply) |
| Tag (senaste) | `v0.1.2-dev` på SHA `7cde3c7` |

## Tester totalt

- **Backend:** 563 → 566 (+3 från TD-44 HSTS-tester). CI: grön
- **Frontend Vitest:** 75 → 76 (+1 från TD-45 LoginForm focus-test) + 35 nya
  unit-tester från TD-46 path-routing = 111 totalt
- **Frontend komponent-tester:** 3 (TD-43 LoginForm + MeProfileForm + ResumeContentForm), oförändrat-fungerande post-TD-46-refactor

## Cost

Oförändrat ~$79.65/mån (inga nya AWS-resurser i denna laptop-session).

## Nästa session — startprompt

```
# Stationär CC startprompt — Fas 1 fortsättning (Block B eller TDs-cleanup)

**Förväntat HEAD:** `09ef399`
**Verifiera:** `git fetch origin main && git log --oneline -5` ska visa:
  09ef399 chore(claude): senior-cto-advisor + 4h-TD-policy
  c505be2 refactor(web): TD-46 — extrahera pathToElementId till lib/forms/ (per-domän)
  994bd1a feat(web): TD-45 — LoginForm focus-flytt vid state.error (a11y)
  b742e50 test(api): TD-44 — HSTS-header anti-regression-test
  0b4614a chore: laptop CC startprompt för TDs-cleanup session (borttagen)

## Mandatory reads vid session-start

1. **CLAUDE.md** — speciellt nya §9.2 (senior-cto-advisor) + §9.6 (4h-TD-policy)
2. **.claude/agents/senior-cto-advisor.md** — ny strategisk beslutsfattare
3. **docs/current-work.md** — sessions-state efter laptop-cleanup
4. **docs/sessions/2026-05-11-0940-tds-cleanup-fas1-block-a-followup.md** — denna session-logg
5. **docs/tech-debt.md** — TDs-status (TD-44/45/46 stängda, TD-49 ny)
6. **BUILD.md §18** — Fas 1-scope (Block B-alternativ)

## Aktivt val: Block B eller TDs-cleanup

### Block B (Fas 1-progression)

Tre möjliga sub-blocks:
- **B1:** Application Management UX-polish (status-flöde, transitions, follow-up)
- **B2:** Dashboard-skiss (start-page med statistik, Hangfire health)
- **B3:** JobTech-integration förstudie (BUILD.md §6, IJobSource-port)

### Eller fortsatt TDs-cleanup

Aktiva TDs prioriterade:
- **TD-39:** Error-summary-mönster (kräver design-input)
- **TD-40:** Path-equality regression-bevakning
- **TD-41:** Select-konvention native vs shadcn (design-beslut)
- **TD-42:** Touch-target projektbrett <44px (projekt-wide pass)
- **TD-47:** RDS CA-bundle-rotation cron (GitHub Actions)
- **TD-48:** Architecture-test Trust=true (NetArchTest)
- **TD-49:** HstsOptions unit-test (blockerad: JobbPilot.Api.UnitTests-projekt
  finns inte → bör skapas in-block om TD-49 tas)

## Ny disciplin från denna session

1. **senior-cto-advisor är default-beslutsfattare** vid multi-approach-val.
   CC presenterar Variant A/B/C → CTO väljer + motiverar mot principer →
   CC går direkt till implementation utan Klas-STOPP (om CTO är entydig).
2. **4h-regel:** in-scope-fix är default. TD lyfts ENDAST om annan-fas /
   saknad-dependency / >4h CC-tid.
3. **Discovery-first vid refactor-TDs:** verifiera spec-påståenden (t.ex.
   "duplikat") innan plan-design. TD-46-läxa.

## Första uppgift

Klas väljer mellan Block B-sub-blocks eller fortsätt TDs-cleanup. CC ska:
1. Läsa mandatory-list ovan
2. Verifiera HEAD
3. Vänta Klas-val
4. För valt scope: discovery → plan-design → CTO-invoke vid multi-approach →
   implementation → reviews → STOPP-rapport → commit + push
```

## ADR-anmärkning

Inga nya ADRs. `senior-cto-advisor`-agent + CLAUDE.md §9.6 är process-uppdateringar,
inte arkitekturbeslut. ADR-kandidat (uppmärksammad av TD-46 code-reviewer):
`src/lib/`-org-konvention (purpose-folder vs domain-folder) — kan skrivas
opportunistiskt vid framtida `lib/`-touch.
