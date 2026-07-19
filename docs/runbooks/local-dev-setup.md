# Local dev-setup — Jobbliggaren

Lokal utveckling bygger på Docker Compose-stack:en i [`docker-compose.yml`](../../docker-compose.yml).
Denna fil beskriver hur du kommer igång från nyklonad repo.

---

## 1. Förkrav

| Verktyg | Version | Installation (Windows) |
|---|---|---|
| Docker Desktop | modern (Engine 28+) | `winget install Docker.DockerDesktop` |
| Docker Compose | v2.x (bundlad) | kommer med Docker Desktop |
| Git | modern | kommer med Git for Windows |
| openssl | (för att generera .env-lösenord) | bundlat med Git for Windows (`/mingw64/bin/openssl`) eller `winget install FireDaemon.OpenSSL` |

Starta Docker Desktop innan du kör compose-kommandon.

---

## 2. Första start

### 2.1 Klona + .env-setup

```bash
git clone https://github.com/klasolsson81/jobbliggaren.git
cd jobbliggaren
cp .env.example .env
```

Generera starka lösenord. På bash/Git Bash/WSL:

```bash
{
  echo "POSTGRES_PASSWORD_DEV=$(openssl rand -hex 16)"
  echo "POSTGRES_PASSWORD_TEST=$(openssl rand -hex 16)"
  echo "REDIS_PASSWORD_DEV="
} > .env
```

På PowerShell:

```powershell
@"
POSTGRES_PASSWORD_DEV=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
POSTGRES_PASSWORD_TEST=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
REDIS_PASSWORD_DEV=
"@ | Out-File -Encoding utf8 .env
```

`.env` är gitignored — committa aldrig. Kontrollera:

```bash
git check-ignore -v .env
# → .gitignore:6:.env	.env
```

### 2.2 Starta default-profile (dev)

```bash
docker compose up -d
```

Tre containrar startar (namn/portar per `docker-compose.yml`):
- `jobbliggaren-postgres-dev` på `5435` (db: `jobbliggaren`, user: `jobbliggaren`)
- `jobbliggaren-redis-dev` på `6379`
- `jobbliggaren-seq` på `5341` (UI + API) och `5342` (ingestion)

### 2.3 Verifiera

```bash
# Status (alla ska vara healthy, Seq up)
docker compose ps

# Postgres
docker exec jobbliggaren-postgres-dev psql -U jobbliggaren -d jobbliggaren -tAc "SELECT version();"
# → PostgreSQL 18.3 ...

# Redis
docker exec jobbliggaren-redis-dev redis-cli ping
# → PONG

# Seq UI
curl -I http://localhost:5341
# → HTTP/1.1 200 OK
```

Öppna http://localhost:5341 i webbläsaren för Seq-dashboarden.

### 2.4 App-config (krävs innan .NET-stacken startar)

Docker-tjänsterna ovan räcker inte — API:t och Worker:n fail-fast-validerar flera options
vid start. Kopiera config-mallen och fyll i:

```bash
cp src/Jobbliggaren.Api/appsettings.Local.json.example src/Jobbliggaren.Api/appsettings.Local.json
# generera EN nyckel per krävd sektion (öppna .example för den fullständiga listan) och
# klistra in dem i appsettings.Local.json:
openssl rand -base64 32   # → FieldEncryption:LocalMasterKeyBase64
openssl rand -base64 32   # → AuditPseudonymization:PepperBase64
openssl rand -base64 32   # → CompanyWatchPseudonymization:PepperBase64
openssl rand -base64 32   # → CvReviewFingerprintPseudonymization:PepperBase64
```

`appsettings.Local.json` är gitignored — committa aldrig. Mallen (`.example`) är spårad och är
källan till sanning för *vilka* lokala nycklar som krävs; hamnar en ny obligatorisk
`ValidateOnStart`-option ska den läggas till i mallen **OCH §7:s fälla-4-lista i SAMMA PR** som
optionen (dev-boot-config-contract, CLAUDE.md §11 — annars fail-fast:ar nästa stack-ägares boot
en krasch i taget). Att starta .NET-stacken: §7.

---

## 3. Test-profilen

Separata instanser på andra portar — används av integration-tester så de
kan köra parallellt med dev-stacken.

```bash
docker compose --profile test up -d
```

Två extra containrar:
- `jobbliggaren-postgres-test` på `5433` (db: `jobbliggaren_test`, user: `jobbliggaren`)
- `jobbliggaren-redis-test` på `6380`

Verifiera:

```bash
docker exec jobbliggaren-postgres-test psql -U jobbliggaren -d jobbliggaren_test -tAc "SELECT version();"
docker exec jobbliggaren-redis-test redis-cli ping
```

Stäng ner:

```bash
docker compose --profile test stop
```

---

## 4. Full-profile

Startar **allt** (default + test) i en kommando. Användbart när man
kör E2E-tester mot verklig stack.

```bash
docker compose --profile full up -d
```

---

## 5. Vanliga operationer

```bash
# Visa status
docker compose ps

# Tail logs
docker compose logs -f postgres-dev
docker compose logs --tail=50 seq

# Stanna allt (behåller data)
docker compose --profile full stop

# Starta allt igen
docker compose --profile full start

# Riv allt inkl. volymer (MISTER DATA — kör endast vid behov)
docker compose --profile full down -v
```

---

## 6. Troubleshooting

### 6.1 Port-konflikter

Om `docker compose up` säger `Bind for 0.0.0.0:5432 failed: port is already allocated`:

- En annan postgres-instans kör lokalt. Stoppa den eller ändra port i compose-filen.
- På Windows: `netstat -ano | findstr :5432` → visar PID → `taskkill /PID <pid> /F`

Samma procedur för 5433 (test-postgres), 6379/6380 (redis), 5341/5342 (seq).

### 6.2 Docker Desktop inte igång

`error during connect: ... The system cannot find the file specified.` → starta
Docker Desktop och vänta på "Engine running"-statusen i dess tray-ikon.

### 6.3 Postgres-volym korrupt

Om postgres-containern restartar med fel som refererar `initdb` eller
`could not read system configuration`:

1. `docker compose down` (utan `-v` — behåll volymer för diagnostik först).
2. `docker compose logs postgres-dev` — leta efter orsaken.
3. Om det är en tom/corrupt volym efter avbruten init:
   ```bash
   docker compose down -v          # raderar volymerna
   docker compose up -d             # Postgres re-initierar
   ```

### 6.4 Seq `firstRun.adminPassword` / `noAuthentication`-fel

Seq 2025.2+ kräver antingen admin-lösenord eller explicit no-auth. I vår
compose-fil har vi satt `SEQ_FIRSTRUN_NOAUTHENTICATION=true` — om du vill
aktivera auth lokalt:

1. Ta bort den raden från compose-filen.
2. Lägg till `SEQ_FIRSTRUN_ADMINPASSWORD=${SEQ_ADMIN_PASSWORD}` under Seq-servicen.
3. Lägg till `SEQ_ADMIN_PASSWORD=...` i `.env` + `.env.example`.
4. `docker compose up -d --force-recreate seq`.

### 6.5 Postgres 18+ volym-mount

Jobbliggaren:s compose mountar `jobbliggaren_postgres_dev_data` på
`/var/lib/postgresql` (**inte** `.../data`). Detta är det nya 18+-mönstret
som tillåter `pg_upgrade --link` vid major-uppgraderingar. Om du migrerar
från en tidigare 17-volym till 18 → läs
https://github.com/docker-library/postgres/issues/37.

### 6.6 Windows-specifika fallgropar

- **WSL2-backend**: Docker Desktop måste köra WSL2-backend för bästa
  volym-IO. Kontrollera i Docker Desktop → Settings → General.
- **Filbehörigheter**: om containern klagar på `permission denied` på
  volyme: Docker Desktop → Resources → File sharing — lägg till
  `C:\DOTNET-UTB` om det inte redan är med.

---

## 7. App-stacken (.NET API + Worker + FE) — start & restart

> **CC äger den lokala stacken helt** (Klas-direktiv 2026-06-13): CC startar, håller
> uppe och startar om Api + Worker + FE. Klas startar INTE egna terminaler. Vid
> `/api/ready ≠ 200`, död Worker, eller FE som visar fel data/login-fel → kör blocket
> nedan. Memory: `feedback_restart_stack_after_commit_stop`.

API + Worker körs **utanför** Docker Compose (compose kör bara Postgres/Redis/Seq, §2).
Alla tre startas av CC som bakgrundsprocesser.

### Fällor (varför en naiv omstart misslyckas)

1. **`${...}` expanderas INTE av .NET-config.** `appsettings.Development.json` har
   `ConnectionStrings:Postgres` med `Password=${POSTGRES_PASSWORD_DEV}`, och det finns
   **ingen** `appsettings.Local.json`. Ge därför den fulla connection-stringen via
   env-var-override (`ConnectionStrings__Postgres`), byggd från `.env`:s
   `POSTGRES_PASSWORD_DEV`. Utan den → DB-auth-fel.
2. **Worker kräver `ConnectionStrings__Redis`** (ADR 0064 — RefreshLandingStatsJob).
   API:t har Redis i appsettings; Worker:n får den bara via env. Startfel
   `ConnectionStrings:Redis saknas` = denna glömd.
3. **FE kräver `BACKEND_URL`.** FE:ns server-side-actions + `getLandingStats`
   (`src/lib/api/landing.ts`) läser `process.env.BACKEND_URL`. Det finns **ingen**
   `.env.local`. Startar du `pnpm dev` UTAN `BACKEND_URL=http://localhost:5049` →
   sidan svarar 200 men **login misslyckas** och landing visar **fallback-siffror**
   (t.ex. "40 000" / "0" i stället för verkliga ~42 700 / 105). Detta såg ut som
   "servern är nere" 2026-06-13.
4. **Obligatorisk lokal config saknas → `OptionsValidationException` vid start.**
   API:t OCH Worker:n fail-fast-validerar flera options (`ValidateOnStart` i
   Infrastructure-DI). En saknad nyckel kraschar starten och NAMNGER exakt vilken.
   `appsettings.Local.json` (gitignored, i `src/Jobbliggaren.Api/`) måste innehålla
   `FieldEncryption` + de tre pseudonymiserings-pepprarna `AuditPseudonymization` +
   `CompanyWatchPseudonymization` + `CvReviewFingerprintPseudonymization` (+ `Email`).
   **Kopiera `appsettings.Local.json.example` → `appsettings.Local.json` och generera
   nycklarna** (`openssl rand -base64 32` per sektion; `.example` är källan till sanning för
   listan). De tre pepprarna tillkom successivt — `AuditPseudonymization` 2026-07-14 (ADR 0090
   D5, #842), `CompanyWatchPseudonymization` 2026-07-18 (#544/#942),
   `CvReviewFingerprintPseudonymization` 2026-07-19 (ADR 0093 D2, #692) — så en dev-DB /
   Local.json som konfigurerades före var och en saknar den (fail-fast NAMNGER exakt vilken).
   Utan `FieldEncryption:Provider=Local` defaultar en worktree-start dessutom till Kms och
   500:ar mot AWS (#802).
5. **Worker läser `DOTNET_ENVIRONMENT`, INTE `ASPNETCORE_ENVIRONMENT`.** Worker:n är en
   generic host (`Host.CreateApplicationBuilder`), inte en web-host. Sätter du bara
   `ASPNETCORE_ENVIRONMENT` kör Worker:n i **Production** och laddar fel appsettings.
   Och Worker:n har **ingen egen** `appsettings.Local.json` men validerar samma
   Infrastructure-options som API:t → den behöver `FieldEncryption`- och
   `AuditPseudonymization`-secrets via env, **lästa ur API:ts `appsettings.Local.json`
   så de MATCHAR** (olika nycklar ⇒ API och Worker kan inte läsa varandras
   krypterade/pseudonymiserade data).

### Portar (matchar `docker-compose.yml`)

| Tjänst | Port | Not |
|---|---|---|
| API | 5049 | `--launch-profile http` |
| FE (Next dev) | 3000 | `pnpm dev` |
| Postgres dev | 5435 | db/user `jobbliggaren`, container `jobbliggaren-postgres-dev` |
| Redis dev | 6379 | container `jobbliggaren-redis-dev` |

### Start / omstart (Git Bash, från repo-roten)

```bash
# Förkrav: docker compose up -d (Postgres/Redis/Seq uppe — §2)
#          + src/Jobbliggaren.Api/appsettings.Local.json ifylld (fälla 4 + .example-mallen).
PW=$(grep -E '^POSTGRES_PASSWORD_DEV=' .env | cut -d= -f2-)
export ConnectionStrings__Postgres="Host=localhost;Port=5435;Database=jobbliggaren;Username=jobbliggaren;Password=$PW"
export ConnectionStrings__Redis="localhost:6379"
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development                 # Worker är generic host (fälla 5)

# 0. SCHEMA: efter en sync till origin/main kan det finnas nya migrationer. Kör dem mot dev-DB:n
#    FÖRE start, annars kör appen mot ett stale schema (42P01 / fel resultat). Single-owner:
#    bara stack-ägaren rör dev-DB:ns schema (§6.5 — migration = farligaste hotspoten).
dotnet ef database update --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api --context AppDbContext
dotnet ef database update --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api --context Jobbliggaren.Infrastructure.Identity.AppIdentityDbContext

# 1. Bygg EN gång → båda .NET-processerna kör --no-build (eliminerar build-racet API↔Worker).
dotnet build Jobbliggaren.sln -c Debug

# 2. Worker-secrets via env, lästa ur API:ts Local.json så de MATCHAR (fälla 5). API:t läser
#    sin egen Local.json och behöver dem inte — men global export skadar inte (samma värden).
export FieldEncryption__Provider=Local
export FieldEncryption__LocalMasterKeyBase64=$(python -c "import json;print(json.load(open('src/Jobbliggaren.Api/appsettings.Local.json'))['FieldEncryption']['LocalMasterKeyBase64'])")
export AuditPseudonymization__PepperBase64=$(python -c "import json;print(json.load(open('src/Jobbliggaren.Api/appsettings.Local.json'))['AuditPseudonymization']['PepperBase64'])")

# 3. API FÖRST (bakgrund) → invänta /api/ready=200 → sedan Worker + FE (bakgrund).
dotnet run --project src/Jobbliggaren.Api --launch-profile http --no-build   # → http://localhost:5049
dotnet run --project src/Jobbliggaren.Worker --no-build                      # Hangfire, ingen HTTP-yta
cd web/jobbliggaren-web && BACKEND_URL=http://localhost:5049 pnpm dev        # → http://localhost:3000 (fälla 3)
```

Hänger en gammal instans på porten: `netstat -ano | grep ':5049.*LISTENING'` →
`taskkill //F //PID <pid>`. FE stale dev-server (Jest-worker-overlay) = `taskkill`
PID + `rm -rf .next` + omstart (kodbugg uteslöts om `pnpm build` är grön; memory
`feedback_stale_devserver_jest_worker_mask`).

### Verifiera

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5049/api/ready  # → 200 (+ /api/live)
curl -s http://localhost:5049/api/v1/landing/stats                        # → {"activeCount":...,"newToday":...,"isStale":false}
curl -s http://localhost:3000/ | grep -oE 'jp-head__stat-num[^>]*>[^<]*'  # → riktiga siffror (tusental = NBSP), EJ "40 000"/"0"
tail -3 /c/tmp/worker-dev.log                                             # → "...Job: klart — ..."

# Jobbannonser LIVE: Worker:ns sync-platsbanken-stream (cron */10) håller dem färska mot JobTech.
docker exec jobbliggaren-postgres-dev psql -U jobbliggaren -d jobbliggaren -tAc \
  "SELECT field||'='||value FROM hangfire.hash WHERE key='recurring-job:sync-platsbanken-stream' AND field IN ('Cron','LastExecution');"
docker exec jobbliggaren-postgres-dev psql -U jobbliggaren -d jobbliggaren -tAc \
  "SELECT count(*) FILTER (WHERE status='Active')||' aktiva / '||count(*)||' totalt' FROM job_ads;"
```

> **`jp-head__stat-num`, inte `stat__num`** (klassen bytte namn) — och tusenavskiljaren är en
> NBSP, så en literal `grep '40 382'` missar den. Att siffran är närvarande + `isStale:false` +
> `BACKEND_URL` satt = FE:t renderar verklig data, inte fallback.

### EJ i stacken

- Ingen Azurite/Minio — fält-kryptering lokalt via `LocalDataKeyProvider` (ADR 0066,
  AES-256-GCM); e-post via `ConsoleEmailSender`. AWS retired (ADR 0066).
