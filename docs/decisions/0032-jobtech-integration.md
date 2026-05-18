# ADR 0032 — JobTech-integration: resilience-stack, dedup-strategi, sync-flöde

**Datum:** 2026-05-12
**Status:** Accepted 2026-05-12 (Klas-GO mottaget)
**Kontext:** F2-P8 JobTech/Platsbanken-integration (BUILD.md §9.1)
**Beslutsfattare:** senior-cto-advisor 2026-05-12 (decision) + Klas Olsson (godkänd 2026-05-12)
**Relaterad:** ADR 0005 (go-to-market, JobAd auth-gated), ADR 0022 (audit log-pipeline), ADR 0024 (audit retention), ADR 0023 (Hangfire-infrastruktur), BUILD.md §3.1 (HTTP-stack), §9.1 (JobTech-integration), §16 (job_ads-schema), ADR 0049 (Proposed — TD-13 PII-fält-kryptering: Beslut 3 motiverar `raw_payload`-exklusion ur envelope-scopet delvis på denna ADR:s §8 sanitizer-allowlist + 30d-purge), TD-56 (stängd P7), TD-70 (search/filter, kommande)

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

---

## Amendment 2026-05-13 — §8 punkt 4 implementeras: audit-wire α via ADR 0035 + right-to-erasure Email-only

**Datum:** 2026-05-13
**Källa:** TD-73 prod-gating-batch (CTO-rond 2026-05-13 punkt 5 + 7)
**Trigger:** prod-gating innan v0.2-prod-tag

### Kontext för amendment

§8 amendment 2026-05-12 punkt 4 ("Right-to-erasure-stöd") och den parallella audit-wire-frågan (`JobAdsSyncedDomainEvent`) deferrades till TD-73 prod-gating-batch. Denna amendment specificerar implementations-mekaniken efter senior-cto-advisor-decision 2026-05-13.

### Beslut

#### Audit-wire α — ersätter `JobAdsSyncedDomainEvent`-spec med `ISystemEventAuditor`

Original §8 specade ett `JobAdsSyncedDomainEvent` som skulle skrivas till `audit_log` via befintlig pipeline (ADR 0022). Den specifikationen var ofullständig: jobben är inte `IRequest`/`ICommand` och passerar inte `AuditBehavior`. Domain-event-dispatcher saknas i JobbPilot (ADR 0022 alt C-deferral).

**Ny mekanism per [ADR 0035](./0035-system-event-audit-pipeline.md):** `ISystemEventAuditor`-port (Application/Common/Auditing) konsumeras direkt av jobben i finally-block efter completion. `SystemAuditEvent.JobAdsSynced` (counts + tidsstämplar) och `SystemAuditEvent.RawPayloadPurged` (rowsAffected + cutoff + retentionDays) serialiseras till `audit_log.payload` jsonb-kolumnen.

`audit_log.payload`-kolumnen aktiveras för Fas 2 system-events via ny EF-migration. ADR 0022:s Fas 4-deferral av `payload` gällde command-audit (CV-text, PII-saner-behov) — system-event-payload har ingen PII, bara counts. Tidig aktivering har ingen GDPR-impact.

#### Right-to-erasure — Email-only nu, Name som ny TD

**Implementerad mekanism:**

- `RedactRecruiterPiiCommand(Identifier, RecruiterIdentifierType)` i Application/JobAds/Commands/RedactRecruiterPii.
- `IAdminRequest` + `IAuditableCommand<Result<int>>` (audit-rad `Admin.RecruiterPiiRedacted` per request, payload `{ identifier, type, rowsAffected }`).
- Handler söker matchande JobAds via `EF.Functions.JsonContains` (säkrare än `.Contains()` mot EF Core 10 Issue #3745) och null:ar `raw_payload` via `ExecuteUpdateAsync(SetProperty(j => j.RawPayload, _ => null))`.
- En aggregerad audit-rad per request (CTO Q3=B, ADR 0024 D4-precedens — "användaren begärde *en* handling").
- Admin-endpoint `POST /api/v1/admin/job-ads/redact-recruiter-pii` med `AuthorizationPolicies.Admin`.

**Total null-out vs surgical jsonb_set:** CTO Q2 = total null-out. Skäl: GDPR Art. 5(1)(c) data-minimisation > debug-värde. 30d-retention via `PurgeStaleRawPayloadsJob` null:ar ändå hela `raw_payload` efter 30 dagar — surgical redaction räddar non-PII i max 30 dagar för en handfull rader. KISS + Saltzer/Schroeder default-deny.

**Name-baserad sökning defererad till TD-75** (ny TD allokerad 2026-05-13): Name-matching kräver multi-path jsonb-search + ev. full-text på `description.text`. YAGNI tills faktisk request finns. Email är primär rekryterar-identifier i JobTech-payloads. `RecruiterIdentifierType.Name` returnerar `Result.Failure(DomainError.Validation("RedactRecruiterPii.NameNotSupportedYet", ...))` med dokumenterad trigger i `docs/runbooks/recruiter-pii-erasure.md`.

**GIN-index på raw_payload defererad till TD-76** (ny TD): seq-scan på ~5–10k rader är acceptabel latens för admin one-off (sekunder). GIN-index har reell write-overhead på stream-cron (~80k operations/dygn). YAGNI tills faktisk latens-trigger eller volym-skifte.

### Krav för stängning av TD-73

- [x] `JobTechPayloadSanitizer` implementerad + unit-tester (F2-P8b 2026-05-13, commit `8c09191`)
- [x] AllowedKeys-lista verifierad mot JobTech-API-spec (web-search 2026-05-12 + JobTech-docs)
- [x] `PurgeStaleRawPayloadsJob` Hangfire-job implementerad + integration-test (F2-P8c 2026-05-13, commit `81dfab6`)
- [x] `RawPayloadPurgedDomainEvent` audit-wire (TD-73 prod-batch 2026-05-13 — ersatt av `SystemAuditEvent.RawPayloadPurged` per ADR 0035)
- [x] `docs/runbooks/gdpr-processing-register.md` skapad eller utökad med JobTech-entry (F2-P8b 2026-05-13)
- [x] ADR 0024 cross-ref för right-to-erasure-cascade till raw_payload (TD-73 prod-batch 2026-05-13)
- [x] Security-auditor verify-pass innan v0.2-prod-tag (TD-73 prod-batch 2026-05-13)

### Operativa konsekvenser

- v0.2-prod-tag är inte längre gated på TD-73. PurgeStaleRawPayloadsJob + audit-wire + Email-only-erasure tillsammans täcker GDPR Art. 5/17/30 för rekryterar-PII i raw_payload.
- Name-baserad erasure hanteras manuellt via runbook (`docs/runbooks/recruiter-pii-erasure.md`) tills TD-75 levereras.

### Referenser

- [ADR 0035](./0035-system-event-audit-pipeline.md) — System-event audit-pipeline (`ISystemEventAuditor`)
- [ADR 0024 §"Cross-ref-amendment 2026-05-13"](./0024-audit-retention-and-art17-cascade.md) — right-to-erasure-cascade-completion
- `docs/runbooks/recruiter-pii-erasure.md` — operativ procedur
- `docs/runbooks/gdpr-processing-register.md` — JobTech-entry
- senior-cto-advisor 2026-05-13 (TD-73-batch, 13 beslut entydigt mot principer)

---

## Amendment 2026-05-16 — §5 clarification: batch-orchestrator MÅSTE köra child-scope per item

**Datum:** 2026-05-16
**Källa:** Root-cause-utredning F2 jobb-ingestion-gap (~5k av ~47k annonser)
**Trigger:** CloudWatch-evidens `/aws/ecs/jobbpilot-dev/worker` — `SyncPlatsbankenSnapshotJob` 60 starts / 0 completes över 4 dygn
**Beslutsfattare:** senior-cto-advisor 2026-05-16 (Variant B, entydigt mot principer) + Klas Olsson (godkänd 2026-05-16)

### Kontext

§5:s dedup-flöde (optimistisk INSERT + `DbUpdateException`-catch på 23505 +
reload + `UpdateFromSource`) är korrekt **men förutsätter implicit
single-command-scope per item**. `UpsertExternalJobAdCommandHandler`s catch
isolerar bara om `SaveChanges` opererar över *en* entitet.

`SyncPlatsbankenSnapshotJob` körde hela ~47k-snapshot-loopen i EN DI-scope →
ett scoped `IAppDbContext` vars EF change-tracker ackumulerade över alla items.
`UnitOfWorkBehavior` kör dessutom en andra `SaveChangesAsync` efter varje
`mediator.Send`, utanför handlerns try/catch, över hela den ackumulerade grafen.
När snapshot ⊇ det stream redan infogat (tusentals dubbletter) gav första
kollisionen en 23505 som per-command-catchen inte kunde isolera vid batch-skala
→ uncaught `DbUpdateException` → `Hangfire.AutomaticRetry`-loop. Korpus
fastnade på stream-ackumulerade ~5k.

### Clarification (förtydligar §5, ändrar inte dedup-mekaniken)

§5:s upsert-flöde förutsätter **single-command-scope per item** — handlerns
23505-catch isolerar endast om `SaveChanges` opererar över *en* entitet.
Batch-orchestratorer (snapshot, ~47k items) MÅSTE därför köra **child-scope
per item** via `IServiceScopeFactory.CreateAsyncScope()` (eget
`IAppDbContext` → change-tracker lever och dör med ett item). Annars bryter
ackumulerad EF change-tracker + `UnitOfWorkBehavior`-SaveChanges
per-command-isoleringen → uncaught 23505. Verifierat: 60 starts / 0 completes
på dev innan fixen (commit `347b238` 2026-05-16).

UNIQUE-index, catch, reload, `Detach`, `IDbExceptionInspector` — allt
oförändrat. Detta är "få §5 att faktiskt fungera vid batch-skala", inte ny
dedup-strategi.

### Implementations-trail

- `src/JobbPilot.Application/JobAds/Jobs/SyncPlatsbanken/SyncPlatsbankenSnapshotJob.cs` (child-scope per item)
- `src/JobbPilot.Application/JobAds/Abstractions/IJobSource.cs` + `JobTechStreamClient` (IAsyncEnumerable-streaming, ~300 MB-OOM-defekt — del a)
- `src/JobbPilot.Infrastructure/DependencyInjection.cs` (`_streamRateLimiter` QueueLimit 0→2 — del b)
- Regressionstest `RunAsync_WhenSnapshotContainsDuplicates_IsolatesPerItemScope_AndCompletes`
- Commits `347b238` + `70a7c54` (2026-05-16)

### Referenser

- Martin Fowler, *PoEAA* (2002) — "Unit of Work" (UoW-gräns = en logiskt atomär förändring)
- Robert C. Martin, *Clean Architecture* (2017) kap. 7 (SRP)
- Microsoft Learn — *Handle concurrency conflicts* (EF Core)
- senior-cto-advisor 2026-05-16 (Variant B, root-cause-fix)

---

## Amendment 2026-05-16 — §9 admin-trigger avvecklad (X4)

**Datum:** 2026-05-16
**Källa:** Root-cause-fix F2 jobb-ingestion, Commit 3-design
**Trigger:** On-disk-verifiering — Hangfire refereras enbart i Worker (ej Infrastructure/Api); admin-endpointen körde snapshot synkront i HTTP-requesten
**Beslutsfattare:** senior-cto-advisor 2026-05-16 (X4, entydigt mot principer) + Klas Olsson (godkänd 2026-05-16, medvetet val mot X2)

### Kontext

§9 (P8b) specade admin-endpoint `POST /api/v1/admin/job-ads/sync/platsbanken`
som **synkron snapshot-import för smoke-test** innan Hangfire-schedulering.
Efter Commit 1 (root-cause-fix 2026-05-16) kör recurring-jobbet
`sync-platsbanken-snapshot` korrekt i Worker. Den synkrona endpointen kvarstod
som ALB-timeout-fälla (~47k upserts = tiotals min vs ALB ~60s idle-timeout).

Att göra endpointen async skulle kräva att Hangfire-klientyta sprids till
Api/Infrastructure (idag Hangfire-fritt — enbart Worker har Hangfire) för en
funktion som (a) recurring-schedule + (b) Hangfire-dashboardens "Trigger now"
redan täcker. YAGNI (Beck 1999) + minimera dependency-coupling (Martin 2017
kap. 14).

### Beslut (X4 — avveckla)

`POST /api/v1/admin/job-ads/sync/platsbanken` returnerar **410 Gone** med
svensk ProblemDetails som pekar operatören till Hangfire-dashboardens
recurring-jobb `sync-platsbanken-snapshot` ("Trigger now"). Admin-auth krävs
fortfarande (gruppen `RequireAuthorization(AuthorizationPolicies.Admin)`).
Route behålls (i stället för borttagen) så operatörer med äldre runbook får
tydlig anvisning.

`SyncPlatsbankenSnapshotCommand` + `-CommandHandler` + `-Result` **borttagna**
(dead code efter X4 — Worker konsumerar `SyncPlatsbankenSnapshotJob` direkt via
`SyncPlatsbankenSnapshotWorker`-wrappern, inte via Mediator-command). CTO:s
ursprungliga "behåll command/handler" vilade på felaktig premiss att Worker
konsumerar command:t; korrigerat → no-dead-code-default (CLAUDE.md §5) gäller.

Ny `SyncPlatsbankenSnapshotWorker` (Worker.Hosting,
`[DisableConcurrentExecution(3600)]`) — analog med stream-wrappern. Snapshot
tar tiotals min efter streaming-fixen; utan overlap-skydd kan
Hangfire-`AutomaticRetry` återskapa loop-symptomen. Recurring-jobb-id
oförändrat → dashboard-trigger fungerar.

### Avvisade (X1/X2/X3)

- **X1** (Hangfire-klient i Api + impl i Infrastructure): sprider Hangfire.Core
  till Hangfire-fritt Infrastructure-lager (§9.2-dep) för obehövd kapacitet.
- **X2** (port i Application + Hangfire-klient + impl i Api composition-root):
  principiellt korrekt OM async-endpoint behövs — men ingen konsument behöver
  den efter Commit 1 + dashboard-backfill-valet (YAGNI). Klas-övervägd, avvisad.
- **X3** (in-process IHostedService/channel utan Hangfire): parallellt
  jobbsystem, DRY-brott, överlever inte pod-restart.

### Konsekvenser

- Förlorad programmatisk HTTP-snapshot-trigger. Acceptabelt —
  recurring-schedule täcker. Framtida API-trigger (om automation kräver) lyfts
  som egen TD i rätt fas med faktisk konsument (X2 = färdig ritning då).
- Initial-backfill efter fix-deploy sker via recurring-cron (02:00 UTC) eller
  AWS-operatörsåtgärd (Klas-operativt, deploy-gated).

### Korrigering 2026-05-16 — ingen Hangfire-dashboard exponerad

Detta amendment (och CTO-resonemanget bakom X4) antog att en Hangfire-dashboard
är driftsatt som operatörens ad-hoc-trigger-väg. **On-disk-verifiering: Worker
är headless — inget `UseHangfireDashboard`/`MapHangfireDashboard` finns.** X4-
beslutet (avveckla endpointen) står oförändrat och stärks (ingen dashboard
heller → ännu mindre skäl att bygga async-HTTP-yta). Operativ konsekvens:
manuell ad-hoc-snapshot kräver AWS-operatörsåtgärd (ECS exec eller Hangfire-
radinsert); steady-state täcks av recurring-cron 02:00 UTC. 410-copy +
endpoint-doc korrigerade att inte hänvisa till en icke-exponerad dashboard.
Saknad operatörs-yta (jobb-status/retry/manuell trigger) lyft som **TD-83**.

### Implementations-trail

- `src/JobbPilot.Api/Endpoints/AdminJobAdsEndpoints.cs` (410)
- `src/JobbPilot.Worker/Hosting/SyncPlatsbankenSnapshotWorker.cs` (ny) + `RecurringJobRegistrar.cs` + `Program.cs`
- Borttagna: `SyncPlatsbankenSnapshotCommand/-CommandHandler/-Result` + handler-test + oanvänd `StubJobSource` (Api-int)
- `tests/JobbPilot.Architecture.Tests/P8cJobsLayerTests.cs` (`SyncPlatsbankenSnapshotWorker_resides_in_Worker_assembly`)
- `AdminSyncPlatsbankenTests` (401/403 behållna, funktionstester → 410-assertion)

### Referenser

- Kent Beck, *XP Explained* (1999) — YAGNI
- Robert C. Martin, *Clean Architecture* (2017) kap. 14 (Component Coupling)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY
- Humble/Farley, *Continuous Delivery* (2010) — operability
- CLAUDE.md §5 (no dead code), §9.2 (dep-disciplin), §9.6 p.5, §10.3 (svensk copy), §13
- senior-cto-advisor 2026-05-16 (X4) + Klas-GO 2026-05-16

---

## Amendment 2026-05-16 — snapshot-trunkerings-resiliens (hybrid; A2 förkastad efter web-verify)

**Datum:** 2026-05-16
**Källa:** Batch 0 root-cause-discovery (CloudWatch `/aws/ecs/jobbpilot-dev/worker`, dev `v0.2.8-dev`, 48h) + JobTech GettingStarted-doc web-verify 2026-05-16
**Trigger:** `SyncPlatsbankenSnapshotJob` 60 starts / 0 completes — `/v2/snapshot` (>364 MB singel-GET) termineras icke-deterministiskt mid-stream → ofångad `System.Text.Json.JsonException` ("reached end of data") → `Hangfire.AutomaticRetry`-storm; korpus fast på stream-ackumulerade ~5 380 (mål ~40k+)
**Beslutsfattare:** senior-cto-advisor 2026-05-16 (agentId `ad8564aafc29be5a0`, hybrid efter web-verify; A2-omvägning + MA 1.1/2.1/3.1/4.1) + dotnet-architect 2026-05-16 (agentId `a6a02546f13bd5236`, design-skiss INNAN kod) + Klas Olsson (godkänd 2026-05-16)
**Status:** Accepted 2026-05-16 (Klas-GO 2026-05-16; amendment-text CC-draftad från CTO/architect-underlaget — medvetet Klas-val mot CLAUDE.md §9.4 verbatim-text-källa, dokumenterat här)

### Kontext för amendment

Root-cause-fixen 2026-05-16 (§5-clarification, child-scope per item, commits `347b238`/`70a7c54`) adresserade 23505-ackumulering men **inte** payload-trunkering. Batch 0-discovery (CloudWatch Logs Insights, verbatim):

| START (UTC) | TRUNC | Delta | BytePos |
|---|---|---|---|
| 2026-05-15 10:57:56 | 10:59:22 | 1m27s | ~21 MB |
| 2026-05-16 02:07:05 | 02:09:12 | 2m07s | ~41 MB |
| 2026-05-16 02:11:51 | 02:17:41 | 5m49s | ~151 MB |
| 2026-05-16 03:04:02 | 03:11:24 | 7m22s | ~364 MB |
| 2026-05-16 03:53:45 | 03:59:00 | 5m16s | ~144 MB |

Ingen `[5402] KLART` förekommer. Hypoteser **motbevisade** av evidensen: `HttpClient.Timeout=5min` (trunkering icke-deterministiskt 87–442 s, ingen tidsvägg vid 300 s), `MaxResponseContentBufferSize=500MB` (364 MB < cap; `ResponseHeadersRead`+`ReadAsStreamAsync`+`DeserializeAsyncEnumerable` bypassar buffer-cap — streaming-fixen v0.2.6-dev fungerar), Polly-pipeline (`AddResilienceHandler("jobstream")` completar vid headers-read; body-trunkering når den aldrig). **Verifierad rotorsak:** upstream/mellanled-anslutningen termineras icke-deterministiskt mitt i en >364 MB singel-GET JSON-array — partiell transfer, ingen resume.

Web-verify (`raw.githubusercontent.com/Jobtechdev-content/Jobstream-content/develop/GettingStartedJobStreamSE.md`, hämtad 2026-05-16): `/snapshot` ~300 MB+ parameterlös singel-GET utan paginering/resume/jsonl-negotiation; rate-limit "one request per minute" (granularitet per api-key/IP/global **ospecificerad**); JobTechs **egen dokumenterade full-korpus-pattern är `/snapshot`-först + repeterade `/stream`-anrop** — ingen dokumenterad stream-only-backfill; stream-retention-djup ospecificerat.

### Beslut

Ursprunglig sessionsinriktning **A2** (eliminera snapshot, bygg korpus stream-only-katch-up) **förkastas** — premissen rev av web-verify (ingen dokumenterad stream-only-backfill; stream-retention-djup okänt → att bygga cold-start på overifierat externt beteende bryter CLAUDE.md §9.5 + Humble/Farley operability). Ersätts av **hybrid**:

1. **§3 förtydligas (ej supersederas):** primär bootstrap förblir `/v2/snapshot` (JobTechs dokumenterade mönster); stream `*/10` + snapshot `02:00` behålls **oförändrat mönster**. Hybrid bevarar §3.
2. **Snapshot-läsningen görs trunkerings-tålig (MA 3.1 Variant A):** `PlatsbankenJobSource.FetchSnapshotAsync` får enumeration-boundary-catch av `JsonException`/`IOException`/`HttpRequestException` — **fysiskt skild** från per-item-upsert-catchen i `SyncPlatsbankenSnapshotJob` (§5-clarification: ofångad enumeration var hela storm-mekanismen — slå aldrig ihop). Bounded retry `MaxSnapshotAttempts=3` (färsk GET per försök; re-yieldad prefix idempotent via UNIQUE-index per §5). Uttömd retry → graceful `yield break` (ingen ofångad exception → ingen `Hangfire.AutomaticRetry`-storm). LoggerMessage EventId 5004/5005.
3. **MA 1.1 = stateless katch-up:** ingen cursor-tabell. Idempotens via UNIQUE-index gör re-walk korrekt (§5 + Fowler 2002 "Idempotent Receiver"); konsistent med stream-jobbets befintliga overlap-window-mönster (§3).
4. **MA 2.1 = behåll snapshot-job/wrapper/recurring-id `sync-platsbanken-snapshot`, ändra bara internals.** Namnet "snapshot" förblir sant under hybrid. `JobType:"snapshot"`-audit-literal + ADR 0036 metric-filter + §9 X4 410-text **oförändrade**.
5. **MA 4.1 = delad process-wide `_streamRateLimiter`** (web: rate-limit-granularitet ospecificerad → separat client-side-limiter ger 429-storm). Ingen DI-ändring.
6. **Drift = recurring inkrementell konvergens, ingen `DisableConcurrentExecution`-timeout-höjning** (Klas-GO 2026-05-16). Korpus konvergerar mot ~40k+ över flera dygn via dagliga best-effort-snapshot-runs (varje run upp till 3 attempts; icke-deterministisk trunkering ⟹ olika prefix-längd per run; unionen växer) + stream `*/10`. 3600 s loop-skydd bevaras orört (höjning vore att försvaga skyddet mot exakt root-cause-symptomet).

### Konvergens-risk (medvetet accepterad)

Om `/v2/snapshot` returnerar items i **stabil ordning** kan bounded retry inom samma run re-läsa samma prefix. Konvergens vilar därför på att trunkerings-byte-positionen varierar **mellan dygn** (empiriskt: 21–364 MB observerat; full >364 MB → vissa runs levererar majoriteten) + att stream `*/10` löpande adderar nya annonser. Konvergens till ~40k+ tar **dygn, ej timmar** (Klas-godkänt 2026-05-16: korrekthet > tempo, CLAUDE.md §9.6). STOPP 3-verifiering (cron-grön) mäter därför `[5402] KLART`/graceful-end + korpus-**tillväxt över tid**, ej omedelbar ~40k. Om konvergens uteblir över rimligt antal dygn: framtida trigger för windowed-stream-katch-up (`updated-after`+`updated-before-date`, architect-skiss bevarad) — dokumenteras som skala-trigger, ej TD (CLAUDE.md §9.6/§9.7).

### Avvisade

- **A2 (stream-only-katch-up, snapshot eliminerad):** premiss rev av web-verify; ingen dokumenterad stream-only-backfill, stream-retention-djup okänt (§9.5).
- **MA 1.1 Variant B (cursor-tabell):** ny migration + bryter "ingen cursor"-mönstret (§3); idempotens gör re-walk korrekt → YAGNI (Beck 1999).
- **MA 2.1 Variant B/C (döp om/eliminera snapshot-job):** blast-radius (audit-literal, ADR 0036-metric, recurring-id-byte) utan funktionsvinst när namnet är sant under hybrid.
- **MA 3.1 Variant B (förlita på Hangfire-retry):** stall-risk vid konsekvent trunkerande fönster, re-walkar allt. **Variant C (retry i `JobTechStreamClient`):** bryter §2:s explicit motiverade wire-only-SRP.
- **MA 4.1 Variant B (separat limiter):** 429-storm under konservativt global/IP-antagande (§9.5). **Variant C (sekvensera):** onödigt koordinations-state; delad limiter sekvenserar redan.
- **Drift: timeout-höjning / one-shot-bootstrap:** försvagar loop-skyddet resp. special-infrastruktur (Ford/Parsons/Kua 2017).

### Implementations-trail

- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/PlatsbankenJobSource.cs` (resilient enumeration, `MaxSnapshotAttempts=3`, EventId 5004/5005)
- `tests/JobbPilot.Api.IntegrationTests/JobAds/JobTechStreamResilienceTests.cs` (regressionstest `FetchSnapshotAsync_WhenResponseTruncatedMidStream_DoesNotThrowUncaught_YieldsParsedPrefix`)
- Oförändrade (verifierade): `IJobSource`/`IJobTechStreamClient`-kontrakt (§2 ACL bevarad), `SyncPlatsbankenSnapshotJob` per-item-catch (§5), `RecurringJobRegistrar`/Worker-wrappers, `_streamRateLimiter` (§ DI)
- Svit grön: Domain 293 / Application 398 / Architecture 51 / Api.Integration 269 (+1) / Worker 26 / Migrate 6 = 1043; build 0/0; code-reviewer GO 0 Block/0 Major

### Referenser

- senior-cto-advisor 2026-05-16 (`ad8564aafc29be5a0`, hybrid + MA-triage) + dotnet-architect 2026-05-16 (`a6a02546f13bd5236`) + code-reviewer 2026-05-16 (`ab3fefc83d7e4f22a`, GO)
- [JobTech GettingStartedJobStreamSE.md](https://raw.githubusercontent.com/Jobtechdev-content/Jobstream-content/develop/GettingStartedJobStreamSE.md) — hämtad 2026-05-16 (snapshot-först-pattern, 1 req/min, retention ospecificerad)
- Fowler, *PoEAA* (2002) — "Idempotent Receiver"; Beck, *XP* (1999) — YAGNI; Humble/Farley, *Continuous Delivery* (2010) — operability; Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017)
- CLAUDE.md §9.4 (verbatim-text-källa — medvetet Klas-override), §9.5 (verifiera externa fakta), §9.6 (in-block vs TD/skala-trigger)
- ADR 0032 §2 (wire-only-SRP), §3 (overlap-window — förtydligad), §5 (dedup + 2026-05-16-clarification), §9 X4 (410 — oförändrad); ADR 0036 (ops-alarms)
