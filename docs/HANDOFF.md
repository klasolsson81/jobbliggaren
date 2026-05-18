# HANDOFF — laptop-fortsättning (FAS 3 STOPP 3b mitt i flödet)

> **Engångs-handoff (Klas-auktoriserat §1.5-undantag för dator-byte).**
> **Laptopen: läs detta, fortsätt, RADERA sedan filen** (`git rm docs/HANDOFF.md`)
> i nästa docs-commit så repot hålls rent. Detta är en continuation-prompt,
> inte permanent dok.

Hej. Du tar över en pågående FAS 3-session mitt i STOPP 3b. Allt nedan är
verifierat on-disk/on-origin vid handoff (2026-05-18), inte gissat.

---

## 1. Förkrav (kör först)

1. `git fetch origin && git status` — **förväntat: HEAD = `47a1378` = origin/main, ren working tree** (OK untracked: `.claude/scheduled_tasks.lock`, `docs/reviews/2026-05-17-agent-roster-gap-cto.md`). Om divergens: `git pull` (build-cache-brus `*.lscache` → `git restore`).
2. `git log --oneline -6` → topp: `47a1378 feat(web): FAS 3 STOPP 3b`, `46291c0 feat(applications): FAS 3 STOPP 3a`, `72c3ca8 docs(design): STOPP A`, `64f7a13 STOPP 2`.
3. **KRITISKT cred-förkrav:** visual-verify auth-läge kräver dev-test-kontots creds i `%USERPROFILE%\.jobbpilot\dev-test-creds.env` (utanför repot, per memory `project_dev_test_account`). **Verifiera att filen finns på laptopen** (`Test-Path $env:USERPROFILE\.jobbpilot\dev-test-creds.env`). Saknas den kan visual-verify auth-läge INTE köras — då: skapa filen från din andra dator / runbook `docs/runbooks/frontend-visual-verification.md` §Dev-test-konto, eller flagga till dig själv.
4. Docker (Testcontainers), .NET 10 SDK, Node 22+/pnpm, `dotnet tool restore`, AWS SSO (ej krävs förrän ev. deploy-uppföljning).

## 2. Mandatory reads

- `CLAUDE.md` (hela — särskilt §1.5/§1.6 session/docs-protokoll, §9.2/§9.6 agent-invocation & in-block-vs-TD, §2 Clean Arch/DDD/CQRS).
- `docs/design/ansokningar-redesign-plan.md` (HELA — den auktoritativa planen; §2/§3/§5/§7/§8 L1–L6, Variant A, 3 identitets-tillstånd, §10/§10b STOPP 2/A-utfall).
- ADR `0046` (FAS 3 scope-redefinition — **status Proposed**, Accepted-flip = Grind 1, Klas-STOPP), `0047` (design-reviewer Area 5 flödesmandat — Accepted), `0048` (cross-aggregat-read-join — Accepted).
- `docs/reviews/2026-05-17-fas3-stopp3a-*.md` (architect-design, (D)-fix, divergence-cto 1+2, migration, testflytt, security GO, code-review GO) + `2026-05-17-fas3-stopp3b-code-review.md`.
- `docs/current-work.md` (status) + `docs/steg-tracker.md` rad 32 (Fas 3 = **Pågående 2026-05-17 ⁷**) + fotnot ⁷.
- `docs/sessions/2026-05-17-1800-fas3-batch1-recordfollowupoutcome.md` (Grind 2/3-historik).

## 3. Memory att läsa

Hela `MEMORY.md`. Särskilt: `feedback_cto_decides_multi_approach` (CC ger ej egen rek vid multi-approach — CTO avgör), `feedback_nonstop_with_pr_reports` (non-stop, rapport efter varje push, ej mid-batch-stopp), `feedback_pathspec_commit_parallel_cc` (alltid `git commit -- <paths>`, verifiera `git show --stat HEAD`), `project_dev_test_account` (cred-path, aldrig creds i repo/chat), `feedback_td_lifting_discipline`, `feedback_spec_edit_approve_classifier_block`.

## 4. Var vi är — exakt nuläge

**Strategisk bakgrund:** FAS 3-stängning. Ursprungliga 3 grindar: Grind 3 (DoD §8) GRÖN; Grind 2 (RecordFollowUpOutcome visual-verify) Klas-godkänd; Grind 1 (ADR 0046 Proposed→Accepted) **kvarstår**. Klas underkände dock `/ansokningar` live **2 ggr** (UUID-rader, ingen visuell hierarki, fel status-mönster) → stor strukturerad STOPP-kedja (STOPP 1 discovery → STOPP 2 plan → STOPP A skrivväg + ADR 0048 → STOPP 3a backend → STOPP 3b frontend).

**Levererat & pushat:**
- **STOPP 3a (commit `46291c0`)** — backend-vertikal: `ManualPosting` VO + invariant `JobAdId ⊕ ManualPosting`, EF owned-entity + migration `20260517222003_AddManualPostingToApplications` (4 nullable kol), `CreateApplicationCommand` + `ManualPostingInput` (ingen Source — Klas-struken), 3 read-handlers EN LEFT JOIN job_ads (ADR 0048, (D)-projektion, query-filter-disciplin), `JobAdSummaryDto`, Zod additivt. Gates alla GO (architect/db-migration/test-writer/security GO 0/0/0/0/2Low/code-reviewer GO 0/0/2/CTO-1+2). Full svit **1260/1260**, ADR 0044-golv PASS. **Deployad `v0.2.15-dev` (run `26005514414` success, migration applied, /api/ready 200).** Provider-divergens (EF InMemory ej relationell) löst via CTO-2 väg (B): read-handler-tester flyttade unit→Npgsql-integration.
- **STOPP 3b (commit `47a1378`)** — frontend: `ApplicationRow` ({titel}—{företag}/fallback, StatusDot), `/ansokningar/[id]` (H1, `JobInfoPanel`, `StatusEditCard`+ny Radix `radio-group`, sektionskort, L1–L6, Variant A, destruktiv→v1 Dialog L2), `/ansokningar/ny` (Jobbtitel/Företag obl. + Annonslänk/Sista ansökningsdag, inget Källa-fält). 3 orphaned-filer raderade (application-card/application-status-badge/status-card, §9.6 grep-bevisat). Gates: nextjs-ui-engineer/test-writer (vitest **447/447**)/code-reviewer GO. tsc 0/eslint 0 err. Backend orört.

**EXAKT DÄR DU TAR VID:** `v0.2.16-dev` tag pushad på `47a1378`, **deploy-dev run `26014066232` var `in_progress` vid handoff.** Klas gav GO för denna deploy (STOPP 3b).

## 5. Uppdrag (gör i ordning, non-stop med rapport efter varje push)

1. **Verifiera deploy:** read-only `gh run view 26014066232 --json status,conclusion` → vänta `completed success`; `curl -s -o /dev/null -w "%{http_code}" https://dev.jobbpilot.se/api/ready` → 200. (Frontend-only batch, ingen ny migration — Migrate-steget no-op.) Om deploy failade: STOPP, diagnos, rapportera Klas.
2. **visual-verify auth-läge** mot live (`pnpm visual-verify`, env: `VISUAL_BASE_URL=https://www.jobbpilot.se VISUAL_BACKEND_URL=https://dev.jobbpilot.se` + source creds). **KÄND LUCKA att hantera FÖRST:** `web/jobbpilot-web/scripts/visual-verify.ts` `createApplicationFixture` skapar idag `{jobAdId:null, coverLetter}` → post-3a blir det **tillstånd-3-fallback** ("Ansökan #{kort-id}"), INTE en `ManualPosting`-renderad rad. För att design-reviewer/Klas ska se redesignens primärväg: utöka fixturen att även skicka `manual:{title,company,...}` (→ ManualPosting-rendering) och helst en JobAd-kopplad ansökan (→ {titel}—{företag}). Detta är in-block tooling-fix (samma mönster som Fas 2/3a-utökningarna av visual-verify.ts) — bedöm scope, ev. senior-cto-advisor om multi-approach. Capturera `/ansokningar` (list), `/ansokningar/[id]` (detalj, alla 3 identitets-tillstånd om möjligt), `/ansokningar/ny` (manuella fält), light+dark, 3 viewports.
3. **design-reviewer render-VETO** (ADR 0047 Area 5 — bindande, light+dark+interaktion) mot skärmbilderna + kod. Rapport `docs/reviews/2026-05-18-fas3-stopp3b-area5-veto.md`.
4. **STOPP → Klas live-verifierar** `/ansokningar`-omarbetningen på `v0.2.16-dev` (han har 2 ggr underkänt — detta är den bindande grinden). Presentera skärmbilder + VETO-verdikt + dev-test-konto-path + fixtur-app-id:n. AskUserQuestion: Godkänd / Underkänd-fynd.
5. **Om Klas godkänner `/ansokningar`:** → **Grind 1** = ADR `0046` Proposed→Accepted via **adr-keeper** (ENDAST efter explicit Klas-GO i chatten — Klas-STOPP). Verifiera prosa/index past-tense vid flip.
6. **FAS 3 formell stängning** (CLAUDE.md §1.5): `docs/steg-tracker.md` rad 32 → **Klar 2026-05-18** + uppdatera fotnot ⁷; `docs/current-work.md`; session-log `docs/sessions/2026-05-18-HHMM-fas3-stangning.md`; **docs-keeper**-synk; **`git rm docs/HANDOFF.md`** (denna fil) i docs-commiten; generera nästa-session-startprompt (Fas 4 AI-layer = egen strategisk Klas-GO §9.2, leverera som copy-paste-block i chatten EJ fil).
7. `/jobb` har samma UX-problem men är **separat tråd EFTER `/ansokningar` godkänd** (Klas-direktiv) — påbörja EJ nu.

## 6. Klas-STOPP-flaggor

- **ADR 0046 Accepted-flip = Klas-STOPP** (gör ej utan explicit GO i chatten).
- **Deploy/tag-push = Klas-GO** (auto-mode-klassificeraren blockerar deploy utan tydligt GO i transcript — det är korrekt; be om GO via AskUserQuestion).
- Klas live-verify av `/ansokningar` = bindande grind (2 ggr underkänt).
- Fynd som kräver kod under verifiering → **senior-cto-advisor**-triage (in-block vs TD §9.6), CC ger ej egen rek.
- Default: non-stop med PR-rapport efter varje push (`feedback_nonstop_with_pr_reports`).

## 7. Disciplin (agenter INLINE)

design-reviewer (render-VETO Area 5, bindande) · adr-keeper (Grind 1 ADR 0046-flip vid Klas-GO) · senior-cto-advisor (multi-approach/fynd-triage, decision-maker) · nextjs-ui-engineer (om UI-fynd) · test-writer (om ny kod, TDD FÖRST) · security-auditor (om PII/auth) · code-reviewer (>5 filer) · docs-keeper (session-end). `git commit -- <paths>` enda form, verifiera `git show --stat HEAD`. Coverage-gate ADR 0044 + perf observe-only ADR 0045 orörda. Backend (STOPP 3a) är klar & deployad — rör EJ utan ny anledning.

## 8. Förbud / utanför scope

`/jobb` (separat tråd efter /ansokningar godkänd), AI/Fas 4, Påminnelser/notifikations-infra (Fas 5 per ADR 0046), Avslags-analys (Fas 6), admin-utökning. Ändra ej BUILD.md/CLAUDE.md/DESIGN.md utan Klas approve-spec-edit.sh. Ändra ej `TestAppDbContextFactory` (delas 42 filer — CTO-2 (C) avvisad). Ingen ny top-level-dep utan BUILD.md §3.1 + Klas-GO.

## 9. Pending operativt (ej blocker)

- Dependabot-PR #5 (@types/node) re-merge när CI grön (lågprio).
- Memory `feedback_spec_edit_approve_classifier_block` bör korrigeras (klassificerare = korrekt by-design, ej false-positive).
- ADR 0046 rad 1610 BUILD.md §18-sync (Påminnelser Fas3→Fas5) = separat framtida Fas 5-beslut, gör EJ nu.

## 10. Förväntat sluttillstånd

`v0.2.16-dev` deploy-verifierad; visual-verify-korpus (inkl. ManualPosting-renderad väg) + design-reviewer Area 5-VETO levererad; Klas live-verifierat & godkänt `/ansokningar`; ADR 0046 Accepted (vid Klas-GO); FAS 3 FORMELLT STÄNGD (steg-tracker rad 32 Klar 2026-05-18); docs-synk + `docs/HANDOFF.md` raderad + nästa-session-startprompt (Fas 4 = egen Klas-GO). Inga nya TDs utan §9.6-grund.

## 11. Avslutning

Rapport efter varje push (non-stop, AFK-tolerant). Klas live-verify är bindande Klas-STOPP. Lycka till — backend-tunga delen är klar & grön; kvar är render-verifiering + Klas-godkännande + formell stängning.
