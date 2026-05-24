# Security-audit — F6 P5 Punkt 4 `/oversikt`

**Datum:** 2026-05-24
**Agent:** security-auditor
**AgentId:** `a11074672eb69e526`
**Verdict:** APPROVED WITH CONDITIONS
**Scope:** Frontend-only (page.tsx, oversikt-page.tsx, notice-list.tsx, notice-row.tsx, today-card.tsx, summary.tsx, summary-row.tsx, mock-data.ts, aggregations.ts, app-shell.tsx, globals.css)

## Severity-räkning

| Severity | Count | Status |
|---|---|---|
| Block | 0 | — |
| Critical | 0 | — |
| High | 0 | — |
| Major | 2 (M-1 + M-2) | Båda preexisting; 1 in-block-fix, 1 TD-lyft |
| Minor | 2 (m-1 + m-2) | Båda preexisting / cosmetic |
| Praise | 14 | Solid defensiv kod |

## Major

### M-1 — 5 av 6 anropade endpoints saknar rate-limit (PREEXISTING)

`/oversikt` triggar `Promise.all` mot 6 endpoints. Endast `/api/v1/job-ads` har `ListReadPolicy`. Övriga 5 (`/me/profile`, `/applications/pipeline`, `/me/saved-job-ads`, `/me/recent-searches`, `/resumes`) är auth-gated men saknar policy.

**Pre-existing.** Ej introducerat av denna leverans. Men `/oversikt` skapar 6x request-amplifikation per sidladdning, höjer DoS-yta-multiplikatorn för kompromissat konto (OWASP API4:2023).

**Åtgärd:** TD-lyft Fas 1 (`TD-92`) — fas-tillhörig, inom Fas 6 P5. CC press:ar mot §9.6: funktion-dependencies finns (alla 5 endpoints + ListReadPolicy existerar); fixet är trivial `.RequireRateLimiting(ListReadPolicy)`-chain. **Kvalificerar för TD** eftersom (a) fyndet är preexisting och fas-spridd, (b) policy-val per endpoint kräver dotnet-architect-rond (är `ListReadPolicy` rätt för pipeline-yta?), (c) scope-spridning över 5 BE-filer är inte rimligt att amplifiera i ren frontend-PR.

### M-2 — `/oversikt` saknas i `middleware.ts PROTECTED_PREFIXES` (defense-in-depth-lucka)

Per ADR 0017: middleware blockerar oautentiserad noise innan request når BE. `PROTECTED_PREFIXES = ["/installningar", "/mig", "/ansokningar", "/cv"]` saknar `/oversikt` (och pre-existing: `/jobb`, `/sokningar`, `/sparade`).

**Effekt:** Inte en bypass — auth-skyddet håller via `getServerSession()` + `redirect("/logga-in")`. Men slöseri (en BE-runda per unauth-request) och inkonsistens.

**Åtgärd (C1, in-block):** Lägg `/oversikt` till `PROTECTED_PREFIXES`. Adressera även pre-existing `/jobb`, `/sokningar`, `/sparade` i samma diff (CTO-godkänd defensive-in-depth-konsekvens).

## Minor

### m-1 — Inga säkerhetsheaders i `next.config.ts` (PREEXISTING)

Saknar CSP/X-Frame-Options/X-Content-Type-Options/Referrer-Policy. Next.js sätter ändå `Cache-Control: private, no-store` automatiskt för `force-dynamic`-routes. Ej akut för Punkt 4. Hör till Fas 2+ när CSP-policy designas mot AI-features.

### m-2 — `.claude/settings.json` whitespace-diff (cosmetic)

En tom rad insatt av sub-agent. Ingen hook avaktiverad, ingen `core.hooksPath`-manipulation, ingen permission-utvidgning. **Icke-malicious.** Per memory `feedback_subagent_hook_bypass_watch`: ALDRIG committas. Stäng utan eskalering.

## Praise (utvalda)

- `__Host-jobbpilot_session`-cookie best-practice (HttpOnly/Secure/SameSite=Strict)
- `safeRedirectPath()` blockerar `//`/`/\`-injection
- Mid-render unauthorized-check defenderar mot token-expiry mellan layout+page
- `force-dynamic` + `cache: "no-store"` blockerar PII-cache-läckage
- `useSyncExternalStore` + SSR-safe snapshot — korrekt hydration utan window-leak
- localStorage-key `jp-oversikt-dismissed-notices` lagrar bara hårdkodade slugs — ingen PII, inga tokens
- JSON.parse + type-guard skyddar mot prototype-pollution
- Ingen `dangerouslySetInnerHTML`; JSX auto-escapar
- Ingen ny BE-yta — CTO-disciplin Variant A hållen

## GDPR-bedömning

- **DPIA-impact:** Nil. Ingen ny PII-lagring, ingen ny telemetri, inget cross-region-flöde
- **Art. 5:** Renderad PII minimerad (email-prefix-fallback om displayName saknas)
- **Art. 32:** Cookie + force-dynamic + per-user-render-isolation OK
- **Art. 17:** localStorage rensas via browser-data-clear; inga PII-slugs

**GDPR-veto: EJ aktiverat.**

## Conditions för APPROVED

1. **C1 (in-block):** `/oversikt` läggs till `middleware.ts PROTECTED_PREFIXES` i samma commit-batch
2. **C2 (TD-lyft):** Skapa TD-92 för rate-limit på 5 endpoints (Major × Fas 1, fas-stängning innan F6-stängning)
3. **C3:** `.claude/settings.json` committas EJ

Re-review krävs ej om C1 implementeras som single-line-fix.

---

*Sparad per CLAUDE.md §9.2. C1 + C2 + C3 hanteras i samma commit-batch.*
