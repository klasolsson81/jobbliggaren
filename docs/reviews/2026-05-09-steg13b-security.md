# Security-audit: STEG 13b (Terraform container-infra — ECR, CloudWatch Logs, IAM, ALB, ECS + Dockerfiles + /api/ready)

**Status:** Approved with Major-findings (Sec-Major-1 kräver ADR-beslut innan apply; Sec-Major-2 kräver fix eller dokumenterad accepted-risk innan apply).
**Granskat:** 2026-05-09
**Auktoritet:** GDPR Art. 5/32, CLAUDE.md §5.4 + §11, ADR 0023 (Worker HTTP-fri), ADR 0024 D7 (LogGroup-retention), ADR 0025 (ECS-egress 0.0.0.0/0), BUILD.md §8.4 + §15.

**Granskat scope:**
- `infra/terraform/modules/ecr/{main,variables,outputs}.tf`
- `infra/terraform/modules/cloudwatch_logs/{main,variables,outputs}.tf`
- `infra/terraform/modules/iam_ecs/{main,variables,outputs}.tf`
- `infra/terraform/modules/alb/{main,variables,outputs}.tf`
- `infra/terraform/modules/ecs/{main,variables,outputs}.tf`
- `infra/terraform/environments/dev/{main,variables,outputs}.tf` (delta från 13a)
- `src/JobbPilot.Api/Dockerfile`
- `src/JobbPilot.Worker/Dockerfile`
- `.dockerignore`
- `src/JobbPilot.Api/Program.cs` (delta: `/api/ready` endpoint)

**Kontext:** Komponering korrekt — `iam_ecs.secret_arns` får alla 4 ARNs (2 placeholder + master + Redis-AUTH); `kms:ViaService`-condition gränsar Decrypt till SM/RDS/EC. Worker-task-role saknar Bedrock-attach (ADR 0023 + Fas 1-disciplin korrekt). ALB drop_invalid_header_fields=true. Inga PII-fält. Inga secrets i klartext-env. Inga state-läckage cross-stack. Stommen är solid — fynd är gränsdragningsfrågor, inte arkitekturbrister.

---

## Critical

Inga.

---

## Major

### Sec-Major-1 — ALB HTTP-only initialt utan dokumenterat ADR-beslut: end-to-end TLS bryts på dev-yta

**Filer:** `infra/terraform/modules/alb/main.tf:82-110` + `infra/terraform/environments/dev/variables.tf:134-138` (default `alb_https_enabled = false`).

ALB exponeras internet-facing på port 80 utan TLS innan domän + ACM finns (STEG 13c). Dev-yta tar emot Bearer-tokens och annan auth-data via klartext HTTP. Konkreta konsekvenser:

1. **Bearer-tokens i klartext över internet** — Sessions-id (Redis-baserad opaque token, ADR 0017) skickas som `Authorization: Bearer <token>`-header. Vid HTTP läses tokens av valfri MITM på path:en mellan klient och ALB-default-DNS (`*.elb.amazonaws.com`). Även om dev-yta = "bara Klas själv" går trafiken över internet (Klas hemnät → AWS-edge), inte via VPN.
2. **Inga säkerhets-headers (HSTS) kan sättas meningsfullt** över HTTP — läsning av Strict-Transport-Security ignoreras av klient.
3. **CSRF-modellen (ADR 0018)** förutsätter Bearer-headers — modellen klarar HTTP, men hela trust-modellen för auth-tokens sönderfaller om transport inte krypteras.
4. **`UseHttpsRedirection()` finns i Program.cs:114** men slår tillbaka på dev-deploy: utan HTTPS-listener returnerar middleware `307 Redirect to https://...:443/...`, ALB har ingen 443-listener → connection refused. Det betyder antingen (a) dev-deploy fungerar inte alls (deploy bryts), eller (b) middleware aktiveras inte för ALB-trafik (om `app.Environment.IsDevelopment()` styr) — Klas måste verifiera vilket.

GDPR-bedömning: Sessions-tokens är inte direkt PII, men ger åtkomst till PII (autentiserad user-data). GDPR Art. 32 (säkerhet vid behandling) kräver "lämpliga tekniska åtgärder" — TLS för auth-tokens är industri-baseline.

**Fix-alternativ (Klas väljer):**

**A — Rekommenderat: pre-13c-mellansteg.** Generera en self-signed cert via ACM (Private CA) eller AWS Certificate Manager med automatiskt-utfärdat ALB-default-cert (kräver hostname-binding, vilket ALB-default-DNS inte stödjer naturligt). Realistiskt = registrera `dev.jobbpilot.se` (eller subdomän som `api-dev.jobbpilot.se`) + ACM-cert nu, slå på `https_listener_enabled = true`, redirect HTTP→HTTPS. Skjuter inte upp någonting — STEG 13c blir då bara Route53-records.

**B — Acceptera HTTP-only via ny ADR.** Skriv ADR (ex `0026-dev-http-only-acceptance.md`) som dokumenterar:
- Scope: bara dev-miljö, bara solo-utvecklare
- Tidsfönster: tas bort senast vid första externa beta-användare
- Mitigation: dokumentera att `dev.jobbpilot.se` aldrig får dela cookies/tokens med staging/prod
- Trigger för upphörande: "när någon utöver Klas får dev-credentials" → omedelbar HTTPS-aktivering
Plus: konfigurera så `UseHttpsRedirection()` skippas i HTTP-only-deploy (annars går alla anrop sönder).

**C — Rakast vetenskapligt: blockera apply tills HTTPS-listener finns.** Inte rekommenderat — försenar 13b-värdet i flera dagar för registrering av domän.

**Status:** Måste adresseras innan apply. Default `alb_https_enabled = false` får inte gälla "tills jag minns att fixa det". Antingen ADR (B) eller A. Vid B: säkerställ att `UseHttpsRedirection()` är environment-gated.

---

### Sec-Major-2 — `UseHttpsRedirection()` aktiv i kod kombinerat med HTTP-only-ALB → deploy bryts eller accepterar HTTP utan att veta varför

**Fil:** `src/JobbPilot.Api/Program.cs:114` (`app.UseHttpsRedirection();`)

Enligt ASP.NET Core default-konfiguration: `UseHttpsRedirection` läser `HttpsPort`-konfiguration; om saknad används `Request.Scheme = "https"` med inferred port (443). Vid dev-deploy bakom ALB:
- ALB sätter `X-Forwarded-Proto: http` (om ingen HTTPS-listener)
- `UseForwardedHeaders` (rad 112) skriver om `Request.Scheme` till `http`
- `UseHttpsRedirection` ser `http` → returnerar `307` med `Location: https://<host>/`
- Klient följer redirect → ALB-port 443 stängd → connection refused

**Resultat:** alla `/api/*`-anrop (inkl. `/api/ready`-health-checks från ALB target-group) får 307. Health-check accepterar bara 200 (matcher i target-group, alb/main.tf:46) → target marked unhealthy → ECS roll-back via deployment_circuit_breaker → service refuses to deploy.

Detta är inte direkt en säkerhets-fråga — det är en operationell defekt — men flaggas under Sec-Major eftersom resultatet är: antingen (a) deploy fungerar inte, eller (b) någon "fixar" det genom att ta bort `UseHttpsRedirection()` permanent vilket sänker säkerhets-baslinjen för staging/prod. Risken: tyst förändring.

**Fix:** environment-gate redirection så den bara kör i prod:

```csharp
if (!app.Environment.IsDevelopment() && !string.IsNullOrEmpty(builder.Configuration["AlbHttpsEnabled"]))
    app.UseHttpsRedirection();
```

Eller koppla till samma `var.alb_https_enabled` via `appsettings.Production.json`-key som task-def klartext-env-var sätter:

```hcl
api_environment = {
  ...
  "AlbHttpsEnabled" = var.alb_https_enabled ? "true" : "false"
}
```

**Status:** Major. Hör ihop med Sec-Major-1: oavsett om Klas väljer A eller B i Sec-Major-1 så måste `UseHttpsRedirection` vara konsistent med ALB-listener-konfigurationen.

---

## Minor

### Sec-Minor-1 — `ECS Exec messaging` med `Resource: "*"` är AWS-gräns men förtjänar IAM-condition

**Fil:** `infra/terraform/modules/iam_ecs/main.tf:167-178` (api) + `:244-254` (worker).

`ssmmessages:*-Channel`-actions kan inte resurs-begränsas (region-globala API-endpoints). Det är AWS-API-design-tvång, inte felimplementering. **Men** — least-privilege ger förbättrings-utrymme via `aws:RequestedRegion`-condition:

```hcl
statement {
  sid    = "EcsExecMessaging"
  effect = "Allow"
  actions = [
    "ssmmessages:CreateControlChannel",
    "ssmmessages:CreateDataChannel",
    "ssmmessages:OpenControlChannel",
    "ssmmessages:OpenDataChannel",
  ]
  resources = ["*"]

  condition {
    test     = "StringEquals"
    variable = "aws:RequestedRegion"
    values   = [data.aws_region.current.name]
  }
}
```

Konsekvens: även om någon stjäl task-role-credentials kan de inte öppna ECS Exec-sessions mot tasks i andra regioner.

**Status:** Defererad. Lyft till STEG 14 eller staging-promote.

### Sec-Minor-2 — `enable_execute_command = true` på dev-services ger debug-eskalering vid komprometterad task-role

**Fil:** `infra/terraform/modules/ecs/main.tf:172` (api) + `:225` (worker).

`enable_execute_command` = true tillåter `aws ecs execute-command` (interaktivt shell i container). I dev acceptabelt för felsökning. **Men** — execute-command i en task med task-role-api ger:
- Direkt-access till alla secret-värden via `env` (secrets är injicerade som env-vars)
- Direkt-access till mounted volumes
- Möjlighet att exfiltrera Bedrock-tokens, DB-passwords i klartext via shell-output

GDPR-bedömning: ingen direkt PII-läcka i dev (placeholder-secrets). Men disciplinen att ECS Exec ska gå via audit-loggad CloudTrail bör dokumenteras innan staging/prod.

**Fix-väg (defererat):** Slå av `enable_execute_command` i staging/prod via env-specifik tfvars; kräv `aws ecs run-task` för debug istället.

**Status:** Defererad — acceptabelt i dev. Lyft i staging-promote-runbook.

### Sec-Minor-3 — Bedrock-policy-attach via `data "aws_iam_policy"`-lookup utan validering = silent drift

**Fil:** `infra/terraform/environments/dev/main.tf:118-120` + `modules/iam_ecs/main.tf:194-197`.

`data "aws_iam_policy" "bedrock_invoke" { name = "JobbPilotBedrockInvoke" }` — om någon raderar policy:n från baseline-stacken eller byter namn (t.ex. case-sensitivity drift `"jobbpilotBedrockInvoke"`), faller `terraform plan` med svårtolkat felmeddelande långt nedströms. Värre: om policy:n omdirigeras (ny policy med samma namn men annan policy-document) attachas den nya tystt vid nästa apply.

**Fix:** Lägg till `lifecycle.precondition` på data-source:

```hcl
data "aws_iam_policy" "bedrock_invoke" {
  name = "JobbPilotBedrockInvoke"

  lifecycle {
    postcondition {
      condition     = self.policy != null && length(self.policy) > 0
      error_message = "JobbPilotBedrockInvoke-policy hittades men är tom — verifiera baseline-stacken."
    }
  }
}
```

Plus: tagga policy:n i baseline med `Purpose = "ecs-task-bedrock-invoke"` + dokumentera i `docs/runbooks/aws-setup.md` att namnet är ett **kontrakt** mellan baseline + dev-stack.

**Status:** Defererad. Defensiv hygien, inte säkerhetsblock.

### Sec-Minor-4 — `secrets`-injection injicerar **hela** secret-värdet som env-var = secret synligt i `/proc/1/environ`

**Fil:** `infra/terraform/modules/ecs/main.tf:69-71` + `:131-133`.

ECS task-def `secrets`-block läser secret-värdet från Secrets Manager och **sätter det som klartext-env-var** i containern. Det är AWS standard-mönster och bättre än hårdkodat — men:

1. Container-`env` är synligt via `aws ecs describe-tasks` (för IAM-principals med `ecs:DescribeTasks` — inkl. `enable_execute_command`-användare)
2. `/proc/1/environ` läsbart för alla processer i container-namespace (i container med en process = ingen, men om malicious code injiceras ger eskalering)
3. `dotnet`-default `IConfiguration` läser env-vars → process-memory har klartext-secret

**Bättre alternativ** (defererat till Fas 1+):
- `secrets-store-csi-driver` mountar secret som file istället för env-var → läses via `IConfiguration.AddJsonFile` med restricted file-permissions
- `valueFrom`-ARN istället för secret-värde direkt: ARN visas i task-def, värdet hämtas runtime utan att hamna i `env`

**Status:** Defererad — ECS task-def `secrets`-block är AWS-standard och bättre än alternativen för Fas 0/1. Lyft som tech-debt vid eventuell GDPR-DPIA.

### Sec-Minor-5 — `readonlyRootFilesystem = false` på api + worker — acceptabel pragmatik men förtjänar follow-up-tech-debt

**Fil:** `infra/terraform/modules/ecs/main.tf:83-84` (api) + `:144-145` (worker).

ASP.NET Core skriver till `/tmp` (DataProtection-keys, Razor-compilation-cache); writeable rootfs är default-pragmatisk. **Men** — readonly rootfs är defense-in-depth mot:
- Webshell-uppladdning vid komprometterad endpoint
- Persistent malware via cron/systemd-replacement
- Container-escape-tekniker som skriver till `/var/run`

**Fix-väg (defererat):**

```json
{
  "readonlyRootFilesystem": true,
  "mountPoints": [
    { "sourceVolume": "tmp", "containerPath": "/tmp", "readOnly": false },
    { "sourceVolume": "dataprotection", "containerPath": "/home/app/.aspnet/DataProtection-Keys", "readOnly": false }
  ],
  "volumes": [
    { "name": "tmp" },          // ephemeral tmpfs
    { "name": "dataprotection" } // motsvarande
  ]
}
```

DataProtection-keys är *redan* synkade via Redis (ADR 0014), så lokal `/home/app/.aspnet/DataProtection-Keys` är ironisk redundans — kan tas bort vid samma fix.

**Status:** Defererad. Tech-debt-värdig, inte blocker.

### Sec-Minor-6 — `image_tag_mutability = "MUTABLE"` i dev — acceptabel men cementerar latent supply-chain-risk

**Fil:** `infra/terraform/modules/ecr/variables.tf:12-16` + `dev/main.tf:127` (default `MUTABLE`).

`MUTABLE` betyder `latest`-tag kan skrivas över → en "approved" deploy kan bytas tyst om någon pushar ny image med samma tag. Lean dev acceptabelt för iteration; STEG 14 GitHub Actions-pipeline ska sätta SHA-tags vilket motverkar detta. **Men** — när 14 är klar bör default flippas till `IMMUTABLE` på alla envs, även dev (SHA-tags fungerar lika bra).

**Status:** Defererad — acceptabel pragmatik Fas 0. Lyft som "STEG 14 follow-up: flippa till IMMUTABLE när SHA-tags är primary tagging-mönster".

### Sec-Minor-7 — `wait_for_steady_state = false` på Api-service döljer deploy-failures vid initial smoke

**Fil:** `infra/terraform/modules/ecs/main.tf:201`.

Vid `terraform apply` returnerar Terraform direkt utan att vänta på att tasks är `RUNNING + healthy`. Konsekvens: om image-pull misslyckas (initial `latest`-tag saknas i ECR innan första `docker push`), märks det inte i Terraform-output — bara via ECS-konsol-inspection eller via `aws ecs describe-services`.

Inte direkt säkerhets-fynd, men opspolicy-relevant: Klas kan tro att 13b-apply lyckats medan service faktiskt är i `PROVISIONING`-loop.

**Fix:** sätt `wait_for_steady_state = true` för dev när image-pipeline är stabil (post-STEG-14). I lean dev: dokumentera procedur "efter `terraform apply`: `aws ecs describe-services --cluster ... --services ... --query 'services[0].deployments'` för att verifiera roll-out".

**Status:** Defererad. Operations-runbook-task.

### Sec-Minor-8 — `/api/ready` är "live"-check, inte "ready" — felklassificering kan dölja DB/Redis-degraderad service

**Fil:** `src/JobbPilot.Api/Program.cs:123`.

Endpoint returnerar `200 { status: "ready", service: "JobbPilot.Api" }` utan att verifiera (a) DB-connectivity, (b) Redis-connectivity, (c) Bedrock-routability. Det är konsistent med kommentaren i task-context ("medvetet 'live' check"), men namngivningen (`/api/ready`) implicerar Kubernetes-style readiness — vilket per konvention betyder "redo att ta trafik" inkl. backing-services.

Konsekvens: ALB-target-group-health-check passerar även om DB är nere → ALB skickar trafik till en task som genast returnerar 500/503 från `/api/v1/*`-endpoints. Kan döljas under load-balancing hos övriga healthy tasks, eller manifestera som intermittent 5xx för slutanvändare.

GDPR-bedömning: ingen direkt PII-fråga. Operations-fråga med säkerhets-implication: Ghosted-detection-jobs (BUILD.md) kan misstas för läge "service är upp" när det egentligen är "service kan inte göra något meningsfullt".

**Fix-väg (rekommenderat för STEG 14 eller follow-up):** Splitta:
- `/api/live` — process är vid liv (returnerar omedelbart, används som container-level HEALTHCHECK)
- `/api/ready` — alla deps OK (DB-ping + Redis-PING), används av ALB target-group

```csharp
app.MapGet("/api/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/api/ready", async (IAppDbContext db, IConnectionMultiplexer redis, CancellationToken ct) =>
{
    var dbOk = await db.PingAsync(ct);
    var redisOk = (await redis.GetDatabase().PingAsync()).TotalMilliseconds < 500;
    return dbOk && redisOk
        ? Results.Ok(new { status = "ready", db = "ok", redis = "ok" })
        : Results.StatusCode(503);
});
```

**Status:** Defererad. Lyft som STEG-13b-follow-up eller del av STEG 14.

### Sec-Minor-9 — `curl` installerad i Api runtime-image för HEALTHCHECK = onödig attack-yta

**Fil:** `src/JobbPilot.Api/Dockerfile:40-42`.

`curl` är binärt + libcurl + libssl + dependencies. Vid komprometterad task ger curl direkt-tooling för:
- Lateral exfiltration (POST PII till externa endpoints)
- DNS-tunnel via curl --resolve
- Loading av remote shellcode via curl + bash

**Alternativ:** ASP.NET-image i .NET 8+ levererar inte `curl` by default — men `wget` finns inte heller. **Bättre HEALTHCHECK utan extern binär:**

```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD test -f /tmp/.healthy || exit 1
```

…och låt ASP.NET-app skriva `/tmp/.healthy` vid first-request. Eller använd .NET-built-in:

```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "/app/healthcheck.dll"]
```

…där `healthcheck.dll` är en miniprogram som anropar `localhost:8080/api/ready`. Mer setup, mindre attack-yta.

**Praktisk dom:** behåll curl i Fas 0 (det är *the standard*), men dokumentera som tech-debt. Mitigerad delvis via:
- `apt-get clean`-mönstret i Dockerfile (rad 42) — bra
- Non-root user — bra
- `readonlyRootFilesystem` skulle hjälpa (Sec-Minor-5)

**Status:** Defererad. Lyft som tech-debt.

---

## Nit

### Sec-Nit-1 — `ConnectionStrings__Postgres` injiceras i **både** Api och Worker

**Fil:** `infra/terraform/environments/dev/main.tf:222-231`.

Worker har `ConnectionStrings__Postgres` (app-rollen `jobbpilot_app`) **+** `ConnectionStrings__HangfireStorage` (worker-rollen `jobbpilot_worker`). Komment-trädet i 13a indikerar att Worker bara ska skriva DML till `hangfire.*`-schema via `jobbpilot_worker`, men `jobbpilot_app`-connection ger Worker DDL/DML på alla scheman.

Granska: är detta avsiktligt (Worker behöver app-data för t.ex. ghosted-detection)? Eller är det defensivt-glömt? STEG 14:s `Install.sql` ska skapa `jobbpilot_worker`-rollen med snäva grants — om Worker har `jobbpilot_app` också kringgås den least-privilege-modellen.

**Status:** Klas verifierar avsikt. Inte ett block — bara dubbel-injection som förtjänar kommentar i task-def.

### Sec-Nit-2 — `aws_region`-variabel passas till `ecs`-modul, men `data "aws_region" "current"` används redan i `iam_ecs`

**Filer:** `modules/ecs/variables.tf:6-9` + `modules/iam_ecs/main.tf:102`.

Inkonsistent mönster. `data "aws_region"` är primary-source-of-truth (läses från provider-config); `var.aws_region` är duplikat som kan drifta. Inte säkerhetsfråga, bara hygien.

**Status:** Defererad.

### Sec-Nit-3 — `lifecycle.create_before_destroy = true` på `aws_lb_target_group` är bra praxis

**Fil:** `modules/alb/main.tf:64-66`. **Praise.**

### Sec-Nit-4 — Defense-in-depth `aws:SourceAccount`-condition på alla 3 task-roller

**Fil:** `modules/iam_ecs/main.tf:16-20`. **Praise** — confused-deputy-skydd korrekt.

### Sec-Nit-5 — `kms:ViaService`-condition på Decrypt-statements separerar SM/RDS/EC från generic decrypt

**Fil:** `modules/iam_ecs/main.tf:94-98` + `:153-161` + `:233-241`. **Praise** — exakt rätt mönster, lärt från Sec-Major-1 i 13a-rapporten.

### Sec-Nit-6 — Worker-task-def `stopTimeout = 30` med dokumenterad `ShutdownTimeoutSeconds = 25` margin

**Fil:** `modules/ecs/main.tf:152`. **Praise** — TD-17 punkt 6 explicit hedrad.

---

## Praise

- **Komposition via `data "aws_iam_policy"`-lookup** för Bedrock-policy istället för cross-stack `terraform_remote_state` — undviker state-koppling.
- **Worker-task-role saknar Bedrock-attach** (modules/iam_ecs/main.tf:200-268) — ADR 0023 + Fas 1-disciplin korrekt implementerat. Worker får inte tyst Bedrock-access.
- **Worker har inga portMappings, inga ALB-target-groups, inga healthCheck** (modules/ecs/main.tf:103-160) — HTTP-fri på alla axlar (ADR 0023).
- **`drop_invalid_header_fields = true` på ALB** (modules/alb/main.tf:17) — defense mot HTTP smuggling.
- **`deployment_circuit_breaker { enable = true, rollback = true }`** på båda services (modules/ecs/main.tf:192-195 + :239-242) — automatisk roll-back vid deploy-fail.
- **`assign_public_ip = false`** på båda ECS services (rad 183 + 236) — tasks i private subnets only, ingen direkt internet-access utöver NAT.
- **`enable_deletion_protection = false` i dev men variabel** (alb/variables.tf:63-67) — staging/prod-skiftar kontrolleras explicit.
- **Inga klartext-secrets i `api_environment` eller `worker_environment`** (dev/main.tf:234-246) — bara host/port/CIDR/scheme. AUTH-token + connection-strings via `secrets`-block. Klar separation.
- **`stickiness { enabled = false }` på ALB target-group** (modules/alb/main.tf:54-57) — Api är stateless, sessions i Redis. Kommentar dokumenterar designval.
- **`ELBSecurityPolicy-TLS13-1-2-2021-06`** vid HTTPS-listener (modules/alb/main.tf:118) — TLS 1.2/1.3 only, korrekt 2026-baseline.
- **CloudWatch LogGroups explicit deklarerade med 30d + KMS** (modules/cloudwatch_logs/main.tf:7-19) — ADR 0024 D7 hedrad. Ingen "Never expire"-glidning.
- **ECR scan_on_push + KMS-encryption** (modules/ecr/main.tf:12-19) — supply-chain-baseline.
- **ECR lifecycle-policy `Keep last 10 images`** (modules/ecr/main.tf:36-50) — storage-bound, undviker cost-spillover.
- **Dockerfile non-root `USER app`** (Api:48 + Worker:41) — uid 1654 (default i .NET 8+ images), korrekt.
- **Pinned image-version `10.0-bookworm-slim`** (Api:7+38, Worker:7+36) — inte `latest`, försäkring mot supply-chain.
- **`apt-get install --no-install-recommends curl && rm -rf /var/lib/apt/lists/*`** (Api:40-42) — minimal image-storlek + cleanup.
- **`.dockerignore` exkluderar `.env`, `appsettings.Local.json`, `.secrets/`, `.git/`, `infra/`, `docs/`** — inga secrets eller infra-state i build-context.
- **Multi-stage build med separat `sdk` + `aspnet`-image** — build-tools (kompilator, NuGet-cache) hamnar inte i runtime-image.
- **`UseAppHost=false` i dotnet publish** (Api:31, Worker:28) — slipper extra apphost-binär (mindre image-storlek + mindre attack-yta).

---

## Sammanfattning

**2 Major:**
- **Sec-Major-1** (HTTP-only ALB) — kräver ADR-beslut innan apply (B-alternativet) eller fix till HTTPS via egen domän/cert (A-alternativet). Default `alb_https_enabled = false` accepteras inte tyst.
- **Sec-Major-2** (`UseHttpsRedirection` vs HTTP-only-ALB) — operationell defekt som hör ihop med Sec-Major-1; oavsett A eller B måste redirect-middleware miljö-gateas.

**9 Minor + 6 Nit defererade.** Sec-Minor-8 (`/api/ready` utan dep-check) och Sec-Minor-1 (`aws:RequestedRegion`-condition på SSM Messages) är rekommenderade men inte block.

**Inga Critical. Inga GDPR-blockers i ren infra-kod.** GDPR-relevansen i Sec-Major-1 (auth-tokens i klartext) är reell men adresseras via ADR + tidsfönster — inte ett ren-Blocker som STEG 13a:s secret-i-log-mönster.

**Block-status:** Approved för apply efter Sec-Major-1 + Sec-Major-2 fixad/ADR-accepterad. ECS-egress 0.0.0.0/0 är redan accepterad via ADR 0025 (carryover från 13a Sec-Major-2).

**Stommen är solid.** IAM least-privilege rätt komponerad (kms:ViaService, aws:SourceAccount, separata roller per service, ingen Bedrock på Worker), secrets-injection-arkitektur korrekt, Dockerfile non-root + minimal-image, ALB drop_invalid_header_fields + TLS 1.3-policy redo. De stora Major-fynden är båda kring HTTP/HTTPS-transport på dev-yta — designval-fråga, inte arkitekturbrist.

**Eskalering till Klas:** Sec-Major-1 har GDPR-art-32-implikation (auth-tokens i transit utan TLS) — kräver explicit beslut: domän-registrering nu (A) eller dokumenterad accepted-risk-ADR med tidsfönster (B). C (block tills HTTPS) inte rekommenderat operationellt.
