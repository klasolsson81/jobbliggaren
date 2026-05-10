# Code-review: STEG 14a — GitHub OIDC + CI/CD-workflows

**Status:** APPROVE-WITH-FIXES
**Granskat:** 2026-05-10
**Auktoritet:** CLAUDE.md §5 (anti-patterns), §6.2 (Conventional Commits), §11.1 (pre-commit), BUILD.md §15.3
**Scope:** Infra (Terraform `github_oidc`-modul + prod-stack-koppling) + CI/CD (`build.yml` + `deploy-dev.yml`)

Inga **Blockers**. 2 **Major** (en deploy-säkerhets-fråga + en pattern-inkonsistens), 4 **Minor**, 3 **Nits**. Container-namn-frågan: **verifierad korrekt**.

---

## Major (bör fixas innan apply)

### M1. `pull_request`-sub-claim ger för bred trust-policy för dev-deploy-rollen
**Fil:** `infra/terraform/modules/github_oidc/main.tf:80`

Trust-policyn för **deploy-dev**-rollen accepterar `pull_request`-sub-claim. Kommentaren motiverar det med "build.yml på PR" — men `build.yml` har `permissions: contents: read` och kallar **aldrig** `configure-aws-credentials`. Att lämna trust-policyn öppen för PR-tokens är defense-in-depth-regression.

**Krav:** Ta bort `pull_request`-mönstret. Om `build.yml` i framtiden ska göra read-only AWS-anrop, skapa då en separat `read_only`-roll med eget sub-claim-scope.

### M2. `versions.tf` saknas i `route53` och `acm` men finns i `github_oidc` — pattern-inkonsistens
**Filer:** `infra/terraform/modules/route53/`, `infra/terraform/modules/acm/`

Inte ett fel i 14a, men granskningen flaggar att den nya modulen följer **rätt** standard medan två tidigare moduler har drift. Notera som tracked TD: route53 + acm bör få `versions.tf` i ett nästkommande `chore(infra)`-commit för konvergens.

---

## Minor (bör fixas, inte blockerande)

### m1. ECS `service`-ARN-konstruktion antar nytt ARN-format
Nytt format (`service/cluster/service-name`) korrekt sedan 2018. För konton skapade efter jan 2020 är `serviceLongArnFormat` default. **OK.**

### m2. `iam_execution_role_arn` matchar faktiska execution-rollens namn-konvention
Verifierat — `${var.dev_name_prefix}-ecs-execution` = `jobbpilot-dev-ecs-execution`. **OK.**

### m3. `dotnet test` på sln-nivå med MTPlatform-runner
SDK 10.0.200 stödjer detta. **OK, men flagga första körningen för verifiering.**

### m4. `pnpm test` är `vitest run` — verifierat
Korrekt — `run`-flaggan gör vitest non-interactive (CI-säkert). **OK.**

---

## Nits

### n1. Kommentar refererar "startprompt-quirken"
**Fil:** `.github/workflows/build.yml:55-57`

"startprompt-quirken" är intern session-jargong som ingen utomstående kan förstå. Kommentaren ska förklara *varför*, inte referera historisk session.

**Föreslås:**
```yaml
# Microsoft.Testing.Platform-runner aktiveras via global.json.
# Hela testsuiten körs — inga filter (gäller även regression-tester).
```

### n2. `ECR_REGISTRY` hårdkodar account-ID — duplicerar `AWS_ACCOUNT_ID`
**Fil:** `.github/workflows/deploy-dev.yml:45-46`

Account-ID upprepas. Drift-risk vid framtida konto-byte. GitHub Actions tillåter inte env-var-interpolation i `env:`-blocket. Lösning: härled `ECR_REGISTRY` i tidigt step via `>> $GITHUB_ENV`.

### n3. `Resolve tag` — input-description matchar inte verklighet
**Fil:** `.github/workflows/deploy-dev.yml:73-80`

Input-description säger "Lämna tom för senaste tag på main" men koden gör inget sådant lookup.

**Föreslås:** sätt `required: true` på `workflow_dispatch.inputs.tag` + uppdatera description.

---

## Specifik teknisk fråga — container-namn

**Verifierat:**
- `modules/ecs/main.tf:53` — Api task-def container `name = "api"`
- `modules/ecs/main.tf:118` — Worker task-def container `name = "worker"`
- `deploy-dev.yml:54-55` — `CONTAINER_NAME_API: api`, `CONTAINER_NAME_WORKER: worker`

**Verdikt:** **Korrekt.**

---

## Bra gjort

- Trust-policy för OIDC är solid — `aud`-claim med `StringEquals` + `sub`-claim med specifika repo+ref-mönster.
- Inline-policy är genuint least-privilege — `iam:PassRole` med `iam:PassedToService` förhindrar PassRole till Lambda/EC2.
- `max_session_duration = 3600` — explicit kort blast-radius.
- `thumbprint_list = []` med kommentar — korrekt för moderna AWS-konton.
- CI-aggregat-job (`build.yml:109-122`) — gör branch-protection enkelt.
- Worker no-wait dokumenterad som tillfällig 14a-state med tydlig kommentar.
- HSTS-smoke-test-verifiering — regression-skydd för ADR 0027/TD-33.
- Modulen är pattern-konsistent med `iam_ecs`.

---

## Sammanfattning

**Status:** APPROVE-WITH-FIXES

| Severity | Antal | Måste fixas innan apply? |
|---|---|---|
| Blocker | 0 | — |
| Major | 2 | M1 ja (säkerhet); M2 nej (utanför scope) |
| Minor | 4 | Nej, men m3 verifiera lokalt |
| Nit | 3 | Nej (n1 högst rekommenderad) |

**Rekommenderad åtgärds-sekvens innan `terraform apply`:**

1. **M1** — ta bort `pull_request`-mönstret.
2. **n1** — uppdatera kommentar i `build.yml:55-57`.
3. **n3** — sätt `required: true` på `workflow_dispatch.inputs.tag`.
4. (Frivilligt) n2 + m3-verifiering.
