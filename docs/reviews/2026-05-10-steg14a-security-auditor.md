# Security-audit: STEG 14a — GitHub OIDC + Actions workflows

**Status:** APPROVE-WITH-FIXES — inga GDPR-blockers, inga sec-critical hard-stops, men **2 sec-major** måste fixas innan apply (de är 2-rads-edits, inte arkitektur).

**Granskat:** 2026-05-10
**Auktoritet:** GDPR Art. 5/32 (data minimization, security of processing), AWS IAM least-privilege, GitHub OIDC security guidance (Sept-2021 GA + advisories 2023-2025), CLAUDE.md §5.4
**Filer:**
- `infra/terraform/modules/github_oidc/main.tf`
- `infra/terraform/modules/github_oidc/variables.tf`
- `infra/terraform/modules/github_oidc/outputs.tf`
- `infra/terraform/environments/prod/main.tf` (rad 98-111)
- `.github/workflows/build.yml`
- `.github/workflows/deploy-dev.yml`

PII-yta: noll. Detta är infra/CI — ingen PII passerar. GDPR-relevansen är indirekt: dålig OIDC-scope = privilege-eskalering = senare PII-blast-radius.

---

## Sec-Major (måste fixas innan apply — 2 fynd)

### M1 — `pull_request`-sub-claim är för bred och skall tas bort

**Fil:** `infra/terraform/modules/github_oidc/main.tf` rad 80
**Nuvarande:**
```hcl
"repo:${var.github_owner}/${var.github_repo}:pull_request",
```

**Problem:** Sub-claim `repo:owner/repo:pull_request` matchar **alla** PRs mot repot, från **alla** källor inklusive forks. Kommentaren (rad 50-55) hävdar att "GitHub-default securitarian" hindrar fork-PRs från att få `id-token` — det är **delvis felaktigt**:

1. GitHub blockerar `secrets` i fork-PR-context (default), men `id-token: write` kontrolleras av workflow `permissions:`-blocket — inte av repo-secret-policy. Om `pull_request_target` (eller en framtida workflow-variant) någon gång kör med `id-token: write` på fork-PR:s **kan** AWS-rollen assumas. Trust-policy är sista försvarsledet — den ska inte lita på workflow-disciplin.
2. `build.yml` (den enda nuvarande PR-workflowen) deklarerar `permissions: contents: read` — alltså **kommer aldrig** be om id-token. Sub-claim-pattern ger noll faktisk nytta i nuläget.
3. Säkerhetspraxis från AWS + GitHub Sept-2023-advisory: scope:a sub-claim till exakt environment eller branch — undvik bredare wildcards när det inte behövs.

**Fix:** ta bort raden + uppdatera kommentar.

### M2 — `refs/heads/main`-sub-claim är onödig och bör tas bort

**Fil:** `infra/terraform/modules/github_oidc/main.tf` rad 79
**Nuvarande:**
```hcl
"repo:${var.github_owner}/${var.github_repo}:ref:refs/heads/main",
```

**Problem:** `build.yml` (pushed-to-main-trigger) deklarerar `permissions: contents: read` — workflowen kan **aldrig** assumera rollen även om sub-claim matchar. Risk: framtida `id-token: write`-tillägg i build.yml för t.ex. SBOM-publicering kan tyst assumera dev-deploy-rollen → privilege-escalation via accidental scope-expansion.

**Fix (Alternativ A, rekommenderat):** ta bort raden helt. Bara tag-baserad deploy är scope:at.

---

## Sec-Minor (rekommenderade — ej blocking)

### m1 — `RegisterTaskDefinition Resource: "*"` mitigation OK
Befintlig `iam:PassRole`-condition + roll-allow-list är gold-standard. Inget krävs.

### m2 — `role-duration-seconds` kan sänkas till 900
**Fil:** `.github/workflows/deploy-dev.yml` rad 90
Sänk från 3600 → 900 (15 min). Räcker för deploy + smoke-test, minskar blast-radius vid runner-kompromiss.

### m3 — Smoke-test loggar response body
`cat /tmp/body` på rad 202 är OK för nuvarande `/api/ready`-kontrakt (ingen PII). **Markera som risk om endpoint-kontraktet ändras** — `/api/ready` får aldrig returnera info som inte tål CI-logg.

### m4 — `cancel-in-progress: false` runbook-rutin
Lägg till i `docs/runbooks/aws-setup.md`: "om deploy-dev hänger >15 min: cancel:a runs via gh CLI; concurrency-grupp släpper omedelbart."

### m5 — `thumbprint_list = []` är rätt syntax
AWS Trust Store hanterar GitHub OIDC sedan juli 2023. Bekräftelse, inget fynd.

### m6 — Ingen token-injection-väg via `$GITHUB_SHA` etc.
Verifierat: alla användningar är quoted, ingen `eval`. **No injection vector found.**

### m7 — `AWS_DEPLOY_ROLE_ARN` som secret är OK
ARN är public-info men GitHub Secret är rätt pattern (förhindrar typo via Terraform → workflow-spridning).

---

## Praise

- ✅ Trust-policy aud-claim är `StringEquals "sts.amazonaws.com"` — exakt match.
- ✅ `iam:PassRole` med både resource-allow-list och `iam:PassedToService`-condition. Gold-standard.
- ✅ ECR-permissions split:ade i två statements (auth-token Resource `*`, push på exakt två repos).
- ✅ Logs-statementet är read-only.
- ✅ `permissions: id-token: write` finns bara i deploy-dev.yml. Build-workflow har `contents: read` default-deny.
- ✅ ECR-image-tag använder både `${github.sha}` (immutable) och `${tag}` (human-readable) — rollback möjlig efter tagg-flytt.
- ✅ HSTS-verifiering i smoke-test är regression-skydd för ADR 0027/TD-33.
- ✅ Ingen `pull_request_target`-trigger någonstans — vanligaste fork-PR-attack-vägen stängd by construction.

---

## Sammanfattning

**0 sec-critical, 2 sec-major, 7 sec-minor.**

Innan apply:
1. **Fix M1** — ta bort `pull_request`-raden i `main.tf` rad 80 (+ uppdatera kommentar rad 50-55).
2. **Fix M2** — ta bort `refs/heads/main`-raden i `main.tf` rad 79 (+ uppdatera kommentar rad 47-49).
3. **Rekommenderat m2** — sätt `role-duration-seconds: 900` i `deploy-dev.yml` rad 90.
