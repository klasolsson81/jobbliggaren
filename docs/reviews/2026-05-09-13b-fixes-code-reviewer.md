# Code-review: STEG 13b-fix-paketet

**Status:** Approved med 0 Blocker / 0 Major / 4 Minor / 3 Nit + Praise
**Granskat:** 2026-05-09
**Auktoritet:** CLAUDE.md §2.1 (Clean Arch), §3 (.NET-standarder), §5.1 (anti-patterns), §6.2 (Conventional Commits), §1.5 (commit-strategi)
**Scope:** Api/Program.cs (env-gate + /api/ready), Dockerfile-städ (api+worker), modules/redis (CS-secret), modules/ecs (healthCheck-borttag), environments/dev (5 modul-anrop + secrets-flow), ADR 0026, TD-29/TD-30, README-cost-tabell. Inga Domain/Application-ändringar.

---

## Blocker

Inga.

## Major

Inga.

---

## Minor

### M1 — Magic string `"Alb:HttpsEnabled"` saknar konstant

`Program.cs:120` läser konfig via raw string. CLAUDE.md §5.1 ("Magic strings — alltid konstanter eller enums"). Etablerat mönster: `ForwardedHeadersConfig.SectionName` och `RateLimitingOptions.SectionName` (per STEG 12-review). Inkonsekvent att gate:n direkt-läser.

**Föreslagen åtgärd:** introducera `AlbOptions` med `public const string SectionName = "Alb";` + `public bool HttpsEnabled { get; init; }` i `JobbPilot.Api/Configuration/`. Bind via `Configuration.GetSection(AlbOptions.SectionName).Get<AlbOptions>()`. Matchar STEG 12-pattern och gör värdet testbart utan att starta ASP.NET.

**Delegera till:** Klas eller dotnet-architect (trivial 30-rad-fix, bör kombineras med test som STEG 12 gjorde för ForwardedHeadersConfig).

### M2 — ALB-modul saknar fail-loud-validation för cert-ARN-mismatch

`modules/alb/variables.tf:69-79`: `https_listener_enabled = true` med `acm_certificate_arn = null` failar i `terraform apply` (aws_lb_listener.https kräver certificate_arn) — men felmeddelandet är AWS-API-leverans, inte tydlig pre-condition. Klas frågade explicit om detta edge-case.

**Föreslagen åtgärd:** lägg till `validation`-block på `acm_certificate_arn`:

```hcl
validation {
  condition     = !var.https_listener_enabled || var.acm_certificate_arn != null
  error_message = "acm_certificate_arn krävs när https_listener_enabled = true (ADR 0026-trigger)."
}
```

Fail-loud vid `terraform plan`, inte vid apply. CLAUDE.md §3.4 ("DomainException för invariant-brott") — samma princip för Terraform-modul-invarianter.

**Delegera till:** Klas eller dotnet-architect.

### M3 — `app.UseHttpsRedirection()`-gate saknar test

CLAUDE.md §2.4 + §7: "Alla nya handlers har minst en test". `Program.cs:120-124` är ny gate-logik som påverkar pipeline-konfig. Per ADR 0026 är detta säkerhets-relevant kod (förhindrar redirect-loop bakom HTTP-only-ALB). I dag enbart manuellt verifierat via terraform plan-narrativ.

**Föreslagen åtgärd:** integration-test via `WebApplicationFactory<Program>` som setter `Alb:HttpsEnabled=true` via `UseSetting` och verifierar att HTTP→HTTPS-redirect-middleware är registrerad (eller motsatsen). Kombineras naturligt med M1:s `AlbOptions`-extraktion.

**Delegera till:** test-writer.

### M4 — Commit-strategi: 3 commits, inte 1

Per CLAUDE.md §1.5 ("Commit docs-uppdateringar separat från feature-commits — inte bundlade") + §6.2 (single-type per commit). Paketet blandar 3 typer:

1. **`feat(infra): STEG 13b — ECS/ALB/ECR/IAM/CloudWatch + Redis CS-secret`** — Terraform-moduler + environments/dev/main.tf + redis-modul-fix
2. **`fix(api): gate UseHttpsRedirection bakom Alb:HttpsEnabled + härd Dockerfiles`** — Program.cs + båda Dockerfile + ECS-modul healthCheck-borttag
3. **`docs: ADR 0026 + TD-29/TD-30 + dev/README-cost`** — ADR + tech-debt + README

Bundla 1+2 ger blandad scope ("infra"+"api"); bundla allt i en commit gör revert hårdare och bryter conventional commits.

**Delegera till:** Klas vid commit-tid.

---

## Nit

### N1 — `/api/ready` lambda är inline-anonym

`Program.cs:139` returnerar literal `new { status = "ready", service = "JobbPilot.Api" }`. Acceptabelt för Fas 0; vid TD-29-stängning byts den ut mot `AddHealthChecks()`-pipeline ändå. TODO-kommentaren §137 är explicit och daterar.

**Status:** Ingen åtgärd.

### N2 — `tostring(var.alb_https_enabled)` i environments/dev/main.tf:246

Terraform `tostring(true)` → `"true"`. ASP.NET `Configuration.GetValue<bool>` parsar case-insensitive — fungerar. Acceptabelt. Alternativ: `var.alb_https_enabled ? "true" : "false"` är mer explicit men inte nödvändig.

**Status:** Ingen åtgärd.

### N3 — Worker Dockerfile-kommentar §32-35 är välmotiverad

Klar runtime-rationale + TD-19 cross-reference + image-storlek-estimat. Pre-empt:ar exakt frågan "varför aspnet-image för HTTP-fri Worker". Rätt detaljnivå.

**Status:** Ingen åtgärd.

---

## Praise

### Block 1 — Clean Architecture intakt
Inga Domain/Application-ändringar. EF Core-imports, AWS SDK, HttpClient direkt-imports — alla noll. Verified via grep.

### Block 2 — Anti-pattern §5.1 cleanly passed
- Inga `DateTime.Now`/`UtcNow` (grep noll-träff i Api)
- Inga `Console.WriteLine` (noll-träff)
- Inga emoji eller utropstecken i `/api/ready`-JSON-response
- Inga hårdkodade secrets — connection-strings via Secrets Manager-ARNs i ECS task-def

### Block 3 — Defense-in-depth dokumenterad i kommentarer
`Program.cs:114-119` (HttpsRedirection-gate) + `:132-137` (TD-29 readiness-vs-liveness) + `Dockerfile §35-43` (ingen curl, fyra rationale-punkter) + `ecs/main.tf:86-89` (inget healthCheck-block, motiverat). Alla peer-review-vänliga, alla cross-refererar ADR/TD/Sec-finding.

### Block 4 — Redis CS-komponering är rätt nivå
`modules/redis/main.tf:70-89` komponerar StackExchange.Redis-format vid Terraform plan/apply. Inga kod-ändringar krävs i `Infrastructure/DependencyInjection.cs` eftersom env-var `ConnectionStrings__Redis` följer existerande pattern. Pragmatiskt och refactor-vänligt.

### Block 5 — ADR 0026 är hårt definierad
30-dagars hard-deadline (2026-06-08) + 5 konkreta triggers + mitigation-stack med 6 explicit-listade åtgärder. Inga "vi tar det sen"-glidning. Implementation-status-checklista med ✅/⏳-markeringar.

---

## Clean Architecture + anti-pattern-status

**§2.1 (Clean Arch):** ingen lager-överträdelse. Api/Program.cs läser bara Configuration. ✓
**§5.1 (anti-patterns backend):** alla checkpoints passerade utom magic string M1 ✓
**§3 (.NET-standarder):** file-scoped namespaces, nullable enabled, var-deklaration konsekvent ✓
**§6.2 (Conventional Commits):** kräver split per M4 ⚠
**§1.5 (commit-strategi docs separat):** kräver split per M4 ⚠

---

## Sammanfattning

4 Minor (M1 magic string, M2 TF-validation, M3 saknad test, M4 commit-split) + 3 Nit (alla "rätt val"). Inga Blocker, inga Major. STEG 13b-fix-paketet är **approved för commit** efter:

1. **M4 commit-split** (obligatoriskt — påverkar pushed history)
2. **M1+M2+M3 kan bundlas till uppföljnings-commit** (`refactor(api): extrahera AlbOptions + test`) — inte blocker för 13b-apply, men flagga TD om defererad

Top-level docs (CLAUDE.md/BUILD.md/DESIGN.md) korrekt orörd. ADR 0026 cross-refererar ADR 0024 D7 + ADR 0025 verifierat. Klar för STOPP-rapport till Klas.
