# Security Audit — STEG 6 Frontend (/ansokningar)

**Status:** Godkänd med reservationer — 0 Blockers, 3 Majors, 4 Minors  
**Datum:** 2026-05-08  
**Auktoritet:** GDPR Art. 5, 6, 32 + CLAUDE.md §5.2, §5.4 + ADR 0017 + ADR 0018

---

## Granskade filer

- `web/jobbpilot-web/src/lib/api/applications.ts`
- `web/jobbpilot-web/src/lib/actions/applications.ts`
- `web/jobbpilot-web/src/lib/actions/application-schemas.ts`
- `web/jobbpilot-web/tests/e2e/helpers/auth.ts`
- `web/jobbpilot-web/tests/e2e/applications.spec.ts`
- `web/jobbpilot-web/src/middleware.ts` (referens)

---

## Blockers

Inga blockers identifierade.

---

## Majors

### Major 1: `coverLetter` / notes — PII-läckage via `body?.detail` (ÖPPEN)

**Fil:** `web/jobbpilot-web/src/lib/actions/applications.ts` (rad 54-55, 92-93, 137-138, 174-175)

Alla fyra Server Actions propagerar `body?.detail` från backend-felsvar direkt till `ActionResult.error`. Om backend returnerar ett ProblemDetails-svar som råkar inkludera cover letter-content eller anteckningsinnehåll i `detail`-fältet vidarebefordras det till klienten.

**Risk:** Medium. Backend-middleware renderar DomainException utan användardata, men mönstret är defensivt svagt. Crosscutting concern — dök upp i kommande AI-features för CV och cover letters.

**Åtgärd:** Ersätt `body?.detail`-genomströmning med statiska felmeddelanden för PII-hanterande endpoints. Backend kan returnera felkoder (enum) som frontenden mappar till svenska strängar.

**Status:** ÖPPEN — tech debt TD-10

---

### Major 2: Middleware skyddar inte `/ansokningar` (FIXAD i detta pass)

**Fil:** `web/jobbpilot-web/src/middleware.ts`

`PROTECTED_PREFIXES` var `["/mig"]` — `/ansokningar` och sub-routes saknade middleware-skydd per ADR 0017 §defense-in-depth.

**Fix:** `PROTECTED_PREFIXES = ["/mig", "/ansokningar"]` tillagd i detta session-pass.

---

### Major 3: E2E-testlösenord hårdkodat + testemail på produktionsdomän (ÖPPEN)

**Fil:** `web/jobbpilot-web/tests/e2e/helpers/auth.ts`

`TEST_PASSWORD = "E2eTestPass123!"` är hårdkodat i källkod. Test-email `test-e2e-{runId}@jobbpilot.se` skapar records med produktionsdomänen.

**Åtgärd (a):** Flytta till `process.env.E2E_TEST_PASSWORD` med `.env.local`-fallback.  
**Åtgärd (b):** Använd `e2e.jobbpilot.invalid` som testdomän (reserverat TLD).

**Status:** ÖPPEN — tech debt TD-11

---

## Minors

- **M1:** `getSessionId()`-funktion dupliceras i `api/applications.ts` och `actions/applications.ts` — extrahera till `session.ts`
- **M2:** `transitionStatusSchema.targetStatus` validerade inte mot enum → fixad till `z.enum(APPLICATION_STATUSES)` i detta session-pass
- **M3:** `env.ts` runtime-validering kan läcka env-variabelnamn i `ActionResult.error` vid saknad konfiguration
- **M4:** `playwright.config.ts` hårdkodar `baseURL` — lägg till `process.env.BASE_URL` för CI-stöd

---

## Godkänt

- Bearer token exponeras aldrig för browsern (`import "server-only"` + `"use server"`)
- `__Host-`-cookie korrekt implementerad: `httpOnly`, `secure`, `sameSite: "strict"`, `path: "/"`
- Zod-validering i alla Server Actions med GUID-regex + maxlängder
- Inga API-anrop från klient-side (server-to-server)
- Ingen PII i loggar i granskade filer
- Open redirect mitigerat i `safeRedirectPath()`
- `BACKEND_URL` exponeras inte till klienten (saknar `NEXT_PUBLIC_`-prefix)
