# ADR 0032 — JobTech-integration: resilience-stack, dedup-strategi, sync-flöde

**Datum:** 2026-05-12
**Status:** Accepted 2026-05-12 (Klas-GO mottaget)
**Kontext:** F2-P8 JobTech/Platsbanken-integration (BUILD.md §9.1)
**Beslutsfattare:** senior-cto-advisor 2026-05-12 (decision) + Klas Olsson (godkänd 2026-05-12)
**Relaterad:** ADR 0005 (go-to-market, JobAd auth-gated), ADR 0022 (audit log-pipeline), ADR 0024 (audit retention), ADR 0023 (Hangfire-infrastruktur), BUILD.md §3.1 (HTTP-stack), §9.1 (JobTech-integration), §16 (job_ads-schema), TD-56 (stängd P7), TD-70 (search/filter, kommande)

## Kontext

JobbPilot ska importera platsannonser från Arbetsförmedlingens JobTech-API:er och persistera dem som `JobAd`-aggregat. BUILD.md §9.1 förskriver:

- `IJobTechClient` interface via Refit + `PlatsbankenJobSource : IJobSource`
- JobStream-prenumeration för realtid + JobSearch för backfill
- Retry med Polly: 3 försök expo backoff
- Circuit breaker efter 5 consecutive failures, 5min cooldown
- Hangfire `SyncPlatsbankenJob` var 10:e min + nattlig full backfill 02:00

BUILD.md §16 förskriver schemat:

```
job_ads
  source (text)         -- 'platsbanken', 'eures', ...
  external_id (text)
  source_url (text)
  raw_payload (jsonb)   -- komplett JobTech-JSON
  UNIQUE(source, external_id)
```

ADR 0005 etablerar att **JobAd-listning/sökning är auth-gated i Fas 2-start**.

**Web-verifierat 2026-05-12:**

- **JobStream** (`https://jobstream.api.jobtechdev.se/`): rate-limit **1 request/min**. `/snapshot` (alla öppna ads) + `/stream?date=ISO8601` (changes). Event-types: new/update/removal. Removal-objekt har `"removed": true` + `"removed_date"`. Auth via `api-key`-header.
- **JobSearch** (`https://jobsearch.api.jobtechdev.se/`): inga publicerade rate-limits (429 vid abuse). "Bulk discouraged — use Stream API". Klassisk REST/JSON.
- **`Microsoft.Extensions.Http.Polly`** är **deprecated** i .NET 10. Standard är `Microsoft.Extensions.Http.Resilience` (byggd på Polly v8) via `AddStandardResilienceHandler()`.

BUILD.md skriver "Polly" som *stack* men preciserar inte paketleverantör. Polly v8 är runtime för Microsofts paket — semantiken (3 retry expo + CB 5/5min) implementeras via konfiguration ovanpå.

Befintlig `JobAd`-domän har: `Title`, `Company` (VO), `Description`, `Url`, `Source` (`JobSource` VO: Manual/Platsbanken/LinkedIn), `Status` (`JobAdStatus`: Active/Expired/Archived), `PublishedAt`, `ExpiresAt`, `CreatedAt`, `DeletedAt`. **Saknar:** `ExternalId`, `RawPayload`, UNIQUE-constraint på (Source, ExternalId).

## Beslut

### 1. Resilience-paket: `Microsoft.Extensions.Http.Resilience` + `AddStandardResilienceHandler`

Använd Microsofts pre-konfigurerade standard-pipeline (built on Polly v8) istället för custom Polly v8-pipeline eller deprecated `Microsoft.Extensions.Http.Polly`. Konfigurera vid behov för att matcha BUILD.md §9.1 semantik:

```csharp
services.AddHttpClient<IJobTechSearchClient>(client =>
{
    client.BaseAddress = new Uri(options.JobSearchBaseUrl);
    client.DefaultRequestHeaders.Add("api-key", options.ApiKey);
    client.DefaultRequestHeaders.Add("accept", "application/json");
})
.AddStandardResilienceHandler(o =>
{
    // 3 försök expo backoff, CB 5/5min per BUILD.md §9.1
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.CircuitBreaker.FailureRatio = 0.5;
    o.CircuitBreaker.MinimumThroughput = 5;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
});
```

**Motivering (Microsoft Learn — Build resilient HTTP apps, .NET 10):**

- Officiell rekommendation i .NET 10. Att medvetet välja deprecated paket bryter versionshygien.
- Microsoft-teamet underhåller `AddStandardResilienceHandler` med best-practice defaults — vi vill inte uppfinna detta.
- Polly v8 är fortfarande runtime (BUILD.md säger "Polly", paketleverantör preciseras här).

### 2. Hybrid client-shape: Refit för JobSearch + typed-client för JobStream

**JobSearch:** klassisk REST/JSON → Refit-interface (BUILD.md §3.1 explicit, §9.1 explicit).

```csharp
public interface IJobTechSearchClient
{
    [Get("/search")]
    Task<JobTechSearchResponse> SearchAsync(
        [Query] string? q,
        [Query("offset")] int? offset,
        [Query("limit")] int? limit,
        CancellationToken ct = default);
}
```

**JobStream:** long-polling NDJSON-stream med polymorft event-schema (`{...}` + `{..., "removed": true, "removed_date": "..."}`). Refit:s `Task<HttpResponseMessage>`-stöd för streams förlorar type-safety. Custom typed-client med per-line `JsonDocument`-parsing ger explicit kontroll över event-discrimination:

```csharp
public interface IJobTechStreamClient : IJobSource
{
    Task<JobTechSnapshotResult> FetchSnapshotAsync(CancellationToken ct);
    IAsyncEnumerable<JobTechStreamEvent> StreamChangesAsync(
        DateTimeOffset since, CancellationToken ct);
}
```

`JobTechStreamEvent` är en diskriminerad sealed class-hierarki:

```csharp
public abstract record JobTechStreamEvent(string ExternalId, DateTimeOffset OccurredAt);
public sealed record JobTechAdUpsert(...) : JobTechStreamEvent(...);
public sealed record JobTechAdRemoval(...) : JobTechStreamEvent(...);
```

**Motivering (Martin 2017 kap. 7 SRP, kap. 9 LSP):** två klienter med två change-reasons (Search-API-shape vs Stream-protocol). LSP via gemensam `IJobSource`-port. Dependency Inversion respekterad.

### 3. Sync-orkestrering: Snapshot 02:00 + Stream var 10:e minut

Båda jobben implementeras via Hangfire per BUILD.md §9.1 + ADR 0023:

| Jobb | Schema | Källa | Syfte |
|---|---|---|---|
| `SyncPlatsbankenStreamJob` | `*/10 * * * *` | `/stream?date=<now-10min>` | Inkrementell uppdatering, removal-events |
| `SyncPlatsbankenSnapshotJob` | `0 2 * * *` | `/snapshot` | Daglig fullbackfill mot drift |

**Rate-limit-respekt:** JobStream:s `1 req/min` är 10× under 10-min-cykeln, så schemat har gott om marginal.

**Motivering:** Stream är primär (BUILD.md "JobStream-prenumeration för realtid"). Snapshot är nattlig korrigerings-flöde mot Stream-event-tapp.

### 4. Domänutökning: `ExternalReference` value object

```csharp
public sealed record ExternalReference
{
    public JobSource Source { get; }
    public string ExternalId { get; }

    private ExternalReference(JobSource source, string externalId)
    {
        Source = source;
        ExternalId = externalId;
    }

    public static Result<ExternalReference> Create(JobSource source, string? externalId)
    {
        if (source == JobSource.Manual)
            return Result.Failure<ExternalReference>(
                DomainError.Validation("ExternalReference.ManualNotAllowed",
                    "ExternalReference kräver extern källa, inte Manual."));
        if (string.IsNullOrWhiteSpace(externalId))
            return Result.Failure<ExternalReference>(
                DomainError.Validation("ExternalReference.IdRequired",
                    "External ID är obligatoriskt."));
        if (externalId.Length > 100)
            return Result.Failure<ExternalReference>(
                DomainError.Validation("ExternalReference.IdTooLong",
                    "External ID får vara max 100 tecken."));
        return Result.Success(new ExternalReference(source, externalId.Trim()));
    }
}
```

**`JobAd`-tillägg (nya properties):**

- `ExternalReference? External { get; private set; }` — `null` för Manual, satt för imported ads
- `string? RawPayload { get; private set; }` — JSON-sträng (lagrat som `jsonb` via EF)

**Nya factory + state-transition-metoder:**

```csharp
public static Result<JobAd> Import(
    string? title, Company company, string? description, string? url,
    ExternalReference external, string rawPayload,
    DateTimeOffset publishedAt, DateTimeOffset? expiresAt,
    IDateTimeProvider clock);

public Result UpdateFromSource(
    string? title, string? description, string? url,
    string rawPayload, DateTimeOffset? expiresAt,
    IDateTimeProvider clock);
```

**Befintliga `JobAd.Create` (Manual) + `Archive()` behålls oförändrade.**

**Motivering (CLAUDE.md §5.1 + Evans 2003 + Vernon 2013):**

- Primitive obsession förbjuden — `(Source, ExternalId)` har value-equality, immutability och invariant (non-empty, max 100).
- Aggregate Consistency Boundary bevarad: en JobAd är *en* annons oavsett källa. Splittring i separat `SourcedJobAd`-aggregate avvisad (YAGNI + bryter aggregate-design).

### 5. Dedup: UNIQUE-index + `DbUpdateException`-catch

EF Core-mapping i `JobAdConfiguration`:

```csharp
builder.OwnsOne(j => j.External, ext =>
{
    ext.Property(e => e.Source).HasConversion(...);
    ext.Property(e => e.ExternalId).HasMaxLength(100);
});

builder.HasIndex("ExternalSource", "ExternalExternalId")
    .IsUnique()
    .HasFilter("\"ExternalExternalId\" IS NOT NULL");
```

Upsert-flöde i Application-handler (`UpsertExternalJobAdCommand`):

```csharp
try
{
    db.JobAds.Add(JobAd.Import(...));
    await db.SaveChangesAsync(ct);
}
catch (DbUpdateException) when (IsUniqueConstraintViolation(ex))
{
    var existing = await db.JobAds
        .FirstAsync(j => j.External!.Source == src && j.External.ExternalId == id, ct);
    existing.UpdateFromSource(...);
    await db.SaveChangesAsync(ct);
}
```

**Motivering (Microsoft Learn — Handle concurrency conflicts):**

- UNIQUE-index = source of truth (defense-in-depth).
- TOCTOU-skydd mot parallella Hangfire-workers (manuell admin-trigger + schemalagd).
- CLAUDE.md §3.6 respekterad (ingen raw SQL UPSERT).

### 6. Removal-handling via `JobAd.Archive()`

Vid `JobTechAdRemoval`-event → matchande JobAd hittas via `(Source, ExternalId)` → `JobAd.Archive()` (befintlig metod, idempotent, raisar `JobAdArchivedDomainEvent`).

**Motivering:**

- `DeletedAt` är GDPR-cascade-mekanism (fel semantik för marknad-lifecycle).
- Hard-delete förstör arbetsmarknad-historik (BUILD.md §13 + ADR 0024 audit-retention).
- `Status=Archived` har redan korrekt domain-semantik.

### 7. Ingen caching mellan Hangfire-runs

DB är källan. Hangfire upserter dit. `GET /api/v1/job-ads` (P7) läser DB direkt.

**Motivering (Beck 1999 YAGNI):**

- Redis-cache av endpoint-svar adresserar DoS-scenario som rate-limit (F2-P2) redan löser.
- Cache-invalidation-tax (Fowler "Two hard things") vid removal-events.

### 8. GDPR: PII-fri externtrafik + sync-audit-events

**Inga PII skickas till JobTech.** Search-params (SSYK-kod, region, fritext) är publik metadata. Användardata kopplas aldrig till JobTech-anrop.

**Sync-job-runs auditeras** via nytt domain-event:

```csharp
public sealed record JobAdsSyncedDomainEvent(
    string Source,
    string JobType,           // "stream" | "snapshot"
    int FetchedCount,
    int AddedCount,
    int UpdatedCount,
    int ArchivedCount,
    int ErrorCount,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt) : IDomainEvent;
```

Eventet skrivs till `audit_log` via befintlig pipeline (ADR 0022). Inga PII i events.

**Motivering:** GDPR Art. 30 (record of processing) + CLAUDE.md §5.1 generaliserad princip.

### 9. Leverans-split i tre sub-batches (P8a/P8b/P8c)

| Batch | Scope | Klas-STOPP |
|---|---|---|
| **P8a** | Domain: `ExternalReference` VO, `JobAd.Import`, `JobAd.UpdateFromSource`, `JobAdImportedDomainEvent`. EF: migration för External (owned-type) + UNIQUE-index + RawPayload (jsonb). Tester (domain + arch). | **JA** — schema-migration-review |
| **P8b** | Infrastructure: `IJobTechSearchClient` (Refit) + `IJobTechStreamClient` (typed) + `PlatsbankenJobSource : IJobSource`. `Microsoft.Extensions.Http.Resilience`-config. `JobTechOptions`. Admin-trigger-endpoint `POST /api/v1/admin/job-ads/sync/platsbanken` (synkron snapshot för smoke-test). WireMock-integration-tester. | **JA** — admin-yta + resilience-config-verifiering mot dev |
| **P8c** | Hangfire: `SyncPlatsbankenStreamJob` (10min) + `SyncPlatsbankenSnapshotJob` (02:00). `JobAdsSyncedDomainEvent` audit-wire. Dedup-handling i `UpsertExternalJobAdCommand`. Removal via `Archive()`. E2E-tester. | **JA** — production schedule = deploy-gränsande |

Mellan dessa STOPP: CC kör non-stop med PR-rapport efter varje push per memory `feedback_nonstop_with_pr_reports`.

## Alternativ övervägda

### Resilience (avvisade)

- **A2 — Direkt Polly v8 med custom `ResiliencePipeline`:** mer kod, mindre standardisering. Microsoft-pre-konfigurerat är best-practice-baseline.
- **A3 — `Microsoft.Extensions.Http.Polly`:** deprecated, ingen diskussion.

### Client-shape (avvisade)

- **B1 Refit-only:** sliter sönder type-safety för Stream:s polymorfa event-schema.
- **B2 vanilla-only:** kastar bort produktivitets-vinsten för Search.

### Sync-flöde (avvisade)

- **C1 Snapshot-only först:** uppskjuter Stream-handling → uppskjuter removal-events → stale data i UI.
- **C3 JobSearch-only:** anti-mönster mot JobTechs explicita "bulk discouraged — use Stream".

### Domänmodell (avvisade)

- **D1 strängpar direkt på JobAd:** classic primitive obsession (CLAUDE.md §5.1).
- **D3 separat `SourcedJobAd`-aggregate:** YAGNI + bryter Aggregate Consistency Boundary (Vernon 2013). En annons är *en* annons oavsett källa.

### Dedup (avvisade)

- **E2 check-then-insert i handler:** race-condition mellan parallella Hangfire-workers.
- **E3 raw SQL UPSERT:** bryter CLAUDE.md §3.6 "använd `IAppDbContext` direkt".

### Removal-handling (avvisade)

- **F1 soft-delete via `DeletedAt`:** semantiskt fel (GDPR-cascade-mekanism).
- **F2 hard-delete:** förstör arbetsmarknad-historik.

## Konsekvenser

### Positiva

- **Microsoft-idiomatic .NET 10 stack** — `Microsoft.Extensions.Http.Resilience` är officiellt rekommenderad standard.
- **Type-safe externtrafik** — Refit för JobSearch + diskriminerad union för Stream-events.
- **Idempotent sync** — UNIQUE-index garanterar dedup oavsett race-condition.
- **GDPR-trovärdighet** — Sync-audit-trail + PII-fri externtrafik.
- **Aggregate-cohesion bevarad** — `JobAd` förblir enda aggregate-roten för annonser, oavsett källa.
- **Inkrementell leverans** — tre sub-batches, naturliga Klas-STOPP-punkter.

### Negativa

- **Två klient-stilar i samma BC** (Refit + typed). Acceptabelt — SRP-vinst > stilenhet.
- **`AddStandardResilienceHandler` har mindre granularitet** än hand-rullad Polly-pipeline. Acceptabelt — Microsoft-defaults är best-practice-baseline.
- **Schema-ändring på `job_ads`-tabellen** kräver EF migration (P8a).

### Risker som adresseras

- **JobTech API-downtime** → resilience-pipeline degraderar graciöst (3 retry expo + CB).
- **Rate-limit-överträdelse** → 10-min-cykel är 10× under JobStream:s 1req/min.
- **Cost-blowout via JobTech-loop** → täcks av befintliga F2-P3 Budget Actions (Bedrock-axeln är blowout-vektorn, inte HTTP-anrop).
- **Stream-event-tapp** → daglig Snapshot återställer fullständig state.

## Implementationsstatus

- **P7 (TD-56 paginering):** ✅ Levererad 2026-05-12 (`0fc4b76`).
- **P8a (domain + migration):** Planerad — kräver Klas-GO för denna ADR.
- **P8b (Infrastructure + admin-trigger):** Planerad efter P8a.
- **P8c (Hangfire-scheduling):** Planerad efter P8b.

## Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP), kap. 8 (OCP), kap. 9 (LSP)
- Eric Evans, *Domain-Driven Design* (2003), "Value Objects"
- Vaughn Vernon, *Implementing Domain-Driven Design* (2013), "Effective Aggregate Design"
- Kent Beck, *XP Explained* (1999) — YAGNI, KISS
- Microsoft Learn — *Build resilient HTTP apps: Key development patterns* (`Microsoft.Extensions.Http.Resilience`, .NET 10)
- Microsoft Learn — *Handle concurrency conflicts* (EF Core)
- JobTech Development docs — JobStream 1 req/min rate-limit (web-verifierat 2026-05-12)
- BUILD.md §3.1 (HTTP-stack), §9.1 (JobTech-integration), §16 (job_ads-schema)
- ADR 0005 (auth-gated JobAd-katalog), ADR 0022 (audit-pipeline), ADR 0023 (Hangfire), ADR 0024 (audit-retention)
- CLAUDE.md §3.6 (IAppDbContext direkt), §5.1 (primitive obsession), §9.6 (in-block-fix-default)

## Validation

- Domain.UnitTests: `ExternalReference.Create`-tester (valid/invalid input), `JobAd.Import`-faktorn (idempotency, invariants), `JobAd.UpdateFromSource`-state-transition.
- Architecture.Tests: anti-regression att Domain inte refererar Refit eller HttpClient.
- Application.UnitTests: `UpsertExternalJobAdCommand`-handler (insert + upsert via DbUpdateException).
- Api.IntegrationTests: WireMock-baserade tester för JobTech-API-shape + resilience-fallbacks (transient 503, rate-limit 429).
- E2E (P8c): faktisk dev-deploy + verifiera SyncPlatsbankenStreamJob kör ~6×/timme.

## Out of scope (denna ADR)

- **Search/filter-yta för `GET /api/v1/job-ads`** — separat batch (TD-70) efter P8c när JobTech-search-param-spec är intern erfarenhet.
- **Anonym publik JobAd-katalog** — ADR 0005 kräver separat ADR efter mätning av JobTech-proxy-kostnad och bot-trafik.
- **JobAd "Räkna om Deep match"-funktion** (BUILD.md §10.x) — Fas 4 (AI).
- **EURES + andra `JobSource`-värden** — endast Platsbanken i denna batch (`JobSource.Platsbanken` redan etablerad i domain).

---

## Amendment 2026-05-12 — §8 PII-stripping + retention för raw_payload

**Datum:** 2026-05-12
**Källa:** security-auditor F2-P8a-aggregat-review Sec-Major-1 (post-hoc audit av c5aa089)
**Trigger:** TD-73 lyft som Fas 2 Major (P8c-gating)

### Kontext för amendment

Ursprungs-ADR §8 säger "PII-fri externtrafik" — det stämmer för **utgående** trafik (search-params är publik metadata). Audit identifierade att **inkommande** trafik inte täcktes — JobTech-API kan returnera rekryterar-PII (namn, email, telefon, firmatecknare för enskild firma) i payload-body. `raw_payload` (jsonb på `job_ads`) lagrar oavkortat → JobbPilot blir data controller per GDPR Art. 4(1) så snart payload persisteras.

### Beslut

§8 utvidgas att täcka **både** utgående och inkommande PII-yta. Två nya krav levereras i P8b (innan P8c production schedule):

**1. PII-stripping vid ingest (P8b-leverans)**

`JobTechAdUpsert`-handler (P8b) får en `JobTechPayloadSanitizer` som strippar kända PII-keys före persistering. Implementation: allowlist över JobTech-schema-keys vi vill bevara, eller blocklist över kända PII-keys (`employer.contact_email`, `employer.contact_name`, etc.). Allowlist-approach föredragen (Saltzer/Schroeder default-deny).

Pseudo-kod:
```csharp
public sealed class JobTechPayloadSanitizer
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "id", "headline", "description", "occupation", "workplace_address",
        "employment_type", "duration", "working_hours_type", "publication_date",
        "last_publication_date", "removed", "removed_date",
        // workplace_address.municipality OK, employer.contact_email INTE OK
        // Slut-lista designas under P8b.
    };

    public string SanitizeForStorage(string rawJson) =>
        // Iterera jsonb-nodes, behåll bara AllowedKeys, returnera serialized.
}
```

**2. Retention-policy för raw_payload (P8c-leverans eller separat batch)**

`raw_payload` null:as via Hangfire-job 30 dagar efter `job_ads.published_at`. Job-spec:
- `PurgeStaleRawPayloadsJob` (Hangfire daglig cron 03:00)
- `UPDATE job_ads SET raw_payload = NULL WHERE published_at < now() - interval '30 days' AND raw_payload IS NOT NULL`
- Audit-event `RawPayloadPurgedDomainEvent(count, cutoff)` skrivs till `audit_log`

30-dagars-fönster motiverat: debug/replay-värdet är högst under första veckorna efter publish; därefter är annonsen historisk. Konfigurerbar via `IOptions<JobTechSyncOptions>.RawPayloadRetentionDays`.

**3. Processing-register-entry**

JobTech som PII-datakälla läggs till i `docs/runbooks/gdpr-processing-register.md` (skapas om saknas) per GDPR Art. 30: datakategori (publicerad annons-metadata + rekryterar-kontaktinfo), syfte (matchning + visning), rättslig grund (legitimt intresse — JobTech har redan publicerat), lagringstid (30 dagar för raw_payload, indefinitively för sanitized fields).

**4. Right-to-erasure-stöd**

Om en rekryterare begär radering — implementeras som del av `DeleteAccountCommand`-mönstret (ADR 0024 cascade) men för "rekryterar-PII" specifikt: jsonb-query mot `raw_payload` med rekryterar-identifier, sanitera matchande rader. Detaljer designas i TD-73-batch.

### Konsekvenser av amendment

- **PII-stripping minskar debug-värdet av raw_payload** — acceptabelt eftersom rekryterar-namn/email sällan är debug-relevant; SSYK-kod, workplace, headline är primära debug-fält och bevarade i allowlist.
- **Sanitizer-yta blir P8b-blocking** — P8c production-schedule gating på att sanitizer + retention-job är levererade och verifierade.

### Krav för stängning av TD-73

- [x] `JobTechPayloadSanitizer` implementerad + unit-tester (F2-P8b 2026-05-13, commit `8c09191`)
- [x] AllowedKeys-lista verifierad mot JobTech-API-spec (web-search 2026-05-12 + JobTech-docs)
- [ ] `PurgeStaleRawPayloadsJob` Hangfire-job implementerad + integration-test (kvar för P8c)
- [ ] `RawPayloadPurgedDomainEvent` audit-wire (kvar för P8c)
- [x] `docs/runbooks/gdpr-processing-register.md` skapad eller utökad med JobTech-entry (F2-P8b 2026-05-13)
- [ ] ADR 0024 cross-ref för right-to-erasure-cascade till raw_payload (kvar för P8c eller separat batch)
- [ ] Security-auditor verify-pass innan P8c-deploy

---

## Amendment 2026-05-13 — JobStream v2 path-migration

**Datum:** 2026-05-13
**Källa:** Klas direkt observation av JobStream Swagger UI (`jobstream.api.jobtechdev.se` visar version 2.1.1)
**Trigger:** F2-P8b post-commit verifiering — Klas såg att v1-endpoints är deprecated i swagger

### Kontext för amendment

Original-ADR §2 + §3 antog v1-endpoints (`/snapshot`, `/stream?date=ISO8601`)
baserat på web-search 2026-05-12. Faktisk JobStream-deployment är på v2 sedan en
icke-publicerad migration. v1-paths är genomstrukna (deprecated) i swagger.

### Beslut

JobTechStreamClient riktar mot **v2-endpoints** istället för v1:

| v1 (deprecated) | v2 (aktuell) |
|---|---|
| `GET /snapshot` | `GET /v2/snapshot` |
| `GET /stream?date=YYYY-MM-DDTHH:MM:SSZ` | `GET /v2/stream?updated-after=YYYY-MM-DDTHH:MM:SS` |

**Skillnader att notera:**

1. **Query-param-namn:** `date` → `updated-after`
2. **Datum-format:** swagger anger `YYYY-MM-DDTHH:MM:SS` utan timezone-suffix.
   UTC implicit. Min impl dropper `Z`-suffixet jämfört med v1.
3. **Extra valbara v2-query-params:** `updated-before` (default "nu"),
   `occupation-concept-id[]` (yrkeskod-filter), `location-concept-id[]`
   (geo-filter). Inte använda i F2-P8b — kan exponeras via TD-70 search/filter
   när tillämpligt.
4. **Response-format:** v2 stöder både `application/json` (JSON-array, samma
   shape som v1) och `application/jsonl` (NDJSON). Min impl deserialiserar
   som JSON-array via `JsonSerializer.DeserializeAsync<List<JobTechHit>>` +
   `DeserializeAsyncEnumerable<JobTechHit>` — defaultar till
   `application/json`, vilket fungerar med v2.

**Auth:** v2-swagger nämner ingen api-key. Min impl skickar `api-key`-header
om värdet finns i `JobTechOptions.ApiKey`; utelämnar headern om tomt. Säker
default oavsett om JobTech kräver auth eller är öppen.

### Implementations-trail

- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechStreamClient.cs`
- `tests/JobbPilot.Api.IntegrationTests/JobAds/JobTechStreamResilienceTests.cs` (WireMock-stubs uppdaterade)

### Operativa konsekvenser

- F2-P8b-deploy mot `v0.2.2-dev` kan ske trots osäkerhet om api-key-kanal
  (`apirequest.jobtechdev.se` ger DNS-fel 2026-05-13). v2-endpoints är publika
  i swagger utan dokumenterad auth.
- TD-70 search/filter-utbyggnad (Fas 2 senare) kan utnyttja v2:s
  `occupation-concept-id` + `location-concept-id` direkt på Stream-endpoint
  istället för att bygga ovanpå JobSearch.
