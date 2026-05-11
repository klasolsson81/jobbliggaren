# Laptop CC startprompt — TDs-cleanup efter Fas 1 Block A

**Skapad:** 2026-05-11 av stationär CC efter Block A apply komplett.
**Du:** Fresh CC på laptop, ev. aldrig sett JobbPilot tidigare.
**OBS:** Denna fil ska tas bort av dig efter att du är klar (se "Slutsteg" nedan).

---

## Förkrav

1. **Repo:** clone om inte gjort tidigare:
   ```bash
   git clone https://github.com/klasolsson81/jobbpilot.git
   cd jobbpilot
   ```
   Annars:
   ```bash
   git pull origin main
   ```

2. **Verifiera HEAD = `09d6f7b`:**
   ```bash
   git log --oneline -3
   ```
   Förväntat topp-rad: `09d6f7b docs: TD-38 STÄNGD — A4 apply komplett`.

3. **Dev-stack-verify (informational, inte blocker för kod-arbete):**
   ```bash
   curl -I https://dev.jobbpilot.se/api/ready
   ```
   Förväntat: 200 + `Strict-Transport-Security`-header.

4. **AWS SSO behövs INTE** för TDs-cleanup-arbete (ren kod, inga AWS-anrop).

5. **Lokala krav:**
   - .NET 10 SDK (per `global.json`)
   - Node 22+ med pnpm
   - Docker Compose (för `make dev`-stack om du behöver Postgres/Redis lokalt)

---

## Obligatorisk läsning vid session-start

1. **CLAUDE.md** — coding-konventioner + agent-flöde
2. **docs/current-work.md** — Block A komplett-status
3. **docs/tech-debt.md** — alla TDs (sök "TD-44", "TD-45", "TD-46" för PRIO-targets)
4. **docs/sessions/2026-05-11-0540-fas1-block-a-kod-komplett.md** — senaste session-logg
5. **docs/steg-tracker.md v1.13** — STEG-historik
6. **BUILD.md §18** — Fas 1-scope

---

## Aktuellt projekt-tillstånd

- **HEAD:** `09d6f7b`
- **Block A komplett (kod + apply).** Stängda TDs: TD-15, TD-31, TD-38, TD-43
- **Tester grön:** Backend 563/563 + Frontend 75/75 Vitest + 3 komponent-tester
- **AWS dev:** live på `https://dev.jobbpilot.se/api/ready` (Api/Worker 1/1 stable, TLS=VerifyFull)
- **Branch:** single-branch policy per ADR 0019 (direct-push till main)

---

## Uppgift: TDs-cleanup

### Klas:s prio (rangordnad)

Klas vill primärt jobba med TDs i denna sekvens (han bekräftar/justerar när han öppnar laptop CC):

**PRIO 1 — TD-44: HSTS-header-anti-regression-test**
- Scope: ~30 raders extension till `tests/JobbPilot.Api.IntegrationTests/Configuration/UseHttpsRedirectionGateTests.cs`
- Bygger på A3-pattern (samma factory-class, ny `[Fact]`)
- Assertion: `Strict-Transport-Security: max-age=31536000; includeSubDomains` finns på response när `Alb:HttpsEnabled=true` i Production
- Anti-regression åt andra hållet: HSTS-header **inte** sätts när `HttpsEnabled=false`

**PRIO 2 — TD-45: LoginForm focus-flytt vid `state.error`**
- Scope: ~10 raders fix i `web/jobbpilot-web/src/components/forms/LoginForm.tsx`
- Pattern: kopiera TD-15-läxan från `resume-content-form.tsx` (useEffect + `document.getElementById(...)?.focus()`)
- A11y-uppgradering (`jobbpilot-design-a11y` §10 punkt 4)

**PRIO 3 — TD-46: Exportera `pathToElementId` för isolated unit-test**
- Scope: ~50 rader netto — extrahera `pathToElementId` från `resume-content-form.tsx` + `me-profile-form.tsx` till `web/jobbpilot-web/src/lib/forms/path-routing.ts` med parametriserade Vitest-tester
- Refactor + test, ingen beteendeförändring

### Lägre prio (ej i denna session om tid saknas)

- **TD-39** (error-summary) — Fas 2+, kräver design-input. **Skip.**
- **TD-40** (path-equality regression-bevakning) — tight scope, kan tas om tid finns
- **TD-41** (Select-konvention native vs shadcn) — kräver design-beslut, **skip för auto**
- **TD-42** (touch-target projektbrett <44px) — projekt-wide pass, stort scope. **Skip.**
- **TD-47** (bundle-rotation cron) — GitHub Actions workflow, isolerat. Senare.
- **TD-48** (architecture-test för Trust=true) — NetArchTest knepigt. Senare.

---

## CC-flöde per TD (per CLAUDE.md §1.5, §6.3, §9)

För varje TD:

1. **Discovery-rapport:** läs target-filer + befintliga patterns. Rapportera kort till Klas.
2. **Plan-design + STOPP** för Klas-GO innan implementation
3. **Implementation** + verify (`pnpm tsc --noEmit` + `pnpm vitest run` för frontend; `dotnet test --solution JobbPilot.sln` för backend)
4. **Agent-reviews** (per CLAUDE.md §9.2):
   - Frontend-TDs: `code-reviewer` + `design-reviewer` parallellt
   - Backend-TDs: `code-reviewer` + `dotnet-architect` parallellt
5. **Commit + push** efter Klas:s diff-granskning (single-branch, direct-push)

### Innan första push

```bash
git fetch origin main
git rebase origin/main   # disjoint scope om Klas inte rört andra filer
```

---

## Quirks från Block A:s arbete (worth knowing)

- **`IWebHostBuilder.UseEnvironment()` är no-op** för minimal API + WebApplicationFactory. Använd env-var i `InitializeAsync` FÖRE Services-access.
- **`IConnectionMultiplexer` kräver SEPARAT replace** utöver `IDistributedCache` i Test-fixturer (TD-37-läxa).
- **`UseHttpsRedirection` middleware** behöver `services.PostConfigure<HttpsRedirectionOptions>(opts => opts.HttpsPort = 443)` i test-host för att redirecta (annars no-op + warning).
- **Native form-controls** är civic-utility-OK (GOV.UK-stil, ej shadcn-tvång).
- **`.dockerignore`-negation** krävs när bundles ska COPY:as från exkluderade kataloger.
- **A11y-pattern (TD-15)** — `fieldA11y(path)` + `pathToElementId(path)` + `useEffect`-focus på `serverError.path` är JobbPilot-standard för forms.

---

## Web-search ENABLED

Vid osäkerhet om externa fakta — sök innan svar (CLAUDE.md §9.5).

---

## Slutsteg (KRITISKT — när Klas-godkända TDs är klara)

**Du MÅSTE ta bort denna fil** + uppdatera docs:

```bash
# 1. Uppdatera docs som reflekterar avslutade TDs
#    - docs/current-work.md (kort "TDs-cleanup-session 2026-05-11"-rad)
#    - docs/tech-debt.md (stäng TDs som genomfördes)
#    - docs/sessions/<ny session-logg>.md (medium detalj per CLAUDE.md §1.5)

# 2. Ta bort startprompt-filen
git rm STARTPROMPT-LAPTOP-2026-05-11.md

# 3. Commit + push
git add docs/
git commit -m "docs: TDs-cleanup session — TD-XX/YY stängda + ta bort startprompt"
git push origin main
```

**Klas är specifik:** denna fil får inte ligga kvar i repo:t. Den är ephemeral session-instruktion, inte permanent dokumentation.

---

## Kontakt-checkpoints (rapportera till Klas)

- Efter discovery för varje TD → STOPP-rapport
- Efter reviews → STOPP-rapport med agent-fynd + in-block-fix-förslag
- Efter commit → STOPP-rapport innan push
- Vid blockers eller spec-drift → STOPP direkt

Klas värdesätter strukturerade STOPP-rapporter över "go ahead"-autonomi för Fas 1-arbete.

---

**Lycka till. Block A-arbetet visar att TDs-cleanup går snabbt när scope hålls tight. Min rek: ta TD-44 först (säkerhets-relaterat, bygger direkt på A3-pattern), sedan TD-45 (snabbt a11y-fix), sedan ev. TD-46 om tid finns.**
