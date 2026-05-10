---
session: STEG 13c apply — HTTPS aktivt på dev
datum: 2026-05-10
slug: steg13c-https-applied
status: KLAR (HTTPS smoke-test PASS, ADR 0027 Accepted, ADR 0026 Superseded)
commits:
  - f0f45c5 feat(infra): STEG 13c — Route53 + ACM-moduler + 308-redirect + dev DNS+ALIAS
  - 3b46945 feat(api): STEG 13c — HSTS-impl + EnsureSafeForEnvironment + 17 tests
  - f2bc555 docs: STEG 13c — ADR 0027 (Proposed) + 3 agent-reviews
  - 8befcce docs: lägg till professionell README med snabblänkar, mermaid-arkitektur, lokal-setup
  - (TBD) fix(infra) + docs commits för apply + supersession
---

# STEG 13c apply — Edge + DNS + HTTPS-flip

## Mål

Slutföra Edge + DNS + HTTPS-aktivering på dev-miljön. Trigger 1 av ADR 0026
(domän + ACM-cert) → supersession via ADR 0027. Klas-prio över STEG 14 efter
diskussion om risk/marginal (ADR 0026-deadline 2026-06-08, 29 dagar marginal).

## Sammanfattning

- ✅ Två nya Terraform-modules (`modules/route53/` + `modules/acm/`) med
  cross-stack-data-lookup-pattern (zone bor i prod/baseline likt KMS)
- ✅ HSTS-implementation i Api/Program.cs med EnsureSafeForEnvironment-paritet
  (dotnet-architect Viktigt-fynd 5)
- ✅ ALB-redirect aktiv (HTTP_301 — AWS hardlimited till 301/302; 308-försök
  failade mid-apply, fixades till 301)
- ✅ Domän `jobbpilot.se` registrerad hos STRATO (~80 kr/år) + NS pekade till
  AWS Route53. DNS propagerade ~30 min trots STRATO:s 24h-varning.
- ✅ ACM-cert utfärdat via DNS-validation (~5 min efter NS propagerat)
- ✅ HTTPS-flip via tfvars-edit + apply (4 add, 1 change, 0 destroy effektivt)
- ✅ ECS rolling deployment ~120s (task-def rev 2 med Alb__HttpsEnabled=true)
- ✅ Smoke-test `https://dev.jobbpilot.se/api/ready` → 200 OK + HSTS-header
- ✅ ADR 0027 Accepted, ADR 0026 Superseded
- ✅ README.md tillagd separat (sober civic-utility-stil)

## Tids-blocks

| Tid (lokal) | Aktivitet |
|-------------|-----------|
| 14:00 | Session-start, Klas väljer STEG 13c över 14, plan-design med snabblänkar/ACM SANs/zone-stack-frågor |
| 14:30 | Klas registrerar jobbpilot.se hos STRATO; CC skriver Terraform-modules (route53 + acm) parallellt |
| 15:00 | code-reviewer + security-auditor invokeras i background; CC fortsätter med env-stack-edits + validate-checks |
| 15:30 | code-reviewer rapporterar APPROVE-with-fixes (M-1: timeouts.create=75m); CC fixar inline |
| 15:45 | security-auditor rapporterar APPROVE-with-fixes (2 Sec-Major dokumenterade i ADR 0027); HSTS-implementation startar |
| 16:00 | dotnet-architect invokeras för HSTS-review; CC bygger Docker-image med HSTS-fix + pushar till ECR |
| 16:15 | dotnet-architect rapporterar Viktigt-fynd 5 (EnsureSafeForEnvironment-paritet); CC implementerar inline |
| 16:30 | Klas tillbaka från STRATO med NS-edit; polling visar ej propagerat globalt än |
| 16:45 | Klas väljer "B — applya nu i background, riskera timeout"; SSO re-login; ACM-apply startar |
| 17:00 | ACM-apply ~background; CC commitar 3 logiska commits (infra/api/docs) + pushar; READMEs skrivs |
| 17:15 | Klas väljer "commita README också"; commit + push |
| 17:30 | ACM-apply rapporterar success; cert validerat på `f72a79d7-...` |
| 17:35 | tfvars-edit med alb_https_enabled + cert_arn; plan failer med "HTTP_308 not allowed" |
| 17:38 | Mid-apply-fix: 308 → 301 i modules/alb + uppdaterad kommentar; re-plan OK |
| 17:42 | HTTPS-flip apply: 4 add 1 change; ECS rolling startar |
| 17:46 | ECS deployment COMPLETED (task-def rev 2, 1/1 running) |
| 17:48 | Smoke-test PASS: 200 OK + Strict-Transport-Security: max-age=31536000; includeSubDomains |

## In-flight fixar

### Fix 1 — ALB redirect 308 → 301 (AWS-API-begränsning)

**Problem:** `terraform plan` på HTTPS-flip failade med
```
Error: expected status_code to be one of ["HTTP_301" "HTTP_302"], got HTTP_308
  with module.alb.aws_lb_listener.http
  on modules/alb/main.tf line 82
```

AWS ALB `RedirectActionConfig.StatusCode` är hardlimited till `HTTP_301 | HTTP_302`.
Inte fångat av `terraform validate` (AWS-API-side validation). 308 (Permanent
Redirect, POST-method-bevarande) som security-auditor rekommenderade som Sec-Minor
är inte implementerbart utan Lambda@Edge eller CloudFront-rewrite.

**Fix:** Replace `HTTP_308` → `HTTP_301` i `modules/alb/main.tf` redirect-block +
uppdaterad kommentar som dokumenterar AWS-begränsningen + mitigation-rationale
(HSTS + browser-cache gör POST-anrop går direkt till HTTPS efter första GET; den
enda 301-redirect klienten ser är initial GET som inte påverkas av method-downgrade).

**Lärdom:** Vid AWS-resource-fält som tar enum-strängar — kolla AWS-docs INNAN
code-review eftersom `terraform validate` ger false-positive. code-reviewer +
security-auditor + dotnet-architect missade denna AWS-side-begränsning trots
explicit prompt om AWS-resource-validering.

## Apply-flöde (faktiskt)

| # | Steg | Resultat | Tid |
|---|------|----------|-----|
| 1 | terraform apply prod/baseline (route53 module) | ✅ 1 add hosted zone Z028392711... | ~30s |
| 2 | Klas STRATO NS-edit till 4 AWS-NS-records | ✅ Sparad, varning "24h prop" | ~5 min UI |
| 3 | DNS-prop polling (3 publika resolvers) | Initialt visade STRATO-NS, väntade ~30 min | passive |
| 4 | terraform plan dev (ACM + ALIAS) | ✅ 4 add, 1 change | ~30s |
| 5 | terraform apply dev (background) | ✅ 4 added efter ~25 min (DNS+ACM-validation) | 25 min |
| 6 | Klas commitar + pushar 4 logiska commits | ✅ f0f45c5, 3b46945, f2bc555, 8befcce | ~3 min |
| 7 | Edit dev/terraform.tfvars med cert-ARN + alb_https_enabled=true | ✅ | 10s |
| 8 | terraform plan HTTPS-flip | ❌ "HTTP_308 not allowed" | 30s |
| 9 | Fix 1 (308→301) + re-plan | ✅ 2 add, 3 change, 1 destroy | 30s |
| 10 | terraform apply HTTPS-flip | ✅ 4 added, 1 changed, 0 destroyed | ~3 min |
| 11 | Polla ECS deployment till COMPLETED | ✅ 120s (task-def rev 2) | 2 min |
| 12 | Smoke-test HTTPS + redirect | ✅ 200 OK + HSTS + 301-redirect | 10s |
| 13 | docs-sync + final commits + push | (denna commit) | ~5 min |

**Total session-tid:** ~3.5 timmar (mycket av det väntan på STRATO-prop + ACM-validation + ECS rolling).

## Resultat

### Smoke-test (definitive verification)

```
$ curl -i https://dev.jobbpilot.se/api/ready
HTTP/1.1 200 OK
Date: Sun, 10 May 2026 15:48:44 GMT
Content-Type: application/json; charset=utf-8
Server: Kestrel
Strict-Transport-Security: max-age=31536000; includeSubDomains

{"status":"ready","service":"JobbPilot.Api"}

$ curl -i http://dev.jobbpilot.se/api/ready
HTTP/1.1 301 Moved Permanently
Server: awselb/2.0
Location: https://dev.jobbpilot.se:443/api/ready
```

End-to-end fungerande från publik internet → DNS (STRATO→AWS-Route53) →
ALB (TLS 1.3 termination via ACM-cert) → ECS Fargate task → Api endpoint
med HSTS-header på response.

### Worker-status (oförändrat)

Worker fortsätter restart-loop pga PLACEHOLDER DB-creds. Inte påverkad av
HTTPS-flip eftersom Worker inte ligger i ALB target-group. Förväntad
transient state tills STEG 14 sätter riktiga creds via `aws secretsmanager
put-secret-value` post-DDL.

## Cost (uppdaterad från STEG 13b)

| Resurs | $/mån |
|--------|-------|
| (Allt från STEG 13b) | 79.00 |
| Route53 hosted zone | 0.50 |
| Route53 queries (DNS-traffic minimal i dev) | ~0.00 |
| ACM-cert | 0.00 (ingår i AWS) |
| **Totalt** | **~79.50** |

Daily burn: ~$2.65. Kostnadsökning från STEG 13c: ~$0.50/mån.

## ADR 0026 supersession

ADR 0027 supersedar ADR 0026 — trigger 1 (domän + ACM-cert utfärdat) aktiverad
28 dagar i förväg av deadline (2026-06-08). DNSSEC defererad till Fas 1 med
4 trigger-villkor dokumenterade i ADR 0027 (multi-tenant / OAuth / incident /
Fas 2-start). Plain-text intra-VPC accepterat Fas 0; mTLS-uppgrade vid Fas 2.

## README.md (parallellt)

Klas frågade under väntan om README.md kunde skrivas medan vi väntade på ACM.
Skrev sober civic-utility-stil README — 569 rader, 17 snabblänkar, 2 Mermaid-
diagram (system-arkitektur + Clean-Arch-lager-graf), 4 tech-stack-tabeller,
komplett directory-tree, lokal-setup-instruktioner kopierade från
`docs/runbooks/local-dev-setup.md` (kritiskt för Klas:s laptop-arbete imorgon).

Avviker från Klas's existing README-Template (Tokyo Night-tema) eftersom
JobbPilots position kräver det. AI-läsbarhet säkrad via semantiska headers +
tabellformat. Uppdateras vid MVP-launch när feature-set, screenshots och
produktdomän finns.

## Lärdomar STEG 13c

- **AWS ALB `RedirectActionConfig.StatusCode` hardlimited.** Detaljerat ovan
  (Fix 1). Lärdom: kolla AWS-API-spec innan code-review godkänner enum-värden.
- **STRATO-NS-propagering snabbare än varnat.** UI sa "upp till 24h" men
  `.se`-zonen propagerade inom ~30 min. Resolve-DnsName mot 8.8.8.8/1.1.1.1/
  9.9.9.9 visade alla AWS-NS efter NS-edit + ~30 min vänta. Worst-case sällan
  worst-case.
- **`aws_acm_certificate_validation` är idempotent vid timeout.** Cert står
  PENDING_VALIDATION upp till 72h innan AWS auto-deletar. Re-apply efter
  timeout plockar upp där den slutade — ingen recreate. Säker strategi:
  applya nu i background, re-applya vid timeout.
- **HSTS-pipeline-ordning kritiskt:** `UseForwardedHeaders → UseHsts →
  UseHttpsRedirection`. Bakom ALB är `Request.IsHttps` annars false →
  HSTS-headern sätts aldrig på response. Dokumenterat i Program.cs-kommentar
  så framtida refactors inte bryter ordningen.
- **Cross-stack data-lookup-pattern (data "aws_route53_zone")** följer
  KMS-master-key-pattern från STEG 13a. Ren state-isolation; dev-stack vet
  inget om prod/baseline-state. Bra blueprint för framtida delade resurser
  (apex-cert i prod när lansering närmar sig, etc).
- **`dig` saknas på Windows.** PowerShell `Resolve-DnsName -Server 8.8.8.8`
  räddade dagen. Worth dokumentera i runbook för framtida DNS-debug.
- **Klas's existing README-mall (Tokyo Night-tema) passade inte civic-utility-
  position.** Sober stil bättre — inga animerade SVGs, inga emojis i prosa,
  två Mermaid-diagram, snabblänkar för skim-navigering.
- **3 commits + push under väntefas är bra disciplin.** Klas bröt startprompt-
  regeln "inga commits förrän HTTPS verifierat" mid-session efter 30+ min
  väntefas. Granular commit-historia bevarade arbetet vid event av timeout/
  recovery. Ingen ångrad commit krävdes.

## Nästa session

**STEG 14** (`GitHub Actions tag-pipeline + första prod-deploy`):

- `.github/workflows/` för build+test+push-to-ECR + tag-trigger för deploy
  (`v*-dev`/`v*-rc`/`v*`)
- Hangfire schema-DDL via Install.sql + REVOKE PUBLIC i RDS
- ConnectionStrings split (jobbpilot_app + jobbpilot_worker via DDL-roles)
- Bootstrap-IAM-user cleanup (per `aws-setup.md §3.4`)
- Första formal deploy via tag-pipeline
- **Stänger Fas 0** per BUILD.md §18

ADR 0026-deadline 2026-06-08 längre relevant (supersedad). Ingen tids-press
för STEG 14 utöver normal Fas 0-progression.
