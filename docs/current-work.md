# Current work — JobbPilot

**Status:** **F2 INGESTION-CRON-VERIFIERING RÖD — FAS 2 FORMELL STÄNGNING FÖRBLIR PAUSAD 2026-05-16 (HEAD `24f9dad` + docs-commits denna session). Snapshot-cron verifierad i CloudWatch (`/aws/ecs/jobbpilot-dev/worker`, deployad `v0.2.8-dev`): `SyncPlatsbankenSnapshotJob: startad [5401]` 7d=`60`, `klart [5402]` 7d=`0` — EXAKT samma "60 starts/0 completes"-symptom som rotorsaken FÖRE v0.2.6-dev, men NY rotorsak: fatal ofångad `System.Text.Json.JsonException: ...reached end of data` vid bytepos 26/41/47 MB → Platsbanken-snapshot-JSON kapas mitt i strömmen → dör före `LogCompleted` → Hangfire `AutomaticRetry`-loop. v0.2.6-dev:s child-scope-per-item fixade 23505-ackumulering men INTE payload-trunkering → defekten oadresserad (andra "falskt fixad"-mönstret i samma pipe). Sekundärt icke-fatalt: `Npgsql 23505` 46 760/24h (≈ hela ~47k-korpusen, child-scope fångar per item) + `Polly RateLimiterRejectedException`. Korpus (autentiserad API): ofiltrerad `/api/v1/job-ads` totalCount=`5 380` (förväntat ~40k+); `q=utvecklare`=`137` oförändrat → ingen full snapshot lyckats; endast `*/10 SyncPlatsbankenStreamJob` (inkrementell) fyller på. **Båda verifieringssteg RÖDA → Fas 2 kan EJ stängas (DoD CLAUDE.md §8 punkt 4).** senior-cto-advisor inline (agentId a5c2b2ca57caee056): (1) Fas 2 FÖRBLIR PAUSAD — mekanisk DoD-konsekvens, ej Klas-GO för pauseringen; (2) rotorsaks-fix = SEPARAT fix-session m/ obligatorisk dotnet-architect-rond + Klas-GO, **INGEN TD** (§9.6-pressad: ej annan fas/ej saknad dependency; Major/Fas-Nu → §9.7 förbjuder TD-kategori) — lever som STOPP-underlag + session-logg + kommande ADR 0032-amendment; (3) runbook-drift-fix gjord in-block (rad 120 `/ecs/jobbpilot-dev-migrate`→`/aws/ecs/jobbpilot-dev/migrate`, family-rader verifierat korrekta orörda); (4) Hangfire retry-storm = Klas-eskalering NU, CTO rekommenderar paus av `sync-platsbanken-snapshot` på dev tills fix (verkställs EJ av CC — Klas-GO + AWS-operatörsåtgärd, manuell trigger är 410 per ADR 0032 Amendment). Ingen egen ingestion-debug/fix påbörjad (Klas-STOPP-flagga + förbud). Se `docs/sessions/2026-05-16-1450-f2-ingestion-verify-red.md`. KLAS-ESKALERINGAR: (a) bekräfta Fas 2 pausad; (b) ingestion-fix egen session — när; (c) pausa snapshot-jobbet på dev nu?**

**(Föregående) F2 SAVED SEARCHES LIVE-VERIFIERAD + a11y ADR 0041 LEVERERAD 2026-05-16 (HEAD `64a6bf8`, deployad `v0.2.7-dev`+`v0.2.8-dev`/Vercel). Auth-gated visuell verifiering KLAR — denna sessions huvudleverans. Deploy `v0.2.7-dev` @ `29cd4ae` (migration `F2SavedSearches` applicerad, CloudWatch EventId 63, /api/ready 200). `visual-verify.ts` utökat med opt-in auth-läge (senior-cto-advisor Variant A): direkt backend-login, `__Host-`-cookie in-memory (aldrig disk, §5.4-risk eliminerad vid källan), temp-fixture-sökning, 3 vp × light/dark. Dedikerat dev-test-konto skapat (Variant C cred-plats `%USERPROFILE%\.jobbpilot\dev-test-creds.env`, utanför repot; runbook+MEMORY-pekare, aldrig creds). design-reviewer→nextjs-ui-engineer auktoritativ token-math→**WCAG 1.4.11 a11y-Blocker bekräftad** i delad `ui/dialog.tsx` (dark dialogyta=dimmad canvas, kant 1.35:1<3:1). senior-cto-advisor Alt 2 + Klas-GO: **ADR 0041 (Accepted)** — nytt semantiskt token `--jp-border-modal` (light `#E2E8F0`/dark `#64748B`=slate-500, ≈3.6:1) + `ui/dialog.tsx` `border-border`→`border-border-modal`. Deployad (Vercel main-push `64a6bf8` + backend `v0.2.8-dev`), live-verifierad: serverad CSS har tokenet, **design-reviewer re-review 0/0/0, Blocker RESOLVED, noll regression**, Klas slutgodkände bilderna. security-auditor PASS (0 Crit/High/Med, 2 Low informativa). Rök-test live grönt: login→create 201→list→**run 200 (paged, totalCount=137 för "utvecklare")**→scoping okänt-id 404 (ADR 0031)→delete 204→borttagen 404. Commits `12fc9e6` (a11y/ADR 0041) + `64a6bf8` (visual-verify auth-läge) pushade; docs-commit denna session. **FAS 2 FORMELL STÄNGNING PAUSAD** — gaten "(a) ingestion-cron verifierad" tillhör separat lokal session (Klas-beslut; EventId 5402 + ~40k+ korpus). `run`=137 träffar visar data finns men full cron/korpus-verifiering är separat spår. ADR 0005-observation: dev-test-kontot skapat via icke-flag-gejtat `/api/v1/auth/register` (kill-switch täcker bara waitlist/invite) — dokumenterad i runbook, CTO+auditor: ej formell TD, triageras i auth-fokuserad touch.**

**(Föregående) F2 SAVED SEARCHES LEVERERAD END-TO-END 2026-05-16 (HEAD `d602968`). Sista oimplementerade Fas 2-leverabeln — Fas 2-milstolpen "söka jobb på Platsbanken + spara sökningar" är FUNKTIONELLT KLAR (modulo ingestion-live-verifiering = separat spår + auth-gated visuell verifiering = pending live-deploy). ADR 0039 (Accepted, Klas-GO): SavedSearch AR + SearchCriteria VO + 6 endpoints JobSeeker-scoped + JobAdSearch delad SPOT-modul (Beslut 1) + run=query/last_run_at→Fas 5 (Beslut 2) + SortBy-i-VO (Beslut 3) + notification lagra-ej-dispatch→Fas 5 (Beslut 4). Klas mid-session-input "smart CV-filter" → ADR 0040 (Proposed, Fas 4+) + BUILD.md §18-backlog (CTO-vägd, gatear ej kod). Backend: 113 tester, Domain 293/App 398/Arch 51/Integration 268 gröna, build 0/0. Frontend: SaveSearchButton(/jobb) + /sokningar + /sokningar/[id] + DeleteSavedSearchDialog, 334 vitest/tsc 0/lint 0. dotnet-architect+CTO(×3) INNAN kod; code-reviewer 0 Block/0 Maj, security-auditor 0 Crit/High/Med, design-reviewer approved (Blocker+2 Minor in-block, re-review OK). OBSERVATION 1→TD-84 (CTO Alt B, projekt-brett, ingen ADR 0031-läcka). Commits: `b82e7cf` ADR 0039, `ae7a521` ADR 0040+BUILD, `b18074f` backend, `717dbd9` TD-84, `d602968` frontend — alla pushade. PENDING: visuell verifiering auth-gated → live-deploy (tag-push=Klas-GO); F2 ingestion-cron-verifiering = separat lokal session (AWS SSO).**

**(Föregående) F2 JOBB-INGESTION ROTORSAK FIXAD + KODKOMPLETT — Commit 1+2+3 + docs pushed 2026-05-16 (HEAD `d454d23`). Snapshot-jobbet 60 starts/0 completes på dev (CloudWatch) pga uncaught Npgsql 23505: hela ~47k-loopen i EN DI-scope → ackumulerad EF-tracker + UnitOfWorkBehavior-SaveChanges bröt ADR 0032 §5 per-command-isolering vid dubbletter. Korpus ~5k av ~47k. Fix: child-scope per item (CTO Variant B, Commit 1 `347b238`) + IAsyncEnumerable-streaming ~300MB OOM-defekt + rate-limiter bounded queue (Commit 2 `70a7c54`) + admin-endpoint avvecklad till 410 (CTO X4, Commit 3 `d454d23`). ADR 0032 §5-clarification + §9-amendment (Klas-GO). 929 tester gröna, build 0/0, code-reviewer 0 Blockers/Majors, CTO+dotnet-architect inline. Cadence: behåll */10 + 0 2 (CTO-rek, Klas-GO). **DEPLOYAD `v0.2.6-dev` (run 25956939801 success, /api/ready 200).** 410-copy korrigerad (ingen Hangfire-dashboard exponerad — Worker headless) + TD-83 lyft (operatörs-yta för Hangfire-jobb, Minor/Trigger). KVARSTÅR: ingen manuell trigger möjlig (ingen dashboard, admin-endpoint 410) → snapshot kör automatiskt via cron **02:00 UTC inatt**; CC verifierar imorgon (CloudWatch EventId 5402 första completionen + `job_ads`-count → ~40k+). HEAD efter copy-fix + docs.**
**(Föregående) UI-REFACTOR DESIGNSYSTEM v2 LEVERERAD 2026-05-16 — civic-utility slate-palett + dark mode (`data-theme`, no-flash, prefers-color-scheme auto), Shell Variant B (sektionerad sidebar, 4px brand-vänsterkant, ADMIN rollgejtad), civic landing, nya `.jp-*`-primitiv. DESIGN.md + 5 skills + 2 agenter → v2. ADR 0037 (Klas-GO). design-reviewer 2 Blockers + 3 Majors åtgärdade in-block. tsc/lint/313 vitest/next build gröna. Ej deployad (tag-push kräver Klas-GO). Öppen punkt: `.jp-h1`/display font-weight-drift jobbpilot.css(500/36px) vs tokens-spec(600/56px) — Klas-auktoritetsbeslut kvarstår.**
**Iteration 2:** broad-screen-centrering + dubbel-login + jobb-separation + post-login-redirect + visual-verify-rutin + TD-82.
**Iteration 3 (ADR 0038 — läsbarhets-omkalibrering):** Klas live-jämförde mot Platsbanken → v2 för litet/tunt. CTO+Klas-GO: GOV.UK-läsbarhetsgolv (brödtext 16px, lede 17, h1/h2/h3 vikt 600, mono data 13/secondary, input 44px, knapp 40, placeholder-exempel borttagna, text-tertiary endast dekorativt). Global token-fix, civic-ledger-form orörd. ADR 0038 (delvis supersession 0037, stänger jp-h1-driften). design-reviewer mot screenshots: ✓ approved 0 blockers.
**Senast uppdaterad:** 2026-05-16 (F2 ingestion-cron-verifiering RÖD — Fas 2-stängning pausad, NY rotorsak: payload-trunkering)
**HEAD:** `24f9dad` + docs-commits denna session (runbook-drift + verify-RÖD session-end)
**Deploy:** `v0.2.8-dev` LIVE på dev-backend (`/api/ready` 200), frontend LIVE på Vercel (www.jobbpilot.se → dev.jobbpilot.se) — F2-frontend + a11y-fix (ADR 0041) deployad & live-verifierad
**Långsiktig bana:** `docs/steg-tracker.md`
**Tech debt:** `docs/tech-debt.md` (aktiva, +TD-80) + `docs/tech-debt-archive.md` (stängda)
**Prod-checklist:** `docs/runbooks/v0.2-prod-launch-checklist.md`

---

## Aktivt nu — F2 live-verifiering + ADR 0041 a11y-fix (levererad 2026-05-16)

Se `docs/sessions/2026-05-16-1430-f2-live-verify-adr0041.md` för full retrospektiv.

| Steg | Innehåll | Status |
|---|---|---|
| 1 | Deploy `v0.2.7-dev` @ `29cd4ae` (Klas-GO) — migration `F2SavedSearches` applicerad (EventId 63), /api/ready 200 | ✅ |
| 2 | `visual-verify.ts` auth-läge (CTO Variant A) + runbook tre-nivå/env-kontrakt + https-guard | ✅ |
| 3 | Dedikerat dev-test-konto + cred-persistens Variant C (utanför repot) + runbook+MEMORY-pekare | ✅ |
| 4 | Auth-gated capture 48 shots × 3 vp × light/dark → design-reviewer | ✅ |
| 5 | a11y-Blocker (WCAG 1.4.11 dark dialog) → ADR 0041 Alt 2 (Klas-GO) → token + `ui/dialog.tsx` | ✅ |
| 6 | Deploy a11y-fix (`v0.2.8-dev` + Vercel) → re-capture live → design-reviewer re-review 0/0/0 RESOLVED | ✅ |
| 7 | security-auditor PASS + rök-test live grönt (create/list/run-137/scoping-404/delete) | ✅ |
| 8 | Commits `12fc9e6`+`64a6bf8` pushade + DESIGN.md-enradare (Klas approve) + docs | ✅ |

**Klas-godkänt:** auth-gated bilderna (`20260516-1424`) slutgodkända; ADR 0041-token-amendment; deploy v0.2.7/v0.2.8-dev; cred-Variant C; DESIGN.md-enradare.

**Fas 2 formell stängning — PAUSAD (medvetet, Klas-beslut):** gaten "(a) ingestion-cron verifierad" tillhör **separat lokal session** (AWS SSO, CloudWatch EventId 5402 + `job_ads`-korpus ~40k+). Auth-gated visuell verifiering (b) + rök-test (c) = **gröna denna session**. `run`=137 träffar bekräftar att data finns, men full cron/korpus-verifiering görs i det separata spåret innan steg-tracker Fas 2 → "Klar".

**Pending operativt:** F2 ingestion-cron-verifiering (separat session). ADR 0005-observation (dev-test-konto via icke-flag-gejtat /register) triageras i auth-fokuserad touch. ADR 0040 (smart CV-filter) detaljdesign vid Fas 4-start. TD-84 vid opportunistisk touch.

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

## Nästa session — LÅST PLAN (Klas-GO för session-start = strategisk transition)

**Samlad session: ingestion payload-trunkerings-fix + F2 sök-yta-omdesign (Klas designbrief vs Platsbanken).** Klas §9.6 p.6-override av CTO-split: B (taxonomi-multiselect) + C (live-typeahead) ingår denna session. senior-cto-advisor (agentId a4318f13a645293cb) + dotnet-architect (a64f2ee9d89379046) plan-design klar. Fortfarande Fas 2 (ej Fas 3). **Fas 2 stängs vid B–E komplett** (Klas-val 2026-05-16 — en samlad stängning när hela sök-visionen live).

**6 linjära commit-batchar, reviewer-pass + STOPP per batch (samlad session ≠ samlad commit-batch):**

| # | Batch | ADR / grind |
|---|---|---|
| 0 | Discovery — verifiera ingestion-rotorsak (CloudWatch byte-offset-varians vs Polly/Timeout-hypotes) + kartlägg sök-kod | Discovery-rapport till Klas, ingen kod |
| 1 | Ingestion-fix (A1/A2/A3 in-session CTO efter Batch 0) | ADR 0032-amendment **STOPP** + deploy + **cron-grön (EventId 5402, korpus ~40k+) hård F2-DoD-gate** |
| 2 | ADR-batch, noll kod | ADR 0042 Accepted + ADR 0039 Beslut 3 superseded **STOPP** |
| 3 | B: `SearchCriteria` single→multi (VO collection-equality + maxantal-invariant + jsonb-datakompat) | architect+test-writer+code-reviewer + **security-auditor BLOCKING** + grön svit |
| 4 | E ("Ny"-tag, `Since`+`IsNew`) sedan D (relevans-sort) | code-reviewer + grön svit |
| 5 | C: typeahead (C1 lokal `job_ads` ILIKE-prefix) | **security-auditor BLOCKING** + db-migration-writer index→**Klas-STOPP** + grön svit |
| 6 | Frontend B–E (kollaps-filter A, multi-select, typeahead, sort, IsNew-badge) | design-reviewer VETO + Klas visuell verifiering |

**Låsta CTO-multi-approach-beslut:** C-källa = **C1** (lokal `job_ads` ILIKE-prefix; C2 JobTech-taxonomi-API avvisat). D-relevans = **D2** (ILIKE-heuristik; D1 tsvector = framtida skala-trigger, dokumenteras i ADR 0042 ej TD). Ingestion **A1/A2/A3 = in-session CTO-rond efter Batch 0-discovery** (A1 frikoppla hämtning/persistens via Infrastructure-buffrad NDJSON = default om timeout-rivning bekräftas).

**ADR-väg:** ingestion → ADR 0032-amendment (samma streaming-beslutsdomän). Sök-IA → **ny ADR 0042**. `SearchCriteria` single→multi → **supersession av ADR 0039 Beslut 3**, beslutet skrivs i ADR 0042 (ej egen ADR 0043). ADR 0039 Beslut 1 (delad JobAdSearch) hålls. ADR 0040 (F = CV-matchning "bra match") **hårt OUT**, ej ens visuell placeholder, endast korsrefererad.

**7 Klas-STOPP:** (1) ingestion-rotorsak+A-variant, (2) ADR 0032-amendment Accepted, (3) ingestion deploy+cron-grön, (4) ADR 0042+0039-supersession Accepted, (5) varje DB-migration (B jsonb om ändrad, C1-index, ev. `CREATE EXTENSION pg_trgm`), (6) security-auditor BLOCKING Batch 3+5, (7) frontend deploy+visuell verifiering. **BUILD.md §18 orörd** (ADR 0042 = beslutskälla).

**Förkrav-blockare innan Batch 1-kod:** ingestion-fix måste vara deployad + cron-verifierad (korpus ~40k+) INNAN B rör samma data-yta — B:s dedupe/identitet kräver riktig korpus, ej 5 380-stympad.

Se startprompt-block i chatten (2026-05-16, ingestion-verify-session-end) + `docs/sessions/2026-05-16-1450-f2-ingestion-verify-red.md`.

---

## Tidigare sessioner (kort)

- **2026-05-13 förmiddag** (denna): TD-73 prod-gating-batch — audit-wire α (ADR 0035) + right-to-erasure (ADR 0032 §8 amendment). 1 commit `c13e1ce`, tag `v0.2.4-dev` deploy success. 33 nya tester. TD-73 stängd; TD-75 + TD-76 lyfta.
- **2026-05-13 morgon:** F2-P8c JobTech Hangfire-jobben + race-säker upsert + 30d-retention. 1 commit `81dfab6`, tag `v0.2.3-dev`. 43 nya tester.
- **2026-05-13 natt:** F2-P8b JobTech Infrastructure-leverans. 5 commits, tag `v0.2.2.1-dev`.
- **2026-05-12 kväll:** F2-P7 + P8a + bootstrap + aggregate-review. 17 commits, 3 nya ADRs.
