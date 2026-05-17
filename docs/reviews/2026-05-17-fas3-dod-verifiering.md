# FAS 3 — DoD-verifiering mot CLAUDE.md §8 (Grind 3)

**Datum:** 2026-05-17
**HEAD:** `aebbeba` (origin/main, i synk)
**Scope:** FAS 3-stängning — separat Klas-DoD-verifiering av RecordFollowUpOutcome-vertikalen (A) + den befintliga 95%-vertikalen (D), redefinierad scope per ADR 0046.
**Körd av:** Claude Code (lokal Release-svit + `scripts/coverage.sh` + frontend lint/tsc/vitest).

## Sammanfattning

Backend-, frontend-, arkitektur-, coverage-, GDPR-, ADR- och code-review-evidensen är **GRÖN**. Tre §8-punkter (manuell dev-test, Lighthouse, rendered-a11y-screenshots) är strukturellt kopplade till Grind 2 (deploy + `visual-verify`) som kräver Klas-GO för tag-push — ej körbara denna session utan GO.

## CLAUDE.md §8 — punkt för punkt

| # | DoD-krav | Status | Evidens |
|---|---|---|---|
| 1 | Implementerad enligt acceptance criteria (BUILD.md §2) | ✅ | A RecordFollowUpOutcome-vertikal (commit `78d3b14`) + D befintlig vertikal verifierad; scope per ADR 0046 (A+D in, B→Fas5, C→Fas6) |
| 2 | Unit + integration tests, coverage ej sänkt | ✅ | Svit **1191/1191** (0 failed, 0 skipped); baseline 1160 → +31. ADR 0044-golv ALLA PASS (tabell nedan) |
| 3 | Architecture tests gröna | ✅ | `JobbPilot.Architecture.Tests` passed (15s) |
| 4 | Manuellt testad i dev-miljön | ⏳ Grind 2 | Kräver deploy (Klas-GO tag-push) |
| 5 | Lokal Lighthouse > 90 på påverkade sidor | ✅ (CTO-tolkad) | senior-cto-advisor `a45ce5da17a9f56fa` Alt (b): ADR 0045 omdefinierade §8 pt 5 till observe-only publik CWV-fitness-function. Uppfylld via observe-only publik CWV (CI `25999085403` lighthouse-job **success**) + design-reviewer rendered-screenshot-VETO (Grind 2d) + Klas-godkännande — identiskt Fas 2-precedens. Auth-gated Lighthouse-tooling = ej FAS 3-scope, ej TD (skulle tyst flippa ADR 0045 observe-only-ram utan Klas-ratchet). CC vidare utan Klas-STOPP per §9.6 pt 5 |
| 6 | Tillgänglighet (tangentbord + skärmläsare) | ◑ kod-nivå ✅ / rendered ⏳ Grind 2 | Kod verifierad verbatim: `errorId` stabilt per followUpId, `aria-invalid`/`aria-describedby` conditional, `role="alert"`, semantisk `<section aria-label>`, `<h2>`, sv-SE-locale, ingen emoji/utropstecken. Rendered-screenshot = design-reviewer VETO (Grind 2) |
| 7 | Domain events dokumenterade | ✅ | `FollowUpOutcomeRecordedDomainEvent(ApplicationId, FollowUpId, FollowUpOutcome, OccurredAt)` — audit-paritet FollowUpAdded (ADR 0022); dokumenterat i session-log + ADR 0046 Beslut 3 |
| 8 | GDPR-konsekvenser bedömda | ✅ | security-auditor GO **0 GDPR** — ingen ny PII (Outcome/OutcomeAt fanns sedan Fas 1), soft-delete-filter `f.DeletedAt is null`, event bär endast ID + enum |
| 9 | ADR skriven | ✅ | ADR 0046 (Proposed — Accepted-flip = Grind 1, Klas-GO) |
| 10 | Code-review genomförd | ✅ | code-reviewer GO 0/0/0 · security-auditor GO 0/0/0/0/1Low · design-reviewer APPROVED kod-nivå 0/0/1 |

## ADR 0044 per-lager-golv (lokal `scripts/coverage.sh`, Release)

| Assembly | Metric | Golv | Lokal | |
|---|---|---|---|---|
| Domain | line / branch | 93 / 91 | **95.4 / 93.2** | ✅ |
| Application | line / branch | 95 / 89 | **97.8 / 91.2** | ✅ |
| Infrastructure | line | 82 | **84.0** | ✅ |
| Api | line | 91 | **93.7** | ✅ |
| Worker | line | observe-only (ADR 0044 B4/5) | 30.7 | n/a — ingen gate |

Global first-party (ej gejtad, trend/audit): line 92.2% · branch 84.7% · method 90.3% (≈ baseline 92.1/84.5/90.2, marginellt upp).

> Worker 30.7% lokalt < session-loggens kontext — förväntad lokal-vs-CI-attributionsskillnad för Worker.IntegrationTests; **observe-only Fas 1, ingen gate, ej blocker** (ADR 0044 Beslut 4/5).

## Frontend §8

- ESLint: 0 errors (3 pre-existing warnings — dokumenterade, ej regression)
- `tsc --noEmit`: clean
- vitest: **389/389** (35 filer)

## Perf (ADR 0045)

Observe-only Fas 1 — orört. Lighthouse-CI lokal körning mot påverkade sidor = Grind 2 (auth-gated, samma deploy-blocker).

## Slutsats

Grind 3 grön för allt som inte kräver deploy. Återstår före formell Fas 3-stängning:

- **Grind 1** — ADR 0046 Proposed→Accepted (explicit Klas-GO, Klas-STOPP).
- **Grind 2** — deploy (Klas-GO tag-push) → `pnpm visual-verify` auth-läge light+dark → Lighthouse → design-reviewer screenshot-granskning → Klas slutgodkänner (§8 pt 4/5/6-rendered faller ut här).
