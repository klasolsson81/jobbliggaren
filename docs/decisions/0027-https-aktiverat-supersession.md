# ADR 0027 — HTTPS aktiverat på dev-ALB; ADR 0026 supersedas

**Datum:** 2026-05-10
**Status:** Accepted (HTTPS smoke-test PASS verifierat 2026-05-10 17:48)
**Kontext:** STEG 13c — Route53 + ACM + ALB HTTPS-flip för dev.jobbpilot.se. ADR 0026 trigger 1 (domän registrerad + ACM-cert utfärdat) aktiverad.
**Beslutsfattare:** Klas Olsson
**Supersedes:** [ADR 0026](./0026-alb-http-only-fas0.md)
**Relaterad:** ADR 0017 (frontend auth — opaque session-id i Redis), ADR 0018 (cookie + CSRF), ADR 0024 D7 (logg-retention 30d), ADR 0025 (ECS egress Fas 0), BUILD.md §15.1 (Route 53 → CloudFront → ALB → ECS-spec), [docs/reviews/2026-05-10-steg13c-security.md](../reviews/2026-05-10-steg13c-security.md), [docs/reviews/2026-05-10-steg13c-code-review.md](../reviews/2026-05-10-steg13c-code-review.md)

## Kontext

ADR 0026 (2026-05-09) accepterade ALB HTTP-only på dev under Fas 0 med 30-dagars tidsfönster (deadline 2026-06-08) och 5 triggers för supersession. Trigger 1 (domän + ACM-cert utfärdat) aktiverades 2026-05-10:

- `jobbpilot.se` registrerades hos svensk registrar (~80 kr/år)
- Route53 hosted zone skapad i `prod/baseline.tfstate` (zone-id `Z028392711DGTDR1MGVC9`)
- ACM-cert för `dev.jobbpilot.se` utfärdat via DNS-validering i eu-north-1
- A-ALIAS-record `dev.jobbpilot.se → ALB` skapad

ADR 0026 supersedas härmed. HTTPS aktiveras på ALB-listenern via dynamic-block-flip (`var.alb_https_enabled = true` + `var.alb_acm_certificate_arn = <arn>`).

## Beslut

ALB-listener konverteras från HTTP-only till **HTTPS-redirect på 80 + HTTPS-forward på 443** med följande konfiguration:

### TLS-konfiguration (ALB)

- **TLS-policy:** `ELBSecurityPolicy-TLS13-1-2-2021-06` (befintligt val i `modules/alb/main.tf:118`)
  - Lägsta TLS-version 1.2, TLS 1.3 prioriterat
  - Inga svaga ciphers (RC4, 3DES, NULL)
  - PQ-2025-09 (post-quantum) lyfts till **TD-32** som Fas 1-uppgrade
- **HTTP→HTTPS-redirect:** `HTTP_308 (Permanent Redirect)` — bevarar request-method (POST→POST). Tidigare 301 tilläts klienter downgrad:a POST→GET vilket kunde tappa request-body.
- **ACM-cert:** DNS-validerat via Route53 CNAME, auto-renewal så länge validation-records ligger kvar (~13 mån livstid + auto-renewal vid 60 dagar kvar)
- **SAN-scope:** bara `dev.jobbpilot.se`. Apex `jobbpilot.se` + wildcard utelämnade — tillkommer separat när prod-stack rullas ut.

### HSTS-konfiguration (ASP.NET Core)

`UseHsts()` registreras i Api/Program.cs gate:at på `!IsDevelopment() && AlbOptions.HttpsEnabled` (samma rationale som UseHttpsRedirection — undviker browser-HTTPS-lock på localhost i dev).

- **max-age:** 365 dagar (HSTS-spec + hstspreload.org-krav)
- **includeSubDomains:** true (skyddar alla framtida subdomäner)
- **preload:** false (kräver hstspreload.org-submission post-prod-launch — oåterkalleligt på kort sikt)

Production-defense via `HstsOptions.EnsureSafeForEnvironment(env)` — fail-loud vid uppstart om `MaxAgeDays < 365` utanför Development/Test eller om Preload-config bryter hstspreload.org-krav. Paritet med `ForwardedHeadersConfig.EnsureSafeForEnvironment` (STEG 12 Sec-Major-1).

### DNSSEC — defererat till Fas 1

DNSSEC på Route53-zonen aktiveras INTE i Fas 0. Trade-offen: DNS-cache-poisoning-yta accepteras i utbyte mot ops-overhead (cross-region KMS i us-east-1, årlig manuell KSK-rotation, ~$1/mån KMS-kostnad).

**Trigger-villkor för aktivering** (Fas 1):
1. **Multi-tenant-trigger:** första icke-Klas-konto i dev/staging → 30 dagars fönster
2. **OAuth-aktivering:** Microsoft/Google-OAuth introduceras (DNS-spoofing kan kapa OAuth-callback)
3. **Säkerhetsincident:** misstänkt DNS-cache-poisoning eller anomalous DNS-query-pattern via CloudWatch
4. **Fas 2-trigger:** JobTech-integration / public-exponering → DNSSEC obligatoriskt innan Fas 2-start

Mitigation under Fas 0:
- HSTS skyddar mot SSL-stripping (HSTS-policy persistar 365 dagar i browser)
- ACM auto-renewal med Route53 DNS-validation — om DNS-spoofing försökte ändra validation-CNAME skulle ACM upptäcka mismatch
- CloudTrail loggar alla Route53-API-anrop — anomali-detection möjlig

### Plain-text ALB → ECS-task — accepterat Fas 0

ALB terminerar TLS; trafik mellan ALB och ECS-task är HTTP (port 8080) inom VPC. Detta accepteras under Fas 0 eftersom:
- VPC är privat /16 (10.0.0.0/16), ECS-tasks i private subnets (10.0.10/11/12.0/24)
- ECS-SG-ingress 8080 enbart från ALB-SG (referenced_security_group_id, inte CIDR)
- Cross-AZ-trafik inom samma VPC är AWS-internt nätverk — inte sniffable från utomstående tenant
- mTLS / in-VPC-encryption blir Fas 2-uppgrade om JobTech / multi-tenant kräver SOC2-bevis

**Trigger för uppgrade:** Fas 2 (multi-tenant) eller compliance-krav (HIPAA/PCI/SOC2).

## Konsekvenser

### Positiva

- **Session-id-skydd via HTTPS** — ADR 0017:s opaque Redis-session-id går nu krypterad mellan klient och AWS-edge. ADR 0026:s 30-dagars klartext-fönster avslutas.
- **HSTS skyddar mot SSL-stripping** — browser nekar HTTP-fallback i 365 dagar efter första HTTPS-besök.
- **308-redirect bevarar POST-method** — framtida POST-API-anrop från icke-redirect-aware-klient tappar inte request-body.
- **GDPR Art. 32-position stärkt** — krypterad transport från klient till AWS-edge är "appropriate technical measure" enligt EDPB-guidance.
- **Domän + DNS-stack på plats** — staging/prod kan återanvända samma route53-zone via data-lookup. Fas 2-arbete dramatiskt enklare.

### Negativa

- **DNSSEC saknas** — DNS-cache-poisoning-attack möjlig (om än osannolik mot solo-dev-fas). Mitigerad av HSTS + ACM-renewal-validation. Trigger-villkor dokumenterade.
- **Plain-text ALB → ECS** — passive in-VPC-sniffing teoretiskt möjligt vid AWS-tenant-isolation-brott (extremt låg sannolikhet). Mitigerad av VPC-isolation + SG-rules.
- **TLS-policy 2021-06** — fortfarande supported av AWS men inte PQ-aware. Lyfts som TD-32 för Fas 1-uppgrade till `ELBSecurityPolicy-TLS13-1-2-2025-09` (post-quantum-cipher-aware).
- **Ny kostnad:** Route53 hosted zone $0.50/mån + ~$0.40 per miljon DNS-queries. Försumbart vid dev-volym.

### Mitigering

- HSTS-config production-defense (EnsureSafeForEnvironment) hindrar tyst regression vid framtida config-fel
- TD-32 (HSTS pipeline-gating-test) lyfts som anti-regression-pattern (motsvarande TD-31 för UseHttpsRedirection)
- DNSSEC-trigger-villkor är datum-konkreta + händelse-konkreta — ingen "vi tar det sen"-glidning

## Implementations-status

Allt klart 2026-05-10:

- ✅ Route53 hosted zone skapad i prod/baseline (`Z028392711DGTDR1MGVC9`)
- ✅ Domän `jobbpilot.se` registrerad hos STRATO + NS pekade på AWS-Route53
- ✅ HSTS-implementation i Api/Program.cs + HstsOptions.cs + EnsureSafeForEnvironment
- ✅ HSTS-tester (165/165 integration-tests gröna inkl. 17 nya)
- ✅ ALB-redirect HTTP→HTTPS aktiv. **Status_code = HTTP_301** (AWS ALB
  RedirectActionConfig hardlimited till 301/302 — 308 ej implementerbart utan
  Lambda@Edge eller CloudFront-rewrite). Mitigation: HSTS+browser-cache gör
  POST-anrop går direkt till HTTPS efter första GET; 301 påverkar bara
  initial GET som inte berörs av method-downgrade.
- ✅ Api Docker-image rebuilt + pushed till ECR med HSTS-fix
- ✅ ACM-cert utfärdat: `arn:aws:acm:eu-north-1:710427215829:certificate/f72a79d7-f964-49c7-abb5-cf81b8639d6a`
- ✅ A-ALIAS `dev.jobbpilot.se → ALB` aktiv
- ✅ `terraform apply` HTTPS-flip: 4 added, 1 changed, 0 destroyed
- ✅ ECS rolling deployment COMPLETED efter ~120s
- ✅ Smoke-test `https://dev.jobbpilot.se/api/ready` → `200 OK`
- ✅ HSTS-header verifierad: `Strict-Transport-Security: max-age=31536000; includeSubDomains`
- ✅ HTTP→HTTPS redirect verifierad: `301 Moved Permanently → https://dev.jobbpilot.se:443/api/ready` (via awselb/2.0)
- ✅ Status: Proposed → **Accepted** (denna ADR)
- ✅ ADR 0026 Status: Accepted → **Superseded by ADR 0027**

## Validering

ADR 0027 omvärderas vid:
- DNSSEC-trigger aktiverad (1-4 ovan)
- TLS-policy-uppgrade till PQ-aware (TD-32)
- Domän-renewal-failure (kontrollera registrar auto-renewal varje halvår)

ADR 0026 markeras som **Superseded by ADR 0027** efter denna ADR satts till Accepted.

## Open follow-ups (TD)

- **TD-32:** TLS-policy uppgrade till `ELBSecurityPolicy-TLS13-1-2-2025-09` (post-quantum) — Fas 1
- **TD-33:** HSTS pipeline-gating-test via `WebApplicationFactory<Program>` — anti-regression motsvarande TD-31 för UseHttpsRedirection
- **TD-34:** DNSSEC aktivering vid Fas 1-trigger (cross-region KMS us-east-1 + KSK-rotation-runbook)
- **TD-35:** Apex (`jobbpilot.se`) + `www.jobbpilot.se` ACM-cert + ALB-cert-association vid prod-stack-rollout
- **TD-36:** mTLS / in-VPC-encryption (ALB → ECS) vid Fas 2 multi-tenant
