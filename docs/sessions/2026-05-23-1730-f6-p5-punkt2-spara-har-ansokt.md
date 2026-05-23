---
session: F6 P5 Punkt 2 — Jobbkort Spara + Har-ansökt (ADR 0053+0024-amend)
datum: 2026-05-23
slug: f6-p5-punkt2-spara-har-ansokt
status: LEVERERAD & PUSHAD (4 commits, deploy pending Klas-GO v0.2.58-dev)
commits:
  - "c015918 feat(saved-job-ads): PR1 Del A backend — SavedJobAd-aggregat + EF migration + cascade + API"
  - "4afc081 feat(saved-job-ads): PR2 Del A FE — Zod-mirror + SaveJobAdToggle + SavedJobAdList + /sparade + modal-footer"
  - "a187467 feat(applications): PR3 Del B backend — CreateApplicationFromJobAdCommand + /from-job-ad/{jobAdId}"
  - "1972f47 feat(applications): PR4 Del B FE + ADR-amends — HarAnsoktButton + ADR 0053-amend + ADR 0024-amend"
deploys:
  - "Pending Klas-GO: tag-push v0.2.58-dev → dev-deploy triggar EF migration 20260523154503_AddSavedJobAds automatiskt"
adrs:
  - "ADR 0053 Amendment 2026-05-23 — lyft Spara/Har-ansökt fas-deferral (2026-05-19); knappar Accepted i modal-footer; match-presentation kvar Fas-4-gated"
  - "ADR 0024 Amendment 2026-05-23 — cascade utökad till SavedJobAds per F6 P5 Punkt 2 Del A"
---

# F6 P5 Punkt 2 — Jobbkort Spara + Har-ansökt

**HEAD vid session-end:** `1972f47` (origin/main). **Push-only.** Tag-push `v0.2.58-dev` pending Klas-GO. Pre-push-gates passerat alla 4 push (gitleaks + tester).

## Mål

Punkt 2 av 5-punkts-ordningen (Klas-bekräftad 2026-05-23): leverera jobbkort-toggles "Spara" + "Har ansökt" som lyfter ADR 0053:s 2026-05-19-deferral. F6 P4b `SavedJobAd`-aggregat avblockerad sedan 2026-05-20; "Har ansökt"-flöde via `CreateApplicationFromJobAdCommand` (ADR 0048 read-join-mönster, inte snapshot — bevarar ADR 0048 Beslut d-disciplin).

## CTO-dom (agentId `ad76c06a752275b17` 2026-05-23)

4 multi-approach-val — alla **Variant A**:

1. **SavedJobAd-shape:** fullt aggregate root (paritet `RecentJobSearch`-mönstret från F6 P4a) — inte denormaliserat snapshot, inte EF entitet utan invariant-skydd
2. **Application-from-JobAd:** befintlig `Application.Create` + ADR 0048-join (NO snapshot) — ADR 0048 Beslut d-respekt: cross-aggregat-read-join i query-vägen, ingen write-side-vidgning
3. **API-form:** separat `POST /api/v1/applications/from-job-ad/{jobAdId}` (inte överlast av befintlig `POST /api/v1/applications`)
4. **FE-pattern:** ADR 0053 modal-footer + inline-toast med länk till ny Application — istället för full-route-redirect (modal-paradigm bevaras)

## Vad som levererades per PR

### PR1 (commit `c015918`) — Backend SavedJobAd-aggregat (Del A)

- **Domain:** `SavedJobAd`-aggregate root med strongly-typed ID + `JobSeekerId`/`JobAdId` cross-aggregate-refs, idempotent `Save()`-fabrik, `SavedJobAdSavedDomainEvent`. 5 Domain-tester (invariant-skydd + idempotency).
- **Application:** `SaveJobAdCommand` + `DeleteSavedJobAdCommand` + `ListSavedJobAdsQuery` (DTO-projektion) + ADR 0031 cross-tenant-disciplin (cookie-bound `ICurrentUser`). 11 Application-tester.
- **Infrastructure:** EF-konfig (composite unique index `(JobSeekerId, JobAdId)`), `AccountHardDeleter` cascade utökad med `SavedJobAds.RemoveRange` (ADR 0024 paritet, GDPR Art. 17). 1 Worker.Integration cascade-test.
- **API:** `/api/v1/me/saved-job-ads` (GET list / POST `{jobAdId}` / DELETE `{savedJobAdId}`).
- **Migration:** `20260523154503_AddSavedJobAds` (additiv, idempotent up/down, partial-index på `JobSeekerId`).

### PR2 (commit `4afc081`) — Frontend Del A

- **Zod-mirror:** `SavedJobAdDto` (per ADR 0020).
- **lib/api/saved-job-ads** + **lib/actions/saved-job-ads:** Server Actions (Zod-validerad input).
- **`SaveJobAdToggle`:** optimistic update + `aria-pressed` (per jobbpilot-design-a11y-skill WAI-ARIA 1.2 toggle-button).
- **`SavedJobAdList`/`SavedJobAdRow`:** RSC-data + optimistic delete.
- **`/sparade`-RSC-sida** + **app-shell nav-länk** (ADR 0054 header-menu).
- **JobAdDetail modal-footer-integration:** `SaveJobAdToggle` synlig i ADR 0053-modalen.
- **Vitest:** 13 nya (6 Zod + 4 list + 3 toggle baseline).

### PR3 (commit `a187467`) — Backend Application-from-JobAd (Del B)

- **`CreateApplicationFromJobAdCommand`** + handler: validerar `JobAdId` existerar (ej deleted/expired) + delegerar till befintlig `Application.Create`-fabrik. **Ingen snapshot** — ADR 0048 read-join löser presentationsvägen.
- **API-endpoint:** `POST /api/v1/applications/from-job-ad/{jobAdId}` (separat från befintlig manuell `POST /api/v1/applications`).
- **Tester:** 4 Application-tester (happy path, not-found, deleted JobAd, idempotency-observation).

### PR4 (commit `1972f47`) — Frontend Del B + ADR-amends

- **`createApplicationFromJobAdAction`:** Server Action mot nya endpoint.
- **`HarAnsoktButton`:** optimistic + inline-toast med länk till ny Application (`/ansokningar/{id}`).
- **JobAdDetail modal-footer-integration:** `HarAnsoktButton` bredvid `SaveJobAdToggle`.
- **adr-keeper** (agentId `ade82c309560fb383`, Klas-override för verbatim-källa per §9.4):
  - **ADR 0053 Amendment 2026-05-23** — Spara/Har-ansökt-knappar Accepted i modal-footer (FE-action-bryggan byggd); match-presentation kvar Fas-4-gated; route-only-supersession består.
  - **ADR 0024 Amendment 2026-05-23** — cascade-rad utökad: SavedJobAds explicit i AccountHardDeleter (GDPR Art. 17-paritet med RecentJobSearches/SavedSearches).
- **README-index uppdaterat** (`docs/decisions/README.md` ADR 0053+0024-rader).
- **Vitest:** 4 nya (HarAnsoktButton inkl. toast-länk + optimistic-rollback).

## Reviews

| Roll | AgentId | Domslut |
|---|---|---|
| security-auditor | `a1c34345919cf4f91` | **APPROVED** — 0 Block / 0 Critical / 0 Major / 0 Medium / 0 Minor. ADR 0031 inte tillämpligt (JobAdId = publik resurs, ingen IDOR-yta). Cross-tenant-skydd via cookie-bound `ICurrentUser`. |
| code-reviewer | `a57baf5e5e5539d9e` | **Approved** — 0 Block / 0 Major / 3 Minor. M1 (audit-noise idempotent no-op) = observation utan åtgärd. M2 (ADR 0024-amend) fixad i PR4 (`1972f47`). M3 (fullt-kvalificerade typnamn i integration-test) fixad i PR2-batchen. |

## Tester-delta

| Suite | Före | Efter | Delta |
|-------|------|-------|-------|
| Domain.UnitTests | 399 | 404 | +5 SavedJobAd |
| Application.UnitTests | 546 | 561 | +11 SavedJobAd + 4 Application-from-JobAd |
| Architecture.Tests | 78 | 78 | (oförändrat) |
| Worker.IntegrationTests | n | n+1 | +`RunAsync_CascadesHardDelete_ToSavedJobAds` (CI) |
| Web vitest (totalt) | 612 | 629 | +13 (Zod 6, list 4, toggle 5, har-ansökt 4) — pnpm test: 66 files / 66 files |

Frontend `pnpm build` grön — RSC-boundary verifierad (`/sparade` + `(.)jobb/[id]` listade som RSC).

## Beslut / detours

- **Ingen snapshot på Application-from-JobAd** — CTO Val 2 Variant A: ADR 0048 read-join Beslut d (cross-aggregat-read-join i query-vägen) löser presentationsvägen utan att vidga write-side. Bevarar Application-aggregatets autonomi.
- **In-card Spara-toggle skippad** — Klas-prompt nämnde det, men endast modal-footer levererades. Kräver `Link → article`-refactor på `JobAdCard` (nested interactive controls inom Link). Egen design-rond rekommenderad.
- **Har-ansökt-knappens idempotens** — FE-state-switch förebygger dubbel-klick → dubbla Applications, men backend tillåter. Ev. domain-invariant senare (skapa-Application-from-JobAd → unique constraint på `(JobSeekerId, JobAdId, Status=Active)`?). Out-of-scope för Punkt 2.

## Disciplin-bekräftelser

- Alla commits explicita pathspec (`-- <paths>`) per memory `feedback_pathspec_commit_parallel_cc`. `.claude/settings.json` aldrig committad.
- Auto-skapad scaffolding (`docs/handoff-oversikt/`, `docs/jobbpilot-v3-bundle/`, `docs/reviews/2026-05-17-agent-roster-gap-cto.md`) orört per Klas-direktiv (memory `feedback_dont_delete_auto_files`).
- ADR-amends via adr-keeper med Klas-override för verbatim-källa per §9.4 + memory `feedback_klas_can_override_adr_verbatim_source`.
- **Inga TDs lyfta** (§9.6 fas-regel — alla fynd inom Fas 6, fixade in-block).
- CC gav **inte** egen rekommendation vid 4 multi-approach-val — CTO är decision-maker per §9.6 + memory `feedback_cto_decides_multi_approach`.

## Pending Klas-operativt

1. **Tag-push `v0.2.58-dev`** (Klas-GO) → dev-deploy triggar EF migration `20260523154503_AddSavedJobAds` automatiskt.
2. **Visual-verify post-deploy** via `pnpm visual-verify` (frontend-runbook, auth-läge).
3. **Korpus-konvergens Punkt 1** — fortfarande ~72h-fönstret från 2026-05-23 v0.2.57-dev-deploy (separat spår).

## Klassiska följdfrågor (ej Punkt 2-scope, för framtida design-rond)

- **In-card Spara-toggle i `JobAdCard`** — `Link → article`-refactor krävs (nested interactive controls). Egen design-rond med design-reviewer för render-flöde + a11y-impact.
- **Har-ansökt idempotens-invariant** — backend tillåter dubbla Applications från samma JobAd; FE-switch förebygger men race-condition möjlig. Domain-invariant övervägs vid opportunistisk touch.

## Nästa session

**Punkt 3 (Landing live-stats)** — ny `GET /api/v1/landing/stats` (cache:ad — Redis / Hangfire-cron / materialiserad PG-vy), frontend swappar `getLandingStats()` per ADR 0056-utbytespunkt. Perf-kritiskt (publik landingpage, ingen auth). Beror på Punkt 1 för korrekta antal (verifiera korpus-konvergens grön innan start). ADR 0056-amend. CTO-rond vid sessionsstart för cache-mekanism.

Startprompt genereras separat per `docs/runbooks/session-start-template.md` när Klas ger GO.
