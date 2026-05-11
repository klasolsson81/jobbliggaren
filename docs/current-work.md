# Current work — JobbPilot

**Status:** **Batch A STÄNGD OCH PUSHED 2026-05-11 — TD-10 + TD-11 stängda + TD-30 retroaktivt arkiverad.** Fas 1-rensning påbörjad enligt batching-plan (6 batches). Två nya TDs lyfta (TD-63 ActionResult kind-union för writes, TD-64 i18n omnibus).
**Senast uppdaterad:** 2026-05-11
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md` (aktiva) + `docs/tech-debt-archive.md` (stängda)

---

## Aktivt nu — Batch A klar, väntar Klas-prioritering för Batch B

Stationär-CC-session 2026-05-11 ~20:00–21:00. Tre arbets-block:

1. **Plan-leverans:** TD-batching-plan för Fas 1-rensning — 6 batches (A–F) + parallell-spår TD-30
2. **Batch A STÄNGD** (commit `0560718`) — TD-10 + TD-11 frontend-säkerhet
3. **TD-30 retroaktivt arkiverad** — Klas-discovery: jobbpilot.se redan köpt + ACM-cert validerat 2026-05-10 + ADR 0027 supersession finns. TD-30 stod kvar i aktiv-listan trots leverans.

### Batch A-leverans (commit `0560718`)

**TD-10 (Major, GDPR Art. 5(1)(f)):** PII-läckage via `body?.detail` / `body?.title` borttagen från 10 Server-Action-sites i `applications.ts` / `me.ts` / `resumes.ts`. Ny helper `_action-error.ts` mappar HTTP-status → svensk text utan att läsa body. Säkerhetsinvariant verifierad: `res.json` anropas aldrig på error-path.

**TD-11 (Major, test-isolation):** E2E-helper härdad — `TEST_USER_PASSWORD` env-var, test-domän `@e2e.jobbpilot.test` (RFC 6761 reserverad TLD, non-resolvable), `assertSafeBaseURL`-guard via URL-hostname-parse på både `loginAs` och `ensureTestUser`.

**Reviews:**
- senior-cto-advisor: Variant B (central helper) över A (per-action) / C (kind-union för writes). Motivering: DRY + SoC + OCP + ADR 0030-symmetri. Variant C lyfts som TD-63.
- code-reviewer: 0 Blocker / 0 Major / 2 Minor / 3 Nit. Minor-1 (URL-substring-bypass) + Nit-1 (DRY 409+422) + Nit-2 (doc-precision) fixade in-block.
- security-auditor: Approved. GDPR-veto passerad utan blocker. TD-10 + TD-11 stängningskriterier uppfyllda.

### Tester (full svit grön)

| Suite | Antal | Diff |
|-------|-------|------|
| Frontend vitest | **227** | +10 (TD-10 helper-tester) |
| tsc --noEmit | grön | — |
| Architecture.Tests | 32 | oförändrat |

### Pushed commits denna session

| Commit | Scope |
|--------|-------|
| `0560718` | `feat(web): Batch A — TD-10 + TD-11 frontend-säkerhet (GDPR Art. 5(1)(f) + test-isolation)` |

### Nya TDs lyfta

- **TD-63** (Minor, Fas 2+): ActionResult kind-union för writes (ADR 0030-symmetri). Variant C-defererad från TD-10 CTO-triage.
- **TD-64** (Minor, Trigger): i18n-migration av inline svenska error-strängar (omnibus).

### TD-30 retroaktiv arkivering

Klas-discovery 2026-05-11: jobbpilot.se redan registrerad, ACM-cert validerat 2026-05-10 (`f72a79d7-...`), STEG 13c HTTPS-flip levererad, ADR 0027 supersession av ADR 0026 dokumenterad. TD-30 stod kvar i aktiv-listan som "Major Nu" trots att alla 8 operativa steg är utförda. Stängd retroaktivt + flyttad till `tech-debt-archive.md`.

**Lärdom:** TD-livscykel-disciplinen (CLAUDE.md §9.7 etablerad 2026-05-11) ska tillämpas redan vid leverans-commit — annars hopar sig "de facto stängda" TDs i aktiv-listan och översiktstabellens sanningshalt bryts.

---

## När nästa session startar

Batch A klar. Återstående Fas 1-batches per TD-batching-plan:

### Batch B (Major + Minor): TD-41 + TD-57 — UI-konvention native vs shadcn

**Blockerar:** Klas-beslut behövs. Tre varianter:
- (a) Behåll native, lyft inline-stilen till `ui/native-select.tsx`-primitiv (DRY-fix)
- (b) Migrera till shadcn `Select` med RHF Controller (full konsistens)
- (c) Hybrid: shadcn för >2-opt, native för 2-opt (kräver dokumenterad gräns)

Efter Klas-beslut: senior-cto-advisor motiverar val → CC implementerar.

### Batch C (Minor cluster): TD-1 + TD-2 + TD-40 — a11y-pass Fas 1

Mekanisk, ~0,5 CC-session. Skip-link + CardTitle heading + path-equality regression-test.

### Batch D (Minor cluster): TD-3 + TD-4 + TD-5 — UX-pass /mig

Samma fil. ~0,5 CC-session. Behöver design-beslut för TD-3 (stum vs guidance) + TD-4 (ta bort vs omformulera label).

### Batch E (Minor): TD-6 + TD-28 — me-flöde säkerhet + observability

~1 CC-session. TD-28 är största post (typed-confirm + re-auth-Server-Action + tester).

### Batch F (Minor solo): TD-12 — backend cross-user isolation test

~0,5 CC-session. Fristående.

### Öppna frågor från plan-leverans (kvarstår)

- **Q1 ADR 0027-luckor (TD-32–TD-36):** allokera retroaktivt / amenda ADR 0027 / lämna som-är?
- **Q2 TD-22/TD-17 operativa apply:** flytta tillbaka till aktiva / runbook-uppgift / ny operativ-TD?

Hanteras vid nästa session-start eller vid första naturliga touch.

---

## Föregående session-summary (referens)

**2026-05-11 ~16:00–20:00:** TD-7 + TD-53a + TD-53b stängda. ADR 0020 (Zod-DTO-validering) + ADR 0030 (ApiResult kind-union) etablerade. CLAUDE.md §9.6 policy-skift (4h-regel → fas-regel + CTO-auto-follow) + §9.7 TD-livscykel.

**2026-05-11 ~20:00–21:00:** Batch A (TD-10 + TD-11) stängd + pushed. TD-30 retroaktivt arkiverad. TD-63 + TD-64 lyfta.

---

## Pre-existing infra (oförändrat)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` |
| API task-def | `jobbpilot-dev-api` |
| Worker task-def | `jobbpilot-dev-worker` |
| Tag (senaste) | `v0.1.2-dev` på SHA `7cde3c7` |

---

## Workflow-disciplin (CLAUDE.md §9.6 + §9.7)

1. Discovery först
2. Multi-approach-val → senior-cto-advisor auto-invokeras (denna session: TD-10 Variant B beslut)
3. **CC går direkt till implementation efter CTO-beslut** — Klas-STOPP endast vid strategiska frågor
4. Agent-reviews parallellt vid relevant scope (denna session: code-reviewer + security-auditor)
5. **In-block-fix-default per fas-regel** — reviews-fynd fixades in-block (assertSafeBaseURL URL-parse, DRY 409+422)
6. TD-livscykel: nya TDs läggs i `tech-debt.md` med korrekt Severity × Fas-placering. Stängda flyttas till `tech-debt-archive.md` med full kropp + stängningsbevis. **Lärdom TD-30:** flytta i samma commit som leveransen.
