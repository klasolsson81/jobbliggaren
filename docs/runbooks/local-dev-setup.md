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
  echo "SEQ_ADMIN_PASSWORD_DEV=$(openssl rand -hex 16)"
} > .env
```

På PowerShell:

```powershell
@"
POSTGRES_PASSWORD_DEV=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
POSTGRES_PASSWORD_TEST=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
REDIS_PASSWORD_DEV=
SEQ_ADMIN_PASSWORD_DEV=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
"@ | Out-File -Encoding utf8 .env
```

**`SEQ_ADMIN_PASSWORD_DEV` är obligatorisk** — compose failar utan den. Auth på dev-Seq
är PÅ sedan 2026-08-04 (#1198), och skälet är inte formalia: dev-Seq bär
`ConsoleEmailSender`-rader med mejlkroppen, alltså aktiverings- och
bekräftelselänkar i klartext. Loopback-bindningen ensam räckte inte som kontroll över
det materialet — den var dessutom mätt fel i månader medan compose-filens egen kommentar
gick i god för den.

**Sedan #1208 skrivs kroppen bara för en mottagare på en domän som RFC 2606/6761
reserverar:** `.test`, `.example`, `.invalid`, `.localhost`, `example.com|net|org`.
Registrerar du lokalt med någon annan adress loggas i stället en rad vars enda fält är
`EmailKind` — ingen mottagare, ingen rubrik, ingen kropp — och då finns ingen länk att
läsa ut. Använd en `.test`-adress: `klas@jobbliggaren.test` är den dokumenterade
dev-inloggningen, och E2E-sviten kör redan mot `e2e.jobbliggaren.test`.

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

Öppna http://localhost:5341 i webbläsaren för Seq-dashboarden. **Den kräver inloggning**
— användarnamn `admin`, lösenord = `.env`:s `SEQ_ADMIN_PASSWORD_DEV` (auth är på sedan
#1198; §6.4 förklarar varför och vad som gäller om du byter lösenordet).

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

Om `docker compose up` säger `Bind for 127.0.0.1:5435 failed: port is already allocated`:

- En annan postgres-instans kör lokalt. Stoppa den eller ändra port i compose-filen.
- På Windows: `netstat -ano | findstr :5435` → visar PID → `taskkill /PID <pid> /F`

*(Felsträngen bar `0.0.0.0:5432` fram till #1198 — fel på båda halvorna: adressen
falsifierades av att alla portar nu binds till `127.0.0.1`, och porten var fel redan
innan, eftersom 5432 är containerporten och 5435 den publicerade.)*

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

### 6.4 Seq `firstRun.adminPassword` — och varför ett bytt lösenord inte tar

Seq 2025.2+ kräver antingen admin-lösenord eller explicit no-auth. **Vi kör admin-lösenord
sedan 2026-08-04** (#1198): `SEQ_FIRSTRUN_ADMINPASSWORD` ur `.env`:s
`SEQ_ADMIN_PASSWORD_DEV`. Användarnamn är `admin`.

**Fällan:** `FIRSTRUN`-variabler läses **bara vid första uppstarten mot en tom volym**.
Ändrar du `SEQ_ADMIN_PASSWORD_DEV` i `.env` och kör `docker compose up -d
--force-recreate seq` händer **ingenting** — det gamla lösenordet gäller fortfarande,
eftersom det ligger i `jobbliggaren_seq_data`. Byte kräver att volymen kastas:

```bash
docker compose down seq && docker volume rm jobbliggaren_jobbliggaren_seq_data
docker compose up -d seq          # läser nu det nya värdet
```

*(`down` tar ett service-argument — `docker compose down [OPTIONS] [SERVICES]`, mätt mot
Compose 2.40.3 2026-08-04. Kör du en äldre 2.x och argumentet inte tas, använd
`docker compose stop seq && docker compose rm -f seq` i stället. **Lägg aldrig till `-v`
här** — det river namngivna volymer bortom Seq:s.)*

Det kastar också loggarna, vilket normalt är önskvärt i dev. Glömt lösenordet är samma
procedur.

**Om du skriptar mot Seq:s API:** `POST /api/users/login` svarar `401` även när
lösenordet är rätt, om anropet saknar Seq:s CSRF-handskakning. Webbläsaren gör
handskakningen automatiskt, så dashboarden på `http://localhost:5341` påverkas inte.

**Raden som bevisar att DITT lösenord är det som grindar** — `User admin logged in
successfully` räcker inte, den säger bara att auth-subsystemet släppte igenom någon.
Leta i stället efter förstauppstarts-raden i `docker logs jobbliggaren-seq`:

```
Enabling username/password authentication, and using the supplied default admin password
```

*"using the supplied ... password"* är Seq som intygar att `SEQ_FIRSTRUN_ADMINPASSWORD`
faktiskt lästes och applicerades. Saknas den raden körde Seq på något annat.

**Vad auth täcker, och vad den inte gör — och skillnaden går mellan LÄS och SKRIV, inte
mellan portarna.**

- **Läsning är grindad, på 5341.** `/api/events`, `/api/users`, `/api/data` ger `401` utan
  inloggning. `/` och `/api` svarar `200` — SPA-skalet respektive rot-dokumentet med
  produktnamn och länklista, inga data.
- **Skrivning är INTE grindad, på någondera porten.** Mätt 2026-08-04: en oautentiserad
  CLEF-POST mot `/api/events/raw` ger `201` på **både 5342 och 5341**. Det ska vara så —
  appen sätter ingen `Seq:ApiKey`, och `appsettings.Development.json` pekar
  `Seq:ServerUrl` på **5341**, alltså skriver appen till läs-porten. Härdar du ingestion
  på 5341 slutar dev-loggningen fungera.
- **På skrivvägen är bind-adressen därmed enda kontrollen, oavsett port.** Det är precis
  den kontroll som var mätt fel i månader, vilket är skälet att den står utskriven här
  i stället för underförstådd. Skrivning ger ingen väg till det redan lagrade innehållet
  (läsvägarna kräver inloggning), men den ger vem som helst med nätverksåtkomst rätt att
  fylla sänken.

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
   `CompanyWatchPseudonymization` + `CvReviewFingerprintPseudonymization`. `Email` är VALFRI —
   `Email:Provider` defaultar till `Console` i koden, så att utelämna sektionen är en stödd
   konfiguration (mallen märker den så sedan #1165). **Undantaget, och det är ett villkorat
   undantag:** sätter du `Email:Provider=Scaleway` (#183) blir `Email:Scaleway:Region` +
   `Email:Scaleway:SecretKey` + `Email:Scaleway:ProjectId` obligatoriska, och DI fail-stoppar med
   namngivet fel om någon saknas. `Region` måste dessutom vara `fr-par` — armen har en allow-list,
   eftersom regionen interpoleras in i endpoint-URL:en och en felstavning annars faller först som
   404 vid första skarpa utskicket. Valideringen är registrerad **inuti Scaleway-armen** just för
   att `Email` ska förbli valfri på default-vägen; `EmailOptions` har medvetet ingen
   `ValidateOnStart`.
   **Kopiera `appsettings.Local.json.example` → `appsettings.Local.json` och generera
   nycklarna** (`openssl rand -base64 32` per sektion; `.example` är källan till sanning för
   listan). De tre pepprarna tillkom successivt — `AuditPseudonymization` 2026-07-14 (ADR 0090
   D5, #842), `CompanyWatchPseudonymization` 2026-07-18 (ADR 0090 D5, #544/#942),
   `CvReviewFingerprintPseudonymization` 2026-07-19 (ADR 0093 D2, #692) — så en dev-DB /
   Local.json som konfigurerades före var och en saknar den (fail-fast NAMNGER exakt vilken).
   `FieldEncryption:Provider` defaultar REDAN till `Local`, så raden i mallen är dokumentation
   och inte en fix — och ett explicit icke-Local-värde fail-stoppar numera loud i DI, eftersom
   KMS-providern och dess klient är raderade (#802). Den kan inte 500:a mot AWS.
5. **Worker läser `DOTNET_ENVIRONMENT`, INTE `ASPNETCORE_ENVIRONMENT`.** Worker:n är en
   generic host (`Host.CreateApplicationBuilder`), inte en web-host. Sätter du bara
   `ASPNETCORE_ENVIRONMENT` kör Worker:n i **Production** och laddar fel appsettings.
   Och Worker:n har **ingen egen** `appsettings.Local.json` men validerar samma
   Infrastructure-options som API:t → den behöver `FieldEncryption`-nyckeln + ALLA TRE
   pseudonymiserings-pepprarna (`AuditPseudonymization` + `CompanyWatchPseudonymization` +
   `CvReviewFingerprintPseudonymization`) via env, **lästa ur API:ts `appsettings.Local.json`
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
#    ALLA tre pseudonymiserings-pepprarna är dual-host (Worker kör AddPersistence + AddJobSources)
#    och fail-fast-valideras vid Worker-boot → alla tre MÅSTE exporteras, inte bara Audit.
export FieldEncryption__Provider=Local
export FieldEncryption__LocalMasterKeyBase64=$(python -c "import json;print(json.load(open('src/Jobbliggaren.Api/appsettings.Local.json'))['FieldEncryption']['LocalMasterKeyBase64'])")
export AuditPseudonymization__PepperBase64=$(python -c "import json;print(json.load(open('src/Jobbliggaren.Api/appsettings.Local.json'))['AuditPseudonymization']['PepperBase64'])")
export CompanyWatchPseudonymization__PepperBase64=$(python -c "import json;print(json.load(open('src/Jobbliggaren.Api/appsettings.Local.json'))['CompanyWatchPseudonymization']['PepperBase64'])")
export CvReviewFingerprintPseudonymization__PepperBase64=$(python -c "import json;print(json.load(open('src/Jobbliggaren.Api/appsettings.Local.json'))['CvReviewFingerprintPseudonymization']['PepperBase64'])")

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
  AES-256-GCM); e-post via `ConsoleEmailSender`. AWS-**infrastrukturen** är riven (ADR 0066) och
  förblir riven: ingen ECS, RDS, KMS eller Secrets Manager. Sedan #183 (2026-08-15) finns **ingen
  AWS-yta alls** — den sista var ett utgående HTTPS-anrop till Amazon SES, och e-posten ligger nu
  hos Scaleway i `fr-par`. Även den är avstängd lokalt: Scaleway-armen registreras bara när du
  själv sätter `Email:Provider=Scaleway`, och den skickar då riktig post som kostar pengar.
