---
session: Laptop-demo UI-audit + Branch protection + ADR 0065 PR-flöde
datum: 2026-05-25
slug: laptop-demo-audit-and-pr-flow-restoration
status: levereras-i-PR-7
commits:
  - ee87f14 fix(web): laptop-demo audit Medel-4 + Medel-7 — searchblock 1040px + stat-label 11.5px (sista direct-push under ADR 0019)
  - 25998da docs(adr): 0065 — PR-flöde återinfört med CI-gate (superseder ADR 0019, amends ADR 0007) — i PR #7
pr:
  - "#7 docs(adr): 0065 — PR-flöde återinfört med CI-gate (superseder 0019) — https://github.com/klasolsson81/jobbpilot/pull/7"
tags:
  - (inga taggar — denna session är pre-launch-disciplin + audit-fix, inga deploys)
---

# Laptop-demo UI-audit + Branch protection + ADR 0065 PR-flöde

## Sammanfattning

Två sammanlänkade leveranser i samma session:

**A. Laptop-demo UI-audit** inför Klas demo 2026-05-26 på HP EliteBook 850 G8 (1920×1080) via projektor. CC genererade audit-rapport med 12 fynd (3 hög / 6 medel / 3 låg). Klas valde CC:s rek-strategi: ljust demo-läge, fixa Medel-4 + Medel-7 in-block, defer Hög-1/2/3 (dark-mode-blockers) till separat session efter demo.

**B. Klas-direktiv "aktivera classic branch protection nu"** — superseder ADR 0019 (solo direct-push). ADR 0065 skriven, GitHub classic branch protection aktiverad via `gh api PUT`, CLAUDE.md + session-start-template uppdaterade. **Klas-policy:** docs-sync ska alltid ingå i samma PR som scope/issue — inga separata docs-only-PRs.

## Mål

1. Audit-rapport för 1920×1080 laptop+projektor-läsbarhet på alla huvudroutes (publika + auth-gated + gäst).
2. Klas-triage per fynd, fixa godkända inom samma session.
3. Aktivera GitHub classic branch protection på `main` efter Klas-GO.
4. Skriva ADR som superseder ADR 0019 och dokumentera ny PR-regim.
5. Uppdatera CLAUDE.md + session-start-template så framtida sessioner följer PR-flödet.

## Fas-flöde

| Fas | Antal STOPPs | Klas-utfall |
|-----|--------------|-------------|
| Audit Fas 1 (inventering + statisk scan + capture) | 1 | Klas-triage per fynd-tabell |
| Audit Fas 2 (fixar) | 0 (CTO-rond Medel-4 entydig) | "GO enligt rek" → Medel-4 + Medel-7 |
| Direct-push `ee87f14` (sista) | 0 | "push" (innan branch protection) |
| Branch protection-konfig | 1 (AskUserQuestion 4-val) | Alla 4 = Recommended |
| ADR 0065 + supersede 0019 + index | 0 | Mekanisk follow-up av GO |
| PR #7 skapad | 1 | (denna STOPP) |
| CLAUDE.md + template + docs-sync | 1 (Klas-GO + approve-spec-edit.sh) | "GO på uppdatera claude" |

## Klas-STOPP-kedja

- **STOPP 1:** Audit-rapport levererad → Klas: "GO enligt rek" (= ljust läge, Medel-4 + Medel-7, defer Hög-1/2/3)
- **STOPP 2:** Branch protection-frågor → Klas: alla 4 = Recommended (Variant A direct-push först, enforce_admins:true, 0 approvals, linear history)
- **STOPP 3:** PR #7 + CLAUDE.md-edit-förslag → Klas: "GO på uppdatera claude. GO på docs sync samt uppdatera session-start-template.md. Framöver skall docs sync alltid ingå i samma PR som scope/issue."
- **STOPP 4:** Spec-edit-guard blockerade CLAUDE.md → Klas körde `bash .claude/hooks/approve-spec-edit.sh` → "Token skapad"
- **STOPP 5 (denna):** PR #7 utökad med CLAUDE.md + template + docs-sync, väntar Klas-merge efter CI grön

## CTO-domar

### Medel-4 — `/jobb` hero dead-zone (senior-cto-advisor 2026-05-25)

Multi-approach (Variant A / B / C). **Variant A — utöka `.jp-hero__searchblock max-width 760 → 1040px`** vald.

Motiveringar (kortform — full text i `Agent`-rapporten):
- YAGNI + KISS (Hunt/Thomas 1999): en CSS-property löser observerat problem
- SRP (Martin 2017 kap. 7): `.jp-hero__searchblock` har ett ansvar; Variant B blandar in topbar-chips = coincidental cohesion
- Civic-utility-paritet (DESIGN.md §1): 1177:s sökfält + GOV.UK Search är breda; smal söklåda är vibey Linear/Notion-aesthetics
- Demo-timing-disciplin: 1 CSS-property + recapture på 2 viewports är försvarbart 14h före demo; Variant B JSX-rearrangement är inte
- Avvisade: Variant B (SRP-brott + 1280-breakpoint-risk + meta-nav-paritet), Variant C (YAGNI — uppfunnen widget motiverad av layout, inte domän), Variant D (lämna ifred — tom yta i kompositionens hjärta är inte civic-stoicism)

Klas-STOPP ej behövd per CLAUDE.md §9.6 (entydigt motiverad mot principer).

## Leveranser

### A. Laptop-demo audit + fixar

**Audit-rapport:** `docs/reviews/2026-05-25-laptop-demo-ui-audit.md` (272 rader, 12 fynd, route-inventering + statisk scan + screenshot-korpus). Screenshots i `C:/tmp/jobbpilot-audit/20260525-0906/` (68 st: 17 routes × light/dark × full+fold).

**12 fynd:**
- **Hög (3):** B1 dark väntelista-CTA osynlig (pre-existerande F-Pre Punkt 6), M1 vit `.jp-header`-söm på dark (pre-existerande F-Pre Punkt 6), `--jp-border-structural` dark `#44598A` ≈2.6:1 < ADR 0041-amendment-golv 3:1
- **Medel (6):** Medel-4 `/jobb` hero dead-zone, Medel-5 auth-pages tom yta, Medel-6 `/installningar` spacing, Medel-7 stat-label 10.5px < 11.5px-spec, Medel-8 empty-state-inkonsistens, Medel-9 `/oversikt` I-dag-kort layout
- **Låg (3):** Platsbanken-emoji extern data, container 1200px gutter per ADR 0052, dev-test-namn i kicker

**Klas-val:** ljust läge för demo. Fixa Medel-4 + Medel-7 in-block. Defer Hög-1/2/3 till separat session efter demo (för stora för en kväll + CTO-rond behövs).

**Fixar implementerade (`ee87f14`):**
- Medel-4: `globals.css:902` `max-width: 760px → 1040px` (CTO Variant A)
- Medel-7: `globals.css:2574` `font-size: 10.5px → 11.5px` (DESIGN.md §4-spec-golv-möte)

**Verifiering:**
- CSS-bundle-extraction via Playwright: `.jp-hero__searchblock { margin-top: 28px; max-width: 1040px; }` ✓
- Re-capture landing 1280/1920/2560 × light/dark × full+fold (12 st i `C:/tmp/jobbpilot-audit-regress/20260525-0921/`)
- design-reviewer GO (0 Block / 0 Major / 1 Minor: Klas verifierar `/jobb` interaktivt efter deploy, 30s)
- code-reviewer GO (0 Block / 0 Major / 1 Minor pre-existing: `.jp-land-top__stat__label font-weight: 600` vs spec 500 — backlog)
- `pnpm build` PASS
- Commit `ee87f14` direct-pushed (sista direct-push under ADR 0019)

### B. Branch protection + ADR 0065 + CLAUDE.md + session-start-template

**Klas-frågor (4 × AskUserQuestion):**
1. Pending commit-strategi → A: direct-push först
2. Admin-bypass → Inkludera admin (strikt)
3. Required approvals → 0 (solo)
4. Linear history → Ja

**Branch protection aktiverad (`gh api PUT repos/klasolsson81/jobbpilot/branches/main/protection`):**
```
required_status_checks.strict: true
required_status_checks.contexts: ["ci"]
required_pull_request_reviews.required_approving_review_count: 0
enforce_admins: true
required_linear_history: true
required_conversation_resolution: true
allow_force_pushes: false
allow_deletions: false
```

**ADR 0065** (`docs/decisions/0065-pr-flow-restoration-with-ci-gate.md`) — 167 rader. Superseder ADR 0019, amends ADR 0007. Två förändrade premisser sedan 2026-05-07 (ADR 0019:s datum):
1. `ci`-aggregat-jobbet finns nu (`build.yml:419-433` med kommentar *"Gör branch-protection-rules enkla att konfigurera"*) — CI-gating är aktuell möjlighet
2. Pre-launch-tröskel — kvalitets-spärrar via PR-tråd har högre värde än per-PR-overhead

**ADR 0019** status → Superseded by 0065 (Relaterad: ADR 0007 amended). README-index uppdaterad.

**CLAUDE.md uppdaterad** (Klas approve-spec-edit.sh-token + en Write-call):
- §1.5 step 4: docs-uppdateringar = egna logiska commits men i SAMMA PR som scope (per ADR 0065 + Klas-direktiv 2026-05-25)
- §6.1: skyddad default-branch + feature-branches `<type>/<short-slug>` + PR krävs + ci grönt + 0 approvals + enforce_admins + linear history + docs-i-samma-PR
- §6.3: rubrik byts till "PR + CI-gate per ADR 0065"; granskningsspärrar = 6 mekanismer (CI-gate ny som §5); PR-tråden primär granskningstrail
- §9.1 step 8: pusha feature-branch + `gh pr create` + agent-rapporter inline + Klas reviewar i PR-vyn

**session-start-template.md uppdaterad** (`docs/runbooks/`, ej skyddad):
- §"Klas-STOPP-flaggor": "PR-rapport efter varje push" → "STOPP-rapport efter varje PR-push" + spec-edit-flagga nämner approve-script
- §"Disciplin": ny sub-sektion "PR-flöde per ADR 0065" med feature-branch, ci-gate, docs-i-samma-PR, linear history, conversation resolution, STOPP-rapport-PR-URL
- §"Förbud": tillagt "INGA direct-pushes till main", "INGA merge-commits", "INGA separata docs-only-PRs"
- §"Förväntat sluttillstånd": PR-leveransen är explicit; tag-deploy efter Klas-merge
- §"CC-checklist": HEAD-SHA stämmer mot senaste **merged PR**; ny rad för PR-flöde-reflektion
- §"Versionshistorik": 2026-05-25-entry

**docs/current-work.md** + denna session-log committade i samma PR (per nya policy).

**PR #7:** `docs/adr-0065-pr-flow-restoration → main`. Första PR-cykeln under nya regimen.

## Reviews

| Reviewer | Fynd | Resolution |
|----------|------|------------|
| audit Fas 1 (CC själv) | 12 fynd | Klas-triage GO på Medel-4 + Medel-7 |
| senior-cto-advisor | Multi-approach Medel-4 | Variant A (max-width 1040px) — entydig |
| design-reviewer (Medel-4 + Medel-7) | 0 Block / 0 Major / 1 Minor | GO — Klas interaktiv `/jobb`-check post-deploy |
| code-reviewer (Medel-4 + Medel-7) | 0 Block / 0 Major / 1 Minor | GO — pre-existing `font-weight: 600` backlog |

Inga nya `docs/reviews/`-filer i denna session utöver `2026-05-25-laptop-demo-ui-audit.md` (agent-rapporter levererade inline i chatten).

## Commits

| Commit | Beskrivning | PR/branch |
|--------|-------------|-----------|
| `ee87f14` | `fix(web): laptop-demo audit Medel-4 + Medel-7 — searchblock 1040px + stat-label 11.5px` | direct-push till `main` (sista under ADR 0019) |
| `25998da` | `docs(adr): 0065 — PR-flöde återinfört med CI-gate (superseder ADR 0019, amends ADR 0007)` | PR #7 `docs/adr-0065-pr-flow-restoration` |
| (CLAUDE.md + template + docs-sync commits) | Vid push av denna session-log | PR #7 (kommande commits i samma push) |

## Beslut + detours

- **Direct-push först, sen protection** (Klas Variant A): Cleanest cutover. Audit-fixen behöver inte PR-overhead för en redan kvalitetsgodkänd ändring. Sista direct-push är dokumenterad i ADR 0065 §"Implementationsstatus".
- **dev.jobbpilot.se 404 vid audit-capture:** deploy-dev-workflow efter `v0.2.72-dev` ej rullad → första visual-verify-körningen gav 90 blanka 8KB-screenshots (404-html). Pivoterade till www.jobbpilot.se (prod) som auth-mål med dev.jobbpilot.se som backend (visual-verify-mönstret stöder split frontend/backend). Lyckad capture på andra försök.
- **CSS-bundle-verify istället för auth-localhost-capture:** `/jobb` är auth-gated, localhost-cookie kräver Secure → __Host- → https (inte tillgängligt på http://localhost:3000). Bevis-strategi: extrahera den serverade CSS-regeln från dev-server (vilken som helst publik route) och verifiera att `.jp-hero__searchblock { max-width: 1040px }` finns i bundlen. Detta är giltigt bevis för CSS-only-edit; Klas verifierar visuellt efter deploy.
- **Spec-edit-guard blockerade CLAUDE.md fyra gånger:** Klas körde `approve-spec-edit.sh` en gång; jag använde en Write-call (full fil-overwrite) i stället för fyra Edit-calls = en token konsumerad.
- **PR #7 utökad i flera commits istället för separata PRs:** Klas-direktiv 2026-05-25 = docs-sync alltid i samma PR. ADR + CLAUDE.md + template + docs-sync = sammanhängande scope "PR-flow restoration".

## Disciplin

- Inga TDs lyfta. Alla audit-fynd antingen fixade in-block (Medel-4 + Medel-7) eller medvetet deferrade (Hög-1/2/3 = separat session, dokumenterad i audit-rapport §5).
- senior-cto-advisor invokerad för Medel-4 multi-approach per CLAUDE.md §9.6.
- design-reviewer + code-reviewer invokerade innan commit `ee87f14`.
- Klas-STOPP respekterad vid varje övergång (5 STOPPs i sessionen).
- `git commit -- <explicita paths>` använd för alla commits (memory `feedback_pathspec_commit_parallel_cc`).
- Inga `--no-verify` / hook-bypass. När spec-edit-guard blockerade, väntade jag Klas-approve-script (memory `feedback_subagent_hook_bypass_watch` + `feedback_spec_edit_approve_classifier_block`).
- Auto-skapade filer (audit-capture.ts, audit-regress.ts) ej raderade utan GO (memory `feedback_dont_delete_auto_files`).
- Klas-direktiv "framöver docs-sync alltid i samma PR" tillämpat omedelbart (denna PR #7 utökad istället för ny PR).

## Temp-skript (engångs-audit, beslut om bevarande post-demo)

- `web/jobbpilot-web/scripts/audit-capture.ts` — Fas 1 baseline-capture (localhost public + www auth, fullPage+fold)
- `web/jobbpilot-web/scripts/audit-regress.ts` — Fas 2 regress-verify (landing 1280/1920/2560 + CSS-bundle-rule-extraction)

Båda klassade som engångs i sina headers. **Ej committade i PR #7.** Kvarstår untracked i Klas working tree tills triage.

## Pending Klas-operativt

1. **Merge PR #7** efter `ci`-aggregatet grönt — första PR under nya regimen
2. **Verifiera demo `/jobb` på laptop interaktivt** efter prod-deploy av Medel-4-fixen (design-reviewer Minor-1, ~30s check: ingen horisontell scroll, chips-kollision OK)
3. **Beslut om audit-temp-skript** — bevara i scripts/ för framtida audit-cykler eller radera?
4. **Nästa session-start** börjar med `git fetch origin main + git checkout -b <type>/<slug>` (per ny rutin)
5. **Post-demo:** öppna separat session för Hög-1/2/3 (dark-mode-blockers) — kräver CTO-rond + ev. DESIGN.md-token-edit (Hög-3 ADR 0041-amendment-väg)
6. Pending från föregående: deploy-dev stable-verify för `v0.2.72-dev`, post-deploy visual-verify brand-paket

## Nästa

- Klas merge:r PR #7
- Klas demar på laptop 2026-05-26
- Post-demo: Hög-1/2/3-fix-session (egen PR)
- Eventuellt: `/jobb` LIVE-vertikal för gäst (ADR 0005-amendment-väg) eller F4 AI-grind (ADR 0051)
