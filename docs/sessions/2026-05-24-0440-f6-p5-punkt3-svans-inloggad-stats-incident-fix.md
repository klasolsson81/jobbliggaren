---
session: F6-P5-P3-svans
datum: 2026-05-24
slug: f6-p5-punkt3-svans-inloggad-stats-incident-fix
status: levererad
commits:
  - e0911a3 — feat(landing): inloggad live-stats i app-header
  - 9d53bb4 — fix(landing): Worker→Redis-anslutning + app-header-stil paritet
  - 79e063f — fix(job-ads): freshness-tagg UTC-kalender-dag-sync
  - 69acdd5 — fix(infra): terraform image-tag variable-validation + TD-91
deploys:
  - tag: v0.2.61-dev
    sha: 69acdd5
    run: 26352086921
    triggad: 2026-05-24 ~04:40 UTC
    status: pending stable när docs-keeper invokerades
  - operativt: terraform apply (targeted aws_ecs_task_definition.worker) → revision 64
  - operativt: manuell ECS task-def revision 65 + force-new-deployment (rätt SHA-image 13d172d...)
tds:
  - TD-91 (Minor × Trigger) — RDS param-group apply_method-drift (paritet TD-85)
adrs:
  - ADR 0023 Amendment 2026-05-24 (Worker får legitim outgoing Redis-write-port via ADR 0064; HTTP-fri-invariantens kärna intakt)
reviews:
  - security-auditor 0/0/0/0/0/0/2 Minor APPROVED (HeaderStats e0911a3)
  - code-reviewer 0 Block/3 Major/4 Minor ALLA in-block-fixade (HeaderStats e0911a3)
  - design-reviewer GO 0/0/3 Minor observationer (HeaderStats e0911a3)
  - dotnet-architect-dom agentId a9446dac40e8fef02 (Worker→Redis-arkitektur 9d53bb4, ADR 0023-amend verbatim)
föregående_sha: 499bf63 (docs-keeper-synk efter PR3 13d172d)
ny_sha: 69acdd5
---

# F6 P5 Punkt 3-svans — inloggad live-stats + Worker→Redis-incident-fix

## Sammanfattning

Svans-session ovanpå F6 P5 Punkt 3-batchen (PR1-3 + tag `v0.2.60-dev` på
`13d172d`). Tre Klas-direktiv levererade plus en kritisk Worker→Redis-incident-
fix som upptäcktes under arbetet:

1. **Live-stats även för inloggade** — HeaderStats client-component i app-header
   med 10-min-polling + delta-affordans (`e0911a3`).
2. **Worker→Redis-anslutning + app-header-stil paritet landing** — incident-fix
   för missad Terraform-secret-injektion (ADR 0064-konsekvens av ADR 0023
   supersedad men IaC-block inte uppdaterat) + stil-paritet med landing-topbar
   (`9d53bb4`). ADR 0023 Amendment 2026-05-24.
3. **Freshness-tagg "IDAG" mismatch mot "igår, kl. 23:37"-copy** — UTC-
   kalender-dag-sync mellan `computeFreshnessLabel` och
   `formatPublishedAtWithTime` (`79e063f`).
4. **Terraform-`:latest`-default-incident** — variable-validation tvingar
   explicit image-tag, blockerar oanvändbar task-def-revision; TD-91 lyft
   för pre-existing RDS-param-group-drift (`69acdd5`).

Tag `v0.2.61-dev` pushad på `69acdd5`, deploy-dev run `26352086921` triggad
~04:40 UTC.

Live verifierat efter manuell ECS-revision 65: `/api/v1/landing/stats` →
`{"activeCount":46328,"newToday":7,"isStale":false}`.

## Leverans per commit

### `e0911a3` feat(landing): inloggad live-stats i app-header

Klas-direktiv: "live-stats även för inloggade". Återanvänder backend
`/api/v1/landing/stats`-endpoint (ADR 0064, etablerad i PR1-3) men flyttar
presentationen från landing-hero till app-header för inloggade användare.

Levererat:
- `HeaderStats` client-component (visibility-aware polling — `requestIdleCallback`
  + `document.visibilityState`-gate; sekventiell-polls-ratchet förhindrar in-
  flight-overlap)
- Route-handler `/api/landing-stats` (Next.js BFF som proxar mot backend)
- AppShell-integration (kolumn-layout, höger om spacer per Klas-stil-paritet
  med landing-topbar)
- 8 vitest-tillägg inkl. M1 (visibility-aware) + M3 (sekventiell-polls-ratchet)

Reviews:
- security-auditor: 0/0/0/0/0/0/2 Minor APPROVED
- code-reviewer: 0 Block / 3 Major / 4 Minor — alla in-block-fixade
- design-reviewer: GO, 0/0/3 Minor observationer (kvar för framtida polish)

### `9d53bb4` fix(landing): Worker→Redis-anslutning + app-header-stil paritet

**Incident 2026-05-24 03:51 UTC.** Efter `e0911a3`-deploy började landing-stats
returnera Floor-värdet (`activeCount:40000, newToday:0, isStale:true`) för alla
användare. CloudWatch-loggar visade Worker `RefreshLandingStatsJob` kraschade
var 5:e min med:

```
StackExchange.Redis.RedisConnectionException: UnableToConnect on localhost:6379
```

**Root cause-analys:**

1. Worker försökte ansluta till `localhost:6379` istället för dev-Redis.
2. `worker_secrets`-blocket i `environments/dev/main.tf` saknade
   `ConnectionStrings__Redis`-injektion.
3. Historiskt: ADR 0023 fastställde "Worker använder INTE Redis"
   (HTTP-fri-invariant). Detta superseddes implicit av ADR 0064 (Worker
   skriver landing-stats till Redis via `ILandingStatsCache`-port), men
   Terraform `worker_secrets`-blocket uppdaterades aldrig.
4. CC:s defekta `?? "localhost:6379"`-fallback i Worker `Program.cs` maskerade
   config-bortfallet — istället för fail-fast vid uppstart kraschade jobbet
   tyst var 5:e minut.

**Fix:**
- Terraform `worker_secrets` får `ConnectionStrings__Redis`-injektion (samma
  pattern som Api).
- Worker `Program.cs` fail-loud `InvalidOperationException`-paritet med Api
  `Infrastructure/DependencyInjection.cs:438-440` (ingen tyst fallback).
- App-header-stil paritet med landing-topbar (17px num, 10.5px label, 28px
  sep, column-layout, höger om spacer) per Klas-direktiv.

**ADR 0023 Amendment 2026-05-24** dokumenterar Worker:s nya legitima outgoing
Redis-write-port. HTTP-fri-invariantens kärna (ingen ASP.NET Core, ingen
Identity-cookie/auth-yta) är intakt; outgoing-portar (System.Net.Http per
ADR 0032 + StackExchange.Redis per ADR 0064) är fortsatt OK — det är HTTP-
server-bagaget som förbjuds.

dotnet-architect-dom agentId `a9446dac40e8fef02` skrev amendment-texten
verbatim (Klas-GO för verbatim-källa per memory
`feedback_klas_can_override_adr_verbatim_source`).

### `79e063f` fix(job-ads): freshness-tagg UTC-kalender-dag-sync

Klas-observation: en annons publicerad kl 23:37 UTC visades inkonsekvent —
tagg "IDAG" + copy "Publicerad igår, kl. 23:37" samtidigt på samma kort.

**Root cause:** `computeFreshnessLabel` använde 24h-fönster
(`Math.floor(ageMs / 86400000)`) medan `formatPublishedAtWithTime` använde
kalender-dag-jämförelse. Vid passage över kalenderdygnsgränsen för en
nyligen publicerad annons gick taggen "IDAG" innan copy-texten flippade.

**Fix:** UTC-kalender-dag via `getUTCDate/getUTCMonth/getUTCFullYear` + `Date.UTC`
i `computeFreshnessLabel`. TZ-agnostiskt — vitest Sverige-TZ + production UTC
ger identiskt beteende.

Tester +2 (kalender-gräns + samma-UTC-dag), 654/654 vitest grönt.

### `69acdd5` fix(infra): terraform image-tag variable-validation + TD-91

**Tidigare incident-rot-orsak:** Terraform `api/worker/migrate_image_tag`
default `"latest"` skapade oanvändbar task-def-revision vid manuell
`terraform apply` — ECR pushar inte `:latest`-taggen, så ECS kunde inte
hitta image:n. Detta var faktiskt vad som hände under 9d53bb4-incident-
debuggingen: targeted `terraform apply` skapade rev 64 med `:latest`-image
som inte gick att pulla; därför krävdes manuell `register-task-definition`
med rätt SHA-image för rev 65.

**Lösningsalternativ vägda:**
- `lifecycle.ignore_changes = [container_definitions]` — **self-vetoad** eftersom
  det skulle ignorera även secret-ändringar (precis det som hände i
  9d53bb4-incident).
- Variable-validation som tvingar explicit värde — **vald**. Default=`""`,
  validation kräver `length>0 && värde!="latest"`. `terraform plan` utan `-var`
  fail-fast med tydlig error.

Workflow påverkas inte (GitHub Actions kör inte `terraform` alls — bara
ECR push + ECS update-service via aws-cli).

**TD-91** (Minor × Trigger) lyft för pre-existing RDS-param-group
`apply_method`-drift (`pending-reboot → immediate` för `rds.force_ssl`,
värdet `1` oförändrat). Ren state-config-drift, ingen funktionell incident.
Adresseras i separat IaC-triage-session paritet TD-85.

**Tag `v0.2.61-dev`** pushad på `69acdd5` → deploy-dev run `26352086921`
triggad ~04:40 UTC.

## Operativa events

| Tid (UTC) | Event | Detalj |
|-----------|-------|--------|
| 03:51 | Worker incident | RedisConnectionException loop upptäckt via CloudWatch |
| ~04:00 | Diagnos | Root cause-analys + Terraform-fix-design (dotnet-architect-rond) |
| ~04:15 | terraform apply | Targeted `aws_ecs_task_definition.worker` → revision 64 (image `:latest`) |
| ~04:17 | aws ecs (manual) | `register-task-definition` revision 65 med SHA-image `13d172d86c0729afee705dfd5152cc20543b9b46` |
| ~04:17 | aws ecs update | `update-service --force-new-deployment` mot `jobbpilot-dev-worker` |
| ~04:18 | Live-verify | `/api/v1/landing/stats` → `{"activeCount":46328,"newToday":7,"isStale":false}` |
| ~04:40 | Tag-push | `v0.2.61-dev` på `69acdd5` → deploy-run `26352086921` triggad |

## Lessons learned

1. **Terraform `:latest`-default-fälla.** Default-värdet ser oskyldigt ut men
   skapar oanvändbara task-def-revisioner vid manuell apply när ECR-pipeline
   bara taggar SHA. Variable-validation som tvingar explicit värde är
   minimalt invasiv (workflow påverkas ej) men eliminerar fällan permanent.

2. **Fail-loud-paritet är arkitekturdisciplin, inte boilerplate.** Worker
   `Program.cs` hade defekt `?? "localhost:6379"`-fallback som maskerade
   config-bortfallet. Api `Infrastructure/DependencyInjection.cs:438-440`
   gör fail-loud `InvalidOperationException`. Sådan paritet ska vara
   default mellan compositions roots när samma resurs konsumeras.

3. **Kalender-dag vs 24h-fönster-konsistens.** När en UI-yta visar både
   tagg och copy baserat på samma timestamp måste fönster-modellen vara
   *exakt* samma. Vid passage över kalenderdygnsgränsen för nyligen
   publicerade annonser blir mismatchen synlig och förvirrande.

4. **ADR-mekanik-amendment vs implicit supersession.** ADR 0064 implicit
   supersedade en del av ADR 0023 (Worker-Redis-invarianten) men IaC-blocket
   uppdaterades inte i samma session. Amendment-disciplin: när en ny ADR
   utökar/förändrar en outgoing-port hos en annan ADR, ska amendment-noten
   på den ursprungliga ADR:n skrivas i samma batch — inte vänta tills
   incidenten inträffar.

5. **dotnet-architect-skriven verbatim-text** (memory
   `feedback_klas_can_override_adr_verbatim_source`) håller — Klas-GO
   gavs explicit för verbatim-källa under sessionen.

## TDs

- **TD-91** (Minor × Trigger) — RDS param-group `apply_method`-drift
  (pending-reboot → immediate för `rds.force_ssl`, värdet `1` oförändrat).
  Paritet TD-85 github_oidc-drift. Adresseras i separat IaC-triage-session.

Inga andra nya TDs (alla in-block-fixade fynd från reviews).

## ADRs

- **ADR 0023 Amendment 2026-05-24** — Worker får legitim outgoing Redis-
  write-port via ADR 0064. HTTP-fri-invariantens kärna intakt; outgoing-
  portar (System.Net.Http per ADR 0032 + StackExchange.Redis per ADR 0064)
  fortsatt OK. Operativ konsekvens: `worker_secrets` kräver
  `ConnectionStrings__Redis`-secret-injektion i `environments/dev/main.tf`
  rad 328-335. Tidigare kommentar "Worker använder INTE Redis" är
  superseded av denna amendment.

## Reviews

| Rond | Agent | Resultat |
|------|-------|----------|
| HeaderStats `e0911a3` | security-auditor | 0/0/0/0/0/0/2 Minor APPROVED |
| HeaderStats `e0911a3` | code-reviewer | 0 Block / 3 Major / 4 Minor — alla in-block-fixade |
| HeaderStats `e0911a3` | design-reviewer | GO, 0/0/3 Minor observationer |
| Worker→Redis `9d53bb4` | dotnet-architect | agentId `a9446dac40e8fef02`, ADR 0023-amend verbatim |

## Pending Klas-operativt

1. **Deploy-dev stable-verify** — `v0.2.61-dev` deploy-run `26352086921`
   väntar slutförande när docs-keeper invokerades.
2. **Visual-verify post-deploy** — landing + app-header rendering, inloggad
   HeaderStats-polling.
3. **Korpus-konvergens Punkt 1** — ~72h-fönstret från `v0.2.57-dev`
   (2026-05-23) klart 2026-05-26.

## Nästa session

**Punkt 4 (Översiktssida `/oversikt`)** — large, frontend+ev. backend.
Återanvänder ADR 0064-mönster (Worker-precomputed Redis-cache) för
aggregat-fält som behöver perf-budget. Stänger TD-82.

Handover-källa: `docs/jobbpilot-v3-bundle/JobbPilot/handoff-oversikt/HANDOVER-oversikt.md`
(Klas-godkänd 2026-05-23). Tre sektioner: Title+I dag / Notiser /
Sammanfattning. Mockdata-tillåtelse för fält där BE saknas (HANDOVER §0).

Egen session med startprompt enligt
`docs/runbooks/session-start-template.md`. CTO-rond: data-mappning
riktigt-vs-mock (HANDOVER §3).
