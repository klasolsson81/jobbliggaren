---
session: STEG 12
datum: 2026-05-09
slug: steg12-kod-pre-launch-gates
status: KLAR
commits:
  - f8488b4  feat(worker): TD-17 punkt 4 — HangfireConnectionStringResolver-fallback (STEG 12)
  - bb26fec  feat(api): TD-21 — ForwardedHeadersConfig + production-defense (STEG 12)
  - d879f96  docs(runbooks): STEG 12 Sec-Major-2 — ForwardLimit + CloudFront-prefix-list
---

# STEG 12 — Kod-pre-launch-gates inför första prod-deploy

## Mål

Stäng kvarvarande kod-delar av STEG 11:s pre-launch-gates så att STEG 13 (Terraform-stack) och STEG 14 (deploy + GitHub Actions) kan ta dem operativt utan kod-blockerare. Smalt scope per Klas:s val A4-sekvens (A1 → A2 → A3).

## Scope-fråga upptäckt vid discovery

Startpromptens Alt A-formulering ("första prod-deploy + applicera STEG 11:s pre-launch-gates") antog mer infra än som faktiskt existerar. Discovery-rapport till Klas avslöjade:
- Inget `.github/workflows/` (bara CODEOWNERS, dependabot, ISSUE_TEMPLATE, PR-template)
- Inget `Dockerfile` för Api eller Worker
- Terraform-stacken har bara baseline (budgets, cloudtrail, kms, secrets_manager, bedrock_model_access) — saknar VPC, RDS, Redis, ECS, ECR, ALB, Route53, ACM, CloudWatch LogGroups, IAM-roller

Klas valde A4: STEG 12 = A1 (kod-pre-launch-gates), STEG 13 = A2 (Terraform-stack), STEG 14 = A3 (GitHub Actions + första deploy + IAM-cleanup).

## Block 1 — Worker HangfireStorage ConnectionString-fallback (TD-17 punkt 4)

**Implementation:**

`HangfireConnectionStringResolver` (statisk testbar metod) lyft från inline-läsning i `Worker/Program.cs`. Fallback-kedja `HangfireStorage → Postgres`:

```csharp
public static string Resolve(IConfiguration configuration)
{
    return configuration.GetConnectionString(PrimaryKey)        // "HangfireStorage"
        ?? configuration.GetConnectionString(FallbackKey)       // "Postgres"
        ?? throw new InvalidOperationException(...);
}
```

Prod-overlay sätter `HangfireStorage` → routar Worker till `jobbpilot_worker`-rollen (DML-only på `hangfire.*`). Api/`AddPersistence` använder `Postgres` → `jobbpilot_app`-rollen. Lateral access-yta minskar.

`Worker/appsettings.Production.json` (ny):
```json
{
  "Hangfire": {
    "PrepareSchemaIfNecessary": false,
    "ShutdownTimeoutSeconds": 25
  }
}
```

ConnectionStrings injiceras via env-vars från ECS task-definition + AWS Secrets Manager — committas inte i overlay.

**Tester:** 5 nya `HangfireConnectionStringResolverTests` (HangfireStorage-prefer + Postgres-fallback + throw-both-missing + null-arg + const-stability).

## Block 2 — Api ForwardedHeadersConfig + production-defense (TD-21 KnownNetworks)

**Initial implementation:**

`ForwardedHeadersConfig` (sealed class, init-only properties, public const SectionName) följer pattern från `RateLimitingOptions` + `HangfireWorkerOptions`. Tre fail-loud parse-metoder:
- `ParseKnownNetworks()` → `IReadOnlyList<IPNetwork>` via `System.Net.IPNetwork.TryParse`
- `ParseKnownProxies()` → `IReadOnlyList<IPAddress>` via `IPAddress.TryParse`
- `ValidateForwardLimit()` → range 1-10

Använder .NET 10 `System.Net.IPNetwork` + `ForwardedHeadersOptions.KnownIPNetworks` (inte deprecated `Microsoft.AspNetCore.HttpOverrides.IPNetwork` — ASPDEPR005-warning vid initial bygg-försök).

`Api/appsettings.Production.json` (ny):
```json
{
  "ForwardedHeaders": {
    "KnownNetworks": [],
    "KnownProxies": [],
    "ForwardLimit": 1
  }
}
```

**Initial reviews:** 2 Sec-Major:

### Sec-Major-1 — KnownNetworks-tom-array saknade production-defense

Tom `KnownNetworks: []` i prod bakom proxy → `Connection.RemoteIpAddress` blir ALB:s VPC-internal-IP → IP-rate-limiting i en bucket → effektivt no-op = OWASP A07-yta. Symmetri-miss mot Worker `safeForAutoSchema` allow-list-pattern.

**Fix in-block:** lyft till testbar metod på `ForwardedHeadersConfig`:

```csharp
public void EnsureSafeForEnvironment(string environmentName)
{
    var safeForEmpty =
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

    if (!safeForEmpty && KnownNetworks.Length == 0)
    {
        throw new InvalidOperationException(...);   // ECS-container startar inte
    }
}
```

`Api/Program.cs` anropar `forwardedCfg.EnsureSafeForEnvironment(builder.Environment.EnvironmentName)` direkt efter binding.

### Sec-Major-2 — Vilseledande overlay-kommentar om CloudFront

Initial kommentar antydde att `ForwardLimit = 2` räcker bakom ALB+CloudFront. Men CloudFront edge-IPs ligger i AWS-managed prefix-list (`com.amazonaws.global.cloudfront.origin-facing`), inte VPC-CIDR. Bara VPC-CIDR i KnownNetworks → middleware stoppar vid CloudFront-hop → `RemoteIpAddress` blir CloudFront-IP, inte klient-IP = `ForwardLimit=1`-effekt utan att märkas.

**Fix in-block:** uppdaterad overlay-kommentar med CloudFront-clarification (ALB-only-deploy använder `ForwardLimit = 1`) + `aws-setup.md §3.3` utvidgad med ForwardLimit-handling-tabell + skript-anvisning för KnownProxies-uppdatering via `aws ec2 describe-managed-prefix-lists`.

**Code-reviewer-fynd (alla fixade in-block):**
- M1 — using-ordning out-of-order i `Api/Program.cs`
- M2 — felmeddelande i `HangfireConnectionStringResolver` förtydligad med konkret env-var-namn + Secrets Manager-path
- M3 — kort kommentar lagd ovanför `for`-loopar i `ForwardedHeadersConfig` (varför for inte foreach — KnownNetworks[i]-position i error)

**Tester (final):** 31 nya på `ForwardedHeadersConfig`:
- 17 parse-tester (defaults + binding + IPv4/IPv6 CIDR + invalid CIDR/IP + Theory på ForwardLimit-range)
- 14 `EnsureSafeForEnvironment`-tester (4 dev/test-allowed + 5 prod-fail-loud + 2 populated-allowed + 3 invalid-env-arg)

## Block 3 — appsettings.Production.json overlays

Två overlay-filer. JSON-comments (`// xxx`) använda — ASP.NET `JsonConfigurationProvider` stödjer dem (`JsonReaderOptions.CommentHandling = Skip` sedan .NET Core 3.0). Comments är load-bearing: dokumenterar pre-launch-gate, env-var-injection-strategi, ConnectionStrings-frånvaro.

## Beslut

- **Allow-list-symmetri mellan parallella entry-points** — Worker `safeForAutoSchema` (PrepareSchemaIfNecessary) och Api `EnsureSafeForEnvironment` (KnownNetworks) ska ha samma struktur. Symmetri-miss = tyst no-op-yta i prod. Lyft till testbar metod på options-klassen så uppstart-validering är unit-testbar utan host-bygge.
- **`System.Net.IPNetwork` > `Microsoft.AspNetCore.HttpOverrides.IPNetwork`** — .NET 10 har deprecated den senare. Använd `KnownIPNetworks`-property istället för `KnownNetworks` på `ForwardedHeadersOptions`.
- **ALB-only-deploy använder `ForwardLimit = 1`** — `ForwardLimit > 1` kräver att alla hops är populerade i KnownNetworks/KnownProxies. CloudFront-prefix-list är dynamisk och kräver script-uppdatering, så förenkla initialt till 1.
- **Pre-launch-gates som operativ-deferral** — operativa AWS-Console/CLI-uppgifter (CloudWatch retention, IAM cleanup, RDS schema-DDL, Hangfire schema-DDL) blir STEG 14, inte STEG 12. Kod är klart att appliceras.
- **Production-overlays committas med tom KnownNetworks** — tom array är medveten pre-launch-gate. Allow-list-defense kompletterar runbook-disciplinen — ECS-container startar inte oavsett om operatorn glömmer overlay.

## Commits

| SHA | Beskrivning |
|-----|-------------|
| f8488b4 | feat(worker): TD-17 punkt 4 — HangfireConnectionStringResolver-fallback (STEG 12) |
| bb26fec | feat(api): TD-21 — ForwardedHeadersConfig + production-defense (STEG 12) |
| d879f96 | docs(runbooks): STEG 12 Sec-Major-2 — ForwardLimit + CloudFront-prefix-list |

## Tester totalt

- **Backend:** 537 (157 Domain + 183 Application + 23 Architecture + 26 Worker + 148 Api Integration) — +35 sedan STEG 11
- **Frontend:** 65 Vitest + 19 Playwright E2E (oförändrat)

## Reviews

| Rapport | Status |
|---|---|
| `docs/reviews/2026-05-09-steg12-security.md` | Approved with 2 Major (Sec-Major-1+2 fixade in-block), 3 Minor + 2 Nit defererade |
| `docs/reviews/2026-05-09-steg12-code.md` | Approved with 0 Major, 3 Minor (M1-M3 fixade in-block), 4 Nit "rätt-val" |

## Lärdomar STEG 12

- **`Microsoft.AspNetCore.HttpOverrides.IPNetwork` är deprecated i .NET 10** — använd `System.Net.IPNetwork.TryParse` + `ForwardedHeadersOptions.KnownIPNetworks` istället. ASPDEPR005-warning vid initial impl fångade detta.
- **Allow-list-pattern mellan Api/Worker entry-points** — symmetri-miss → tyst no-op-yta. Replikera strukturellt mellan parallella entry-points.
- **`ForwardLimit > 1` kräver alla hops i KnownNetworks/KnownProxies** — bara VPC-CIDR räcker inte vid CloudFront+ALB-kedja. CloudFront edge-IPs ligger i AWS-managed prefix-list, inte VPC.
- **JsonConfigurationProvider stödjer `// comments` sedan .NET Core 3.0** — användbart för load-bearing pre-launch-gate-dokumentation i overlays. Verifiera att Prettier i lint-staged inte strippar dem.
- **Statisk testbar metod på options-klassen > inline-Program.cs-logik** — uppstart-validering kan unit-testas utan host-bygge.

## Nästa session

**STEG 13 — Infra-as-code-stack (Alt A2).** Bygg Terraform-modules för:
- VPC + subnets (private + public, multi-AZ)
- RDS PostgreSQL 18.3 (multi-AZ, encrypted, automated backups)
- ElastiCache Redis (replication group, encrypted in-transit + at-rest)
- ECS Fargate cluster + task-definitioner (Api + Worker)
- ALB + listeners (HTTPS via ACM) + target groups
- ECR repositories (api + worker)
- Route53 zone + ACM-cert (`dev.jobbpilot.se`)
- CloudWatch LogGroups med `retention_in_days = 30` (TD-22 pre-launch-gate)
- IAM execution-roles + task-roles + secrets-policies

Inkluderar Dockerfiles för Api + Worker. Pre-launch-gates appliceras inline:
- KnownNetworks=VPC-CIDR populerad
- ConnectionStrings-poster i AWS Secrets Manager (jobbpilot_app + jobbpilot_worker)
- CloudWatch retention=30
- Hangfire schema-DDL via Install.sql (operativt i STEG 14, runbook-procedur)

Stort scope (1-3 sessioner). security-auditor + dotnet-architect i loop. Inget GitHub Actions ännu (det är STEG 14).

## Open follow-ups (tech-debt)

- TD-13 (PII-encryption Fas 2 — kombineras med TD-27)
- TD-14 (DeleteResumeVersion Fas 4)
- TD-15 (Resume-formulär a11y Fas 1)
- TD-18 (intervju-states-utökning)
- TD-19 (Worker defense-in-depth Fas 2 — inkl Hangfire.AspNetCore-trim)
- TD-20 (SqlQuery<FormattableString>-refactor opportunistiskt)
- TD-23 (Redis MULTI/EXEC opportunistiskt)
- TD-24 (cascade-paginering Fas 4)
- TD-25 (per-konto try/catch opportunistiskt)
- TD-26 (AI-kostnadstak Fas 4)
- TD-27 (EmailHash-HMAC Fas 2)
- TD-28 (Frontend typed-confirmation-UX för DELETE /me)

Sec-Minor från STEG 12 (defererade):
- Sec-Minor-1: ForwardedHeaders flag-set hårdkodad (lyfts om CloudFront-Host-routing aktualiseras)
- Sec-Minor-2: Worker-allow-list `IsEnvironment("Test")` är dead branch
- Sec-Minor-3: Felmeddelanden vid CIDR/IP-parse läcker raw-värdet (CIDR/IP är publik infra-info, ingen secret-leak — ändra mönstret innan det kopieras till secret-bärande config-loop)
- Sec-Nit-1: Verifiera prettier-config strippar inte JSON-comments
