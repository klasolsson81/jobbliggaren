# .NET-architect-review: STEG 13b-fix-paket (verifiering av kritiska + viktiga fynd)

**Status:** OK — alla kritiska fynd från ursprungliga reviewen är korrekt åtgärdade
**Granskat:** 2026-05-09

**Granskat scope:**
- `infra/terraform/modules/redis/main.tf` + `outputs.tf`
- `infra/terraform/environments/dev/main.tf`
- `infra/terraform/modules/ecs/main.tf`
- `src/JobbPilot.Api/Program.cs`
- `src/JobbPilot.Api/Dockerfile`
- `src/JobbPilot.Worker/Dockerfile`
- `src/JobbPilot.Worker/Program.cs`
- `src/JobbPilot.Infrastructure/DependencyInjection.cs`
- `docs/tech-debt.md` (TD-29, TD-30)

---

## Bekräftade fixar

### [OK] Redis CS-fix

`infra/terraform/modules/redis/main.tf:70-89` + `outputs.tf:24-27` + `environments/dev/main.tf:158, 228`.

`secret_string` komponeras via `format("%s:%d,password=%s,ssl=True,abortConnect=False", primary_endpoint_address, port, auth_token)` — exakt StackExchange.Redis ConfigurationOptions-format. `ConnectionMultiplexer.Connect(string)` (`DependencyInjection.cs:131`) och `AddStackExchangeRedisCache(opts.Configuration = ...)` (`DependencyInjection.cs:118-122`) parsar detta direkt utan kod-ändring.

`ssl=True` matchar `transit_encryption_enabled = true` (`redis/main.tf:113`) — encryption-toggle och CS-flag är synkroniserade.

Plan-tids-säkerhet OK: `aws_elasticache_replication_group.this.primary_endpoint_address` är known-after-apply, så `secret_version` uppdateras vid apply när endpoint finns. Ingen tom sträng vid plan.

IAM utvidgad korrekt (`dev/main.tf:158`) — execution-rollen kan läsa nya secret-ARN.

Klartext `Redis_Host`/`Redis_Port` borttaget (verifierat via grep — 0 träffar).

### [OK] UseHttpsRedirection env-gate

`src/JobbPilot.Api/Program.cs:114-124`. `GetValue<bool>("Alb:HttpsEnabled")` returnerar default `false` när nyckel saknas — Production utan flag skippar `UseHttpsRedirection()`. Korrekt fix mot Sec-Major-2.

ASP.NET Core configuration-binder parsar string `"false"`/`"true"` (case-insensitive) till bool — `tostring(var.alb_https_enabled)` (`dev/main.tf:246`) ger giltig string-input.

Development-OR ger oss dev-cert-redirect via Kestrel — bevarar lokal HTTPS.

### [OK] Api Dockerfile

`src/JobbPilot.Api/Dockerfile:37-43`. `apt-get install curl` borttaget; ingen `HEALTHCHECK`-direktiv. Kommentaren motiverar varför (Fargate ignorerar Docker HEALTHCHECK; ALB är auktoritativ). Mindre attack-yta.

### [OK] Worker Dockerfile-kommentar

`src/JobbPilot.Worker/Dockerfile:30-36`. Klargörande av aspnet:10.0-runtime-användning + TD-19-länk till bytestig.

### [OK] /api/ready TODO

`src/JobbPilot.Api/Program.cs:124-128`. TD-29-referens + förklaring liveness vs readiness inline. Endpoint oförändrad.

### [OK] TD-29 + TD-30

`docs/tech-debt.md`. TD-29 har konkret kod-snippet (`AddDbContextCheck<AppDbContext>` + `AddRedis` + tag-filter `ready`). TD-30 binder till ADR 0026-deadline 2026-06-08 med operativa steg.

### [OK] ECS task-def utan healthCheck

`infra/terraform/modules/ecs/main.tf:86-89`. Korrekt borttagning. Container-level health-check var redundant: ALB target-group hits `/api/ready` direkt via service-registration. Container-crash detekteras via exit code; ECS startar om automatiskt.

### [OK] Worker konfig-minimering

`worker_secrets` (`dev/main.tf:234-237`): bara `Postgres` + `HangfireStorage`. `worker_environment` (`dev/main.tf:252-254`): bara `DOTNET_ENVIRONMENT`. Verifierat mot `Worker/Program.cs` — `AddIdentityAndSessions` laddas inte (grep visar noll Redis-referenser i Worker-projektet). `AddCoreIdentityForWorker` är HTTP-fri och rör inte Redis.

---

## Kvarvarande issues

Inga kritiska eller viktiga fynd.

### [Nice-to-have]

`infra/terraform/modules/redis/main.tf:84` — `format()` använder positional args; ett mer self-documenting alternativ vore en `local`-variabel `redis_cs = "${endpoint}:${port},password=${token},ssl=True,abortConnect=False"` med inline-kommentar per fält. Strikt taget kosmetiskt — funktionellt ekvivalent.

### [Nice-to-have]

Worker `worker_secrets` har både `Postgres` + `HangfireStorage` pekande på olika DB-roller. Det är korrekt per TD-17 punkt 4, men en kommentar ovanför `worker_secrets` som länkar till TD-17/hangfire-schema.md skulle göra Terraform självförklarande för framtida läsare.

---

## Praise

- **Lifecycle-pinning av `auth_token`** (`redis/main.tf:128-134`) är bra defensive design — förhindrar oavsiktlig auth-token-rotation via TF apply utan medveten secret-rotation-rutin.
- **Dual-secret-strategin** (raw `auth_token` + komponerad `connection_string`) ger både debug/rotation-värde och clean app-injection.
- **Inline-kommentar** vid `Alb__HttpsEnabled` (`dev/main.tf:239-247`) länkar till ADR 0026 + Sec-Major-2 + Program.cs-referens — gör Terraform-koden tracable mot review-trail.
- **Konfig-driven gate** (env-var > kompilerings-tid-flag) följer §11.2 i CLAUDE.md (allt via `IOptions<T>`/`IConfiguration` snarare än hårdkodat).

## Referenser

- CLAUDE.md §2.1 — Clean Architecture lager-gränser (Worker/Api separation av Redis-konfig respekterad)
- CLAUDE.md §5.1 — Konfiguration via `IOptions<T>`/`IConfiguration` (inte hårdkodad)
- ADR 0026 — ALB HTTP-only Fas 0, deadline 2026-06-08 (TD-30)
- BUILD.md §15.4 — `/api/ready` health-endpoint-spec
