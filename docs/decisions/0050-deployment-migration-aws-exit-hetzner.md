# ADR 0050 — Deployment-migration: full AWS-exit → Hetzner CAX31 + Cloudflare

**Status:** Accepted
**Datum:** 2026-05-19 (Proposed); **Accepted 2026-06-08** (efter targeted amendment, se Livscykel-not + Amendment 2026-06-08)
**Kontext:** Post-Fas-3 + pre-migration-discovery-session (Block 2). Inför MVP-presentation 2026-05-25 + studentbudget-kostnadshygien. Accepted-flippen 2026-06-08 sker post-AWS-teardown (ADR 0066) som strategisk riktnings-bekräftelse — faktisk provisionering är fortsatt framtida Klas-gatat arbete (se Amendment 2026-06-08, Sekvensering).
**Beslutsfattare:** Klas Olsson (riktnings-GO 2026-05-19; Accepted-GO + sizing/sekvens-dom 2026-06-08); dotnet-architect (IaC-/sizing-/deploy-review §9.2, 2026-05-19 + 2026-06-08); senior-cto-advisor (§9.6 decision-maker, strategiskt fas-skifte, 2026-05-19 + 2026-06-08); security-auditor (secrets/master-nyckel/PII-residens §9.2, 2026-06-08)
**Relaterad:** ADR 0005 (kostnadsskydd — relevans-skifte post-migration, ej supersession); ADR 0019 (direct-push, granskningsspärrar); ADR 0065 (PR-flöde + automerge — denna amendment levereras via PR); ADR 0066 (AWS dev-stack-teardown — löser KMS-beroendet via `LocalDataKeyProvider`, se Amendment 2026-06-08); ADR 0049 (TD-13 envelope-encryption — KMS-beroendet LÖST via ADR 0066 `LocalDataKeyProvider`, kvarvarande Hetzner-härdning = TD-102); ADR 0051 (AI-provider — Bedrock utgår, möjliggör ren exit). Underlag: `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md`; `docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-{architect,security,cto}.md`. BUILD.md Bilaga B planerad `NNNN-aws-over-azure.md` — denna ADR fyller den slotten med motsatt slutsats (helt moln-exit, ej moln-byte).

> **Livscykel-not:** Skriven 2026-05-19 av Claude Code på explicit Klas-begäran
> (medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen för
> denna session). Besluts-substansen är transkriberad från Block 2-beslut +
> dotnet-architect-/senior-cto-advisor-domar — inga nya beslut konstruerade.
>
> **Revision + Accepted-flip 2026-06-08 (Claude Code, §9.4-Klas-override-precedens
> `feedback_klas_can_override_adr_verbatim_source`):** ADR:n skrevs 2026-05-19 —
> FÖRE AWS-teardown (ADR 0066, 2026-05-26) och FÖRE `LocalDataKeyProvider`
> (2026-06-06). Tre delar var därmed föråldrade och amenderades före Accepted-flip:
> (1) "Öppen fråga — KMS-beroende" beskrev en migrations-blocker som ADR 0066
> sedan LÖST (krypto provider-agnostiskt migrerat; security-auditor 2026-06-08
> bekräftade kod-bevisat); (2) rollback-storyn ("behåll AWS-stacken körande")
> är ogiltig — AWS är rivet; (3) sizing (CX32) vägde aldrig ARM CAX-serien.
> Revisionen är grundad i dotnet-architect- + security-auditor- + senior-cto-
> advisor-domar 2026-06-08 (`docs/reviews/`) — inga nya beslut konstruerade
> utöver CTO-domarna. Klas godkände Accepted-flip + CAX31-sizing + Fas-4-före-
> Hetzner-sekvens 2026-06-08. Faktisk provisionering/migration utförs INTE denna
> session (Sekvensering: Hetzner sist, vid MVP före beta-testare).

---

## Kontext

JobbPilot driftas på AWS (eu-north-1) som en lean dev-stack som i praktiken
bär hela driften: ECS Fargate (API 0,5 vCPU/1 GB, Worker 0,25 vCPU/0,5 GB),
RDS PostgreSQL `db.t4g.micro` (1 GB RAM, 20 GB→autoscale 100), ElastiCache
Redis `cache.t4g.micro`, ALB, VPC, KMS, Secrets Manager, CloudTrail. Korpuset
är live ~45 000+ jobbannonser.

Month-to-date-kostnad 2026-05-19 ≈ $44,65, trajektoria ~$2,3/dygn (tidsbaserad
infra: Fargate/RDS/ALB/VPC). På en studentbudget är detta den dominerande
återkommande kostnaden, och den drivs av ren infrastruktur — inte av
AI-inferens (Fas 4 ej byggt) eller trafik.

Klas-beslut 2026-05-19: avveckla AWS helt efter MVP-presentationsveckan
(juni 2026). Block 1 (budget-höjning $50→$100) skippades medvetet — ingen
funktionell vinst på en stack som rivs (separat Klas-beslut, session-loggen).
ADR 0051 (Anthropic Direct, Bedrock utgår) tar bort det enda kvarvarande
motivet att behålla en AWS-tether → en **ren** exit blir möjlig (ej hybrid).

## Beslut

### Beslut 1 — Full AWS-exit, ej hybrid

All JobbPilot-drift lämnar AWS. Ingen kvarvarande AWS-tjänst (ingen
Bedrock-tether per ADR 0051, ingen kvar-RDS, ingen kvar-S3). Hybrid-scenariot
(behåll Bedrock/utvald AWS-tjänst) avvisas — det bevarar AWS-konto,
IAM/SDK-koppling och en kostnads-svans för marginell nytta. Ren exit ger
enklare ops-yta och eliminerar AWS-SDK-beroenden på driftboxen.

### Beslut 2 — Backend: Hetzner Cloud CAX31 (ARM), all-in-one Docker Compose

> **Amenderad 2026-06-08:** ursprungsvalet **CX32** (x86, 8 GB) uppgraderades till
> **CAX31** (ARM, 16 GB) efter dotnet-architect-/senior-cto-advisor-dom. Skälet:
> ADR 0050:s ursprungstext vägde bara CX22 vs CX32 (båda x86) — ARM CAX-serien
> övervägdes aldrig. Se motivering + avvisade alternativ nedan.

Hetzner Cloud **CAX31** (8 vCPU shared ARM Ampere Altra / 16 GB RAM / 160 GB
NVMe / 20 TB trafik, ~€15,99/mån, EU-datacenter Falkenstein/Nuremberg/Helsinki,
pris-verifierat 2026-06-08). En box kör hela stacken (backend + frontend) i Docker Compose:
.NET API + .NET Worker + PostgreSQL + Redis + Caddy (reverse proxy, auto-TLS) + Next.js (next start).
**Detta är den totala compute-kostnaden — Postgres är co-tenant i
container på boxen, ingen separat managed-DB-kostnad** (Ubicloud managed-PG
~$15/mån avvisad, se Alternativ).

FE-tillägget (`next start`, ~0,5 GB under last) ryms inom CAX31:s 16 GB headroom; den
dimensionerande vektorn förblir ingestion-OOM (Worker-minnesprofil), inte FE steady-state.
**Bindande regel: `next build` körs i CI — enbart den byggda imagen skickas till boxen;
build-peaken (~2–4 GB) belastar aldrig boxens RAM-feldomän.**

**Sizing-motivering (CAX31 över CX32/CAX21):** På en single-box samsas API +
Worker + Postgres + Redis om samma RAM-feldomän (AWS isolerade RDS/Redis
managed; en VPS gör det inte). Den dimensionerande risken är kod-bevisad:
`JobTechStreamClient` har `MaxResponseContentBufferSize = 500 MB`
(Platsbanken-ingestion, ADR 0032/TD-13-grunden) — en dokumenterad
minnes-blowout-vektor som konkurrerar med Postgres hot-index (46k+ annonser +
raw_payload-jsonb + STORED generated columns + FTS-GIN-index) på samma RAM.
På CX32:s 8 GB ligger PG:s working-set (~2–3 GB) + Worker-ingestion-spik +
Redis + .NET-heapar + OS farligt nära taket. CAX31:s **16 GB** ger headroom för
`mem_limit` per service (skydda Postgres mot Worker-OOM — se mem_limit-noten
under Amendment 2026-06-08) + korpus-tillväxt; **160 GB** disk rymmer PG + WAL +
Docker-images + pg_dump-staging. ARM-risken är låg: hela stacken är ARM64-ren
2026 (.NET 10 tier-1 `linux-arm64`, Npgsql/Hangfire/Postgres/Redis/Caddy
multiarch); enda historiska ARM-fällan (`System.Drawing`/libgdiplus) är
Fas-4-PDF-gated och ej aktuell vid cutover. ~€9/mån merkostnad mot CX32 köper
bort den största single-box-risken (Nygard *Release It!* — Bulkheads/Steady
State: medvetet SPOF-val för beta kompenseras med headroom i delad resurs).

### Beslut 3 — Frontend: co-tenant container på CAX31 (Vercel-exit)

> **Amenderad 2026-06-14 (Klas-direktiv):** ursprungsbeslutet **"Vercel behålls"**
> är supersederat — Next.js-frontend flyttar in som `next start`-container i samma
> Compose-stack bakom Caddy på CAX31.
>
> **Ursprungligt Beslut 3 (supersederat):** *"Next.js-frontend kvar på Vercel (EU). Ingen
> ändring — Vercel free/Pro-nivå bär frontend; ingen anledning att flytta in den på
> VPS-boxen och därmed öka dess RAM-/ops-börda."*
>
> **Varför supersederat:** Det ursprungliga argumentet (RAM-/ops-börda → behåll FE off
> the box) vägde aldrig jurisdiktions-/konsolideringsvinsten. FE:s verkliga fotavtryck
> (~0,5 GB under last) ryms komfortabelt inom CAX31:s headroom (se Beslut 2,
> FE-sizing-meningen). Vercel är ett US-bolag — att behålla applikationshosting hos
> en US-leverantör skapar en inkonsekvens med Beslut 4:s avvisning av Cloudflare R2
> (CLOUD Act / Schrems II / GDPR Kap. V). Den distinktionen görs nu fullt konsekvent:
> **Cloudflare behålls = edge-only** (TLS/DNS/proxy/DDoS — ingen applikationshosting,
> inget data-at-rest hos ett US-bolag; bara edge-transit passerar ett US-bolag);
> **Vercel lämnar = applikations-tier + data-at-rest blir EU-resident** (Hetzner-EU),
> enbart edge-transit kvarstår hos ett US-bolag. Detta är en strikt konsekvensstärkning
> av samma Schrems II / Kap. V-logik som motiverade R2-avvisningen — inte en
> komplikation.
>
> **Sizing re-validerad (dotnet-architect, 2026-06-14):** CAX31 (16 GB) håller med
> ~8 GB headroom även i worst-case-samvaro (ingestion-spik + SSR-last), **förutsatt**
> att `next build` körs i CI och enbart den byggda imagen skickas till boxen —
> build-peaken (~2–4 GB) får aldrig belasta boxens RAM-feldomän. **Build-in-CI är den
> bindande regeln.**

### Beslut 4 — Cloudflare-proxy + Hetzner-EU-backup-offload

> **Amenderad 2026-06-08:** backup-målet **Cloudflare R2** ersattes med
> **Hetzner-EU Storage Box** efter security-auditor-/senior-cto-advisor-dom
> (M-4). Skälet: `pg_dump` bär icke-krypterad PII (bara 4 kolumner är
> fält-krypterade per ADR 0049; e-post/namn/`waitlist_entries`/audit-IP i
> klartext) och Cloudflare är ett US-bolag (CLOUD Act) → R2 vore en
> tredjelandsöverföring (GDPR Kap. V/Schrems II). Hetzner-EU håller hela
> data-livscykeln i samma jurisdiktion som boxen.

Cloudflare gratis-tier framför boxen (TLS-edge/DNS/CDN/DDoS) — **Cloudflare-proxy
"Full (strict)"** mot ett giltigt origin-cert på Caddy (aldrig "Flexible" =
klartext på sista benet) + origin-IP-lockdown (origin accepterar bara
Cloudflare-IP:er på 443) + HSTS. Caddy reverse-proxiar nu **både** API:et (`/api/*`)
och Next.js-servern (`localhost:3000`, övriga routes); origin-cert + origin-IP-lockdown
("Full (strict)") är oförändrade och täcker hela origin.

Nattlig `pg_dump` → **Hetzner-EU Storage Box** (~€3,20/mån/1 TB,
samma EU-jurisdiktion som boxen) — backups ligger INTE på boxens 160 GB (håller
disk-budgeten hållbar mot korpus-tillväxt + WAL + Docker-images).
**Dumpen klient-side-krypteras (age) före upload oavsett mål** — fält-krypteringen
skyddar bara fyra kolumner *i* dumpen; resten kräver eget krypto-lager. Plus
definierad backup-retention/rotation (bortre gräns för icke-krypterad PII i
gamla dumpar; ADR 0024:s RDS-14d-rotation finns ej gratis på Hetzner — måste
byggas). Detaljerna = **TD-107**.

## Konsekvenser

### Positiva

- Återkommande kostnad ~€16/mån (CAX31, inkl. co-tenant-DB) + ~€3/mån EU-backup
  ≈ **~€19/mån totalt**, vs ~$45+/mån AWS-trajektoria — **~80% reduktion**, materiell
  på studentbudget. (Amenderat 2026-06-08: ursprungstexten angav ~€6,80 för CX32.)
  Vercel var på free/Pro-nivå och utgör ingen €-post i ADR:ns totalkostnad — FE
  flyttar in på den redan betalda CAX31, ingen ny återkommande compute-kostnad tillkommer.
- Ren ops-yta: en box, Docker Compose, inga moln-SDK-/IAM-tethers.
- Eliminerar AWS-SDK-beroenden i kodbasen (jfr ADR 0051 — `AWSSDK.BedrockRuntime`
  byggs aldrig).
- En färre extern subprocessor och US-bolags-beroende (Vercel-exit).
- ADR 0005:s kostnadsskydds-apparat (Budget Actions, Bedrock-deny,
  registrations_open-gating) blir **i stort sett moot post-migration** —
  relevans-skifte, ej supersession (ADR 0005-text orörd; flaggas i Block 4).

### Negativa

- **Singel-box blast-radius:** API/Worker/Postgres/Redis/Next.js (next start) delar OS, RAM och
  feldomän. En OOM eller box-incident tar hela produkten, inte en isolerad
  container (kontrast mot AWS managed-isolering). Next.js-tillägget är ett
  medvetet utökat SPOF-val, kompenserat av CAX31:s headroom och build-in-CI-regeln.
- Självhanterad Postgres + Redis + backups: ingen managed RDS-HA, ingen
  point-in-time-restore out-of-the-box, patch-/vacuum-/WAL-ansvar på Klas.
- Ops-börda flyttas från AWS-managed till Klas-manuell (Docker Compose-deploy,
  Caddy-config, restore-drill).

### KMS-beroende — LÖST 2026-05-26 via ADR 0066 (amenderat 2026-06-08)

> **Amenderad 2026-06-08:** denna sektion hette ursprungligen "Öppen fråga —
> KMS-beroende (migrations-blocker, EJ löst denna session)" och beskrev
> krypto-flytten som en oläst blocker. Den **prosan är föråldrad** — blockern
> löstes 2026-05-26 av ADR 0066 (`LocalDataKeyProvider`). security-auditor
> bekräftade 2026-06-08 kod-bevisat att omframingen är korrekt. En Accepted-ADR
> får inte bära en falsk blocker → sektionen omskriven.

**ADR 0049 (TD-13) implementerade PII-fält-kryptering för fyra user-ägda
kolumner via envelope-encryption (per-användar-DEK wrappad av master-nyckel,
lagrad i `user_data_keys`).** Den ursprungliga implementationen wrappade DEK via
AWS KMS (`GenerateDataKey`/`Decrypt`, CMK i HSM). En full AWS-exit (Beslut 1)
tar bort AWS KMS — men **det beroendet är redan löst**:

ADR 0066 (2026-05-26) införde `LocalDataKeyProvider` som ett andra
`IDataKeyProvider`-impl (config-switch `FieldEncryption:Provider` Kms/Local).
Local-grenen wrappar per-användar-DEK med en lokal AES-256-GCM master-nyckel
i stället för KMS. **Hela ADR 0049:s besluts-substans är oförändrad** —
envelope-strukturen (per-JobSeeker wrapped-DEK), owner-AAD-bindningen,
fail-closed-invarianten och `IFieldEncryptor`-primitiven (ren BCL `AesGcm`) är
identiska; bara DEK-wrap-mekanismen bytte. Verifierat e2e healthy 2026-06-07.
Crypto-erasure-semantiken (ADR 0049 Beslut 2) är bevarad.

**Kvarvarande Hetzner-arbete är därmed INTE "om-hemma krypto-mekanismen" (gjort)
utan att härda den självhanterade master-nyckelns prod-skyddsmodell + rotation**
— en känd, scopead, kod-bevisad TD: **TD-102** (Major, Hetzner-deploy;
ADR 0049-amendment-scope). Detaljerna (master-nyckel-skydd via
systemd-credentials/sops+age, körbar re-wrap-rotation, security-gates) listas
i "Pre-beta-data-gates" under Amendment 2026-06-08.

### Mitigering

- Nattlig klient-side-krypterad `pg_dump` → Hetzner-EU Storage Box +
  dokumenterad restore-drill innan produktions-cutover (DoD-grind) — **TD-107**.
- Caddy auto-Let's-Encrypt (DNS-01 via Cloudflare-plugin); health-checks +
  extern uptime-monitor (UptimeRobot/BetterStack free) ersätter
  ALB/CloudWatch-health.
- KMS-beroendet är redan löst (ADR 0066 `LocalDataKeyProvider`); kvarvarande
  master-nyckel-prod-härdning + rotation = **TD-102** (egen security-auditor-
  granskning av faktisk prod-config före real-PII).
- Lasttest mot 46k-korpuset (NBomber, ADR 0045) före cutover för att
  validera CAX31-sizing empiriskt.

## Alternativ övervägda

- **CX22 (2 vCPU/4 GB/40 GB):** Avvisad. Under-provisionerad för co-tenant
  Postgres med 46k+ korpus + ingestion-minnesprofil; noll headroom för
  korpus-tillväxt; 40 GB disk snäv (PG + WAL + backups + raw_payload).
- **CX32 (x86, 4 vCPU/8 GB/80 GB, ~€6,80):** ursprungsvalet — **avvisat
  2026-06-08**. 8 GB är under-provisionerat för co-tenant Postgres + den
  kod-bevisade ingestion-OOM-vektorn (500 MB-buffer) + korpus-tillväxt på en
  delad feldomän. x86 ger noll fördel (ingen x86-only-dep i stacken). Prisdeltat
  (~€9/mån mot CAX31) är trivialt mot en helprodukts-OOM på en singel-box
  (samma resonemang som CX22→CX32-avvisningen, förlängt ett steg på kod-bevisad
  grund).
- **CAX21 (ARM, 4 vCPU/8 GB/80 GB):** Avvisad 2026-06-08. ARM-ekvivalent till
  CX32 men samma 8 GB-tak — ARM-byte utan RAM-vinst löser inte
  single-box-RAM-feldomän-risken.
- **Hybrid (behåll Bedrock/utvald AWS-tjänst på AWS):** Avvisad — bevarar
  AWS-konto/IAM/SDK-tether + kostnads-svans för marginell nytta. ADR 0051
  eliminerar Bedrock-motivet helt.
- **Stanna på AWS:** Avvisad — dominerande återkommande kostnad på
  studentbudget; ingen funktionell vinst som motiverar den mot Hetzner-paritet
  för en beta-skala.
- **Annan VPS/PaaS (DigitalOcean/OVH/Vultr/Coolify-managed):** Ej djup-jämförd
  — Klas pre-beslutade Hetzner (Block 2). Provider-jämförelsen blev därmed
  akademisk; sizing-frågan (CX22 vs CX32 vs CAX-serien) var den enda levande
  beslutsaxeln och är avgjord i Beslut 2 (CAX31).
- **Managed Postgres utanför boxen (Ubicloud på Hetzner ~$15/mån, eller annan):**
  Ej valt för beta-skala — **fördubblar nästan backend-budgeten** (~€16 + ~$15)
  + nätverkshop + extern beroende-yta. Co-tenant Postgres i container på CAX31
  är tillräckligt om sizing hålls (16 GB ger headroom). Kan omvärderas vid
  skala-signal (Trigger, §9.6) — ej TD.

## Implementationsstatus

**Accepted 2026-06-08 (riktning bekräftad). Ingen migration/provisionering
utförd.** Accepted-flippen dokumenterar den bekräftade riktningen — den binder
ingen infra (en reversibel "two-way-door"; DNS-cutover är den enda irreversibla
flippen och utförs ej denna session). Faktisk Hetzner-provisionering är framtida
Klas-gatat arbete; per Sekvensering (Amendment 2026-06-08) sker den **sist**,
vid MVP före beta-testare, med samtliga Pre-beta-data-gates lösta först.

## Validering

Uppskjuten till migrations-utförandet: NBomber-lasttest mot 46k-korpus
(ADR 0045-budgetar), klient-side-krypterad `pg_dump`-restore-drill (TD-107),
end-to-end-rök på Hetzner-box före DNS-cutover.

**Rollback (amenderat 2026-06-08):** den ursprungliga rollback-storyn ("behåll
AWS-stacken körande tills Hetzner-paritet verifierad") är **ogiltig** — AWS är
rivet (ADR 0066, 2026-05-26). Den korrekta modellen: **lokal Docker-Compose-stack
på Klas laptop är paritets-baselinen** (samma image-byggväg som Hetzner-prod,
dev/prod-paritet). Rollback vid misslyckad cutover = återgå till lokal-dev +
ej-cutad DNS (Cloudflare). DNS-cutover är den reversibla flippen; tills den sker
påverkas ingen live-trafik (ingen live-miljö existerar idag).

## Amendment 2026-06-08 — sizing-uppgradering, backup-mål, KMS-omframing, security-gates, sekvensering

**Beslutsfattare:** Klas Olsson (Accepted-GO + CAX31-sizing + Fas-4-före-Hetzner-
sekvens). **Underlag:** dotnet-architect + security-auditor + senior-cto-advisor
(decision-maker) 2026-06-08 (`docs/reviews/2026-06-08-adr-0050-*`). **Kontext:**
ADR:n skrevs 2026-05-19, före AWS-teardown (ADR 0066) + `LocalDataKeyProvider` —
denna amendment re-validerar mot nuläget och flippar Proposed→Accepted.

### Sammanfattning av ändringar (inline ovan)

1. **Beslut 2 sizing:** CX32 (x86, 8 GB) → **CAX31** (ARM, 16 GB). Kod-bevisad
   ingestion-OOM-vektor + single-box-RAM-feldomän.
2. **Beslut 4 backup:** Cloudflare R2 → **Hetzner-EU Storage Box** + obligatorisk
   klient-side-kryptering (R2 = CLOUD Act-tredjelandsöverföring av icke-krypterad
   pg_dump-PII).
3. **KMS-beroende:** "oläst migrations-blocker"-prosan ersatt — beroendet löst
   av ADR 0066 (`LocalDataKeyProvider`), kvarvarande härdning = TD-102.
4. **Rollback-story:** "behåll AWS körande"-modellen ersatt (AWS rivet) med
   lokal-Compose-paritets-baseline.

### Amendment 2026-06-14 — Vercel-exit, FE co-tenant på CAX31

5. **Beslut 3 (Vercel-exit):** ursprungsbeslutet "Vercel behålls" supersederat — FE
   flyttar in som `next start`-container i Compose-stacken bakom Caddy. Titel uppdaterad
   (+ Vercel borttaget). CLOUD Act-konsekvensstärkning: Cloudflare kvarstår som edge-only
   (ingen applikationshosting, inget data-at-rest), Vercel lämnar (applikations-tier +
   data-at-rest EU-resident). Caddy reverse-proxiar nu hela origin (API + Next.js).
   Beslut 2-prosan generaliserad till "hela stacken (backend + frontend)";
   TD-106-scope utvidgad till FE-container + Caddy-FE-route + CI FE-build-steg.
   Beslutsfattare: Klas Olsson (direktiv 2026-06-14); sizing re-validerad av dotnet-architect.

### mem_limit-mekanik (konsekvens-not till Beslut 2)

Compose-stacken sätter **hybrid `mem_limit`**: hård cap på Worker + Redis (skydda
Postgres mot Worker-ingestion-OOM), generös/osatt cap på Postgres
(data-durabilitet — en hård PG-cap kan OOM-killa mitt i query). Bulkhead-principen
(Nygard *Release It!*): cappa angriparen (Worker-burst, Hangfire-Postgres-storage
→ dödad spik retryas durabelt), inte offret (PG). CAX31:s 16 GB upplöser det
nollsummespel detta vore på 8 GB. Mekanik-detaljer = TD-106.

**TD-106 scope-utvidgning (Beslut 3-amendment 2026-06-14):** TD-106:s Compose-stack-scope
vidgas till att inkludera FE-containern (`next start`, healthcheck, `mem_limit`),
Caddy-FE-routen (`localhost:3000`-proxying), samt ett CI FE-build-steg
(`next build` → image) som bindande pre-requisite för deployment till boxen.

### Pre-beta-data-gates (security-auditor 2026-06-08 — MÅSTE grönt före första real-PII)

Dessa är gates **före första real-PII (beta-testare)**, INTE före denna Accepted-
flip. Strategin *som riktning* har inga GDPR-blockers (Hetzner-EU at-rest
GDPR-ren; krypto provider-agnostiskt migrerat). Waitlist är tom idag. Gates bärs
operativt av TD-102 (master-nyckel), TD-106 (stack/härdning), TD-107 (backup).

| # | Gate | Källa | Hemvist |
|---|---|---|---|
| B-1 | Master-nyckel ALDRIG plaintext-på-disk på beta-VPS (systemd-credentials TPM-bunden el. sops+age→tmpfs; plaintext OK bara lokalt) | Blocker | TD-102 |
| B-2 | Gitleaks/historik-scan: ingen master-nyckel/cred committad; rotation om läckt | Blocker | **Verifierad GRÖN 2026-06-08** (`appsettings.Local.json` i .gitignore, aldrig committad; inget nyckel-värde i historik) |
| M-3 | Körbar idempotent master-nyckel-re-wrap-rotation + kadens (minst årlig + händelse-driven vid box-kompromiss/offboarding) | Major | TD-102 |
| M-4 | pg_dump klient-side-krypterad + backup-retention/rotation definierad + EU-jurisdiktion | Major | TD-107 |
| M-5 | Cloudflare "Full (strict)" + origin-IP-lockdown (bara CF-IP på 443) + HSTS | Major | TD-106 |
| M-6 | VPS-härdnings-baseline (SSH-key-only, brandvägg, fail2ban, auto-patch, PG/Redis ej publika, swap/core-dump-hygien mot master-nyckel-minnesläck) | Major | TD-106 |
| M-1 | ADR 0050 KMS-blocker-prosa amenderad → TD-102-omframing | Major | **Åtgärdad denna amendment** |
| M-2 | ADR 0049-amendment: self-managed master-nyckels prod-skyddsmodell + accepterad minne-restrisk + namngiven skala-trigger för extern KV/HSM | Major | TD-102 (ADR 0049-amendment-scope) |

**Obligatorisk re-review:** en andra security-auditor-granskning av den faktiska
prod-konfigurationen (master-nyckel-injektion, backup-kryptering, TLS-topologi,
härdning) krävs **innan första beta-data laddas** (TD-102 punkt 3). Den
granskningen är gaten — inte denna design-dom.

### Sekvensering (Klas-beslut 2026-06-08)

Hetzner-provisionering är **inte** nästa steg. AWS är rivet (kostnad €0), all dev
kör lokalt, waitlist är tom — att deploya nu vore premature deployment för noll
användare (YAGNI; value over activity, Winters et al. *SWE at Google* 2020).
**Ordning:** (1) Fas 4 (AI Layer, ADR 0051) — alternativt TD-rensning — byggs/testas
lokalt; (2) **Hetzner-provisionering sist, vid MVP före beta-testare**, med
samtliga Pre-beta-data-gates lösta + andra security-granskning först. ADR 0050
Accepted dokumenterar riktningen så den är redo; exekvering väntar på produktbehov.

### AWS-kodhygien (separat Klas-GO, ej i denna ADR-PR)

Döda AWS-workflows (`deploy-dev.yml`, `rds-ca-bundle-check.yml`) bör rensas
in-block (CTO axel 6) men `.github/`-touch kräver egen Klas-GO + egen
`chore(infra)`-commit — **defereras till separat PR** (ingår ej i denna docs/ADR-
PR). `AWSSDK.KeyManagementService` BEHÅLLS (KMS referens-impl, ADR 0066-
reversibilitet). `AWSSDK.SecretsManager` rensas när Migrate re-homas (TD-105).

## Relaterade beslut

- **ADR 0005** — kostnadsskydd/launch-gating. Post-migration blir Budget
  Actions/Bedrock-deny/registrations_open i stort sett moot. **Relevans-skifte,
  ej supersession** — ADR 0005-text ändras inte; flaggas i Block 4.
- **ADR 0019** — direct-push/granskningsspärrar. Oförändrad; migration följer
  samma STOPP-disciplin.
- **ADR 0049** — TD-13 envelope-encryption. KMS-beroendet **LÖST** via ADR 0066
  `LocalDataKeyProvider` (ej längre migrations-blocker). Kvarvarande Hetzner-
  prod-härdning + rotation = **TD-102** (ADR 0049-amendment-scope, M-2/M-3).
- **ADR 0066** — AWS dev-stack-teardown. Löste KMS-beroendet (`LocalDataKeyProvider`)
  och gjorde rollback-storyn ("behåll AWS körande") ogiltig. Komplementär:
  ADR 0066 var temporär semester-pause, ADR 0050 är permanent provider-exit.
- **ADR 0051** — AI-provider Anthropic Direct/Bedrock utgår. Möjliggör ren
  exit (Beslut 1). AI-transfer (US, opt-in) är separat grindad, rör ej VPS-residens.
- **ADR 0065** — PR-flöde + automerge. Denna amendment levereras via PR.
- **TD-101** (mejl-väg) / **TD-102** (master-nyckel + rotation) / **TD-104**
  (logg-sink) / **TD-105** (Migrate-re-home) / **TD-106** (Compose-stack + proxy
  + härdning) / **TD-107** (krypterad EU-backup) — Hetzner-fas-arbetet, alla
  gated på denna ADR.
- **BUILD.md Bilaga B** — planerad `NNNN-aws-over-azure.md`. Denna ADR fyller
  slotten med motsatt slutsats (moln-exit, ej moln-byte). adr-keeper
  uppdaterar "Planerade ADRs"-listan.

## Referenser

- `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md` — Block 2/3-discovery,
  web-verifierade priser/sizing (Hetzner CX-plans, pris-justering 2026-04-01)
- `docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-architect.md` (sizing/deploy/
  migrations-dom), `-security.md` (2 Blockers + 4 Majors, KMS-omframing-bekräftelse),
  `-cto.md` (decision-maker, 10 axlar)
- dotnet-architect IaC-/sizing-review 2026-05-19 + 2026-06-08
- senior-cto-advisor §9.6-triage 2026-05-19 + decision-maker-rond 2026-06-08
- security-auditor secrets/master-nyckel/PII-residens-dom 2026-06-08
- ADR 0005 / 0019 / 0049 / 0051 / 0065 / 0066 · CLAUDE.md §2.5 (perf), §9.2, §9.6
- Hetzner Cloud pricing (web-verifierat 2026-06-08): CX32 ~€6,80, CAX31 ~€15,99,
  CAX21; EU-DC Falkenstein/Nuremberg/Helsinki; ingen native managed-PG (Ubicloud
  tredjepart ~$15/mån)
- Microsoft Learn — ASP.NET Core forwarded-headers/proxy; .NET linux-arm64 tier-1
- Nygard *Release It!* (Bulkheads/Steady State); Ford/Parsons/Kua *Building
  Evolutionary Architectures* (two-way-door); Winters et al. *SWE at Google*
  (value over activity); GDPR Art. 32/17/44–46 + EDPB CEF 2025
