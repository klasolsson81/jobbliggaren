# Arkitektur-dom: ADR 0050 deployment-migration (AWS-exit → Hetzner VPS)

**Roll:** dotnet-architect (obligatorisk för IaC/infra-scope, CLAUDE.md §9.2 + ADR 0036-precedens). **Status:** strategisk design-dom, ingen kod/provisionering. **Mottagare:** senior-cto-advisor (decision-maker på multi-approach) + Klas (ADR Proposed→Accepted). **On-disk verifierat 2026-06-08** mot `JobbPilot.Migrate/Program.cs`, `Directory.Packages.props`, `Infrastructure/DependencyInjection.cs`, `Api/Configuration/ForwardedHeadersConfig.cs` + `AlbOptions.cs`, `.github/workflows/`.

---

## Sammanfattning

ADR 0050:s kärnbeslut (full AWS-exit, single-box Hetzner, Vercel-FE, Cloudflare-edge) håller efter AWS-teardown och LocalDataKeyProvider-skiftet. **Tre punkter behöver dom utöver ADR 0050:s nuvarande text:**

1. **Sizing-axeln (Major, multi-approach → CTO):** ADR 0050 missade ARM. Min rekommendation är **CAX31 (ARM, 8 vCPU / 16 GB / 160 GB, ~€16/mån)** — inte CX32. Motivering nedan. Detta är ett genuint multi-approach-val; CTO avgör.
2. **Migrations-vägen (Major, entydig):** `JobbPilot.Migrate` är hårt AWS-Secrets-Manager-bunden i fem mode-grenar. Re-homing är icke-trivialt och blockerar all deploy. Entydig dom: env/IConfiguration-conn-strings + behåll two-phase least-privilege.
3. **Reverse-proxy/ForwardedHeaders (entydig):** Befintlig `ForwardedHeadersConfig` är redan proxy-portabel — den behöver bara ny CIDR-overlay, inte omskrivning. **Caddy** rekommenderas entydigt.

Resten (deploy-topologi, Worker, logg-sink, AWS-kodhygien) har övervägande entydiga domar med tydliga TD-avgränsningar för Hetzner-fasen.

---

## 1. Sizing — CX32 (x86) vs CAX31 (ARM 8/16) vs CAX21 (ARM 4/8)

**[Major — genuint multi-approach-val, CTO avgör]**

### ARM-kompatibilitet i stacken (web + on-disk verifierat)

Hela stacken är ARM64-ren 2026:
- **.NET 10** — förstklassigt `linux-arm64`-stöd; officiella `mcr.microsoft.com/dotnet/aspnet:10.0`-multiarch-images. ARM64 tier-1-RID sedan .NET 6.
- **Npgsql 10.0.3 / EFCore.PostgreSQL 10.0.2** (verifierat i `Directory.Packages.props`) — managed ADO.NET, ingen native ARM-risk.
- **Hangfire.PostgreSql 1.21.1** — ren SQL/managed, arkitektur-agnostisk.
- **PostgreSQL 18.3** — officiella `postgres:18`-images multiarch (arm64).
- **Redis / Caddy** — officiella arm64-images/binaries.
- **StackExchange.Redis, Refit, Polly, FluentValidation, Mediator.SourceGenerator** — managed/IL, source-generators körs på build-host (CI), inte target.

**Enda historiska ARM-fällan** (`System.Drawing.Common` native libgdiplus) finns inte i stacken — CV-PDF-rendering är Fas-4-gated (`project_cv_pdf_features_gated_on_fas4_ai`). Framtida verifikationspunkt, ej blocker nu.

**Slutsats ARM-kompat:** Ingen blockerande inkompatibilitet. ARM-risken är **låg**.

### RAM-axeln mot single-box-co-tenancy

På single-box delar API + Worker + Postgres + Redis **samma RAM-feldomän**:
- **Postgres-co-tenant:** 46k-korpus med `raw_payload jsonb` + STORED generated columns (ssyk/region) + FTS GIN-index + pg_trgm. Effektiv sök kräver att `shared_buffers` + OS page-cache rymmer hot-index. På 8 GB total box måste Postgres dela med två .NET-runtimes + Redis → realistiskt `shared_buffers` ~1–2 GB, trångt.
- **Ingestion-OOM-vektorn (dimensionerande risk):** `JobTechStreamClient` har `MaxResponseContentBufferSize = 500_000_000` (500 MB, `DependencyInjection.cs` rad 111) + `client.Timeout = 5 min`. Worst-case snapshot-buffer + dedup/upsert-working-set kan momentant ta 0,5–1+ GB i Worker-processen på samma box som Postgres servar ingestion-upserts. Exakt vad ADR 0050 redan identifierade som skälet CX32 över CX22.
- **Korpus-tillväxt:** 46k är baseline; full korpus + retention växer. Headroom = driftstabilitet, inte lyx.

### Domen

| Box | vCPU/RAM/Disk | ~€/mån | Bedömning |
|---|---|---|---|
| CAX21 (ARM) | 4 / 8 / 80 | ~€6–7 | **Avråds.** RAM-paritet med CX32, samma 8 GB-trångboddhet. |
| CX32 (x86) | 4 / 8 / 80 | ~€6,80 | ADR 0050:s val. Fungerar men ingen RAM-marginal mot ingestion-spik + tillväxt. x86 ger noll fördel (ingen x86-only-dep). |
| **CAX31 (ARM)** | **8 / 16 / 160** | **~€16** | **Rekommenderas.** Dubbel RAM eliminerar co-tenant-trängseln single-box skapar. Dubbla cores ger Worker+API-headroom. Dubbel disk för korpus+pg_dump-staging+WAL. ARM-risk låg. |

**Rekommendation: CAX31.** ~€9/mån köper bort största single-box-risken. Kvalitet > tempo (§9.6). **CTO avgör** (kostnad-vs-headroom-tradeoff, memory `feedback_cto_decides_multi_approach`). **Källa:** AWS Well-Architected REL (single-box = medvetet SPOF för beta → RAM-headroom primär mitigering); Hetzner CAX/CX-docs (web 2026-06-08).

---

## 2. Deploy-topologi

**[Övervägande entydig; en delfråga → CTO]**

**Container vs native — Docker Compose all-in-one är rätt.** Repo har redan Dockerfiles (API/Worker/Migrate) + Compose lokalt → dev/prod-paritet (12-Factor §X, Fowler). systemd-native skulle bryta paritet för noll vinst vid denna skala.

**Postgres: i container på boxen (co-tenant), INTE Ubicloud, INTE native.** Ubicloud (~$15/mån) fördubblar budget + extern beroende. Named volume (persistent). **Major-mitigering:** volym på persistent disk + `pg_dump` → offsite är icke-förhandlingsbart (container-volym utan offsite = oacceptabel data-loss-risk).

**Redis: i container.** Precomputed cache (ADR 0064) + session-index + token-revocation. Regenererbart utom sessions (sessions-förlust = re-login, acceptabelt beta). Ingen AOF initialt.

**Blast-radius — single-box feldomän:** acceptabelt för beta MED mitigeringar: (1) offsite pg_dump nattligen, (2) restore-runbook verifierad (Ford/Parsons/Kua: backup utan restore-test = hypotes) → TD, (3) `restart: unless-stopped` + healthchecks, (4) `mem_limit` per service så Worker-OOM inte dödar Postgres.

**→ CTO-delfråga:** hårda `mem_limit` per container från start (skyddar PG men kan döda ingestion-spik) vs headroom-flexibilitet på 16 GB.

---

## 3. Reverse-proxy / TLS

**[Entydig — och enklare än ADR 0050 antyder]**

**On-disk-upptäckt:** Repo har **redan** `ForwardedHeadersConfig.cs` (TD-21/STEG 12) + `AlbOptions.cs`. `ForwardedHeadersConfig` är **proxy-agnostisk** — binder `KnownNetworks`/`KnownProxies`/`ForwardLimit` från config, parsar fail-loud, har `EnsureSafeForEnvironment`. **Behöver bara ny config-overlay för Hetzner — ej omskrivning.** `AlbOptions.HttpsEnabled` är AWS-ALB-specifik namngivning (Minor rename-fynd).

**Proxy-val: Caddy, entydigt.** Auto-HTTPS/ACME, minimal Caddyfile (~15-25% av nginx), HTTP/3, OCSP-default. nginx:s stora-fil-fördel irrelevant (API-JSON, ej stora statiska filer; de ligger på Vercel/Cloudflare). Traefik överkomplext för fix single-box-Compose.

**Cloudflare-framför-Caddy TLS (kritiskt):** Cloudflare proxy "Full (strict)" + Caddy med riktigt Let's Encrypt-cert på origin. Flexible/non-strict = klartext-sista-ben = oacceptabelt (bryter `__Host-`-cookie + PII-klartext). **ACME-utmaning:** Cloudflare-proxy kan blocka HTTP-01 → använd **DNS-01 via Caddy cloudflare-plugin** (robustast, riktiga publika cert). → TD Hetzner-fas.

**Kestrel ForwardedHeaders mot Caddy:** Caddy+Kestrel containers på samma Docker-nät. `KnownNetworks` = Docker-bridge-subnät (ej Cloudflare-ranges). Caddy `trusted_proxies cloudflare` propagerar äkta klient-IP. `ForwardLimit = 1` (Caddy). **Minor kodhygien:** `AlbOptions` + docstrings refererar "ALB:s VPC-CIDR"/`aws-setup.md` → rename `AlbOptions`→`ReverseProxyOptions` + ny `hetzner-setup.md`. → TD Hetzner-fas. **Källa:** Microsoft Learn ASP.NET Core forwarded-headers; Caddy docs (trusted_proxies/DNS-01).

---

## 4. Migrations-väg (TD-105)

**[Major — blockerande, men entydig dom]**

**On-disk-verklighet (`Migrate/Program.cs`):** Djupt AWS-bunden — 5 mode-grenar (`init/bootstrap/ensure-extensions/explain-search/schema`), var och en instansierar `AmazonSecretsManagerClient` (rad 133/227/279/353/401), `RequiredEnv("AWS_REGION")` i fyra grenar, `init` skriver conn-strings via `PutSecretValueAsync`, `ConnectionStringFactory.ForPersisted` använder `SSL Mode=VerifyFull;Root Certificate=<RDS-bundle>` (RDS-CA-specifikt).

**Dom: Re-home till env/IConfiguration; behåll two-phase least-privilege; ny TLS-postur.**
1. Ersätt `AmazonSecretsManagerClient` med conn-strings via env/Docker-secrets. Eliminerar `AWSSDK.SecretsManager` från Migrate.
2. **Two-phase least-privilege BEHÅLLS** — 3 roller (`jobbpilot_migrations`/`jobbpilot_app`/`jobbpilot_worker`, `Roles.cs`) är säkerhets-värde + arch-test-låsta. Riv inte för beta (kvalitet > tempo). Enda AWS-biten är *var lösenorden lagras*, inte *att rollerna finns*. Init-flödet förenklas (pwds från env vid provisionering); Phase A–D-SQL (REVOKE PUBLIC/CREATE ROLE/GRANTs) är AWS-agnostiskt, behålls.
3. **TLS:** mot Postgres-container på Docker-nät → `SSL Mode=Require` utan CA-bundle räcker (privat nät). `ForMigrate` (Trust=true) redan portabel.
4. **Var migrations körs:** Compose oneshot-container (`docker compose run --rm migrate ensure-extensions && ... schema`) FÖRE API/Worker-start. `Database.MigrateAsync` idempotent. **Källa:** 12-Factor §III+§V; ADR 0033-precedens.

**Legitim Hetzner-fas-TD (saknad dep: VPS-Postgres existerar ej) per §9.6.** TD-105 bekräftas.

---

## 5. Worker på samma box (ADR 0023 HTTP-fri)

**[Entydig]**

- **Hangfire-storage: Postgres (behåll), INTE Redis.** `Hangfire.PostgreSql 1.21.1` + dedikerat `hangfire`-schema + `jobbpilot_worker`-roll redan byggt. Postgres-jobb överlever Redis-restart (durabilitet). Co-tenant fine (isolerat schema/roll).
- **Ingestion-mem-profil = samma RAM-argument som sizing (punkt 1).** På CAX31 (16 GB): Worker `mem_limit` ~3–4 GB + Postgres `shared_buffers` ~2–4 GB med marginal. På 8 GB blir det knapphet. **Starkaste enskilda RAM-argumentet för CAX31.**
- **Worker delar Postgres med API:** acceptabelt på NVMe + separata Npgsql pool-storlekar per service (så ingestion inte svälter API-pool). → konfig-detalj Hetzner-fas.

---

## 6. Logg-sink (TD-104)

**[Multi-approach → CTO; ny top-level-dep kräver §9.2-motivering]**

**On-disk:** Ingen Serilog (noll PackageReference). Appen loggar via `Microsoft.Extensions.Logging` console — ren `ILogger`-abstraktion. Flera kod-kommentarer refererar Serilog felaktigt (`DependencyInjection.cs` rad 243/281) → Minor städ vid wiring. Seq-container tar emot inget.

**Dom: behåll `ILogger`-seam (DIP redan ren). Lägg INTE Serilog top-level-dep nu (§9.2). Defer till medveten observability-TD.** Sink-val när wiring sker:
- **Seq self-hosted:** paritet+EU-residens, men +RAM på box (~200-400 MB).
- **Loki+Grafana:** lättare, men 3 containers mer ops.
- **Managed (Grafana Cloud/Axiom):** noll box-RAM, men PII-residens-fråga (§5.1).
- **Lutning (CTO avgör):** Seq self-hosted om CAX31-RAM-domen går igenom; managed/Loki på 8 GB. **Sizing- och logg-domen kopplade.** Plus **CTO-fråga:** Serilog vs OTel-logs-pipeline (OTel-yta finns transitivt).

---

## 7. Döda AWS-workflows + AWSSDK-deps

**[Entydig dom med fas-split]**

**On-disk:** Döda workflows `deploy-dev.yml` (OIDC→ECR→ECS mot riven stack) + `rds-ca-bundle-check.yml`. AWSSDK-deps i 5 csproj: `AWSSDK.KeyManagementService` (Infrastructure + 3 testprojekt, medvetet behållet referens-impl), `AWSSDK.SecretsManager` (Migrate), `AWSSDK.Core 4.0.6.1` (CVE-pinnad transitiv).

**Rensa NU (in-block, döda ej referens):** `deploy-dev.yml` + `rds-ca-bundle-check.yml` — pekar på riven stack, vilseleder. Ren död config, ingen dependency → in-block-städ, INTE TD (§9.6). **Caveat (`feedback_dont_delete_auto_files`):** handskrivna deploy-workflows (ej auto-scaffolding) → borttagning OK, men `.github/`-touch → flagga Klas-GO.

**DEFER (TD/policy):** `AWSSDK.SecretsManager` tas bort NÄR Migrate re-homas (hård TD-105-dep). `AWSSDK.KeyManagementService` + `AWSSDK.Core` — **BEHÅLL** (ADR 0049/0066-reversibilitet, `Provider=Kms` levande referens). Ingen TD — stående policy.

---

## 8. Nya TDs för Hetzner-fasen

Per §9.6: Hetzner-provisionering = annan fas → legitima TDs. Föreslagna (ID-allokering vid skrivning):

| Föreslagen TD | Severity | Beroende | Kärna |
|---|---|---|---|
| Migrate AWS-Secrets-Manager-avkoppling | Major | VPS-Postgres | Punkt 4 (= TD-105) |
| Hetzner Compose-stack + reverse-proxy | Major | Box | Caddy, CF Full-strict + DNS-01, ForwardedHeaders-overlay, restart/healthcheck, mem_limit |
| `AlbOptions`→`ReverseProxyOptions` rename | Minor | Compose-stack | Punkt 3 docstring-städ |
| pg_dump → backup + restore-runbook | Major | VPS-Postgres | Single-box durability |
| Logg-sink-wiring (TD-104) | Major | Box+sink-val | Punkt 6 |
| Transaktionell mejl (Hetzner) | Minor | Box+provider | = TD-101 |
| Connection-pool-budgetering | Minor | Compose-stack | Punkt 5 |
| Hetzner deploy-workflow | Major | Compose-stack | Ersätter raderad deploy-dev.yml |

**INTE TDs:** KMS-AWSSDK-borttagning (reversibilitets-policy); döda-workflow-borttagning (in-block).

---

## Referenser

- CLAUDE.md §2.1, §9.2, §9.5, §9.6, §11.3
- ADR 0023 (Worker HTTP-fri), ADR 0033/0034 (Migrate CLI-mode), ADR 0049/0066 (KMS referens-impl), ADR 0050
- Microsoft Learn — ASP.NET Core forwarded headers; .NET linux-arm64 tier-1
- AWS Well-Architected REL; Fowler (dev/prod-paritet); Ford/Parsons/Kua (migration-fitness, backup-restore-test)
- On-disk: `Migrate/Program.cs`, `ConnectionStringFactory.cs`, `Roles.cs`, `Directory.Packages.props`, `DependencyInjection.cs`, `ForwardedHeadersConfig.cs`, `AlbOptions.cs`, `.github/workflows/deploy-dev.yml`

**Tre punkter för CTO (multi-approach):** (1) Sizing CAX31 vs CX32 vs CAX21; (2) Compose mem_limit-mekanik; (3) Logg-sink + Serilog-vs-OTel.
