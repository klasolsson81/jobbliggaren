# ADR 0026 — ALB HTTP-only acceptance under Fas 0 (tidsfönster + triggers för upphörande)

**Datum:** 2026-05-09
**Status:** Accepted
**Kontext:** STEG 13b — security-auditor Sec-Major-1 över ALB-modulen i `infra/terraform/modules/alb/`. ALB lyssnar initialt på HTTP port 80 utan TLS-encryption.
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0017 (frontend auth pattern — opaque session-id i Redis), ADR 0018 (cookie + CSRF strategy), ADR 0024 D7 (logg-retention 30d), ADR 0025 (ECS egress acceptance Fas 0), BUILD.md §15.1 (Route 53 → CloudFront → ALB → ECS-spec), `docs/runbooks/aws-setup.md`, `docs/reviews/2026-05-09-steg13b-security.md` (Sec-Major-1)

## Kontext

STEG 13b:s `alb`-modul exponerar ECS-tasks via en Application Load Balancer. ALB-listenern är initialt **HTTP-only på port 80** — ingen HTTPS-listener på port 443, ingen TLS-terminering, inget ACM-certifikat.

security-auditor STEG 13b Sec-Major-1 flaggade detta som GDPR Art. 32-implikation:
- Authentication-flödet (per ADR 0017) returnerar en **opaque Redis-baserad session-id** som klienten skickar i `Authorization: Bearer <id>`-header på efterföljande requests.
- Med HTTP-only-ALB går session-id i klartext över internet mellan klient (Klas hemnät) och AWS-edge i `eu-north-1`.
- Session-id ger access till JobbPilot-konto-yta inkl. PII (email, JobSeeker-profil, Application/Resume-aggregat).

Tre alternativ utvärderades:

1. **Alt A — Registrera `jobbpilot.se` + ACM-cert + Route53 NU.** Tekniskt enklast på lång sikt men kräver:
   - Domän-registrering (~80 kr/år hos svensk registrar, opersistalbar tidskostnad ~1 timme)
   - Route53 hosted zone + delegering från registrar (~30 min DNS-propagering)
   - ACM DNS-validering (~5-30 min)
   - Bundlas in i 13b — utvidgar scope.

2. **Alt B — ADR-acceptans med tidsfönster + triggers.** Pragmatisk Fas 0-position. Dokumenterar mitigation-stack och ger automatiskt upphörande vid definierade trigger-villkor.

3. **Alt C — Blockera STEG 13b-apply tills Alt A löst.** Konservativaste vägen. Försenar deploy-pipeline-verifiering utan proportionerlig säkerhetsvinst (dev-yta är solo-utvecklare).

Klas:s val: **Alt B**.

**Krafter som spelar in:**

1. **Threat-model för dev-fasen.** ECS-tasks är dev-miljön. Endast Klas själv har konton. Ingen multi-tenant-yta. Realistic threat = passive nätverks-sniffing från klient till AWS-edge (~10-15 hops genom svenska ISP:er). Sannolikheten för riktad attack mot exakt denna trafik är låg under solo-utvecklingsfas.

2. **Dev-yta är intern men inte privat.** ALB-default-DNS (`*.elb.amazonaws.com`) är publikt nåbar — ingen authentication är nödvändig för att hitta endpointen. Brute-force-skanning av AWS-prefix-listor kan upptäcka tjänsten. Mitigation: rate-limiting per ADR 0017 + Sec-Minor-2-runbook + STEG 11 TD-21 (auth-write 20/min IP, auth-loose 30/min IP) — gjord i kod, aktiv vid första apply.

3. **PII-mitigation-stack redan på plats** (per STEG 11 + ADR 0024 D7):
   - IP-anonymisering /24+/48 i app-loggar (`AuthAuditLogger`, `RequestContextProvider`)
   - EmailHash via SHA-256 (HMAC-rotation defererad till TD-27)
   - Audit-log Art. 17-cascade-anonymisering efter 30 dagar
   - CloudWatch retention 30 dagar (`/aws/ecs/<env>/<service>` + RDS-export)

4. **Domän-registrering bundlat i 13b skulle dubbla scope.** STEG 13b är redan tight på review-yta (5 nya moduler + Dockerfiles + IAM + secrets-flöde). En 6:e modul (route53) + ACM-validering-väntan komplicerar STOPP-cykeln.

## Beslut

ALB-listener accepteras som **HTTP-only** under Fas 0 med följande gränser:

> **Variabel-namn-anteckning:** ADR refererar till `var.alb_https_enabled`
> (env-stack-tfvars-variabel som operatören ändrar). Modul-input heter
> `https_listener_enabled` och mappas via `module "alb" {
> https_listener_enabled = var.alb_https_enabled }`. Single source of truth:
> env-stack-variabeln.

### Tidsfönster

**Maximalt 30 dagar från ADR-acceptans (2026-05-09).** Faktisk upphörande-deadline: **2026-06-08**. Efter denna datum måste antingen:
- ALB ha HTTPS-listener aktiverad (`var.alb_https_enabled = true` i `environments/dev/`), eller
- ECS-services + ALB ha tagits ner (`terraform destroy module.alb module.ecs`), eller
- Ny ADR utfärdats som superseder denna 0026 med uttryckligen förlängt fönster och dokumenterad orsak.

### Triggers för upphörande (vilken som helst aktiverar)

ADR 0026 upphör automatiskt vid någon av:

1. **Domän-trigger:** `jobbpilot.se` eller annan hostable domän registrerad och ACM-cert utfärdat. → flippa `var.alb_https_enabled = true` + sätt `acm_certificate_arn`. ALB HTTP-listener konverterar till HTTPS-redirect via Terraform-dynamic-block (existerande modul-kod).

2. **Multi-tenant-trigger:** Annan användare än Klas själv får konto i dev-miljön (klasskamrater för intern testning, demo-konton, etc). Inom 24 timmar från första icke-Klas-kontoskapande måste HTTPS aktiveras eller dev tas ner.

3. **Tidsgräns-trigger:** 30 dagar från 2026-05-09 = **2026-06-08**. Auto-trigger oavsett feature-status.

4. **Säkerhetsincident-trigger:** Misstänkt sniffing, anomalous CloudTrail-pattern (t.ex. SUCCESS-anrop från okänd region eller IP), security-auditor-fynd med bedömning "session-id exfiltration risk". Omedelbar trigger.

5. **Fas 2-trigger:** STEG 13b → Fas 2 (JobTech-integration, public-exponering) får inte påbörjas innan HTTPS aktiverat. Konsistens med ADR 0005:s "obligatoriska kostnadsskydd innan Fas 2" + ADR 0025:s Fas 1→2-omvärderingstrigger.

### Mitigation-stack under fönstret

1. **Rate-limiting aktivt** (STEG 11 TD-21) — auth-write 20/min IP, auth-loose 30/min IP, account-deletion 1/60s UserId
2. **App-logg-anonymisering** (ADR 0024 D7) — IP /24+/48 + EmailHash SHA-256
3. **Audit-cascade Art. 17** (ADR 0024 D3-D6) — anonymiserings-fönster 30d efter soft-delete
4. **CloudTrail-monitoring** — alla AWS-API-anrop loggade; ADR 0005 §1 Budget Action-trigger vid $50
5. **Restriktiv egress** (ADR 0025-mitigation) — TLS-only out till Bedrock + Secrets Manager + KMS, DenyInsecureTransport på state-bucket
6. **Inga publika ALB-DNS-marknadsföring** — Klas lägger inte ut ALB-default-DNS i README, social media, eller deployer som listas i Shodan-style scanners. ALB-default-DNS är "obfuscation by URL" (icke-säkerhet, men minskar incidentella besökare).

### Vad som SKA dokumenteras innan upphörande

När ADR 0026 supersedas (vilken trigger som än aktiverar):
- Skapa ny ADR (ADR 0026.x eller 0027) som superseder denna
- Dokumentera trigger som aktiverade (1-5 ovan)
- Verifiera + commit `var.alb_https_enabled = true` + `acm_certificate_arn` i `environments/dev/terraform.tfvars` ELLER `terraform destroy` på alb + ecs-modulerna
- Uppdatera `current-work.md` + `steg-tracker.md`

## Konsekvenser

### Positiva

- **STEG 13b kan apply:as utan extra scope-utvidgning** — domän-registrering blir egen task (STEG 13c eller separat)
- **Tidsfönster + triggers är dokumenterade och datum-konkreta** — ingen "vi tar det sen"-glidning
- **Mitigation-stack är reell** — rate-limiting + IP-anonymisering + audit-cascade + CloudTrail + restriktiv egress mitigerar attack-yta utan TLS
- **Hardening-väg är förberedd** (`var.alb_https_enabled` + `acm_certificate_arn` redan i koden) — supersession är en `terraform.tfvars`-edit + apply

### Negativa

- **Session-id i klartext** under maximalt 30 dagar mellan Klas hemnät och AWS-edge eu-north-1 — passive nätverks-sniffing skulle ge fullständig konto-access om utförd
- **Avvikelse från BUILD.md §15.1** spec (Route 53 → CloudFront → ALB → ECS) — kompenseras av tidsfönster
- **GDPR Art. 32-bedömning bygger på dev-fas-context** — om kontexten skiftar (multi-tenant, public-exponering) före upphörande är ADR:n inte längre giltig

### Mitigering

- Trigger 4 (säkerhetsincident) är öppen — Klas avgör subjektivt vid CloudTrail-anomaly om ADR 0026 ska upphöra omedelbart
- Trigger 2 (multi-tenant) är hårt definierad — första icke-Klas-konto = 24h-fönster
- Tidsgräns 30 dagar är defensivt val — kan kortas (men inte förlängas) via ny ADR

## Alternativ övervägda

(Avvisade alternativ inline i Kontext-sektionen.)

### Alt A — Registrera domän + ACM nu

Avvisat eftersom:
- Utvidgar STEG 13b-scope under aktiv reviewa-cykel (5 moduler + Dockerfiles redan stort)
- ACM DNS-validering tar 5-30 min, kan blockera STEG 13b-test-cykel
- Domän-registrering är operativ task (svensk registrar, betalning) som hör hemma i egen STEG (13c)
- Bundlat scope ökar risk för partial-rollback om något brister

### Alt C — Blockera apply

Avvisat eftersom:
- Försenar deploy-pipeline-verifiering utan proportionerlig säkerhetsvinst i Fas 0
- Threat-model för solo-dev-fas är väsentligt lägre än multi-tenant-fas
- Mitigation-stack (rate-limiting, IP-anonymisering, audit-cascade) är reell — inte spelad
- "Perfect is the enemy of good" i pre-launch-kod-fas

## Implementations-status

- ✅ ALB HTTP-only via `var.alb_https_enabled = false` (default i `environments/dev/variables.tf`)
- ✅ ALB-modul stödjer HTTPS via dynamic-block + ACM-cert-input — flip vid trigger
- ✅ Mitigation-stack på plats (STEG 11 + STEG 12 + ADR 0024 D7)
- ✅ Tidsfönster + 5 triggers dokumenterade ovan
- ⏳ Supersession (vid trigger): ny ADR + flippa `alb_https_enabled = true` + ACM-cert
- ⏳ TD-30: Domänköp tidigareläggning — operativ task, kopplad till denna ADR

## Validering

ADR 0026 omvärderas och supersedas vid någon av triggers (1-5) ovan. Senast 2026-06-08.

Klas-disciplin: kontrollera ADR-status varannan vecka via `git log --since="2 weeks ago" docs/decisions/0026-*` + `current-work.md` Aktivt-nu-sektion. Om datum 2026-06-08 närmar sig och ingen trigger aktiverats än → forced trigger 3 → supersession-ADR krävs eller `terraform destroy`.
