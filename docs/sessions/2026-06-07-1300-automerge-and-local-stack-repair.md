---
session: Auto-merge-setup + Dependabot-merge + lokal-stack-reparation
datum: 2026-06-07
slug: automerge-and-local-stack-repair
status: docs-sync via PR (forts. samma dag efter städnings-PR #14)
commits:
  - "#11 chore(deps): nuget backend-bumpar (Refit 10.2.0 bevarat)"
  - "#15 chore(deps): web npm-bumpar (12 updates)"
  - "#16 ci(deps): dependabot-automerge.yml (patch/minor)"
  - "#17 ci(deps): blockerande vuln-gate (High/Critical) — Klas-GO B"
  - "#18 ci: label-automerge.yml (egna PRs, opt-in)"
---

# Auto-merge + Dependabot + lokal-stack-reparation

Forts. samma dag efter att städ-PR #14 (Refit-cert + AWS-info-rensning) mergats.
Ingen app-kod rörd — CI-workflows + operativ lokal-stack-reparation + docs.

## A. Dependabot-PRs mergade

- **#11** (nuget-grupp): 11 backend-bumpar (Microsoft.* 10.0.7→10.0.8,
  StackExchange.Redis 2.13.1→2.13.17, Testcontainers 4.11→4.12,
  Microsoft.Testing.Extensions.CodeCoverage 18.6.2→18.7.0, Npgsql m.fl.).
  **Refit 10.2.0 bevarat** — #11-diffen rörde inte Refit-raden, så 3-way-merge
  mot main (där #14 satte 10.2.0) behöll cert-fixen. Verifierat på main efteråt.
- **#12 → #15:** web-minor-grupp. #12 (7 updates) blev "out of date" mot strict
  branch protection; dependabot **supersedde** den med #15 (12 updates, färsk bas)
  och stängde #12 ("no longer needed"). #15 mergad.
- Lärdom: strict "require branches up to date" + squash gör att Dependabot-PRs
  öppnade före en merge måste rebasas; dependabot sköter det självt (ev. som ny PR).

## B. Auto-merge-feature (3 PRs)

Klas-förslag (Gemini-inspirerat). Justerat mot JobbPilot-regler: via PR (ej
direct-push), och med förbehåll dokumenterade i workflow-kommentarerna.

- **#16 `dependabot-automerge.yml`** — GitHubs officiella mönster. Aktiverar
  GitHub auto-merge på Dependabot patch/minor (`dependabot/fetch-metadata@v2` →
  `update-type`); major → manuell. `pull_request` (ej `_target`), actor-guard
  `github.actor == 'dependabot[bot]'`, least-privilege `contents/pull-requests:
  write`, `$PR_URL` via `env:`. security-auditor ✓ (`a60bb5f08176fdf7f`).
- **#17 blockerande vuln-gate (Klas-GO "B")** — security-auditors enda Major:
  observe-only `audit` (ADR 0045) läses av ingen vid auto-merge. Lade
  blockerande scan FÖRE `gh pr merge --auto`: NuGet `dotnet list package
  --vulnerable` (grep Severity, exit 1 vid High/Critical) + `pnpm audit
  --audit-level=high`. Gated på patch/minor. ADR 0045 observe-only-status för
  `ci` oförändrad — gaten gäller bara auto-vägen.
- **#18 `label-automerge.yml`** — egna PRs opt-in: sätt label `automerge` →
  auto-merge när required `ci` grön. Default (utan label) = manuell merge →
  ADR 0065 #4 (manuell diff-granskning) bevarad. Valt label-baserat över
  actor-baserat (som hade tagit bort granskning för all egen kod). `automerge`-
  label skapad i repot.

## C. Branch protection hade fallit tyst bort

Vid försök att aktivera "Allow auto-merge" var checkboxen disabled. Diagnos via
`gh api`: 403 *"Upgrade to GitHub Pro or make this repository public"* — repot
var **privat på GitHub Free**, där branch protection inte finns. ADR 0065-grinden
(satt 2026-05-25 när repot var publikt) hade alltså **inte varit enforced** sedan
visibility-bytet — PR-flödet hade varit disciplin-baserat, inte server-tvingat.

- Klas satte repot **publikt igen**. gitleaks full-historik **ren** (587 commits,
  0 läckor) verifierad före flip.
- CC **återställde** ADR 0065-protection via `gh api PUT branches/main/protection`
  (exakt config: required `["ci"]`, strict, enforce_admins, 0 approvals,
  required_linear_history, required_conversation_resolution, force/del blockerade)
  + `gh api PATCH allow_auto_merge=true`.
- **LÄRDOM (ej i ADR än, Klas avböjde doc-note):** branch protection + auto-merge
  är bundna till publikt/Pro; vid visibility-byte måste de verifieras/återställas.

## D. Lokal-stack-reparation

Efter omstart körde bara FE (`:3000`) + Docker (postgres/redis/seq). **API:t
(`:5049`) och Worker:n var nere.** Symptom Klas såg:
- Login "Kunde inte nå servern" → FE-proxy (`BACKEND_URL=http://localhost:5049`)
  nådde inget BE.
- Landing "40 000 / 0 nya" = **Floor-fallback** (`activeCount:40000, newToday:0,
  isStale:true, refreshedAt:null`) — Worker:n ej igång → inga precomputed stats
  i Redis (ADR 0064).

Åtgärd: startade `JobbPilot.Api` + `JobbPilot.Worker` i bakgrunden
(`ASPNETCORE_ENVIRONMENT=Development`, http-profil 5049). `/api/ready` Healthy.
RefreshLandingStatsJob (`*/5 * * * *`) populerade Redis → riktiga stats
(42 771 aktiva, 53 nya idag, `isStale:false`); Platsbanken-corpus re-importerades
(~42 800 annonser). **Stats-timing-not:** newToday=0 sågs initialt för att
refreshen kördes tidigt i importen; självkorrigerade vid nästa `*/5`-körning.

## E. Konto återskapat

`klasolsson81@gmail.com` fanns **inte** i lokala DB:n (20 användare, alla
e2e-test). Live-kontot låg i AWS RDS som revs (ADR 0066); lokal DB skapad färsk
2026-06-06. Återskapat via backend `POST /api/v1/auth/register` — **ej gated av
`RegistrationsOpen`** (kill-switchen gatar bara Invitations + Waitlist; FE:s
`/registrera`→`/vantelista`-308 är frontend-only). Temp-lösenord, regular user.
Admin-roll ej satt (kräver `AdminBootstrap:InitialAdminEmail` + API-restart;
`IdempotentAdminRoleSeeder` tilldelar bara roll till befintlig user, skapar ej).

## F. Bugg upptäckt → lead-item nästa session

Klick på jobb → intercepting-modal `/jobb/[id]` (ADR 0053) kraschar med Next.js
16 dev:s maskerade **"Jest worker encountered N child process exceptions"**-fel
= ohanterat undantag vid RSC-render. Job-detalj-API auth-gatad (401 unauth =
korrekt). Ej pinpointat — kräver authed repro + FE-route-inspektion. Hypoteser:
RSC-render-throw i modal-route, ev. EF strongly-typed-VO-translation (memory
`feedback_ef_strongly_typed_vo_contains_translation`).

## Nästa session (Klas-GO)

**Systematisk regressions-audit:** features som funkade på AWS-live men kan vara
trasiga lokalt, med **VPS-portabilitets-lins**. Egen `/clear`-session med
self-contained startprompt. Preliminär karta: job-modal (lead), sök/filter
EF-VO-translation, AWS-SDK→Local-provider-features (fält-kryptering/mejl) e2e,
OAuth-login (ej konfig lokalt — beslut), VPS-checklista (ingen hårdkodad
localhost, allt via IConfiguration/env).

## Operativt / pending

- API + Worker kör som bakgrundsprocesser startade denna session; stoppar de
  (laptop-omstart/terminal-stäng) måste de startas om: `dotnet run --project
  src/JobbPilot.Api --launch-profile http` + `dotnet run --project
  src/JobbPilot.Worker` (Development).
- Admin-roll till klasolsson81 om admin-panel behövs (nästa session, setup-steg).
- Lösenordsbyte lokalt går via ConsoleEmailSender → Seq (`localhost:5341`),
  ingen riktig inkorg.
