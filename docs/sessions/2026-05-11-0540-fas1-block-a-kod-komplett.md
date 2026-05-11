---
session: Fas 1 Block A — kod-fas komplett (A1+A2+A3+A4 + parallell TD-43)
datum: 2026-05-11
slug: fas1-block-a-kod-komplett
status: KLAR (kod-fas) — apply-fas A4 väntar Klas-GO
commits:
  - 267e120 fix(web): TD-15 — Resume-formulär a11y-koppling Zod path → aria-invalid + fokus-flytt
  - 4df70c2 docs: TD-15 STÄNGD + TD-39/TD-40 lyfta + reviews för Fas 1 Block A1
  - cc585a7 feat(web): JobSeeker profil-edit-yta — Fas 1 Block A2
  - d55b460 docs: TD-41 + TD-42 lyfta + reviews för Fas 1 Block A2
  - 1221240 test(api): TD-31 — UseHttpsRedirection env-gate anti-regression (Sec-Major-2)
  - b4e9199 docs: TD-31 STÄNGD + reviews för Fas 1 Block A3
  - 5428805 docs: TD-43 + TD-44 lyfta — komponent-tests för forms + HSTS-anti-regression
  - 6b2b0ca test(web) — TD-43 (parallell CC-session) — komponent-tests
  - 01cc656 docs (parallell CC-session) — TD-43 STÄNGD + TD-45/TD-46
  - ebb7550 feat(infra): TD-38 — TLS-hardening för Api/Worker connection-strings
  - 48ebe0e docs: reviews för Fas 1 Block A4 (TD-38 TLS-hardening)
  - 766a655 docs: TD-47 + TD-48 + apply-runbook + Block A summary
---

# Fas 1 Block A — kod-fas komplett

## Mål

Block A som första Fas 1-arbetspass: Resume/JobSeeker-UX-polish + 2 säkerhets-TDs
(TD-31 + TD-38). Sub-block-sekvens A1 → A2 → A3 → A4 enligt plan.

## Sammanfattning

Alla fyra sub-block + en parallell session (TD-43) implementerade och pushade.
12 commits totalt. 9 follow-up-TDs lyfta. Apply-fas för A4 (TD-38) väntar
Klas-GO. Backend 554 → 563 (+9 tester), Frontend 65 → 75 + 3 nya komponent-
tester (TD-43 via parallell CC-session).

**Stängda TDs:** TD-15, TD-31, TD-43.
**Nya TDs:** TD-39, TD-40, TD-41, TD-42, TD-44, TD-45, TD-46, TD-47, TD-48.

## Sub-block-genomgång

### A1 — TD-15 Resume-formulär aria-invalid + focus-flytt

**Scope:** koppla Zod issue.path till `aria-invalid` + `aria-describedby` per
fält i `resume-content-form.tsx` (16 fält), plus focus-flytt till första
fel-fält via `useEffect`.

**Variant-val:** Variant 1 (minimal a11y-fix utan zodResolver-refactor) över
Variant 2 (strukturell). CLAUDE.md "don't add abstractions beyond what the
task requires" — Variant 2 var arkitektur-refactor utanför TD-15-scope.

**Reviews:**
- design-reviewer APPROVE-WITH-FIXES (M1 focus-flytt, n1 redundant suffix
  fixade in-block; m1 + m2 lyfta som TD-39/TD-40)
- code-reviewer APPROVE (inga in-block)

**Lärdom:** design-reviewer fångade focus-flytt-saknaden — code-reviewer
missade det. A11y-specifik review är värt sin tid på frontend-touch.

### A2 — JobSeeker profil-edit-yta

**Scope:** bygga frontend edit-yta som konsumerar befintlig
`PATCH /api/v1/me/profile`. Backend (UpdateMyProfileCommand + handler) fanns
redan; frontend saknade edit-funktionalitet (`(app)/mig/page.tsx` var
read-only).

**6 nya filer + 1 utökad:**
- `lib/types/me.ts`, `lib/api/me.ts`, `lib/actions/me-schemas.ts`,
  `lib/actions/me-schemas.test.ts`, `lib/actions/me.ts`,
  `components/me/me-profile-form.tsx`
- `app/(app)/mig/page.tsx` utökad (2 kort: read-only Kontoinformation +
  edit Profil)

**Designval:** Native `<select>` + native checkbox (civic-utility-aestetik,
GOV.UK-stil) över shadcn Select. A11y-pattern från TD-15 (`fieldA11y` +
`pathToElementId` + `useEffect`-focus) kopierat lokalt — inte extraherat
till delad lib (YAGNI tills 3+ konsumenter, "rule of three").

**Reviews:**
- design-reviewer APPROVED-with-conditions (Mi2/Mi3/Mi4/Mi6 fixade in-block
  — ellipsis Unicode, tidsformat, nätverksfel-text, legend-styling.
  M1+M2+Mi1 → TD-41+TD-42)
- code-reviewer APPROVED med Minor (klient-Zod-borttagning AVVISAD pga
  a11y-koppling — backend skickar bara generic error, klient-path-mapping
  förlorad utan klient-Zod)

**Lärdom:** Code-reviewer-fynd kan vara avvisad-värd om det skulle bryta
TD-15-pattern. Discipline-spår: "behåll det som löser TD-15, lyft andra
optimeringar som TDs".

### A3 — TD-31 UseHttpsRedirection env-gate-test

**Scope:** Anti-regression-test för `Program.cs:155` env-gate på
`UseHttpsRedirection()` (Sec-Major-2 STEG 13b).

**3 testfall:**
1. Production + Alb:HttpsEnabled=false → 200 (no redirect)
2. Production + Alb:HttpsEnabled=true → 307
3. Development → 307

**Pattern:** abstract base `HttpsRedirectionGateFactoryBase` + 3 sealed
concrete factories (Disabled-Production / Enabled-Production / Development).
Cost: 3 separata Postgres+Redis Testcontainers (~30 sek extra på CI).

**Iter-fynd:**
1. **CA1816 build-fel** på abstract base-DisposeAsync (ProductionStartupFactory
   är `sealed` så CA1816 triggade inte där). Fix: `GC.SuppressFinalize(this)`.
2. **Tests 2+3 fail (200 inte 307)** — `UseHttpsRedirection` middleware
   loggar `"Failed to determine the https port for redirect"` när varken
   `ASPNETCORE_URLS`/`ASPNETCORE_HTTPS_PORTS`/`HTTPS_PORT` är satt. Fix:
   `services.PostConfigure<HttpsRedirectionOptions>(opts => opts.HttpsPort = 443)`
   i ConfigureWebHost.

**Reviews:**
- code-reviewer APPROVE (snake_case-testnamn matchar grannfilen — repo-wide-fråga)
- dotnet-architect APPROVED-with-fixes (Mindre 1: redundant `UseEnvironment`
  borttagen; Mindre 3: `GC.SuppressFinalize` flyttad FÖRE `base.DisposeAsync`.
  Mindre 4 HSTS-coverage → TD-44)

**Lärdom:** `UseHttpsRedirection` kräver känd HTTPS-port via env-var ELLER
`HttpsRedirectionOptions.HttpsPort`. Test-config-quirk som hade låst 2-iter-
debug-cykel om det inte upptäckts via systematisk diagnos.

### TD-43 (parallell CC-session) — Komponent-tests för forms

**Scope:** Vitest + RTL + user-event baseline för 3 forms (LoginForm,
MeProfileForm, ResumeContentForm).

**Klas-val:** parallell-session-flöde över sekventiell. A4 (.NET/infra) och
TD-43 (frontend) har disjoint filer → noll merge-konflikter på fil-nivå.
Push-race hanteras via rebase-disciplin.

**Resultat:** parallell session pushade 2 commits (6b2b0ca + 01cc656) före
A4-pushen. Min rebase mot origin/main blev ren fast-forward — disjoint
filer verifierat.

**Sidoeffekt:** TD-43-sessionen tog TD-45+TD-46 för sina a11y-follow-ups.
Mina A4-rapporter referenser till "TD-45" (bundle-rotation) + "TD-46"
(architecture-test) blev fel. Lösning: skapade TD-47 + TD-48 i denna session
i stället. Historisk audit-trail i sparade rapporter pekar på de gamla
numren — accepterat.

**Lärdom:** Parallell-session-mönstret fungerar bra för disjoint scope, men
TD-numrering behöver koordineras eller "first-come first-served". Inget
strukturellt problem, bara observation.

### A4 — TD-38 TLS-hardening (kod-fas)

**Scope:** Stänga TLS-MITM-ytan inom VPC genom att splitta
`BuildConnectionString` i Migrate till två varianter:
- `ForMigrate`: SSL Mode=Require + Trust=true (bootstrap-task)
- `ForPersisted`: SSL Mode=VerifyFull + Root Certificate (Api/Worker)

**Implementation:**
- `ConnectionStringFactory.cs` (ny `public static class`, lyft från Program.cs)
- `infra/certs/rds-global-bundle.pem` (165 KB, 108 certs, AWS publika)
- `Api/Dockerfile` + `Worker/Dockerfile` — `COPY ... /etc/ssl/certs/`
- `.dockerignore`-negation (annars exkluderas `*.pem` + `infra/`)
- 6 unit-tester i nytt `JobbPilot.Migrate.UnitTests`-projekt

**Reviews (3 parallella — security PRIO):**
- **security-auditor APPROVED** med 8-pkt Apply-fas-checklist + S-Minor-1
  (bundle-rotation-bevakning, → TD-47) + S-Minor-2 (Migrate Trust=true OK för
  dev, omvärdera pre-staging)
- **code-reviewer Changes Requested** med **1 BLOCKER B1** (.dockerignore
  exkluderar `infra/` + `*.pem`) + Major (test-coverage). Båda fixade in-block
- **dotnet-architect APPROVED** med Mindre 1 (ConnectionStringFactory-lift,
  fixade in-block) + Mindre 2 (architecture-test, → TD-48)

**B1-fix:** explicit `!infra/certs/` + `!infra/certs/*.pem` negation i
`.dockerignore`. Build-context hade annars saknat bundle:n → `COPY` failat.

**Lärdom:** `.dockerignore` är icke-trivial vid första COPY av filer från
exkluderade kataloger. Kombineras med security-auditors implicit antagande
"bundle är i build-context" som kunde missats om code-reviewer inte fångat
det.

## Decisions sammanfattade

1. **Variant 1 över Variant 2 för TD-15** (minimal fix utan refactor)
2. **Native form-controls** för A2 (civic-utility över shadcn-konsekvens)
3. **A11y-pattern kopieras lokalt** i stället för delad lib (YAGNI rule of three)
4. **Klient-Zod behålls** för A2 (a11y-koppling kräver path från Zod issues)
5. **3 separata Testcontainers** för A3 (cost OK för pattern-konsistens)
6. **Parallell CC-session-flöde** för A4 + TD-43 (disjoint scope)
7. **A4 apply defererad** — kräver AWS SSO + CloudWatch-bevakning, bättre i
   dedikerad session med Klas-GO

## Pre-existing infra (oförändrat)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` (200 OK + HSTS) |
| API task-def | `jobbpilot-dev-api:3` |
| Worker task-def | `jobbpilot-dev-worker:2` |
| Tag (senaste) | `v0.1.0-dev` på SHA `8215658` |

## Tester totalt

- **Backend:** 563 (var 554, +6 från A3 + 3 från A4 ConnectionStringFactory)
- **Frontend:** 75 Vitest (var 65, +10 från A2 me-schemas) + 3 nya
  komponent-tester (TD-43 parallell)
- **CI:** Senaste pushed state grön

## Cost

Oförändrat ~$79.65/mån (inga nya AWS-resurser i Block A kod-fas).

## Nästa session

**Klas:s val:**

### Alternativ 1: Apply A4 (TD-38) först
- AWS SSO + tag `v0.1.1-dev` → deploy-dev.yml
- Re-run Migrate-task + force-redeploy Api/Worker
- 8-pkt security-checklist från `docs/runbooks/td-38-tls-apply.md`
- Estimerad tid: 30-45 min inkl. CloudWatch-bevakning

### Alternativ 2: Block B start
Möjliga sub-blocks (Klas:s val):
- **B1:** Application Management UX-polish (status-flöde, transitions, follow-up)
- **B2:** Dashboard-skiss (start-page med statistik, Hangfire health)
- **B3:** JobTech-integration förstudie (BUILD.md §6, IJobSource-port)

### Alternativ 3: TDs-cleanup
- TD-44 (HSTS-anti-regression-test) — ~30 rader extension till A3:s testfil
- TD-39/TD-40 (Resume a11y-uppföljning)
- TD-41 (Select-komponent-konvention)

## ADR-anmärkning

Inga nya ADRs i Block A. Block A var implementation av befintliga ADR:er
(0021 Resume + 0024 GDPR + 0026/0027 HTTPS). Eventuell ADR om TLS-postur
för persisterad data (per security-auditors fråga) defererad — TD-38-stängningskommentar
i tech-debt.md räcker enligt security-auditor.
