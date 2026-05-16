# Current work — JobbPilot

**Status:** **F2 JOBB-INGESTION ROTORSAK FIXAD + KODKOMPLETT — Commit 1+2+3 + docs pushed 2026-05-16 (HEAD `d454d23`). Snapshot-jobbet 60 starts/0 completes på dev (CloudWatch) pga uncaught Npgsql 23505: hela ~47k-loopen i EN DI-scope → ackumulerad EF-tracker + UnitOfWorkBehavior-SaveChanges bröt ADR 0032 §5 per-command-isolering vid dubbletter. Korpus ~5k av ~47k. Fix: child-scope per item (CTO Variant B, Commit 1 `347b238`) + IAsyncEnumerable-streaming ~300MB OOM-defekt + rate-limiter bounded queue (Commit 2 `70a7c54`) + admin-endpoint avvecklad till 410 (CTO X4, Commit 3 `d454d23`). ADR 0032 §5-clarification + §9-amendment (Klas-GO). 929 tester gröna, build 0/0, code-reviewer 0 Blockers/Majors, CTO+dotnet-architect inline. Cadence: behåll */10 + 0 2 (CTO-rek, Klas-GO). **DEPLOYAD `v0.2.6-dev` (run 25956939801 success, /api/ready 200).** 410-copy korrigerad (ingen Hangfire-dashboard exponerad — Worker headless) + TD-83 lyft (operatörs-yta för Hangfire-jobb, Minor/Trigger). KVARSTÅR: ingen manuell trigger möjlig (ingen dashboard, admin-endpoint 410) → snapshot kör automatiskt via cron **02:00 UTC inatt**; CC verifierar imorgon (CloudWatch EventId 5402 första completionen + `job_ads`-count → ~40k+). HEAD efter copy-fix + docs.**
**(Föregående) UI-REFACTOR DESIGNSYSTEM v2 LEVERERAD 2026-05-16 — civic-utility slate-palett + dark mode (`data-theme`, no-flash, prefers-color-scheme auto), Shell Variant B (sektionerad sidebar, 4px brand-vänsterkant, ADMIN rollgejtad), civic landing, nya `.jp-*`-primitiv. DESIGN.md + 5 skills + 2 agenter → v2. ADR 0037 (Klas-GO). design-reviewer 2 Blockers + 3 Majors åtgärdade in-block. tsc/lint/313 vitest/next build gröna. Ej deployad (tag-push kräver Klas-GO). Öppen punkt: `.jp-h1`/display font-weight-drift jobbpilot.css(500/36px) vs tokens-spec(600/56px) — Klas-auktoritetsbeslut kvarstår.**
**Iteration 2:** broad-screen-centrering + dubbel-login + jobb-separation + post-login-redirect + visual-verify-rutin + TD-82.
**Iteration 3 (ADR 0038 — läsbarhets-omkalibrering):** Klas live-jämförde mot Platsbanken → v2 för litet/tunt. CTO+Klas-GO: GOV.UK-läsbarhetsgolv (brödtext 16px, lede 17, h1/h2/h3 vikt 600, mono data 13/secondary, input 44px, knapp 40, placeholder-exempel borttagna, text-tertiary endast dekorativt). Global token-fix, civic-ledger-form orörd. ADR 0038 (delvis supersession 0037, stänger jp-h1-driften). design-reviewer mot screenshots: ✓ approved 0 blockers.
**Senast uppdaterad:** 2026-05-16 (UI-refactor v2 + iter 2 + iter 3 ADR 0038)
**HEAD:** `261ea12`/`661e72d`/`b97cda2`/`bc5b712`/`2d8effd` + iter-3-commits
**Deploy:** `v0.2.5-dev` LIVE på dev-backend, frontend LIVE på Vercel (www.jobbpilot.se → dev.jobbpilot.se) — v2-frontend ej deployad än
**Långsiktig bana:** `docs/steg-tracker.md`
**Tech debt:** `docs/tech-debt.md` (aktiva, +TD-80) + `docs/tech-debt-archive.md` (stängda)
**Prod-checklist:** `docs/runbooks/v0.2-prod-launch-checklist.md`

---

## Aktivt nu — UI-refactor designsystem v2 (levererad 2026-05-16)

Se `docs/sessions/2026-05-16-ui-refactor-designsystem-v2.md` för full retrospektiv.

| Steg | Innehåll | Status |
|---|---|---|
| 1 | Token-migrering: slate `--jp-*` (light+dark), `@custom-variant`, `@theme inline`, JetBrains Mono, density, full `.jp-*`-utilities | ✅ |
| 2 | DESIGN.md v2 (Klas-GO) + 5 skills + 2 agenter | ✅ |
| 3 | ThemeProvider Variant A (CTO) — no-flash, `useSyncExternalStore`, noll deps | ✅ |
| 4 | Shell Variant B — sektionerad sidebar, ADMIN rollgejtad (beslut A) | ✅ |
| 5 | Civic landing (`(marketing)/page.tsx`) | ✅ |
| 6 | Primitiv: status-dot/pill/match-bar, delad theme-toggle, shadcn-align | ✅ |
| 7 | Ledger-restyle /jobb /ansokningar /cv /mig /admin/granskning | ✅ |
| 8 | ADR 0037 + docs + commit `261ea12` + push | ✅ |

**Öppen punkt (Klas):** `.jp-h1`/`.jp-h2`/display font-weight + display-storlek-drift — `jobbpilot.css` (verbatim-implementerad, 500/36px) vs `tokens-full.md`/DESIGN.md §4 (600/56px). Kräver auktoritetsbeslut innan v2 stängs; ej blockerande.

**Pending före v2-deploy:** visuell browser-QA light+dark (`pnpm dev`); ev. Vercel-deploy (tag-push = Klas-GO).

---

## Arkiv — Vercel-deploy 2026-05-14

### Levererat (5 commits, 1 Klas-cleanup)

| Commit | Innehåll | Effekt |
|---|---|---|
| `cbe4a10` | Vercel DNS-records (apex A 216.198.79.1 + www CNAME projekt-specifik + CAA Let's Encrypt) — Terraform applied i prod/baseline | DNS pekar mot Vercel ✅ |
| `25aa476` | Ta bort pnpm-workspace.yaml + flytta ignoredBuiltDependencies till package.json's pnpm-field | Hypotes-test (fel orsak) men hygienförbättring behållen |
| `9d0eae4` | next build/dev --webpack flag (force Webpack istället för Turbopack-default) | Hypotes-test (fel orsak) men säkerhetsmarginal behållen |
| `fcfe710` | **vercel.json med "framework": "nextjs"** | **LÖSNINGEN** ✅ |
| (Klas UI 00:50) | Dashboard Framework Preset = Next.js (defense-in-depth match) + radera oönskat `jobbpilot-web`-projekt | Cosmetic cleanup |

### Root cause — `framework: null` i Vercel project settings

Avslöjad av CTO-godkänd diagnos via lokal `vercel pull` + inspektera `.vercel/project.json`. När projektet skapades via "New Project"-flödet i UI valdes inte Application Preset = Next.js explicit (Klas noterade dropdown:n "försvann"). Vercel-platform-side hade `framework: null` → routing-tabellen registrerades inte som Next.js → ALLA URLs gav 404 NOT_FOUND oavsett auth/build-bundler/workspace-config.

### CTO-rond 2026-05-13 kväll — diagnos först (entydigt mot principer)

CTO valde Gemini-approach (systematisk diagnos) över ChatGPT (delete-project först). Motivering: Saltzer/Schroeder Fail-Safe Defaults + Beck TDD-spirit + CLAUDE.md §9.4 Discovery + YAGNI.

### End-to-end verifierat (Klas screenshots 00:50 2026-05-14)

| URL | Status | Fungerar |
|---|---|---|
| `jobbpilot.se` | 301 → www | ✅ |
| `www.jobbpilot.se/` | 200 LandingPage | ✅ (designsystem-demo, behöver login/register-CTA) |
| `www.jobbpilot.se/logga-in` | 200 | ✅ |
| `www.jobbpilot.se/mig` | 200 | ✅ Klas profil + Admin-roll |
| `www.jobbpilot.se/admin/granskning` | 200 | ✅ Audit-logg LIVE med System.JobAdsSynced cron-events |
| `www.jobbpilot.se/jobb` | 200 | ✅ **3391 jobbannonser från Platsbanken** |
| `www.jobbpilot.se/api/me` | 401 (utan auth) | ✅ Backend-koppling fungerar |

### Disciplinmissar + lärande

3 misslyckade hypoteser innan datadriven diagnos (auth, pnpm-workspace, Turbopack). ~2h Klas-tid på gissningar.

**Lärande:** `vercel pull` + inspektera `.vercel/project.json` är obligatorisk första-diagnos vid Vercel-konstigheter. Settings-mismatch mellan dashboard och vad CC ser från utsidan är osynlig utan det steget.

### TD-status

- **TD-81** lyft 2026-05-14 — Minor Trigger — middleware.ts → proxy.ts (Next.js 17-uppgradering). Källa: Vercel-deploy-session build-warning. Risk i nuläget noll, hanteras vid Next.js 17.

Aktiva: 22 (TD-13 Major Fas 2 + TD-26 Major Fas 4; resten Minor).

### Pending operativt för Klas

- **Landing-page-CTA** (Klas observation 00:48): `(marketing)/page.tsx` är design-system-demo, saknar "Logga in" + "Anmäl till väntelistan"-knappar. Civic-utility-MVP-krav.
- **Backend prod-stack-bring-up** (ADR 0036 D1) — Fas 7-prep, frontend pekar på dev-backend tills dess
- AWS SSO-token-livslängd, JobTech-API-key, BUILD.md §9.1 sync — kvarstår

### Nästa session — Klas-val

1. **Landing-page-CTA-fix** (snabb, civic-utility-MVP-blocker)
2. **F2-P11 / nästa Fas 2-feature** TBD
3. **v0.2-prod-tag-prep** (TD-13 PII-encryption är enda kvarstående Major Fas 2, CTO confirmed defer 2026-05-13)
4. **OIDC-drift-städning** (pre-existing 2 change-poster i prod/baseline-Terraform, fix opportunistiskt)

---

## Tidigare aktivitet — TD-80 STÄNGD (JobAd.Url scheme-whitelist)

### Levererat

| Område | Innehåll |
|---|---|
| `JobAd.cs` ValidateCore | Whitelist via `Uri.UriSchemeHttp`/`UriSchemeHttps`-konstanter (default-deny per Saltzer/Schroeder + OWASP A01:2021). Skydd genom alla 3 entry-points (Create/Import/UpdateFromSource) som delar `ValidateCore` |
| Tester FIRST (TDD) | 17 nya unit-tester (4 Theory-metoder med 13 InlineData-cases): http/https/uppercase positive + javascript/JAVASCRIPT/data/vbscript/file/ftp/gopher negative + UpdateFromSource state-bevarande post-fail |
| `UpsertExternalJobAdCommandHandler` | Ingen ändring krävdes — befintlig `Skipped`-flow (rad 53-57 + LogSkippedValidation) hanterar Import-failure rent. Worker sync-jobb propagerar `skipped++` i metrics |

### CTO-rond — skippad

Beslutet entydigt mot Saltzer/Schroeder 1975 default-deny + OWASP A01:2021 whitelist-rekommendation. Ingen multi-approach-fråga (whitelist > blacklist är etablerad princip; `Uri.UriSchemeHttp`-konstanter är idiomatisk .NET-form).

### Reviewers INLINE

| Reviewer | Verdict |
|---|---|
| security-auditor (re-audit av egen Blocker) | Approved 0/0/0 — defense-in-depth komplett, alla 3 entry-points skyddade, persistens säker via Worker `Skipped`-flow |
| code-reviewer | Approved 0/0/0 — typsäkra konstanter, korrekt nullable-flow, [Theory]+[InlineData] DRY, state-bevarande post-fail verifierat |

### Backend full svit grön

| Suite | Pre | Post | Delta |
|---|---|---|---|
| Domain.UnitTests | 225 | **242** | +17 |
| Application.UnitTests | 354 | 354 | 0 |
| Architecture.Tests | 50 | 50 | 0 |
| Api.IntegrationTests | 254 | 254 | 0 |
| Worker.IntegrationTests | 26 | 26 | 0 |
| Migrate.UnitTests | 6 | 6 | 0 |
| **Totalt** | **915** | **932** | **+17 grönt** |

### TD-status

- **TD-80** Major Fas 2 → **STÄNGD 2026-05-13** (flyttad till `tech-debt-archive.md`). Defense-in-depth FE Zod-refine (commit 70e1505) + BE Domain `ValidateCore`-whitelist.

Aktiva: 21 (TD-13 Major Fas 2 + TD-26 Major Fas 4; resten Minor). **0 Major Fas Nu, 0 Major Fas 1.**

---

## Tidigare aktivitet — F2-P10 frontend `/jobb`-katalog UI KOMPLETT

### Levererat (frontend-only batch)

| Område | Innehåll |
|---|---|
| ADR 0030 amendment 2026-05-13 | `rateLimited`-variant förstklassig i `ApiResult<T>` — RFC 9110 Retry-After, default 60s |
| `lib/dto/_helpers.ts` | `rateLimited`-kind + `parseRetryAfter` + `responseToResult` mappning av 429 |
| 5 konsument-pages | ansokningar, ansokningar/[id], cv, cv/[id], mig (renderProfile), admin/granskning — alla med rateLimited-case + civic-utility-copy |
| `lib/dto/job-ads.ts` | Zod-schemas: jobAdStatus/Source/SortBy/Dto + listJobAdsResult + jobAdFilters (regex-defense + URL-scheme http(s)-refine för XSS-skydd) |
| `lib/job-ads/status.ts` | Labels + variant-mappning (Aktiv/Utgången/Arkiverad + 4 sort-options + 4 source-labels) |
| `lib/api/job-ads.ts` | `getJobAds(query)` server-only fetcher → `ApiResult<ListJobAdsResult>` |
| `components/job-ads/` | StatusBadge + Card + List + Pagination (GOV.UK-numeric) + Filters (Client, RHF + manuell safeParse) |
| `app/(app)/jobb/page.tsx` | Server Component, async searchParams (Next.js 16), 6-fall switch + assertNever |
| `app/(app)/layout.tsx` | Nav-länk "Jobb" tillagd (första item) |
| `tests/e2e/jobb.spec.ts` | 7 Playwright-tester (auth-redirect + render + filter-submit + validation + reset + nav) |

### CTO-rond F2-P10 — 4 entydiga beslut

| Q | Beslut | Kort motivering |
|---|---|---|
| Q1 | **A** Utöka `ApiResult<T>` med `rateLimited` | CCP/REP, OCP via assertNever, Saltzer/Schroeder Economy of Mechanism |
| Q2 | **A** URL-driven server-state (router.push) | CLAUDE.md §4.3+§5.2, Fielding HATEOAS, Beck YAGNI |
| Q3 | **A** `JobAdStatusBadge` + `lib/job-ads/status.ts` | REP/CCP, SRP, codebase-konsekvens |
| Q4 | **A** Numeric pagination GOV.UK-stil | civic-utility-konvention, WCAG keyboard-direkthopp, Norman affordance |

### Reviewers INLINE

| Reviewer | Verdict |
|---|---|
| design-reviewer | Approved med 6 Minor (5 pre-existing patterns); Minor 1+2 (badge role=status, dubbel aria-live) fixade in-block |
| code-reviewer | Approved (0/0/3); M1 (kollaps-kommentar) + M2 (badge role=status) fixade in-block; M3 (Card focus-wrap) defererat — gäller framtida `/jobb/[id]` |
| security-auditor | **BLOCKER → fixad** XSS-vektor via `javascript:`-URL i `<a href={jobAd.url}>`. Zod-refine `^https?://` blockar FE-side. **TD-80 lyft** för BE Domain-tightening (annan fas per §9.6 punkt 1) |

### Tester

- vitest: **313/313 grönt** (+29 nya: 23 dto/status/filters/badge/card/list/pagination + 5 nya rateLimited i `_helpers.test.ts` + 1 uppdaterad assertNever-test + 8 URL-scheme-tester efter security-fix)
- `npx tsc --noEmit`: clean
- `pnpm lint`: 0 errors, 3 pre-existing warnings (audit-log-table.test, delete-account-dialog watch, applications.spec applicationId)

### TD-status

- **TD-80** lyft 2026-05-13 — Major Fas 2 — JobAd.Url scheme-whitelist (http/https) i Domain.ValidateInputs (security-auditor F2-P10 split)

Aktiva: 22 (TD-13 + TD-26 + TD-80 Major; resten Minor).

### Pending operativt för Klas

- **Vercel-deploy** för `/jobb` LIVE — egen Klas-op (DNS, env-vars för BACKEND_URL + auth-cookie-domain)
- **Lokal Lighthouse-pass + axe-DevTools** på `/jobb` mot dev-backend — Klas kör manuellt
- AWS SSO-token-livslängd, JobTech-API-key, BUILD.md §9.1 sync mot ADR 0032 §3 — kvarstår

---

## Tidigare aktivitet — D+A-session KOMPLETT (TD-79 + TD-70 stängda)

### Levererat Del A (TD-70 — F2-P9 search/filter)

| Commit | Innehåll |
|---|---|
| `d4294b6` | feat(jobads): F2-P9 search/filter-yta ?ssyk&?region&?q + ListReadPolicy rate-limit (TD-70) |
| Tag `v0.2.5-dev` | Triggered deploy run 25797979739 — 7m success, Phase E migration applied |

**Endpoint:** `GET /api/v1/job-ads?ssyk=<concept-id>&region=<concept-id>&q=<text>` (auth-gated + rate-limited 60/min per UserId)

**CTO-rond:** 11 entydiga beslut (Q1-Q11) + 1 follow-up-triage av security-auditor Major (in-block-rate-limit-fix).

**Reviewers:** dotnet-architect → senior-cto-advisor → db-migration-writer → test-writer → security-auditor (Major: rate-limit → CTO-triage in-block) → senior-cto-advisor (rond 2) → code-reviewer APPROVED 0/0/2/2.

**Tests:** Domain 225 + Application **354** (+31) + Architecture 50 + Api **254** (+14) + Worker 26 + Migrate 6 = **915 grönt (+45 nya)**.

### Levererat Del D (TD-79 pipeline-hygien)

| Commit | Innehåll |
|---|---|
| `94ec84a` | chore(infra): lifecycle.ignore_changes=[task_definition] på ECS api+worker services (TD-79) |

**Plan-output post-fix:**

| Resurs | Pre-fix plan | Post-fix plan |
|---|---|---|
| `aws_ecs_service.api.task_definition` | ~ update | ❌ no-op |
| `aws_ecs_service.worker.task_definition` | ~ :8 → :1 (rollback) | ❌ no-op |
| `aws_ecs_task_definition.api` | -/+ replace | ✓ apply genomförd (revision :13 ny, service ignorerar) |
| `aws_db_parameter_group.this` | ~ apply_method cosmetic | ~ kvarstår (pre-existing, ej TD-79-scope) |

**Live-state efter apply:**
- `jobbpilot-dev-api`: TaskDef `:13` (CI/CD-ägd revision behållen)
- `jobbpilot-dev-worker`: TaskDef `:8` (NOT rolled back to `:1`)
- `https://dev.jobbpilot.se/api/ready` → HTTP 200 OK
- 3 CloudWatch-alarms fortsatt i OK-state
- AdminBootstrap__InitialAdminEmail nu Terraform-ägd i task-def-content (env-var-ägarskap löst)

### CTO-rond 2026-05-13 (v0.2-prod-tag-readiness) — 5 beslut

1. **Q1 v0.2-definition:** Tolkning (c) — första prod-deploy-triggande tag oavsett feature-completeness. Frontend kommer i `v0.2.x`-patch-tags efter. Motivering: Continuous Delivery (Humble/Farley 2010), Fitness Functions (Ford/Parsons/Kua 2017).
2. **Q2 BUILD.md §14.4-alerts:**
   - JobTech-sync 3 consecutive failures → **In-block-fix FÖRE tag** (fas-relevant + observability)
   - Backend 5xx-rate > 1% / 5 min → **TD-77 Fas 8** (YAGNI vid 1-user-volym)
   - DB CPU > 80% / 10 min → **TD-78 Fas 8** (samma logik)
3. **Q3 SystemEventAuditor failure-alarm (EventId 5602) → In-block-fix FÖRE tag** (ADR 0035 §6 egen leveransspec; Art. 30 record-of-processing-kongruens)
4. **Q4 RDS backup-retention:** **14d för prod** (industry-common, EDPB CEF 2025 verifierad acceptans, KISS över 35d-max utan TD-13)
5. **Q5 TD-13 (PII-encryption + crypto-erasure):** **Defer Fas 2-stängning** (EDPB CEF 2025 verifierar standard practice räcker, fas-regel CLAUDE.md §9.6)

### Smoke-test 2026-05-13 — AUDIT-WIRE VERIFIERAD LIVE

CloudWatch Logs Insights mot `/aws/ecs/jobbpilot-dev/worker`:

| Cron-tick | Stream-result | audit_log INSERT |
|---|---|---|
| 08:21:55 UTC | fetched=1029, added=72, errors=0 | ✓ INSERT INTO audit_log (… payload …) |
| 08:30:47 UTC | fetched=1076, added=84, errors=0 | ✓ INSERT INTO audit_log (… payload …) |
| 08:40:41 UTC | (pågående vid query-tid) | ✓ INSERT INTO audit_log (… payload …) |

`SystemEventAuditor` skriver `System.JobAdsSynced` per cron-tick via
idempotens-check + insert. **0 EventId 5602 (Critical audit failure)** i
loggarna. TD-73 audit-wire fungerar i prod-flöde.

### Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13)

- [AWS RDS Backup Retention](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_WorkingWithAutomatedBackups.BackupRetention.html) — default 7d console / 1d API, max 35d
- [EDPB CEF 2025 Report (PDF, 2026-02)](https://www.edpb.europa.eu/system/files/2026-02/edpb_cef-report_2025_right-to-erasure_en.pdf) — automatic overwrite cycles + live-radering acceptabelt; crypto-erasure inte krav
- [Terraform aws_cloudwatch_log_metric_filter](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudwatch_log_metric_filter)
- [Terraform aws_cloudwatch_metric_alarm](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudwatch_metric_alarm) — provider v6.30 stable

### TD-status

- **TD-77** lyft 2026-05-13 — Backend 5xx-rate-alarm, Fas 8 Klass-launch
- **TD-78** lyft 2026-05-13 — DB CPU > 80% alarm, Fas 8 Klass-launch
- **TD-13** Major Fas 2 — bekräftad ej launch-blocker per CTO Q5 + EDPB CEF 2025

Aktiva: 21 (TD-13 + TD-26 Major; resten Minor).

### Pending Klas-GO (in-block-fix-batch FÖRE v0.2-tag)

Per `docs/runbooks/v0.2-prod-launch-checklist.md` §9. Tre leveranser:

1. **CloudWatch-alarm: JobTech-sync 3 consecutive failures** — Terraform-utbyggnad i `modules/cloudwatch_security_alarms` (eller ny `cloudwatch_ops_alarms`-modul)
2. **CloudWatch-alarm: SystemEventAuditor failure (EventId 5602)** — stänger ADR 0035 §6-gap
3. **RDS backup-retention 7d → 14d** — prod-Terraform (dev oförändrad)

**Scope:** 2-3 commits, ~3-4h CC-tid.
**Klas-STOPP-territorium per CLAUDE.md §9.6 punkt 5:** v0.2-definition är strategisk + prod-Terraform-state + tag-push behöver explicit Klas-GO.

### Pending operativt för Klas (sedan tidigare)

- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API)
- Frontend-deploy till Vercel (kommer i v0.2.x-patch efter v0.2)
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Tidigare aktivitet (TD-73 prod-gating-batch — komplett)

### Tidigare commits

| Commit | Innehåll |
|---|---|
| `c13e1ce` | feat(jobads): TD-73 prod-gating — audit-wire α + right-to-erasure för rekryterar-PII |

### Granskningstrail

- `docs/sessions/2026-05-13-0730-td73-prod-gating.md` — session-log (skapas i denna session-end)
- Reviewers INLINE: dotnet-architect + senior-cto-advisor + code-reviewer + security-auditor
- Tidigare session: `docs/sessions/2026-05-13-0700-f2-p8c-hangfire-jobs.md`

### Leveranser

| Område | Innehåll |
|---|---|
| **Ny ADR 0035** | System-event audit-pipeline (bypass-port parallell till IAuditTrailEraser). EventType-konvention `System.<Event>`, AggregateType `System.<Aggregate>`. Idempotens-skydd vid Hangfire-retry. Best-effort-semantik vid audit-failure. |
| **ADR 0032 amendment** | §8 punkt 4 levererad: audit-wire via `ISystemEventAuditor` (inte domain-event), Email-only right-to-erasure, Name→TD-75, GIN-index→TD-76 |
| **ADR 0024 cross-ref-amendment** | Pekare till ADR 0035 + ADR 0032 §8 för rekryterar-PII-cascade (separat från ADR 0024 D6 user-cascade) |
| **Domain** | `AuditLogEntry.Payload` + `CreateSystemEvent`-factory (bevarar Guid.Empty-invariant) |
| **Application ports** | `ISystemEventAuditor`, `IRecruiterPiiPurger`, `SystemAuditEvent`-record-hierarki, `RedactRecruiterPiiCommand` (+ validator + enum) |
| **Infrastructure** | `SystemEventAuditor` (idempotens-check via (EventType, AggregateId)-lookup), `RecruiterPiiPurger` (`EF.Functions.JsonContains` + `ExecuteUpdateAsync`), EF-migration `AddAuditLogPayload` |
| **EF-config** | `AuditLogEntryConfiguration.Payload` jsonb-mapping |
| **Worker/Hangfire** | Audit-wire i `SyncPlatsbankenStreamJob` (finally med exception-mask-skydd), `SyncPlatsbankenSnapshotJob`, `PurgeStaleRawPayloadsJob` |
| **Admin endpoint** | `POST /api/v1/admin/job-ads/redact-recruiter-pii` med `RequireAuthorization(Admin)` + `JsonStringEnumConverter` |
| **Architecture-tester** | ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor (Application + Infrastructure) |
| **Runbooks** | `recruiter-pii-erasure.md` (auto-flöde Email + manuell-flöde Name); `gdpr-processing-register.md` uppdaterad |

### Reviewers INLINE (CLAUDE.md §9.2)

| Reviewer | Tidpunkt | Verdict |
|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss approved; 5 multi-approach → CTO |
| senior-cto-advisor | EFTER architect, INNAN kod | 13 beslut entydigt mot principer (Martin/Evans/Fowler/Beck/Saltzer-Schroeder/GDPR). **INGET Klas-STOPP** behövdes per CLAUDE.md §9.6 punkt 5 |
| code-reviewer | EFTER impl, INNAN commit | GO. 0 Blocker, 0 Major, 3 Minor (Minor-1 + Minor-2 in-block-fixade per §9.6; Minor-3 är planerad uppföljning) |
| security-auditor | EFTER impl, INNAN commit | APPROVED-WITH-CONDITIONS. 0 Critical, 0 GDPR-Blocker, 0 Major, 4 Sec-Min (acceptable as-is) |

### CTO-rond 2026-05-13 (TD-73 prod-gating) — 13 beslut

1. **Q1 AggregateId:** Per-run-Guid (via Hangfire jobId-pattern) — OCP-väg framåt
2. **Q2 Erasure-shape:** Total null-out via `SetProperty(_ => null)` — KISS + data-minimisation > debug-värde
3. **Q3 Audit-granularitet:** En aggregerad audit-rad per request — ADR 0024 D4-precedens
4. **Q4 RedactCmd.AggregateId:** Per-request-Guid (RequestId) — följer Q3
5. **Q5 GIN-index:** Defer till TD-76 — YAGNI vid F2-volym
6. **R-Risk1 Atomicitet:** Best-effort + Hangfire retry + idempotens-check + Critical log — Fowler 2018
7. **R-Risk2 Name-matching:** Email-only nu, Name som TD-75 — YAGNI + Art. 17 kräver inte name-identifier
8. **M1 ADR-shape:** Ny ADR 0035 + amendment till ADR 0032 §8 + cross-ref ADR 0024 — Ford/Parsons/Kua immutability
9. **M2 Klas-STOPP-buntning:** INGET Klas-STOPP — entydiga principer i alla 13 frågor
10. **M3 Snapshot-shim:** SyncPlatsbankenSnapshotCommand har redan inte IAuditableCommand — no-op
11. **M4 ICorrelationIdProvider:** Impl-validation räcker
12. **M5 SystemEventAuditor lifetime:** Scoped (matchar IAppDbContext)
13. **M6 Volym:** GIN-defer korrekt även vid sanity-check (5-15k INSERTs/dygn netto)

### Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13)

- [Npgsql 10.0 Release Notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Trailhead Technology — EF Core 10 PostgreSQL Hybrid DB](https://trailheadtechnology.com/ef-core-10-turns-postgresql-into-a-hybrid-relational-document-db/)
- [GitHub Issue #3745](https://github.com/npgsql/efcore.pg/issues/3745) — Contains-regression
- [PostgreSQL Docs 18 — GIN Indexes](https://www.postgresql.org/docs/current/gin.html)
- [pganalyze — GIN Index The Good and Bad](https://pganalyze.com/blog/gin-index)

### Tester (full svit grön)

- Domain.UnitTests: 218 → **225** (+7: CreateSystemEvent-invarianter + Payload-default)
- Application.UnitTests: 307 → **323** (+16: SystemEventAuditor + RedactCommand + Validator)
- Architecture.Tests: 46 → **50** (+4: ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor × Application + Infrastructure)
- Api.IntegrationTests: 234 → **240** (+6: AdminRedactRecruiterPiiTests end-to-end mot Postgres)
- Worker.IntegrationTests: 26 (oförändrat)
- Migrate.UnitTests: 6 (oförändrat)

Totalt backend: 837 → **870 grönt** (+33 nya).

### Disciplinmissar fångade + fixade

1. **Architect föreslog `EF.Functions.JsonContains` i Application-handler** — Clean Arch-brott (Npgsql i Application). Refactor: skapade `IRecruiterPiiPurger` Application-port + Postgres-impl. Samma mönster som `IAuditTrailEraser`.
2. **Architect+arch-test listade `RedactRecruiterPiiCommandHandler` som ISystemEventAuditor-konsument** — fel; handlern är `IAuditableCommand` + går via `AuditBehavior`. Fixad i arch-test + ADR 0035 §7 docs-not.
3. **Stream-job finally-block kunde maska originalexception vid audit-failure** (code-reviewer Minor-1). Fixad in-block med try/catch (CA1031-suppress) + Cwalina/Abrams §7.5-not.
4. **`JsonStringEnumConverter` saknades** för admin-endpoint enum-deserialisering — fixad via `[JsonConverter(typeof(JsonStringEnumConverter<>))]` på `RecruiterIdentifierType`.

### Tag-cykel + deploy

- `v0.2.4-dev` på `c13e1ce` → push 08:13 UTC → deploy run `25786909619`.
- Deploy completion: 08:20 UTC (~6m42s).
- Ready-probe: `https://dev.jobbpilot.se/api/ready` → **200 OK** verifierat efter deploy.

### Smoke-test status — väntar nästa cron-tick

**Pending verifikation:** Nästa stream-cron `*/10` (08:40 UTC) ska skriva
första `System.JobAdsSynced`-raden i `audit_log` via nya `ISystemEventAuditor`.
Verifikation via CloudWatch logs (Worker-task) eller psql mot dev-RDS:

```sql
SELECT event_type, aggregate_type, aggregate_id, occurred_at,
       payload->>'Source' as source,
       payload->>'Fetched' as fetched,
       payload->>'Added' as added
FROM audit_log
WHERE event_type LIKE 'System.%'
ORDER BY occurred_at DESC
LIMIT 5;
```

Förväntad rad: `event_type = 'System.JobAdsSynced'`, payload med counts.

### TD-status

- **TD-73** Major → **STÄNGD 2026-05-13** (flyttad till `tech-debt-archive.md`)
- **TD-75** Minor lyft — Name-baserad rekryterar-PII-radering (Trigger: första Name-begäran)
- **TD-76** Minor lyft — GIN-index på raw_payload jsonb (Trigger: latens >5s eller volym ×10)

Aktiva: 19 (TD-13 + TD-26 Major; resten Minor). **0 Major Fas Nu, 0 Major Fas 2 (gating blockerare borta).**

### Pending operativt (oförändrat sedan P8c)

- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API)
- Frontend-deploy till Vercel
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Nästa session — Klas-val

1. **v0.2-prod-tag-förberedelse:** TD-73 stängd; samla prod-launch-checklist (CloudWatch-alarms, retention-jobb-verifiering, ev. backup-strategi). Inga aktiva Major-blockare kvar i Fas 2.
2. **F2-P9 search/filter-yta** (TD-70): GET `/api/v1/job-ads` med `?ssyk=...&region=...&q=...` per JobTech v2 `occupation-concept-id` + `location-concept-id`.
3. **Frontend-deploy** till Vercel + JobAd-katalog UI.

---

## Tidigare sessioner (kort)

- **2026-05-13 förmiddag** (denna): TD-73 prod-gating-batch — audit-wire α (ADR 0035) + right-to-erasure (ADR 0032 §8 amendment). 1 commit `c13e1ce`, tag `v0.2.4-dev` deploy success. 33 nya tester. TD-73 stängd; TD-75 + TD-76 lyfta.
- **2026-05-13 morgon:** F2-P8c JobTech Hangfire-jobben + race-säker upsert + 30d-retention. 1 commit `81dfab6`, tag `v0.2.3-dev`. 43 nya tester.
- **2026-05-13 natt:** F2-P8b JobTech Infrastructure-leverans. 5 commits, tag `v0.2.2.1-dev`.
- **2026-05-12 kväll:** F2-P7 + P8a + bootstrap + aggregate-review. 17 commits, 3 nya ADRs.
