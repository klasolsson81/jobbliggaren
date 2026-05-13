---
session: D+A — TD-79 pipeline-hygien + F2-P9 search/filter (TD-70)
datum: 2026-05-13
slug: d-a-pipeline-search
status: komplett + deployad
commits:
  - 94ec84a chore(infra): lifecycle.ignore_changes=[task_definition] på ECS api+worker services (TD-79)
  - d4294b6 feat(jobads): F2-P9 search/filter-yta ?ssyk&?region&?q + ListReadPolicy rate-limit (TD-70)
tag: v0.2.5-dev
deploy: success 7m (run 25797979739), Phase E migration applied
---

# D+A-session — TD-79 + F2-P9 (TD-70)

## Mål

Tvådelade leveranser per Klas-prompt:
- **Del D — TD-79:** lifecycle.ignore_changes på ECS-services för pipeline-Terraform-coexistens
- **Del A — TD-70:** F2-P9 search/filter-yta för JobAd-katalog (`?ssyk&?region&?q`)

Båda i samma session med PR-rapport efter varje commit per memory `feedback_nonstop_with_pr_reports`.

## Del D — TD-79 STÄNGD (commit `94ec84a`)

### Förkrav verifierade

- HEAD `5a32962` clean
- AWS SSO aktiv
- Dev `/api/ready` → 200 OK
- 3 CloudWatch-alarms i OK-state (jobtech-sync, auditor-write, worker-log-pipeline-health)

### Leverans

`lifecycle { ignore_changes = [desired_count, task_definition] }` på:
- `module.ecs.aws_ecs_service.api`
- `module.ecs.aws_ecs_service.worker`

Pattern verifierat mot HashiCorp officiella docs (web-search 2026-05-13).

### Plan-output post-fix

| Resurs | Pre-fix | Post-fix |
|---|---|---|
| `aws_ecs_service.api.task_definition` | ~ update | ❌ no-op |
| `aws_ecs_service.worker.task_definition` | ~ :8 → :1 (potentially destructive rollback) | ❌ no-op |
| `aws_ecs_task_definition.api` | -/+ replace | ✓ apply (revision :13 ny, service ignorerar) |
| `aws_db_parameter_group.this` | ~ apply_method cosmetic | ~ kvarstår (pre-existing, ej TD-79-scope) |

### Apply-verifiering

- `terraform apply` 4s success (1 add, 1 change, 1 destroy)
- API task-def `:13`, Worker `:8` (NOT rolled back to `:1`)
- `/api/ready` 200 OK post-apply
- **Bonus:** AdminBootstrap__InitialAdminEmail-env-var-ägarskap löst som biverkan (Terraform äger task-def-content, CI/CD äger revision)

### Disciplin

Klas-STOPP före `terraform apply` per CLAUDE.md §9.2 — Klas godkände efter plan-rapport.

## Del A — TD-70 STÄNGD (commit `d4294b6`, tag `v0.2.5-dev`)

### Discovery + web-search (parallell)

- JobAd-aggregat: Title, Company (owned VO), Description, Url, Status, Source, Published/Expires/Created, External (owned VO), `RawPayload` (jsonb). **Inga SSYK/Region-fält top-level.**
- `ListJobAdsQuery` med page/pageSize/sortBy (TD-56 levererat) — utvidgas, inte ersätts
- JobTech v2 `occupation-concept-id` + `location-concept-id` är hierarkiska taxonomi-strängar (`MVqp_eS8_kDZ`), inte numeriska SSYK
- `JobTechPayloadSanitizer` allowlist bevarar `occupation/concept_id` + `workplace_address/region_concept_id`
- Npgsql `EF.Functions.ToTsVector("swedish", ...).Matches(...)` stöder fulltext (deferrat trigger-baserat)

### dotnet-architect design-skiss

10 multi-approach-val identifierade (Q1-Q10) + identifierade öppna frågor om EF.Functions-purism i Application. Levererade alternativ med branschens motivering — INGA egna beslut per memory `feedback_cto_decides_multi_approach`.

### senior-cto-advisor rond 1 (11 entydiga beslut)

| Q | Beslut | Kort motivering |
|---|---|---|
| Q1 | **B** raw `string?` filter | Beck YAGNI, Evans §14 ACL — JobTech-taxonomi ≠ JobbPilots ubiquitous language |
| Q2 | **C** Postgres generated columns + B-tree partial index | Hunt/Thomas DRY (single source of truth), Knuth — fri optimering |
| Q3 | **A** `EF.Functions.Like + .ToLower()` | Beck YAGNI för Fas 2-volym; tsvector trigger-baserat |
| Q4 | **A** utvidga `ListJobAdsQuery` | Martin OCP, Fielding REST-konvention |
| Q5 | **A** behåll `/api/v1/job-ads` | Filter är collection-narrowing, inte separat resurs |
| Q6 | **A** EF.Functions direkt i Application | Generated columns Q2-C eliminerar `JsonContains`-behovet; ILike senare → Like+ToLower |
| Q7 | strikt regex `^[A-Za-z0-9_-]{1,32}$` + q MinLength(2).MaxLength(100) | Saltzer/Schroeder default-deny, OWASP API8:2023 |
| Q8 | **A** egen DB-filtering | ADR 0032 dataägarskap, Nygard *Release It!* resilience |
| Q9 | Migration krävs (db-migration-writer) | Q2-C tvingar SQL-migration |
| Q10 | **A** DTO oförändrad | Beck YAGNI + Fowler Speculative Generality |
| Q11 | **INGET Klas-STOPP** | Default-bana per ADR 0019; alla beslut entydiga mot principer |

### Implementation

| Fil | Ändring |
|---|---|
| `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQuery.cs` | Lägg till `Ssyk?, Region?, Q?` optional params |
| `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQueryValidator.cs` | Regex `^[A-Za-z0-9_-]{1,32}$` + Q `MinLength(2).MaxLength(100)` + `IsNullOrWhitespace`-bypass |
| `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQueryHandler.cs` | `ApplyFilters` via `EF.Property<string?>(...)` shadow-props + `EF.Functions.Like(j.Title.ToLower(), pattern)` med CA1304/CA1311 pragma-suppress |
| `src/JobbPilot.Infrastructure/Persistence/Configurations/JobAdConfiguration.cs` | Shadow-properties `SsykConceptId` + `RegionConceptId` via `HasComputedColumnSql(..., stored: true)` |
| `src/JobbPilot.Infrastructure/Persistence/Migrations/20260513111555_F2P9JobAdSearchColumns.cs` | `AddColumn<string>` fluent + raw SQL `CREATE INDEX … WHERE … IS NOT NULL` partial-index |
| `src/JobbPilot.Api/Endpoints/JobAdsEndpoints.cs` | Nya query-params + `.RequireRateLimiting(ListReadPolicy)` på GET-routes |

### Migration-friktion (löst inline)

1. **`EF.Functions.ILike` är Npgsql-specifik** (compile-error Application-side) → bytte till `EF.Functions.Like + .ToLower()` (provider-agnostiskt API).
2. **`.ToLowerInvariant()` saknar Npgsql-translation** (runtime-failure i integration-tester) → bytte till `.ToLower()` med CA1304/CA1311 pragma-suppress + kommentar (LINQ-translation till SQL `LOWER()`, runtime-culture irrelevant).
3. **Migration-konflikt** db-migration-writer skapade `20260513120000_F2P9JobAdSearchColumns.cs` (raw SQL) men EF CLI genererade samtidigt `20260513111555_F2P9ShadowPropertiesSync` (fluent AddColumn) — båda försökte ADD COLUMN samma kolumner. Löst genom att ta bort raw-SQL-migrationen + appendera CREATE INDEX-SQL till EF-fluent-migrationen + rename:a klassen till `F2P9JobAdSearchColumns`.

### security-auditor → CTO-triage → in-block-rate-limit

**Major-fynd:** `/api/v1/job-ads` saknar rate-limit. Auth-gated räcker mot anonym DoS men inte mot multi-query-attack från komprometterat konto (OWASP API4:2023 "Unrestricted Resource Consumption" + `Like '%q%'` är sequential scan).

**CTO-rond 2 beslut:** In-block-fix (inte TD). Default per CLAUDE.md §9.6. Argument:
- Fyndet tillhör nuvarande fas (F2-P9 search-yta är vektorn)
- Rate-limit-infrastruktur etablerad (5 existerande policies)
- Konsekvens-värde: andra tunga endpoints har policies → bryta mönstret kräver positivt skäl
- OWASP API4 norm + Mastercard-granskning-norm (CLAUDE.md §1)

**Konkret leverans:**
- Ny `ListReadPolicy` konstant + policy-block (60/min per UserId sub-claim, FixedWindow, QueueLimit=0)
- `RateLimitingOptions.ListRead` med XML-doc + revisit-trigger
- `.RequireRateLimiting(ListReadPolicy)` på GET-routes (NOT POST — admin-flöde)
- Dedikerad `ListReadRateLimitApiFactory` med aggressiv test-limit (3/60s) + AuthWrite-höjd för registrering
- Nytt integration-test `ListReadRateLimitTests` (verifierar 429 + Retry-After-header)
- `ApiFactory` + `StrictRateLimitApiFactory` env-var-overrides för att inte krocka mellan test-collections

### code-reviewer

APPROVED 0 Blocker / 0 Major / 2 Minor / 2 Nit. Approve som-är. Implementation-fidelity verifierad mot CTO-design och CLAUDE.md §2-§10.

### Tester (full svit grön)

| Suite | Pre | Post | Delta |
|---|---|---|---|
| Domain.UnitTests | 225 | 225 | 0 |
| Application.UnitTests | 323 | **354** | +31 (validator-cases) |
| Architecture.Tests | 50 | 50 | 0 |
| Api.IntegrationTests | 240 | **254** | +14 (13 filter + 1 rate-limit-429) |
| Worker.IntegrationTests | 26 | 26 | 0 |
| Migrate.UnitTests | 6 | 6 | 0 |
| **Totalt** | **870** | **915** | **+45 gröna** |

### Tag-push + deploy + smoke

- Tag `v0.2.5-dev` på `d4294b6` → pushed 12:05 UTC
- Deploy run `25797979739` — 7m success
- Phase E migration applied (F2P9JobAdSearchColumns)
- API task-def `:15` + Worker `:9` live
- `https://dev.jobbpilot.se/api/ready` → 200 OK
- Unauth + invalid-input smoke → 401 (auth-gating fungerar)

## Web-search-källor (CLAUDE.md §9.5)

- [HashiCorp aws_ecs_service registry](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/ecs_service)
- [HashiCorp lifecycle meta-argument](https://developer.hashicorp.com/terraform/language/meta-arguments/lifecycle)
- [JobTech JobSearch API v2](https://jobtechdev.se/en/components/jobsearch)
- [Npgsql EF Core Full Text Search](https://www.npgsql.org/efcore/mapping/full-text-search.html)
- [OWASP API Security Top 10 2023 API4](https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/)

## TD-trigger-kandidater (lyfts EJ — CC pressade mot §9.6 vid CTO-rond)

- Micro-ADR-amendment 0032 §10 "Derived columns från raw_payload"
- tsvector + GIN för fulltext-search (trigger: Fas 3 UX-research för stemming ELLER prod-latens >100ms)
- `JobAdSearchIndex` CQRS read-model (trigger: >50k rader)
- DTO-utvidgning med Ssyk/Region per rad (trigger: UX-behov)
- JobTech v2 proxy hybrid-search (Fas 3 dual-source-UX)
- Micro-ADR "när krävs Application-port för EF.Functions?" (om TD-73-precedens vs Q6-A behöver formaliseras)

## Pending operativt för Klas

- AdminBootstrap-env-var-ägarskap — **LÖST** som biverkan av TD-79 (Terraform äger task-def-content)
- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API — bekräftat)
- Frontend-deploy till Vercel (kommer i v0.2.x-patch efter F2-P9 backend-leverans)
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs (kvarstår)

## Nästa session — Klas-val

1. **v0.2-prod-tag-förberedelse:** TD-70 + TD-79 stängda; samla prod-launch-checklist
2. **Frontend-deploy** till Vercel + JobAd-katalog UI som konsumerar F2-P9 search-endpoint
3. **F2-P10/nästa Fas-2-feature** TBD per Klas roadmap-prioritet
