---
session: Lokal regressions-audit (VPS-portabilitets-lins)
datum: 2026-06-07
slug: lokal-regressions-audit
status: pågående — docs klara, hook-guard pending Klas-GO, PR ej pushad
bas-HEAD: 02650e8
branch: chore/lokal-regressions-audit-vps-lins
commits:
  - "(pending) chore(hooks): version-drift-guard i session-start — BLOCKERAD av permission-classificerare, Klas-GO krävs"
  - "(pending) docs(tech-debt): TD-104 observability-sink + TD-105 Migrate VPS-portabilitet"
  - "(pending) docs(sessions): lokal regressions-audit + ADR-0050-input-checklist"
---

# Lokal regressions-audit (VPS-portabilitets-lins)

Systematisk genomgång av features som funkade på AWS-live men kunde vara trasiga
lokalt efter AWS-avvecklingen (ADR 0066), med VPS-portabilitets-lins (ADR 0050,
Proposed). Lead-item: job-modalen kraschade. Discovery-först per fynd; ingen
symptom-fix utan rotorsak (Klas-direktiv).

## Mål

1. Rotorsaka + fixa job-modal-kraschen (LEAD), ej maskerad symptom-fix.
2. Triagera övriga lokal-regressioner (sök/filter EF-translation, crypto/mejl
   e2e, OAuth, VPS-portabilitet).
3. Dokumentera VPS-portabilitets-fynd som input till ADR 0050.

## Vad som gjordes per item

### LEAD — Job-modal `/jobb/[id]`-krasch (ROTORSAK)

Föregående session antog att kraschen var modal/intercept-specifik (RSC-render-
throw). **Det var fel.** Reproduktion mot körande dev-server (curl med
`Next-Url:/jobb`-header för att trigga intercepten + session-cookie) gav HTTP 500
`Jest worker encountered 2 child process exceptions`. Isolering: BÅDE fullsidan
(`/jobb/[id]`) OCH modal-intercepten kraschar identiskt; `/jobb` + `/oversikt`
funkar. Prod `pnpm build` lyckas (alla routes, inkl. intercepten) → INTE
build/serialiserings-fel.

Dev-loggen (`.next/dev/logs/next-development.log`) visade `Compiling
/(.)jobb/[id]` → 0.7s → worker-krasch, plus en ström av `write EPIPE`-
uncaughtExceptions. **Rotorsak:** den körande dev-servern (PID 39712, detached
bakgrundsprocess startad förra sessionen) körde på **stale node_modules** (next
**16.2.4**/react **19.2.4**, installerade 2026-05-06) medan committad
`pnpm-lock.yaml` kräver **16.2.7**/**19.2.7** — Dependabot #15 bumpade lockfilen
2026-06-07 08:25 men `pnpm install` kördes aldrig efter laptop-omstart. Den
detached processens stdout-pipe var bruten (EPIPE) → jest-worker-render-barnen
dog hårt (process-exit) vid render av UNCACHADE tunga routes; redan-cachade
routes överlevde.

**Fix:** `pnpm install --frozen-lockfile` (synkade node_modules→16.2.7) +
`taskkill` stale PID + ren `pnpm dev`-restart. **Verifierat:** fullsida + modal-
intercept + `/sparade` ger nu HTTP 200, noll jest-worker-fel.

**Triage-nyckel:** Detta är en **dev-miljö/operativ regression, INTE en kodbugg.**
Prod-build fungerar på både versioner → VPS/prod opåverkat, ingen kodfix. Notera:
Next 16.2.6 hade säkerhetsfixar (CVE:er + React-issue) → 16.2.4 lokalt var även
säkerhetsmässigt stale.

### Sök/filter EF Core 10 + Npgsql VO-`Contains` — HEALTHY

Memory `feedback_ef_strongly_typed_vo_contains_translation` varnar att
`List<JobAdId>.Contains` 500:ar på riktig Postgres (InMemory missar). Verifierat:
den kända instansen (`GetJobAdStatusBatchQueryHandler`) är korrekt workaround:ad
(projicerar `s.JobAdId.Value`→Guid server-side, filtrerar client-side med
`HashSet<Guid>`; kommentaren citerar CI-500:orna 2026-05-23). E2e mot riktig
Postgres: free-text (Relevance) / ssyk-filter / suggest / save→batch-read→saved-
list→unsave — alla 200, korrekt VO-round-trip. Enda andra `.Contains` i
Application är samma workaround. Ingen åtgärd.

### Crypto/mejl e2e

**Crypto HEALTHY.** Skapade CV via API med PII/content-markörer → DB-inspektion:
`resume_versions.content_enc` = ciphertext (`v1:`+base64), klartext-läck-check 0
rader; `user_data_keys.wrapped_dek` = magic-byte **0x4C** ('L'=Local), `cmk_key_id`
`local-v1`, 62 byte = exakt LocalDataKeyProvider wire-format
`[0x4C,0x01]+nonce12+ct32+tag16`. DEK-wrap via LocalDataKeyProvider (ADR 0066/0049)
bekräftat e2e. Nya writes är encrypted-only (min rad: `content` jsonb = null).

Legacy: 29/30 `resume_versions`-rader har plaintext PII i `content` jsonb —
men det är en **by-design read-only shadow** (`PropertySaveBehavior.Ignore` på
before/after-save; EF skriver ALDRIG `content`), retention-fallback under dual-
state-fönstret (TD-13/ADR 0049, droppas i framtida migration). Lokal seed/test-
data, identiskt schema på AWS → **EJ regression**.

**Mejl:** `ConsoleEmailSender` är enda registrerade `IEmailSender` (DI-switch,
default Console; SES borttaget ADR 0066), loggar via `ILogger`
LoggerMessage(3001), console-sink lever (API-stdout visar strukturerade loggar).
Live-trigger (waitlist-confirmation) blockerad av `RegistrationsOpen`-kill-switch
(503, förväntat — invitation kräver admin).

**FYND (Seq):** Seq-containern (`localhost:5341`) kör men **tar emot inget** —
det finns **ingen Serilog alls** i lösningen (noll `Serilog.*`-PackageReference,
ingen `WriteTo`-config, `Program.cs` har ingen logging-setup; default
Microsoft.Extensions.Logging→console). Seq event-query returnerar `[]`. Detta
motsäger CLAUDE.md §11.3 ("seq (local Serilog sink)") + TD-101-blocket +
förra sessions-noten ("ConsoleEmailSender → Seq") — alla faktiskt inexakta.
`docker-compose`-kommentaren "Produktion kör aldrig Seq (CloudWatch istället)"
är dessutom inaktuell (CloudWatch borta, ADR 0066 → ingen prod-logg-sink finns).
→ **TD-104**.

### OAuth-login — EJ implementerat

Backend har ingen OAuth-scheme (ren session/Bearer-auth, ingen
AddCookie/AddOAuth/AddOpenIdConnect). FE:s Google/LinkedIn/Microsoft-knappar i
`auth-card.tsx` (rad 25-27) är **explicita FAS-DEFERRAL-stubs** som bara
navigerar till `/logga-in?provider=x` query-param. OAuth fungerade aldrig på
AWS-live heller → **ingen regression**. Klas:s fråga ("ska det funka lokalt?")
är prematur: OAuth är obyggt; lokal-vs-prod-config-valet fattas när det byggs
(client-ID/secret/redirect-URI). **Ingen TD, ingen ADR** (CTO: placeholder-TD =
dumpning-anti-pattern §9.6).

### VPS-portabilitet — input till ADR 0050

Backend + FE är i stort portabelt: allt via `IConfiguration`/env, **ingen**
hårdkodad backend-URL i FE (allt `env.BACKEND_URL`), ingen CORS-config behövs
(backend icke-browser-reachable, ADR 0018; FE proxar server-side), Worker vägrar
localhost-fallback (kräver explicit Redis-config).

**Fynd → TD:**
- **`JobbPilot.Migrate` är helt AWS-Secrets-Manager-bunden** (`AmazonSecretsManagerClient`
  hämtar master-creds + app-conn-strings + `PutSecretValue`, two-phase least-
  privilege). Ej VPS-portabel; oanvänd lokalt. → **TD-105**.
- Ingen prod-logg-sink efter CloudWatch-exit → konsoliderad i **TD-104** (samma
  observability-gap, undvik dubblett).

**ADR-0050-input-checklist (deploy-tidsvärden, ej TD — sätts vid VPS-provisionering):**
- [ ] `Email:BaseUrl` defaultar `http://localhost:3000` — MÅSTE override:as till
      riktig domän på VPS (annars pekar invitation-/waitlist-mejl-länkar mot
      localhost).
- [ ] `__Host-jobbpilot_session`-cookien har `secure: true` — kräver HTTPS på
      VPS (reverse-proxy/TLS-terminering). Funkar lokalt för att browsers
      behandlar `localhost` som secure context; gäller INTE en plain-HTTP VPS.
- [ ] Logg-sink-strategi för prod (se TD-104).
- [ ] Migrations-strategi för VPS (se TD-105).
- (info) `AWSSDK.KeyManagementService` kvar i Infrastructure = dead weight vid
  `FieldEncryption:Provider=Local` (instansieras bara vid `Kms`); behållet för
  ADR 0066-reversibilitet. Ingen åtgärd.

## Beslut (senior-cto-advisor `ad6b2bf569b9f98bd`)

1. **Lead-durabelt artefakt — Approach C:** icke-blockerande version-drift-guard
   i `session-start.sh` (parity-check, Twelve-Factor §10 / fitness function) +
   docs-lärdom. In-block, ingen TD. (Approach B docs-only avvisad — beror på
   mänskligt minne, samma failure-mode som idag.)
2. **Seq/observability — TD-104** (Hetzner-fas, kriterium 1). Wire INTE Serilog
   nu (YAGNI + föregriper ADR-0050-prod-sink-beslut + §9.2 nya deps). §11.3-
   korrigering = spec-edit → Klas-STOPP, görs som TD-104-följdarbete.
3. **OAuth — docs-only**, ingen TD/ADR.
4. **VPS — TD-105** (Migrate); checklist-items i denna logg; (e) AWSSDK ingen
   åtgärd.
5. **Agent-invocation:** code-reviewer JA (hook/docs rörs); security-auditor NEJ
   (ingen säkerhetskod ändras, crypto verifierat healthy, plaintext-legacy är
   känd by-design); dotnet-architect NEJ (ingen .NET/IaC i PR).

## Detours / lärdomar

- **Read-verktyget** kan inte läsa `docs/current-work.md` i sin helhet (>25k
  tokens) — använd `offset`+liten `limit` för Edit-ankare, eller Bash för vy.
  current-work.md har vuxit stort; trimning är en framtida separat touch.
- **Stack-instabilitet:** API + Worker (detached bakgrundsprocesser från förra
  sessionen) hade dött; startades om. Worker failade först pga parallell-build-
  kollision (API + Worker byggde `JobbPilot.Domain.dll` samtidigt, Defender-lås)
  → starta Worker EFTER att API byggt klart.

## Klas-STOPP / pending

- **Hook-guard (`.claude/hooks/session-start.sh`)**: permission-classificeraren
  blockerade editen (self-mod av agent-startup-config). Kräver Klas-auktorisering
  eller att Klas applicerar diffen. Diff förberedd. **Detta är PR:ns keystone-
  artefakt** (Approach C) — utan den faller leveransen till docs-only (avvisat).
- **CLAUDE.md §11.3-korrigering**: spec-edit, uppskjuten till TD-104-följdarbete
  (Klas-GO + `approve-spec-edit.sh`).

## Nästa steg

1. Klas-beslut om hook-guarden (auktorisera / applicera själv / droppa).
2. Commit docs (TD-104/105 + current-work + denna logg) + ev. hook.
3. code-reviewer inline, PR mot main, ci-gate grön, agent-report i PR-body.
4. (Framtida) §11.3-korrigering vid spec-edit-GO; observability-wiring + Migrate-
   VPS-portabilitet vid Hetzner-fas (ADR 0050).
