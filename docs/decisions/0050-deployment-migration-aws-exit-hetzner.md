# ADR 0050 — Deployment-migration: full AWS-exit → Hetzner CX32 + Vercel + Cloudflare

**Status:** Proposed
**Datum:** 2026-05-19
**Kontext:** Post-Fas-3 + pre-migration-discovery-session (Block 2). Inför MVP-presentation 2026-05-25 + studentbudget-kostnadshygien.
**Beslutsfattare:** Klas Olsson (riktnings-GO 2026-05-19); dotnet-architect (IaC-/sizing-review §9.2); senior-cto-advisor (§9.6 strategiskt fas-skifte)
**Relaterad:** ADR 0005 (kostnadsskydd — relevans-skifte post-migration, ej supersession); ADR 0019 (direct-push, granskningsspärrar); ADR 0049 (TD-13 KMS-envelope — **AWS-KMS-beroende, migrations-blocker, se Konsekvenser/Öppen fråga**); ADR 0051 (AI-provider — Bedrock utgår, möjliggör ren exit). Underlag: `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md`. BUILD.md Bilaga B planerad `NNNN-aws-over-azure.md` — denna ADR fyller den slotten med motsatt slutsats (helt moln-exit, ej moln-byte).

> **Livscykel-not:** Skriven 2026-05-19 av Claude Code på explicit Klas-begäran
> (medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen för
> denna session). Besluts-substansen är transkriberad från Block 2-beslut +
> dotnet-architect-/senior-cto-advisor-domar — inga nya beslut konstruerade.
> Status **Proposed**: Accepted-flip kräver separat Klas-GO och utförs ej
> denna session. Faktisk migration utförs INTE denna session.

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

### Beslut 2 — Backend: Hetzner Cloud CX32, all-in-one Docker Compose

Hetzner Cloud **CX32** (4 vCPU shared / 8 GB RAM / 80 GB disk, ~€6,80/mån,
EU-datacenter Falkenstein/Helsinki, pris-verifierat 2026-05-19 efter
pris-justering 2026-04-01). En box kör hela backend-stacken i Docker Compose:
.NET API + .NET Worker + PostgreSQL + Redis + Caddy (reverse proxy, auto-TLS).

CX32 vald över CX22 (2 vCPU / 4 GB / 40 GB, ~€3,79/mån) på sizing-grund:
på en box samsas allt om samma RAM (AWS isolerar RDS/Redis managed; en VPS
gör det inte). Med 45 000+ annonser + raw_payload-jsonb + sök/typeahead-index
behöver Postgres co-tenant ~2–3 GB för att hålla sök snärtig, och
Platsbanken-ingestion-jobbet är den dokumenterade minnes-blowout-vektorn
(ADR 0032/TD-13-grunden). CX22:s 4 GB totalt + 40 GB disk är under-provisionerat
för det; CX32:s 8 GB/80 GB ger headroom och är dessutom en perf-uppgradering
över dagens trånga `db.t4g.micro` (1 GB). Prisdeltat (~€3/mån) är trivialt mot
en helprodukts-OOM på en singel-box.

### Beslut 3 — Frontend: Vercel behålls

Next.js-frontend kvar på Vercel (EU). Ingen ändring — Vercel free/Pro-nivå
bär frontend; ingen anledning att flytta in den på VPS-boxen och därmed öka
dess RAM-/ops-börda.

### Beslut 4 — Cloudflare: backup-offload + proxy

Cloudflare gratis-tier framför boxen (TLS-edge/DNS/CDN/DDoS). Cloudflare **R2**
som mål för nattlig `pg_dump`-offload — backups ligger INTE på boxens 80 GB
(R2 har ingen egress-cost; håller disk-budgeten långsiktigt hållbar mot
korpus-tillväxt + WAL + Docker-images).

## Konsekvenser

### Positiva

- Återkommande kostnad ~€6,80/mån (~$7,40) vs ~$45+/mån AWS-trajektoria —
  ~85% reduktion, materiell på studentbudget.
- Ren ops-yta: en box, Docker Compose, inga moln-SDK-/IAM-tethers.
- Eliminerar AWS-SDK-beroenden i kodbasen (jfr ADR 0051 — `AWSSDK.BedrockRuntime`
  byggs aldrig).
- ADR 0005:s kostnadsskydds-apparat (Budget Actions, Bedrock-deny,
  registrations_open-gating) blir **i stort sett moot post-migration** —
  relevans-skifte, ej supersession (ADR 0005-text orörd; flaggas i Block 4).

### Negativa

- **Singel-box blast-radius:** API/Worker/Postgres/Redis delar OS, RAM och
  feldomän. En OOM eller box-incident tar hela produkten, inte en isolerad
  container (kontrast mot AWS managed-isolering).
- Självhanterad Postgres + Redis + backups: ingen managed RDS-HA, ingen
  point-in-time-restore out-of-the-box, patch-/vacuum-/WAL-ansvar på Klas.
- Ops-börda flyttas från AWS-managed till Klas-manuell (Docker Compose-deploy,
  Caddy-config, restore-drill).

### Öppen fråga — KMS-beroende (migrations-blocker, EJ löst denna session)

**ADR 0049 (TD-13, stängd 2026-05-19) implementerade PII-fält-kryptering för
fyra user-ägda kolumner via AWS-KMS-envelope (per-användar-DEK, KMS
`GenerateDataKey`/`Decrypt`, dedikerad CMK).** En full AWS-exit (Beslut 1)
**tar bort AWS KMS**. Den load-bearing GDPR-krypto-mekanismen kan inte
migreras genom att bara flytta containrar — wrapping-nyckeln (CMK) och
DEK-envelope-operationerna måste om-hemmas till en icke-AWS-nyckel­förvaltning
(t.ex. HashiCorp Vault Transit, libsodium/age med nyckel i Hetzner-Secrets,
eller annan KMS) **med bevarad crypto-erasure-semantik** (ADR 0049 Beslut 2,
EDPB CEF 2025-motiverad).

Detta är en **oläst migrations-blocker** som måste lösas och designas (egen
discovery + sannolikt ADR 0049-amendment eller ny ADR) **innan** faktisk
migration utförs. Den är medvetet ej löst i denna ADR — den namnges för att
exit-planen inte tyst ska anta att krypto "bara följer med". Flaggas till
adr-keeper + som Block-4-noterad öppen punkt; kandidat för TD (annan
fas/saknad dependency, §9.6) — Klas/CTO-triage.

### Mitigering

- Nattlig `pg_dump` → Cloudflare R2 + dokumenterad restore-drill innan
  produktions-cutover (DoD-grind).
- Caddy auto-Let's-Encrypt; health-checks + extern uptime-monitor
  (UptimeRobot/BetterStack free) ersätter ALB/CloudWatch-health.
- KMS-blockern (ovan) löses som egen designomgång före migration — ej
  parallellt med container-flytt.
- Lasttest mot 45k-korpuset (NBomber, ADR 0045) före cutover för att
  validera CX32-sizing empiriskt.

## Alternativ övervägda

- **CX22 (2 vCPU/4 GB/40 GB):** Avvisad. Under-provisionerad för co-tenant
  Postgres med 45k+ korpus + ingestion-minnesprofil; noll headroom för
  korpus-tillväxt; 40 GB disk snäv (PG + WAL + backups + raw_payload).
- **Hybrid (behåll Bedrock/utvald AWS-tjänst på AWS):** Avvisad — bevarar
  AWS-konto/IAM/SDK-tether + kostnads-svans för marginell nytta. ADR 0051
  eliminerar Bedrock-motivet helt.
- **Stanna på AWS:** Avvisad — dominerande återkommande kostnad på
  studentbudget; ingen funktionell vinst som motiverar den mot Hetzner-paritet
  för en beta-skala.
- **Annan VPS/PaaS (DigitalOcean/OVH/Vultr/Coolify-managed):** Ej djup-jämförd
  — Klas pre-beslutade Hetzner (Block 2). Provider-jämförelsen i startpromptens
  Block 2 blev därmed akademisk; sizing-frågan (CX22 vs CX32) var den enda
  levande beslutsaxeln och är avgjord i Beslut 2.
- **Managed Postgres utanför boxen (t.ex. liten managed PG hos Hetzner/annan):**
  Ej valt för beta-skala (extra kostnad/komplexitet); co-tenant Postgres på
  CX32 är tillräckligt om sizing hålls. Kan omvärderas vid skala-signal
  (Trigger, §9.6) — ej TD.

## Implementationsstatus

**Proposed.** Ingen migration utförd. Ingen infra-ändring denna session.
Faktisk migration är ett framtida Klas-gatat arbete efter MVP-presentationen
2026-05-25, med KMS-blockern (Konsekvenser/Öppen fråga) löst först.

## Validering

Uppskjuten till migrations-utförandet: NBomber-lasttest mot 45k-korpus
(ADR 0045-budgetar), `pg_dump`-restore-drill, end-to-end-rök på Hetzner-box
före DNS-cutover. Rollback: behåll AWS-stacken körande tills Hetzner-paritet
verifierad; DNS-cutover (Cloudflare) är den reversibla flippen.

## Relaterade beslut

- **ADR 0005** — kostnadsskydd/launch-gating. Post-migration blir Budget
  Actions/Bedrock-deny/registrations_open i stort sett moot. **Relevans-skifte,
  ej supersession** — ADR 0005-text ändras inte; flaggas i Block 4.
- **ADR 0019** — direct-push/granskningsspärrar. Oförändrad; migration följer
  samma STOPP-disciplin.
- **ADR 0049** — TD-13 KMS-envelope. **AWS-KMS-beroende = namngiven
  migrations-blocker** (Konsekvenser/Öppen fråga). Cross-ref; ev. amendment
  vid KMS-rehoming-design.
- **ADR 0051** — AI-provider Anthropic Direct/Bedrock utgår. Möjliggör ren
  exit (Beslut 1). Skrivs i samma Block 4.
- **BUILD.md Bilaga B** — planerad `NNNN-aws-over-azure.md`. Denna ADR fyller
  slotten med motsatt slutsats (moln-exit, ej moln-byte). adr-keeper
  uppdaterar "Planerade ADRs"-listan.

## Referenser

- `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md` — Block 2/3-discovery,
  web-verifierade priser/sizing (Hetzner CX-plans, pris-justering 2026-04-01)
- dotnet-architect IaC-/sizing-review 2026-05-19 (denna session)
- senior-cto-advisor §9.6-triage 2026-05-19 (denna session)
- ADR 0005 / 0019 / 0049 / 0051 · CLAUDE.md §2.5 (perf), §9.2, §9.6
- Hetzner Cloud pricing (web-verifierat 2026-05-19): regular-performance /
  pris-justering 2026-04-01
