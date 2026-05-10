# Security-audit: STEG 13c (Route53 hosted zone + ACM-cert + ALIAS-record dev.jobbpilot.se)

**Status:** APPROVE-with-fixes (Sec-Major-1 + Sec-Major-2 bör adresseras innan flippa-till-HTTPS-applyen; Sec-Minor 1-3 är defense-in-depth-rekommendationer som kan tas i nästa iteration). **Inga Sec-Critical. Apply av STEG 13c-resurserna i sig blockeras inte** — själva DNS+ACM-kompositionen är korrekt och säkerhetshöjande mot nuvarande HTTP-only-yta.

**Granskat:** 2026-05-10
**Auktoritet:** GDPR Art. 32, CLAUDE.md §5.4 + §11, ADR 0026 (HTTP-only Fas 0 — supersedas av denna STEG), BUILD.md §15.1, AWS ALB security-policy-docs (verifierat via web-search 2026-05-10), Route53 DNSSEC-docs (verifierat via web-search 2026-05-10).

**Granskat scope:**
- `infra/terraform/modules/route53/{main,variables,outputs}.tf` (NY)
- `infra/terraform/modules/acm/{main,variables,outputs}.tf` (NY)
- `infra/terraform/environments/prod/{main,variables,outputs}.tf` (delta — module "route53")
- `infra/terraform/environments/dev/{main,variables,outputs}.tf` (delta — `data "aws_route53_zone"` + module "acm_dev" + ALIAS-record)
- Kontext: `infra/terraform/modules/alb/main.tf` (TLS-policy, listener-flip), `infra/terraform/modules/network/main.tf` (ALB-SG)

---

## Sammanfattning

STEG 13c är **säkerhetshöjande netto** — den avslutar ADR 0026:s HTTP-only-fönster och är trigger 1-aktivering enligt ADR 0026 §"Triggers för upphörande". Klartext-session-id-fönstret över internet stängs så snart `alb_https_enabled = true` flippas i samma terraform.tfvars-cykel som referererar `acm_dev_certificate_arn`.

**DNS+ACM-kompositionen i sig är korrekt:**
- `aws_acm_certificate_validation` används som källa till `certificate_arn`-output (inte raw `aws_acm_certificate.this.arn`) → konsumenter kan inte plocka upp ovaliderad cert
- `create_before_destroy` på ACM → ingen listener-downtime vid framtida cert-rotation
- DNS-validering (inte EMAIL) → auto-renewal-vänlig
- ALIAS-record (gratis, AWS-internt — inte CNAME) korrekt val mot ALB
- Apex hosted zone i prod/baseline + cross-stack data-lookup via `data "aws_route53_zone"` → state-isolation respekterad, ingen sensitive data läcker mellan stacks

**Fynd som adresseras nedan:**
- 0 Critical
- 2 Major (DNSSEC + HSTS-strategy — inte blocker för 13c-applyen, men måste göras eller dokumenteras innan beta-användare)
- 3 Minor (TLS-policy-uppdatering 2025-09-PQ tillgänglig; plain-text ALB→ECS Fas 1-bedömning; ACM-validation-timeout)
- 4 Praise (bra defaults)

---

## Critical

Inga.

---

## Major

### Sec-Major-1 — DNSSEC saknas på `aws_route53_zone "this"`: DNS-cache-poisoning + spoof-risk under hela JobbPilots livstid

**Filer:** `infra/terraform/modules/route53/main.tf:13-20` (resurs-definitionen) + `infra/terraform/environments/prod/main.tf:78-83` (modul-instansiering).

`aws_route53_zone` skapas utan DNSSEC-signing aktiverat. Detta är default-beteende, men för en domän som ska bära auth-flöden, OAuth-callbacks (Gmail/Google planeras enligt BUILD.md), och PII-trafik är frånvaron av DNSSEC en faktisk attack-yta:

1. **DNS-cache-poisoning** — utan DNSSEC kan resolver-cachen hos en mellan-ISP förgiftas (Kaminsky-style eller off-path-injection). En förgiftad cache pekar `dev.jobbpilot.se` mot angripares ALB-replika med valid Let's Encrypt-cert för en lookalike-domän → fungerande HTTPS-yta som angripare kontrollerar. TLS skyddar inte mot detta — DNS-svaret är komprometterat *innan* TLS-handshake.
2. **NXDOMAIN-injection / takeover-skydd** — DNSSEC validerar även negativa svar (NSEC/NSEC3). Utan signering kan angripare svara "domänen finns inte" på selektiva subdomäner och blockera tjänsten utan att Klas märker det.
3. **OAuth-callback-yta** (kommer enligt ADR 0023 + BUILD.md) — när Gmail/Google OAuth-callback registreras på `dev.jobbpilot.se/oauth/...` blir DNS-integritet en del av OAuth-trust-modellen. Om DNS-svar kan spoofas kan angripare ta emot auth-codes.

**GDPR Art. 32-bedömning:** "lämpliga tekniska åtgärder" inkluderar DNS-integritet för domäner som bär PII-trafik. För Fas 0 (solo-dev, dev.jobbpilot.se, ingen OAuth ännu) är riskprofilen lägre än Fas 1+. För apex-zonen (jobbpilot.se) som senare bär staging/prod är DNSSEC en hård rekommendation.

**Trade-off (förstått):**
- KSK-rotation är manuell — AWS rekommenderar årligen (web-search 2026-05-10)
- KMS-nyckel för DNSSEC måste vara `us-east-1` + `ECC_NIST_P256` (annan region än övriga JobbPilot-resurser → splitt KMS-management)
- TTL på alla records cappas till max 1 vecka när DNSSEC är på
- KSK-management kräver CloudWatch-alarms för `DNSSECInternalFailure` + `DNSSECKeySigningKeysNeedingAction`
- Operativ börda: ~2-4h initial setup + 1h/år rotation + alarmövervakning

**Fix-alternativ (Klas väljer):**

**A — Acceptera Fas 0 utan DNSSEC, dokumentera i ADR 0027** (rekommenderas givet solo-dev + ingen OAuth ännu). ADR 0027 (som ändå måste skapas för att superseda 0026) inkluderar sektion "DNSSEC-strategi": deferral till Fas 1-trigger med konkret datum eller event (t.ex. "första OAuth-integration" eller "första icke-Klas-användare på prod").

**B — Aktivera DNSSEC nu** på apex-zonen. Lägg till `aws_route53_key_signing_key` + `aws_route53_hosted_zone_dnssec` + `aws_kms_key` i `us-east-1` (separat aliased provider). Operativ börda accepteras. → delegera till **dotnet-architect** (terraform-arkitektur) eller **db-migration-writer** (om det räknas som infra-migration).

**Bedömning:** Sec-Major (inte Critical eftersom Fas 0-threat-model och frånvaro av OAuth ännu reducerar exploit-sannolikhet under fönstret tills Fas 1). **Måste avgöras (Alt A eller B) innan ADR 0027 färdigställs.** Apply av STEG 13c-resurser i sig blockeras inte av detta fynd.

**Delegera till:** **adr-keeper** (ADR 0027 ska inkludera DNSSEC-deferral-eller-aktivering-beslut), **dotnet-architect** vid Alt B (terraform-implementation av key_signing_key + KMS-key cross-region).

---

### Sec-Major-2 — HSTS-strategi inte beslutad: efter HTTPS-flippet är `301 → https://` korrekt, men utan `Strict-Transport-Security`-header förblir första-besöket downgrade-bart

**Filer:** `infra/terraform/modules/alb/main.tf:82-110` (HTTP→HTTPS-redirect via 301) + Api/Program.cs (HSTS-middleware-config — inte i diff men måste verifieras).

**Konkret problem efter att HTTPS flippats:**
1. ALB:n returnerar `HTTP/1.1 301 Moved Permanently` med `Location: https://dev.jobbpilot.se/...` på port 80 → korrekt redirect-flow.
2. Men ALB sätter **inga response-headers själv** — HSTS måste komma från Api-tasks (eller från CloudFront, vilken inte är i scope).
3. ALB:s 301-svar har **inget HSTS-header**. Första request på HTTP är fortfarande sniffbar av MITM som kan strippa redirect och sätta upp en SSL-stripping-MITM. Klassiskt sslstrip-attack-mönster.
4. Subsequent HTTPS-requests *kan* sätta HSTS — men bara om Api skickar headern. ASP.NET Core's `app.UseHsts()` är default `false` i Development, on i Production. Måste verifieras post-flip att `ASPNETCORE_ENVIRONMENT=Production` aktivt skickar `Strict-Transport-Security: max-age=...`.
5. **Preload-list-deltagande** (https.cio.gov / hstspreload.org) kräver `max-age >= 31536000; includeSubDomains; preload` på apex-domän + alla subdomäner. Det är en separat operativ task.

**301 vs 308:** Web-search 2026-05-10 bekräftar 301 är OK för redirect-bytes där metoden får ändras (GET→GET, POST→GET-toleration). 308 bevarar metod men är mindre kompatibelt med äldre klienter. För en API-redirect där POST→HTTPS-POST måste fungera är **308 säkrare** — angripare kan inte konvertera en POST-with-credentials till en GET-with-credentials-in-querystring. Men för dev.jobbpilot.se (där frontend = Vercel + browser-baserade XHR-anrop) är 301 standard och fungerar.

**Rekommendation:**
1. Innan apply som flippar `alb_https_enabled = true`: **verifiera att Api skickar `Strict-Transport-Security`-header i Production**. `app.UseHsts()` ska vara aktiv i `Program.cs` med `max-age >= 31536000; includeSubDomains` (preload kommer senare).
2. Överväg byte 301 → 308 i `aws_lb_listener.http` för POST-säkerhet. Ändring i `modules/alb/main.tf:94`: `status_code = "HTTP_308"`. Marginell förbättring men gratis.
3. **HSTS-preload-list-registrering** är Fas 1-task (kräver att alla framtida subdomäner är HTTPS-only — irreversibelt löfte). Inte blocker för 13c.

**Bedömning:** Sec-Major (efter HTTPS-flippet finns ett residualfönster där HSTS inte kan sättas på första HTTP-besöket — inhereent i alla HTTP→HTTPS-redirect-arkitekturer utan preload). **Adressas innan ADR 0026-supersession (ADR 0027) skrivs som "klart" — verifiera HSTS-aktivering + dokumentera preload-deferral.**

**Delegera till:** **dotnet-architect** (HSTS-config i Program.cs + verifiering att den aktiveras under Production), eventuellt **adr-keeper** (om preload-list-deltagande blir egen ADR).

---

## Minor

### Sec-Minor-1 — TLS-policy-pinning: `ELBSecurityPolicy-TLS13-1-2-2021-06` är fortfarande supported men en post-quantum-policy finns nu (PQ-2025-09)

**Fil:** `infra/terraform/modules/alb/main.tf:118` — `ssl_policy = "ELBSecurityPolicy-TLS13-1-2-2021-06"`.

Web-search 2026-05-10 (AWS docs):
- `ELBSecurityPolicy-TLS13-1-2-2021-06` förblir AWS:s rekommenderade default — TLS 1.2 + TLS 1.3, brett klient-stöd, ingen deprecation aviserad
- AWS introducerade 2025-09 nyare policies med post-quantum-hybrid-stöd: `ELBSecurityPolicy-TLS13-1-2-Res-PQ-2025-09` + FIPS-varianten. Backward compatibility bevarad (klienter som kan PQ-TLS får hybrid; andra fortsätter med TLS 1.2/1.3 klassiskt)
- För civic-utility-tjänst som ska leva 5+ år är PQ-policy en framtidssäkring (harvest-now-decrypt-later är reell hot-modell mot PII)

**Rekommendation (icke-blocker):** uppgradera till `ELBSecurityPolicy-TLS13-1-2-Res-PQ-2025-09` när Klas är bekväm med att verifiera att alla klienter (Vercel-frontend, mobil-app om den kommer) inte har TLS-handshake-issues. Inget i diffen som tvingar denna uppgradering nu — `2021-06` är säkert val 2026-05-10.

**Bedömning:** Sec-Minor — defense-in-depth, framtidssäkring. Beslutet kan dröja till Fas 1.

**Delegera till:** ingen omedelbar delegation. ADR 0027 (eller separat note i runbooks/aws-setup.md) bör nämna PQ-policy som "Fas 1-uppgraderingskandidat".

---

### Sec-Minor-2 — Plain-text trafik ALB → ECS-task (port 8080) över VPC: Fas 0 OK, men dokumentera

**Filer:** `infra/terraform/modules/alb/main.tf:36-38` (target-group `protocol = "HTTP"`, port 8080) + `infra/terraform/modules/network/main.tf:196-203` (ECS-SG ingress 8080 från ALB-SG, intra-VPC).

Efter HTTPS-flippet termineras TLS på ALB. Trafik från ALB → ECS-tasks går som HTTP/1.1 plain-text på port 8080 inom VPC (10.0.0.0/16). Detta är industri-standard mönster (TLS termination på edge), men:

1. **Hot-modell:** angripare som kommit in i VPC:n (komprometterad container, IAM-läcka, SSM-session-misuse) kan paket-sniffa intra-VPC. Sannolikhet i Fas 0 = mycket låg (ingen inloggad CI/CD-pipeline ännu, inga andra containers).
2. **Skydd:** AWS VPC är "shared-tenancy isolated" — andra AWS-kunder kan inte sniffa er VPC. ENI-isolation enforces på hypervisor-nivå.
3. **Fas 1-uppgradering:** AWS Service Connect eller AWS App Mesh kan ge mTLS task-till-task. För ALB→ECS specifikt: enable `protocol = "HTTPS"` på target-group + själv-signerat cert på ECS-task. Operativ börda tveksamt värd det i Fas 1; verkligt värdefullt i Fas 2 (multi-tenant + flera services).

**Bedömning:** Sec-Minor — Fas 0 acceptabelt. **Dokumentera explicit i ADR 0027 §"Konsekvenser/Negativa"** att TLS termineras på ALB, intra-VPC är plain-text, och uppgraderingsväg finns vid Fas 2-trigger.

**Delegera till:** **adr-keeper** (ADR 0027-text).

---

### Sec-Minor-3 — `aws_acm_certificate_validation`-timeout är default (45 min): kan vara för kort om DNS-propagering hänger

**Fil:** `infra/terraform/modules/acm/main.tf:61-64`.

Resursen har inget explicit `timeouts`-block. Default är AWS provider's 45 min. Vid första apply (där apex-zonen NS-pekare just lagts hos registrar) finns risk för DNS-propagering >45 min hos vissa svenska ISP-resolvers. Apply failar och kräver re-run.

**Rekommendation (icke-blocker):**
```terraform
resource "aws_acm_certificate_validation" "this" {
  certificate_arn         = aws_acm_certificate.this.arn
  validation_record_fqdns = [for r in aws_route53_record.validation : r.fqdn]

  timeouts {
    create = "75m"
  }
}
```

**Bedömning:** Sec-Minor (operativ-, inte säkerhets-fynd — men eftersom audit-scope inkluderar "DNS-validation OK för auto-renewal" tas det med). Apply re-run är fix på operatörs-sida.

**Delegera till:** **dotnet-architect** vid revidering, eller skip om Klas accepterar re-run-risken.

---

## Genomgång av audit-fokusområden (verifiering punkt-för-punkt)

| # | Fokus | Bedömning | Severity |
|---|---|---|---|
| 1 | DNSSEC saknas | Sec-Major-1 ovan | Major |
| 2 | ACM-cert DNS-validation OK för auto-renewal | **OK** — `validation_method = "DNS"`, `aws_route53_record.validation` består av CNAMEs som ACM behöver för annual renewal-check. EMAIL-validation skulle ha krävt manuell e-post-bekräftelse var 13:e månad. DNS är rätt val. | — |
| 3 | TLS-policy `ELBSecurityPolicy-TLS13-1-2-2021-06` aktuell? | Sec-Minor-1 ovan — fortfarande supported, PQ-uppgradering finns | Minor |
| 4 | SAN bara `dev.jobbpilot.se` — minimal scope | **OK / Praise** — inga apex/wildcard. Reduces blast-radius vid cert-kompromiss. Wildcard skulle kräva DNSSEC-prioritering. Konsistent med "civic-utility-disciplin" (CLAUDE.md §1). | Praise |
| 5 | ALIAS `evaluate_target_health = true` DDoS-amplification? | **OK / Praise** — Route53 ALIAS är inte ett rekursivt DNS-svar (det är en AWS-intern direkt-ARN-pekning). DDoS-amplification kräver att DNS-svaret är större än request — ALIAS-svar är litet (A-record IP). `evaluate_target_health = true` ger automatisk failover om ALB-target-group rapporterar 0 healthy targets → robusthet, inte risk. | Praise |
| 6 | `allow_overwrite = true` på validation-CNAMEs | **OK** — AWS-rekommenderat mönster (verifierat web-search 2026-05-10). Risken "maska state-drift" är teoretisk; i normal Terraform-flow är `for_each` över `domain_validation_options` deterministisk och drift = fel ska syns i `terraform plan`. För denna single-cert-stack är overwrite-flaggan korrekt. **Vid framtida multi-cert-stacks** (staging-cert + dev-cert i samma zone) bör flaggan re-evalueras. | — |
| 7 | Apex hosted zone i prod/baseline + cross-stack data-lookup | **OK / Praise** — `data "aws_route53_zone"` med `name = var.apex_domain_name` läser bara hosted-zone-id och NS-records (publik info). Ingen sensitive data passerar stack-gränsen. State-isolation respekterad (dev-stack kan inte mutera prod-stackens zone). KMS-pattern (`alias/jobbpilot-master-key`) är samma mönster — konsistent med STEG 13a. | Praise |
| 8 | DNS-record-leak via outputs | **OK** — `route53_name_servers` är publik info (allt världen kan göra `dig NS jobbpilot.se`). `acm_dev_certificate_arn` är ARN, inte cert-content. `dev_fqdn` är bara strängkonkatenering. Inga `sensitive = true` fattas där det behövs. | — |
| 9 | HTTP→HTTPS-redirect 301 vs 308 + HSTS | Sec-Major-2 ovan | Major |
| 10 | Plain-text ALB → ECS Fas 0 OK? | Sec-Minor-2 ovan | Minor |
| 11 | GDPR Art. 32 ny security-yta vs old (HTTP-only) | **STEG 13c är säkerhetshöjande netto.** Old: session-id i klartext över internet hela vägen → klient. New (efter flip): TLS-skyddad mellan klient och AWS-edge i `eu-north-1`; intra-VPC plain-text på 8080 (Sec-Minor-2). Old: ingen domän-binding (ALB-default-DNS, easy spoof om angripare gissar). New: `dev.jobbpilot.se` med ACM-cert (omöjligt att spoofa utan att kompromettera Route53-zone eller ACM). **ADR 0026 trigger 1 är aktiverad** — ADR 0027 ska superseda 0026 efter denna apply. | Praise (netto) |

---

## Praise

- **`aws_acm_certificate_validation` används korrekt som källa till `certificate_arn`-output** (`acm/outputs.tf:1-4`). Detta förhindrar att downstream (`module.alb`) plockar upp ovaliderad cert-ARN och försöker binda HTTPS-listener mot ett cert AWS ännu inte signerat. Subtil men kritisk pattern — bra att den följs.
- **`create_before_destroy` på `aws_acm_certificate`** (`acm/main.tf:19-21`). Förhindrar listener-downtime vid framtida cert-rotation eller domain-name-byte. Ingen self-inflicted apply-outage.
- **DNS-validation framför EMAIL-validation.** Auto-renewal fungerar så länge validation-CNAMEs ligger kvar i Route53. Inga manuella förnyelse-steg som riskerar missas.
- **Minimal SAN-scope (`dev.jobbpilot.se` bara, ingen wildcard).** Reduces blast-radius vid privatkey-kompromiss.
- **ALIAS-record (gratis, AWS-internt) framför CNAME-mot-`*.elb.amazonaws.com`.** Healthcheck-aware failover, ingen extra DNS-kostnad.
- **Cross-stack data-lookup-pattern via `data "aws_route53_zone"`** (`environments/dev/main.tf:300-303`). State-isolation respekterad, ingen tight coupling till prod/baseline-state-fil. Konsistent med befintligt KMS-alias-pattern (STEG 13a).
- **Pre-condition på `aws_lb_listener.https`** (`modules/alb/main.tf:133-138`). Fail-fast vid plan-tid om operatör glömmer ACM-cert. Bättre UX än kryptiskt AWS-API-fel mid-apply.
- **Detaljerad apply-flöde-dokumentation i kommentar** (`environments/dev/main.tf:283-298`). Steg 1-5 inklusive ADR 0027-supersession. Operatörs-vänligt och granska-bart.

---

## Sammanställd block-/godkännande-status

| Resurs | Apply-status |
|---|---|
| `module.route53` (prod/baseline — apex hosted zone) | **APPROVE** apply nu. Sec-Major-1 (DNSSEC) hanteras separat: antingen aktivera nu (Alt B → kräver dotnet-architect-implementation) eller dokumentera deferral i ADR 0027 (Alt A). |
| `module.acm_dev` (dev — ACM-cert + validation-CNAMEs) | **APPROVE** apply. Inga blockers. |
| `aws_route53_record.dev_alb_alias` (dev — ALIAS) | **APPROVE** apply. |
| Flippa `alb_https_enabled = true` + sätt `alb_acm_certificate_arn` | **APPROVE-with-fixes** — verifiera HSTS-aktivering i Api/Program.cs Production-config (Sec-Major-2) innan flippet. Överväg 301→308 (Sec-Major-2 fix-2). |
| ADR 0027 (supersedar ADR 0026) | **MUST INCLUDE:** trigger 1-aktivering, DNSSEC-beslut (Alt A eller B från Sec-Major-1), HSTS-status (Sec-Major-2), TLS-policy-pinning + PQ-uppgraderingsväg (Sec-Minor-1), plain-text-intra-VPC-acceptans (Sec-Minor-2). |

---

## Rekommenderad apply-sekvens (med säkerhets-checkpoints)

1. **Apply prod/baseline** (`module.route53`) → få NS-records → uppdatera registrar → vänta 30 min för NS-propagering. **STOPP — verifiera `dig NS jobbpilot.se @8.8.8.8` returnerar AWS-NS.**
2. **Apply dev** (`module.acm_dev` + ALIAS-record). ACM väntar in DNS-validering 5-30 min. **STOPP — verifiera `aws acm describe-certificate ...` returnerar `Status: ISSUED`.**
3. **Verifiera HSTS-config i Api/Program.cs** (`app.UseHsts()` aktiv i Production, `max-age >= 31536000`, `includeSubDomains`). Kör lokal smoke. **STOPP — Klas konfirmerar Sec-Major-2 fix-1.**
4. **Edit `terraform.tfvars`:** `alb_https_enabled = true`, `alb_acm_certificate_arn = "<output från steg 2>"`. Apply. ALB HTTP-listener flippar till 301-redirect; HTTPS-listener på 443 går live.
5. **Smoke-test** `curl -I https://dev.jobbpilot.se/api/ready` → `200`, `Strict-Transport-Security`-header närvarande, `curl -I http://dev.jobbpilot.se/api/ready` → `301`/`Location: https://...`.
6. **Skriv ADR 0027** med Sec-Major-1-beslut + Sec-Major-2-status + Sec-Minor-1+2-acceptans dokumenterad. Supersedar 0026.
7. **Update `current-work.md` + `steg-tracker.md`** att 13c är klart + ADR 0026 supersedad av 0027.

---

## Delegering

| Fynd | Mottagare |
|---|---|
| Sec-Major-1 (DNSSEC) | **adr-keeper** (ADR 0027 ska innehålla beslutet); **dotnet-architect** vid Alt B (terraform-implementation av key_signing_key + cross-region KMS) |
| Sec-Major-2 (HSTS-aktivering + 301→308) | **dotnet-architect** (verifiera Program.cs + eventuellt edit `modules/alb/main.tf:94` 301→308) |
| Sec-Minor-1 (PQ-TLS-policy) | Inget omedelbart; **adr-keeper** noterar Fas 1-uppgraderingskandidat i ADR 0027 |
| Sec-Minor-2 (plain-text intra-VPC) | **adr-keeper** (dokumentera i ADR 0027 §"Konsekvenser/Negativa") |
| Sec-Minor-3 (ACM-validation-timeout) | **dotnet-architect** vid revision; skip om Klas accepterar re-run |
| ADR 0027 (supersedar 0026) | **adr-keeper** |

---

## Källor (web-search 2026-05-10)

- AWS ALB Security Policies: <https://docs.aws.amazon.com/elasticloadbalancing/latest/application/describe-ssl-policies.html>
- AWS ALB Create HTTPS Listener: <https://docs.aws.amazon.com/elasticloadbalancing/latest/application/create-https-listener.html>
- AWS Route53 DNSSEC Configuration: <https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/dns-configuring-dnssec.html>
- AWS Route53 KSK Management: <https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/dns-configuring-dnssec-ksk.html>
- AWS Route53 DNSSEC Customer-Managed Keys: <https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/dns-configuring-dnssec-cmk-requirements.html>
- AWS Implementing HSTS Across Services: <https://aws.amazon.com/blogs/security/implementing-http-strict-transport-security-hsts-across-aws-services/>
- HTTPS-Only Standard (HSTS): <https://https.cio.gov/hsts/>
- MDN Strict-Transport-Security: <https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Strict-Transport-Security>
- terraform-aws-modules ACM (DNS-validation reference): <https://registry.terraform.io/modules/terraform-aws-modules/acm/aws/latest>
