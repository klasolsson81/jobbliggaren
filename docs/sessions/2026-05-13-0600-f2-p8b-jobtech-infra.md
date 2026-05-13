---
session: F2-P8b — JobTech Infrastructure-leverans (Refit + Resilience + admin-trigger)
datum: 2026-05-13
slug: f2-p8b-jobtech-infra
status: Klar — väntar Klas-GO för tag-push v0.2.2-dev
commits:
  - 8c09191  # feat(jobads): F2-P8b — JobTech Infrastructure + admin-trigger-endpoint
deploy_tag: (väntar Klas-GO — resilience-verifiering mot dev krävs per CTO-rond 1)
---

# Session 2026-05-13 (efterm./natt) — F2-P8b JobTech Infrastructure

## Mål

Leverera ADR 0032 §9 P8b-batch:
- IJobSource Application-port + DTOs
- IJobTechSearchClient via Refit 10.x
- IJobTechStreamClient typed-client
- PlatsbankenJobSource som IJobSource
- Microsoft.Extensions.Http.Resilience + AddStandardResilienceHandler
- Custom Polly-pipeline för JobStream med RateLimiter (1 req/min)
- JobTechOptions + ValidateOnStart
- JobTechPayloadSanitizer (TD-73 punkt 1)
- Admin-trigger-endpoint POST /api/v1/admin/job-ads/sync/platsbanken
- WireMock integration-tester (503 retry, polymorft event-schema)

Klas-disciplinpåminnelse vid session-start: **reviewers INLINE per CLAUDE.md §9.2,
inte post-hoc** (referens: F2-P8a-disciplinmiss där 4 reviewers kördes efter
merge). Etablerat i denna session som standard.

## Vad blev klart

| Område | Innehåll |
|---|---|
| **Application port** | `IJobSource` i `JobAds/Abstractions/` + `JobAdSnapshot`/`JobAdChange`/`JobAdUpsert`/`JobAdRemoval` (LSP-diskriminerad union) + `JobAdImportItem` transport-DTO |
| **Infrastructure** | `JobTechOptions`, `JobTechPayloadSanitizer` (pure static allowlist), `JobTechSearchResponse` (wire-DTOs internal), `IJobTechSearchClient` (internal Refit), `IJobTechStreamClient` + `JobTechStreamClient` (internal typed), `PlatsbankenJobSource` (internal sealed partial — LoggerMessage source-gen) |
| **DI** | `AddJobSources`-extension i `DependencyInjection.cs`. Search via `AddStandardResilienceHandler` (3 retry expo + CB 5/5min). Stream via custom `AddResilienceHandler` (RateLimiter → Retry → CB) med process-statisk `FixedWindowRateLimiter(1, 1 min)`. `MaxResponseContentBufferSize=500 MB` (sec-Min-3 DoS-cap) |
| **Application command** | `SyncPlatsbankenSnapshotCommand` (IAdminRequest) + `SyncPlatsbankenSnapshotResult` + handler (bulk-fetch + in-memory split — race-skydd via DbUpdateException är medvetet P8c-scope per ADR 0032 §5) |
| **Api** | `AdminJobAdsEndpoints.MapAdminJobAdsEndpoints` mappad i `Program.cs:256`. `RequireAuthorization(AuthorizationPolicies.Admin)` |
| **Tester** | Sanitizer 8 + Handler 4 + Architecture 4 + Api.Integration 6 (3 admin + 2 stream-resilience + 1 stub) |
| **GDPR-docs** | `docs/runbooks/gdpr-processing-register.md` skapad med JobTech-entry (TD-73 punkt 3) |

**Commits:** 1 (`8c09191`) — 24 filer, 1703 insertions.

## ADR-status

- **ADR 0032** Accepted — P8a + P8b § levererade. §8-amendment punkt 1 (sanitizer)
  + punkt 3 (processing-register) levererade i denna batch. Punkt 2 (raw_payload
  retention via Hangfire) + punkt 4 (right-to-erasure) kvarstår för P8c.

## Tester (full svit grön)

| Suite | Före → Efter |
|---|---|
| Domain.UnitTests | 218 → 218 (oförändrat) |
| Application.UnitTests | 258 → 270 (+12: sanitizer 8 + handler 4) |
| Architecture.Tests | 33 → 37 (+4: JobSourceLayerTests) |
| Api.IntegrationTests | 226+ → 234 (+6: admin auth/flow + WireMock resilience) |
| Migrate.UnitTests | 6 (oförändrat) |

Totalt backend: ~765 tester gröna.

## Reviewers INLINE (CLAUDE.md §9.2 — disciplin-fix från F2-P8a)

| Reviewer | Tidpunkt | Fynd | Resolution |
|---|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss approved. Två viktiga noter: (1) IJobSource ska ligga i `Application/JobAds/Abstractions/` per-aggregate, (2) JobStream kräver separat RateLimiter — `AddStandardResilienceHandler` täcker inte proaktiv throttling | Båda implementerade |
| code-reviewer | EFTER impl, INNAN commit | 0 Blocker, 1 Major (race-skydd — ADR-acknowledged P8c-scope), 4 Minor (allowlist-keys, rate-limiter docstring, PublishedAt-fallback, integration-test för upsert) | Alla Minor fixade in-block |
| security-auditor | EFTER impl, INNAN commit | 0 Critical, 0 GDPR-Blocker, 2 Major (Description-fri-text SECURITY-NOTE, processing-register-fil), 3 Minor (mailto-URL-guard, API-key-logging-kommentar, MaxResponseContentBufferSize) | Alla Major + Minor fixade in-block |

CTO-advisor ej invokerad — architect gav entydiga svar, inga multi-approach-val
kvarstod (per CLAUDE.md §9.6 + memory `feedback_cto_decides_multi_approach`).

## Disciplinmissar fångade + fixade

1. **NU1902 (CVE NU1902 OpenTelemetry.Api 1.14.0)** — WireMock.Net 1.25.0 drog
   in OpenTelemetry transitivt. Fix: pinning till 1.15.3 i `Directory.Packages.props`
   via `CentralPackageTransitivePinningEnabled` (web-verifierat 2026-05-12 enligt
   GHSA-g94r-2vxg-569j).
2. **CA1848/CA1873 LoggerMessage warnings** — `LogInformation/LogDebug` med
   parametrar bryter `TreatWarningsAsErrors`. Fix: `[LoggerMessage]`
   source-gen-pattern (samma som `IdempotentAdminRoleSeeder.cs`).
3. **CA1822 Sanitizer SanitizeForStorage** — metoden använder inte instance-data.
   Fix: gjorde hela klassen `public static` istället för `public sealed`.
   DI-registrering togs bort. Konsumenter anropar `JobTechPayloadSanitizer.SanitizeForStorage(...)`.
4. **CA1305 DateTime.ToString utan IFormatProvider** — JobTechStreamClient
   formaterar `since` till JobTech-API-param. Fix: `CultureInfo.InvariantCulture`.
5. **CA1859 SanitizeObject return-type** — fix: `JsonObject` istället för `JsonNode`.
6. **xUnit1051 TestContext.Current.CancellationToken** — handler-tester använde
   `await db.SaveChangesAsync()` utan ct. Fix: propagera ct genom alla EF-anrop.
7. **Sanitizer-test SanitizeForStorage_PreservesPublicMetadata FAIL** — allowlist
   saknade `text`-key (nested i description.text). Fix: lade till `text`,
   `conditions`, `abilities`.

## Web-search räddade scope

Tre kritiska fakta-verifieringar via web-search (CLAUDE.md §9.5):

1. **JobTech API:er** — bekräftade jobsearch/jobstream endpoints + API-key krävs
   via apirequest.jobtechdev.se (datum-verifierat 2026-05-12).
2. **Microsoft.Extensions.Http.Resilience** — senaste stabila 10.5.0 för .NET 10.
3. **OpenTelemetry CVE-pinning** — GHSA-g94r-2vxg-569j patchad i 1.15.3
   (`opentelemetry.api/CVE-2026-40894`).

## Lärdomar

- **Reviewers INLINE räddar scope** — 2 Major-fynd från security-auditor
  (Description-PII-doc + processing-register) hade förmodligen lyfts som TDs
  vid post-hoc audit. Inline-discipline gav in-block-fix per §9.6.
- **Architect-rond före kod = preventiv mot Polly-detour** — utan architect:s
  varning hade `AddStandardResilienceHandler` använts på JobStream → 1-req/min
  brutits vid retry-loop. Detta är "design-as-decision-not-test"-värde.
- **Process-statiska rate-limiters är test-fientliga** — fix: separat DI-container
  i `JobTechStreamResilienceTests` som testar bara retry+CB-pipeline utan
  rate-limit-bagage. P8c-Hangfire-jobben kommer dela samma limiter i prod.
- **CVE-pinning är inte luxe — det är default** — varje gång ett nytt paket
  läggs till bör senaste CVE-status verifieras. NU1902 är `WarningAsError`.

## Pending

**Klas-STOPP-flagga (CTO-rond 1 2026-05-12):** admin-endpoint exponerar synkron
JobTech-call → verifiera resilience-config mot dev INNAN tag-push.

Steg som väntar Klas-GO:

1. Skapa tag `v0.2.2-dev` på commit `8c09191` → deploy-pipeline triggar
2. Smoke-test admin-endpoint mot dev:
   - `POST https://dev.jobbpilot.se/api/v1/admin/job-ads/sync/platsbanken`
     (med admin-session via Klas-konto)
   - Verifiera 200-respons med counts från riktig JobTech-snapshot
   - Verifiera att `job_ads`-tabellen får nya rader med External-ref + sanerad RawPayload
3. (Operativt pending) JobTech-API-key registrering på apirequest.jobtechdev.se
   krävs INNAN admin-trigger fungerar — annars 401 mot JobTech

## Nästa session — F2-P8c (Hangfire)

Per ADR 0032 §9 leverans-plan:

- `SyncPlatsbankenStreamJob` (cron `*/10 * * * *`) — använder `IJobSource.StreamChangesAsync`
- `SyncPlatsbankenSnapshotJob` (cron `0 2 * * *`) — använder `IJobSource.FetchSnapshotAsync`
- `UpsertExternalJobAdCommand` med DbUpdateException-catch-pattern (ADR 0032 §5)
- Removal-handling via `JobAd.Archive` (ADR 0032 §6)
- `JobAdsSyncedDomainEvent` audit-wire (ADR 0032 §8)
- `PurgeStaleRawPayloadsJob` (cron `0 3 * * *`, 30 dagars retention) — TD-73 punkt 2
- `RawPayloadPurgedDomainEvent` audit-wire
- TD-73 punkt 4 (right-to-erasure-cascade till raw_payload)
- E2E-tester på dev: verifiera Stream-cron kör ~6×/timme

## Tidsuppskattning

~5h CC-tid effektivt (24 filer, 1703 insertions, 4 agent-ronder, 7 disciplinmiss-
fixar). Reviewers-INLINE-discipline kostade ~1h extra men sparade post-hoc fix-batch.

**HEAD vid session-end:** `8c09191` (icke-deployad — tag-push väntar Klas-GO)
