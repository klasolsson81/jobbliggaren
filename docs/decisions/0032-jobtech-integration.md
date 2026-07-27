# ADR 0032 — JobTech-integration: resilience-stack, dedup-strategi, sync-flöde

**Datum:** 2026-05-12
**Status:** Accepted 2026-05-12 (Klas-GO mottaget)
**Kontext:** F2-P8 JobTech/Platsbanken-integration (BUILD.md §9.1)
**Beslutsfattare:** senior-cto-advisor 2026-05-12 (decision) + Klas Olsson (godkänd 2026-05-12)
**Relaterad:** JobAd-katalog auth-gated (Fas 2), ADR 0022 (audit log-pipeline), ADR 0024 (audit retention), ADR 0023 (Hangfire-infrastruktur), BUILD.md §3.1 (HTTP-stack), §9.1 (JobTech-integration), §16 (job_ads-schema), ADR 0049 (Accepted — TD-13 PII-fält-kryptering: Beslut 3 motiverade `raw_payload`-exklusion ur envelope-scopet delvis på denna ADR:s §8 sanitizer-allowlist + "30d-purge" — **båda grunderna är falsifierade**, se ADR 0049 §B och Amendment 2026-07-26 §C2 för regeln), TD-56 (stängd P7), TD-70 (search/filter, kommande)

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

> **SUPERSEDED IN PART, 2026-07-26 (#845):** §8's retention statements below (`raw_payload` nulled
> *"30 dagar efter `published_at`"*, and the Art. 30 line's *"30 dagar för raw_payload"*) are false,
> and so is the *"30 days after the ad leaves the feed"* wording that A2/B4(a) proposed to replace
> them with. The rule's home is **ADR 0032 Amendment 2026-07-26 §C2**; the arithmetic behind both
> falsehoods is worked once in **§C1**. The three drifts diagnosed below stand.
>
> ⚠ **Superseded by the 2026-07-13 amendment (#842) — see below.** The heading reads as a blanket PII
> guarantee. It holds for **outbound** traffic only. The **inbound** surface — recruiter contact details
> in the ad body, stored verbatim in `job_ads.description` — is real, was never mitigated, and is
> FTS-searchable by any user today.

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

> ⚠ **Superseded by the 2026-07-13 amendment (#842) — see below.** The sanitizer strips the **field, not
> the address**: it is a key-name filter that never inspects a value, and it deliberately retains every
> free-text key (`description`, `text`, `company_information`, `needs`, `requirements`,
> `salary_description`).

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

> ⚠ **Superseded by the 2026-07-12 (#824, A1/A2) and 2026-07-13 (#842) amendments — see below.** Three
> drifts: the 02:00 backfill restores the payload nightly, so the real rule is "30 days after the ad
> **leaves the feed**"; the cron is `30 4 * * *`, not 03:00; and the job never touches
> `job_ads.description`, which is where the recruiter PII actually is.
>
> **SUPERSEDED IN PART, 2026-07-26 (#845):** the replacement rule stated immediately above — *"30 days
> after the ad leaves the feed"* — is **also false**, in both directions; the arithmetic is worked once
> in **Amendment 2026-07-26 §C1**. The rule's home is **§C2**. This banner's other two drifts (the cron
> and the untouched `description`) stand.

`raw_payload` null:as via Hangfire-job 30 dagar efter `job_ads.published_at`. Job-spec:
- `PurgeStaleRawPayloadsJob` (Hangfire daglig cron 03:00)
- `UPDATE job_ads SET raw_payload = NULL WHERE published_at < now() - interval '30 days' AND raw_payload IS NOT NULL`
- Audit-event `RawPayloadPurgedDomainEvent(count, cutoff)` skrivs till `audit_log`

30-dagars-fönster motiverat: debug/replay-värdet är högst under första veckorna efter publish; därefter är annonsen historisk. Konfigurerbar via `IOptions<JobTechSyncOptions>.RawPayloadRetentionDays`.

**3. Processing-register-entry**

JobTech som PII-datakälla läggs till i `docs/runbooks/gdpr-processing-register.md` (skapas om saknas) per GDPR Art. 30: datakategori (publicerad annons-metadata + rekryterar-kontaktinfo), syfte (matchning + visning), rättslig grund (legitimt intresse — JobTech har redan publicerat), lagringstid (30 dagar för raw_payload, indefinitively för sanitized fields).

> ⚠ **Superseded by the 2026-07-12 (#824, A1) and 2026-07-13 (#842) amendments — see below.**
> "indefinitively för sanitized fields" is false (seven STORED generated columns die with `raw_payload`),
> and the stated retention covers a surface that does not hold the recruiter PII. The Art. 30 register
> inherits both errors and is corrected separately (local file, ADR 0072).

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

> **Note 2026-07-26 (#845):** the "30d-retention" duration cited in the paragraph below is superseded
> (ADR 0032 Amendment 2026-07-26 §C2), and the `RedactRecruiterPii` mechanism it motivates was **deleted** by #842 PR1.
> Historical record; do not act on it.

**Total null-out vs surgical jsonb_set:** CTO Q2 = total null-out. Skäl: GDPR Art. 5(1)(c) data-minimisation > debug-värde. 30d-retention via `PurgeStaleRawPayloadsJob` null:ar ändå hela `raw_payload` efter 30 dagar — surgical redaction räddar non-PII i max 30 dagar för en handfull rader. KISS + Saltzer/Schroeder default-deny.

**Name-baserad sökning defererad till TD-75** (ny TD allokerad 2026-05-13): Name-matching kräver multi-path jsonb-search + ev. full-text på `description.text`. YAGNI tills faktisk request finns. Email är primär rekryterar-identifier i JobTech-payloads. `RecruiterIdentifierType.Name` returnerar `Result.Failure(DomainError.Validation("RedactRecruiterPii.NameNotSupportedYet", ...))` med dokumenterad trigger i `docs/runbooks/recruiter-pii-erasure.md`.

> ⚠ **Superseded by the 2026-07-13 amendment (#842) — see below.** The rationale ("Email är primär
> rekryterar-identifier") is **falsified**: the email is never a structured key in storage at all. The
> deferral deferred the only branch that could ever have worked and shipped the one that provably never
> matches. **TD-75 is closed as VOID; the rationale is withdrawn, not re-scoped.**

**GIN-index på raw_payload defererad till TD-76** (ny TD): seq-scan på ~5–10k rader är acceptabel latens för admin one-off (sekunder). GIN-index har reell write-overhead på stream-cron (~80k operations/dygn). YAGNI tills faktisk latens-trigger eller volym-skifte.

### Krav för stängning av TD-73

> ⚠ **Superseded by the 2026-07-13 amendment (#842) — see below.** Five of these seven boxes were ticked
> over controls that do not do what the checklist says they do. The boxes are kept as the historical
> record and annotated per item; **TD-73's closure no longer supports a prod tag** (see the re-imposed
> gate below).

- [x] `JobTechPayloadSanitizer` implementerad + unit-tester (F2-P8b 2026-05-13, commit `8c09191`)
  — ⚠ **RE-OPENED 2026-07-13 (#842):** the class exists and works as written, but it strips the *field*,
  not the *address*; it never covered the free-text surface the PII actually occupies.
- [x] AllowedKeys-lista verifierad mot JobTech-API-spec (web-search 2026-05-12 + JobTech-docs)
  — ⚠ **RE-OPENED 2026-07-13 (#842):** the list was verified against the JobTech *schema*, never against
  the PII surface. Every retained free-text key carries recruiter contact details in practice.
- [x] `PurgeStaleRawPayloadsJob` Hangfire-job implementerad + integration-test (F2-P8c 2026-05-13, commit `81dfab6`)
  — ⚠ **RE-OPENED 2026-07-13 (#842/#845):** the job never touches `job_ads.description`, and the 02:00
  backfill restores what it nulls. Its own doc claims to erase exactly the PII it cannot reach.
- [x] `RawPayloadPurgedDomainEvent` audit-wire (TD-73 prod-batch 2026-05-13 — ersatt av `SystemAuditEvent.RawPayloadPurged` per ADR 0035)
  — ✅ **STANDS** for the purge job's system event. ⚠ **But the erasure command's audit payload promised
  above (`{ identifier, type, rowsAffected }`) was never written:** `AuditLogEntry.Create` hard-codes
  `payload: null` (`AuditLogEntry.cs:81-92`), and a rejected request is not audited at all
  (`AuditBehavior.cs:35-38`). Art. 5(2)/30 coverage for recruiter-PII erasure does not exist.
- [x] `docs/runbooks/gdpr-processing-register.md` skapad eller utökad med JobTech-entry (F2-P8b 2026-05-13)
  — ⚠ **RE-OPENED 2026-07-13 (#842/#824):** the entry inherits the false retention statement from point 3
  above. Correction is local (ADR 0072).
- [x] ADR 0024 cross-ref för right-to-erasure-cascade till raw_payload (TD-73 prod-batch 2026-05-13)
  — ⚠ **RE-OPENED 2026-07-13 (#842):** the cascade registry (`ADR 0024:467-472`) lists **only**
  `raw_payload` and never `job_ads.description`. It records a cascade that erases nothing. Dated in-file
  amendment to ADR 0024 ships with #842.
- [x] Security-auditor verify-pass innan v0.2-prod-tag (TD-73 prod-batch 2026-05-13)
  — ⚠ **RE-OPENED 2026-07-13 (#842):** the 2026-07-12 security-auditor review
  (`docs/reviews/2026-07-12-824-dpia-archived-ad-security.md`) falsifies the controls this pass signed off.
  A fresh verify-pass is a condition of lifting the re-imposed gate.

### Operativa konsekvenser

> ⚠ **WITHDRAWN by the 2026-07-13 amendment (#842) — see below.** The first bullet is the sentence that
> released the prod gate, and **all three controls it names are falsified.** The gate was **RE-IMPOSED**
> on 2026-07-13, on the condition that ADR 0106 Tier B ship. The second bullet is false too: the manual
> runbook fallback searched `description` and then deleted only `raw_payload`, and TD-75 is closed as void.
>
> **2026-07-26 (#845):** Tier B shipped (`269a4603`, 2026-07-15) and Tier A shipped (`daa4b51d`,
> 2026-07-17). **The prod gate's status is owned solely by ADR 0106** and is not restated here; see
> Amendment 2026-07-26 §C6.

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

---

## Amendment 2026-05-23 — snapshot-retention: defense-in-depth miss-cleanup + ExpiresAt-cron + ApplyCriteria Status=Active SPOT

**Datum:** 2026-05-23
**Källa:** F6 P5 Punkt 1 snapshot-retention-batch — root-cause-discovery 2026-05-23 (`GET /api/v1/job-ads?ssyk=2512`-totalCount ~56k mot förväntat ~40k aktiva annonser; korpus innehåller historiska Platsbanken-poster som aldrig arkiveras när JobStream `removal`-event missas eller annons faller ur snapshot utan removal-event).
**Trigger:** Klas-observation 2026-05-23 (UX-räkne-drift > 40 % över förväntad aktiv korpus) → discovery → CTO-/architect-domar → in-block-fix (samma release-enhet som ADR 0032 2026-05-16-amendment per Martin 2017 kap. 13 REP).
**Beslutsfattare:** senior-cto-advisor 2026-05-23 (agentId `a8e277380b446bb02`, Q1=(c) defense-in-depth, Q2=(i) återanvänd `JobAd.Archive()`, Q3=(B) `ExecuteUpdateAsync` + aggregerad audit, Q4=(W) ApplyCriteria-filter, Q5 trösklar, Q6 amendment-form) + dotnet-architect 2026-05-23 (agentId `a10f8271fe298246c`, port-design + cron-schema + bulk-update-mekanik) + Klas Olsson (godkänd 2026-05-23).
**Status:** Accepted 2026-05-23 (Klas-GO mottaget; amendment-text CC-draftad från CTO/architect-underlaget per memory `feedback_klas_can_override_adr_verbatim_source` — medveten override av CLAUDE.md §9.4 verbatim-text-källa).

### Kontext för amendment

ADR 0032 §3 (snapshot 02:00 + stream `*/10`) + §6 (removal via `JobAd.Archive()`) förutsätter att **antingen** snapshot ELLER stream signalerar att en annons inte längre är aktiv. Faktisk observation 2026-05-23: korpus ackumulerar historiska annonser som **aldrig** arkiveras. Två oberoende läckage-paths identifierade:

1. **Stream-removal-event missas** — `JobStreamClient` har overlap-window men event-tappa under nätverks-failover eller circuit-breaker-öppet (resilience-pipeline §1) är möjlig.
2. **Annons faller ur snapshot utan removal-event** — JobTech kan ta bort annonser från `/v2/snapshot` utan att samma run emitterar `removed: true` på `/v2/stream`. Snapshot-trunkering (2026-05-16-amend) gör situationen värre: vid icke-deterministisk trunkering vet vi inte om en utelämnad annons är borttagen eller utanför trunkerings-prefixet.

`JobAd.ExpiresAt` sätts av `Import`/`UpdateFromSource` men respekteras inte automatiskt av domain-modellen — `Status` förblir `Active` även när `ExpiresAt < now()`. Ingen befintlig mekanism arkiverar baserat på `ExpiresAt`.

**Konsekvens:** `JobAdSearchQuery.ApplyCriteria` (ADR 0062) returnerar både `Active` och `Archived` JobAds → `/jobb`-listans `totalCount` reflekterar inte längre marknadens faktiska aktiva korpus → UX-räkne-drift + relevans-skuld (`ts_rank` rangerar gamla annonser jämbördigt med nya).

### Beslut

**Beslut 1.A — Snapshot-miss-cleanup (defense-in-depth primär)**

Ny Application-port `IJobAdSnapshotMissTracker` (Application/JobAds/Abstractions) paritet med `IUserDataKeyStore` / ADR 0049 TD-13 C2:

```csharp
public interface IJobAdSnapshotMissTracker
{
    Task<SnapshotMissUpdateResult> ApplyAsync(
        string source,
        IReadOnlySet<string> seenExternalIds,
        CancellationToken ct);

    Task<int> ArchiveJobAdsWithMissCountAtLeastAsync(
        string source,
        int missThreshold,
        CancellationToken ct);

    Task<int> GetMaxObservedSnapshotSizeAsync(
        string source,
        TimeSpan window,
        CancellationToken ct);
}
```

`SyncPlatsbankenSnapshotJob` (Application/JobAds/Jobs/SyncPlatsbanken) bygger `seen`-set under enumeration, läser `SnapshotOutcomeRecorder.Outcome` efter `FetchSnapshotAsync` returnerar, kontrollerar floor-trösklar (Beslut 1.D), och anropar `tracker.ApplyAsync(source, seen, ct)` ENDAST om snapshot är **komplett** (ej trunkerad). Trunkerad snapshot → skippa miss-tracking helt (skydd mot massiv falsk archive vid partial transfer).

Infrastructure-impl `JobAdSnapshotMissTracker` (Infrastructure/JobAds/SnapshotMisses) underhåller en separat tabell `job_ad_snapshot_misses(source text, external_id text, miss_count int, first_missed_at timestamptz, last_missed_at timestamptz)`:

- PK composite `(source, external_id)`.
- Partial-index `(source, miss_count) WHERE miss_count >= 1`.
- Vid `ApplyAsync`: parametriserat Postgres `INSERT ... ON CONFLICT (source, external_id) DO UPDATE SET miss_count = miss_count + 1, last_missed_at = now()` för rader i `(externa_id-domän) \ seen`; samtidigt `DELETE` för rader i `seen` (reset vid återkomst). Raw SQL motiverat — bookkeeping-tabell utanför EF change-tracker, ortogonal mot `IAppDbContext` (ISP per Martin 2017 kap. 11).
- `job_ad_snapshot_misses` exponeras **EJ** via `IAppDbContext` (ISP). Arch-test `JobAdRetentionLayerTests` låser.

Ny Application-job `RetainPlatsbankenJobAdsJob` (Application/JobAds/Jobs/RetainPlatsbankenJobAds) anropar `tracker.ArchiveJobAdsWithMissCountAtLeastAsync("platsbanken", N=3, ct)` som internt kör:

```csharp
await db.JobAds
    .Where(j => j.External!.Source == JobSource.Platsbanken
             && j.Status == JobAdStatus.Active
             && db.Set<JobAdSnapshotMiss>().Any(m =>
                    m.Source == "platsbanken"
                 && m.ExternalId == j.External.ExternalId
                 && m.MissCount >= 3))
    .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, _ => JobAdStatus.Archived), ct);
```

**N=3 consecutive misses** innan archive (CTO Q5). Konvergens-fördröjning ~72h efter deploy (3 dagliga snapshot-runs). Acceptabel trade-off mot false-positive-archive vid transient JobTech-API-hicka.

**Beslut 1.B — ExpiresAt-cron (defense-in-depth sekundär)**

Ny Application-job `ExpireJobAdsJob` (Application/JobAds/Jobs/ExpireJobAds) arkiverar JobAds vars `ExpiresAt < now()`:

```csharp
await db.JobAds
    .Where(j => j.Status == JobAdStatus.Active && j.ExpiresAt < clock.UtcNow)
    .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, _ => JobAdStatus.Archived), ct);
```

Skydd mot bägge läckage-paths: (a) annonser där JobTech satte `ExpiresAt` men aldrig emitterade `removed`-event når tröskeln direkt; (b) annonser utan stream-removal som faller ur snapshot men har korrekt `ExpiresAt` arkiveras utan att vänta på N=3 miss-tracking.

Defense-in-depth-motivering (Saltzer/Schroeder 1975 — fail-safe defaults): två orthogonala mekanismer fångar disjunkta failure-modes. Miss-cleanup fångar fall där JobTech tar bort utan `ExpiresAt`-signal; ExpiresAt-cron fångar fall där `ExpiresAt` är satt men snapshot/stream är opålitlig.

**Beslut 1.C — `JobAd.Archive()` återanvänds (ej `Expired`, ej ny `Closed`)**

Bulk-archive använder `Status = JobAdStatus.Archived` — samma terminal-state som stream `removal`-events (§6). En annons är *en* annons oavsett varför den arkiverades (Vernon 2013 — Aggregate Consistency Boundary). `Expired`-värdet i `JobAdStatus`-SmartEnum förblir **dead code** (YAGNI per Beck 1999 — reserveras för framtida distinktion om/när "annonsen utgick" vs "annonsen togs bort" får produkt-värde). Ingen ny `Closed`-status införs.

> **Amendment 2026-07-16 (ADR 0111, #886):** reservations-klausulen ovan är **upphävd** — `Expired`
> retirerades ur `JobAdStatus` (deklarerad + FE-renderad utan writer i hela produktens historia).
> Writer-beslutet ("återanvänd `Archived`") står oförändrat och formaliseras av ADR 0111. En framtida
> "utgången"-distinktion härleds i display-lagret ur `ExpiresAt`, inte via en reserverad dead-code-
> status; återinförande av livscykel-status styrs av ADR 0111 punkt 3 (writer + distinkt invariant +
> beslut om historiska rader, i samma ändring). Q2(iii) nedan bär samma upphävda reservation.

**Trade-off accepterad:** `JobAd.Archive()`s domain-event `JobAdArchivedDomainEvent` raisas **EJ** vid bulk-archive (ADR 0032 §6 jämfört). `ExecuteUpdateAsync` bypassar EF change-tracker och `IDomainEventDispatcher`. Verifierat 2026-05-23: **0 subscribers** på `JobAdArchivedDomainEvent` i Application/Infrastructure (grep `JobAdArchivedDomainEvent` returnerade endast raise-site i domain). Ingen reaktiv-flöde-regression. Audit-trail bevarad via Beslut 1.E aggregerad `ISystemEventAuditor`-rad.

**Beslut 1.D — Trösklar för komplett-snapshot-detektion**

`SyncPlatsbankenSnapshotJob` anropar `tracker.ApplyAsync` **endast** om alla tre kriterier uppfyllda:

1. `SnapshotOutcomeRecorder.Outcome.TruncatedAndExhausted == false` (snapshot-trunkering-amend 2026-05-16: graceful `yield break` vid uttömd retry → outcome flaggar trunkering).
2. `outcome.ParsedTotal >= max(SnapshotAbsoluteFloor, SnapshotRelativeFloorRatio × max_observed_snapshot_size_last_7d)` där standard:
   - `SnapshotAbsoluteFloor = 30000` (under detta = uppenbart degraderad snapshot oavsett historik).
   - `SnapshotRelativeFloorRatio = 0.80` (under 80 % av rullande 7-dygns-max = misstänkt liten snapshot).
3. `max_observed_snapshot_size_last_7d` läses från `IJobAdSnapshotMissTracker.GetMaxObservedSnapshotSizeAsync("platsbanken", TimeSpan.FromDays(7), ct)` (impl: `SELECT MAX(snapshot_size) FROM job_ad_snapshot_runs WHERE source=… AND completed_at > now() - interval '7 days'`; bookkeeping-tabell ortogonal mot `job_ad_snapshot_misses` — completed-run-historik).

Vid trunkering eller floor-failure: skippa miss-tracking-uppdatering helt (varken increment eller reset). Bevarar tidigare miss-counts oförändrade. CTO Q5: "skydd mot falsk archive vid degraderad snapshot är dyrare än konvergens-fördröjning vid 1 förlorad run".

**Beslut 1.E — Aggregerad `ISystemEventAuditor`-rad per retention-pass (ADR 0035)**

Inga per-item `JobAdArchivedDomainEvent`-raise. En aggregerad audit-rad per `RetainPlatsbankenJobAdsJob`-/`ExpireJobAdsJob`-pass via befintlig `ISystemEventAuditor`-port (ADR 0035):

```csharp
public sealed record JobAdsRetentionCompleted(
    string Source,
    string Mechanism,        // "snapshot-miss" | "expires-at"
    int ArchivedCount,
    DateTimeOffset CompletedAt) : SystemAuditEvent
{
    public override string EventType => "System.JobAdsRetentionCompleted";
}
```

Audit-rad per pass (även 0-count) bevarar operativ observabilitet (cron körde, hittade inget) per Humble/Farley 2010 operability. Konsistent med `JobAdsSyncedDomainEvent`-mönstret (§8 amend 2026-05-13).

**Beslut 1.F — Cron-schema**

| Recurring-id | Schema (UTC) | Innehåll |
|---|---|---|
| `sync-platsbanken-snapshot` | `0 2 * * *` | Oförändrad (ADR 0032 §3) + ny miss-tracking-uppdatering vid komplett snapshot (Beslut 1.D-kriterier). |
| `retain-platsbanken-job-ads` | `15 3 * * *` | Snapshot-miss-retention (Beslut 1.A). 75 min efter snapshot-start → garanti att snapshot-run hunnit complete (tiotals min post-streaming-fix per 2026-05-16-amend). |
| `expire-job-ads` | `45 3 * * *` | ExpiresAt-cron (Beslut 1.B). 30 min efter retention → ortogonal kedja, ingen race på `Status`-fältet. |

Båda nya jobben i Worker-lagret via `DisableConcurrentExecution(300)` per ADR 0032 §9 X4-precedens (overlap-skydd; 5 min är gott om marginal mot förväntad `ExecuteUpdateAsync`-tid på ~51k-rad-tabell).

**Beslut 1.G — `JobAdSearchQuery.ApplyCriteria` får `Status = Active`-filter (SPOT)**

Ortogonalt mot Beslut 1.A–1.F men i samma release-enhet (CTO Q4=(W), REP per Martin 2017 kap. 13): `IJobAdSearchQuery.ApplyCriteria` (ADR 0062) får `source.Where(j => j.Status == JobAdStatus.Active)` som **första** filter-steg, före `ApplyQ`/`ApplyFilters`. SPOT-mekanism — alla tre konsumenter (`ListJobAdsQueryHandler`, `RunSavedSearchQueryHandler`, `ListRecentSearchesQueryHandler`) får filtret automatiskt. Cross-ref dokumenterad i ADR 0062-amendment 2026-05-23.

**Klas-STOPP-flaggad i CTO-domen:** `/jobb`-UX-räkning hoppar från ~56k till ~40k i **samma deploy** som retention-jobben aktiveras (CTO Variant 1: filter + retention samma release). Alternativet (filter först, retention senare) skulle visa korrekt aktiv-count innan retention faktiskt arkiverat → läckage av historiska poster i andra ytor (admin, audit-export). Variant 1 vald: konsistent state över alla läs-ytor från deploy-tillfället, även om UX-räkne-droppet blir synligt.

**Beslut 1.H — Post-archive circuit-breaker (operator-ofog-skydd)**

Källa: security-auditor 2026-05-23 (agentId `a82b9f511ec54889b`, fynd H1) → CTO-tilläggsrond 2026-05-23 (agentId `acfe2963371fde555`, in-block-fix entydig mot §9.6).

Beslut 1.D:s tre uppströms-skydd (trunkering / absolut 30 000 / relativ 80% × max_7d) skyddar mot **trasiga snapshots från JobTech-sidan** men inte mot **trasig konfig från operatör-sidan**. En operatör som råkar sätta `SnapshotMissThreshold=1` eller `SnapshotAbsoluteFloor=1` i `appsettings.Production.json` kan trigga mass-arkivering (50 %+); range-validatorerna tillåter det (`[Range(1, 30)]` resp `[Range(1, 1_000_000)]`).

Ny tröskel `JobSourceRetentionOptions.MaxArchivePctPerRun` (default `0.25`, range `[0.05, 1.00]`). `RetainPlatsbankenJobAdsJob` gör pre-archive-COUNT via två nya port-metoder `IJobAdSnapshotMissTracker.CountActiveJobAdsAsync` + `CountArchiveCandidatesAsync`, beräknar `ratio = candidates / active` (defaultar `0` när `active = 0` för att undvika div-by-zero), och vid `ratio > MaxArchivePctPerRun`:

1. **Skriver audit-rad FÖRE throw** (`ThresholdAborted=true`, `AbortReason="max-archive-pct-exceeded"`) → granskningsbart spår även efter Hangfire-retry-loop (Vernon 2013 — events är sanningen).
2. **Kastar `DomainException("RetainPlatsbankenJobAds.MaxArchivePctExceeded", message)`** med detaljerat ratio + count + max-värde i meddelandet → Hangfire markerar jobbet failed; CloudWatch metric filter `event_name=retention_aborted` (LoggerMessage EventId 5703) ger ops-alarm tills konfig korrigerad.

Räkne-exempel: korpus 56k aktiva, förväntad första-körning ~10k archive ≈ 18 % < 25 % → släpps igenom. Steady-state ~0-2 %. Operator-ofog ger 50 %+ → stoppas. CTO motiverade 0.25 framför security-auditor:s 0.20 (noll marginal mot förväntad första-körning ger falsk-positiv-risk mot legit JobTech-fluktuation; 25 % > worst-legit, < worst-ofog).

`JobAdsRetentionCompleted`-audit-eventet utvidgat med `AbortReason: string?` (null om inte aborterat). `ExpireJobAdsJob`-audit har samma fält men sätter alltid `null` (ingen pre-check där — bulk-UPDATE on `ExpiresAt < now()` har ingen analog ofog-yta).

**Motivering (defense-in-depth):** Saltzer/Schroeder 1975 explicit > implicit; säkerhetsmekanismer ska vara default-on i-process snarare än default-correct-if-someone-set-it-up. Bullrig fail-loud-retry är **funktionen** vid operator-ofog, inte buggen — operator-config-attack ska störa ops tills nån tittar. Per §9.6 in-block-fix (samma fas, ingen saknad dependency); ingen TD lyft.

### Avvisade alternativ

- **Q1 (a) endast ExpiresAt-cron:** läcker när JobTech inte sätter `ExpiresAt` eller satt-värde är otillförlitligt; täcker inte stream-removal-event-tapp. Defense-in-depth-principen kräver två oberoende mekanismer.
- **Q1 (b) endast snapshot-miss-cleanup:** konvergens-fördröjning N=3 dagar; annonser med korrekt `ExpiresAt` arkiveras onödigt sent.
- **Q2 (ii) ny `Closed`-status:** YAGNI — `Archived` har korrekt domain-semantik (terminal, idempotent). Ny status fragmenterar `JobAdStatus`-konsumentlogik utan produkt-värde.
- **Q2 (iii) återanvänd `Expired`:** distinktion mot stream-removal-`Archived` är prematur (ingen UI/audit-yta särskiljer). `Expired`-värdet är dead code men reserveras (kan aktiveras vid framtida produkt-behov utan amendment-konflikt).
- **Q3 (A) per-item `JobAd.Archive()` med change-tracker:** ~tusentals-rad-archive per pass laddar hela grafen → minne + SaveChanges-latens. `ExecuteUpdateAsync` bypassar change-tracker → bulk-UPDATE-SQL direkt. EF Core 8+ global query-filter (DeletedAt) respekteras automatiskt → soft-deleted rader rörs ej. SmartEnum-converter fungerar med statiska readonly-värden (architect-verifierat 2026-05-23).
- **Q3 (C) raw SQL UPDATE i Infrastructure-impl:** bryter CLAUDE.md §3.6 "använd `IAppDbContext` direkt"; EF Core 8+ `ExecuteUpdateAsync` är idiomatic .NET 10-väg.
- **Q4 (Y) filter i varje konsument-handler:** bryter SPOT (Fowler 2018 — Single Point of Truth); tre konsumenter blir tre divergens-risker; lägger inte filter-disciplin vid den port-gräns ADR 0062 etablerade.
- **Q4 (Z) filter via global query-filter på `JobAd`:** ändrar default-läsning för hela aggregatet → admin-ytor som vill visa arkiverade annonser måste `IgnoreQueryFilters()`, vilket ADR 0048 explicit förbjuder i query-filter-disciplinen.
- **Cursor-tabell för snapshot-tracking:** förkastad redan 2026-05-16 (snapshot-trunkerings-amend) — idempotens via UNIQUE-index gör cursor onödig. Frånvaro-räknare (`job_ad_snapshot_misses`) är ortogonal mot konvergens-katch-up, EJ cursor.

### Konsekvenser

#### Positiva

- **Korpus konvergerar mot aktiv-marknads-storlek** (~56k → ~40k) över ~72h efter deploy.
- **Defense-in-depth mot bägge läckage-paths** — miss-cleanup fångar JobTech-removal-event-tapp; ExpiresAt-cron fångar ofullständig `removal`-signalering.
- **SPOT-filter `Status=Active` i ApplyCriteria** — tre konsumenter får filtret från en plats; kompilator-garanti mot divergens.
- **Skydd mot massiv falsk archive vid degraderad snapshot** — floor-trösklar (Beslut 1.D) skippar miss-tracking-uppdatering helt vid trunkering eller undersnitts-storlek.
- **Bulk-UPDATE via `ExecuteUpdateAsync`** — O(antal-archive) SQL, ej O(antal-rader-i-graf) change-tracker. Hot-path-säkert även vid stora dygnliga retention-runs.
- **Audit-trail bevarad** — aggregerad `ISystemEventAuditor.JobAdsRetentionCompleted`-rad per pass; konsistent med ADR 0035-mönstret.

#### Negativa / accepterade trade-offs

- **`Expired`-värdet förblir dead code** i `JobAdStatus`-SmartEnum (YAGNI; reserveras).
  *(Reservationen upphävd 2026-07-16 — `Expired` retirerad; se Amendment vid Beslut 1.C / ADR 0111, #886.)*
- **Konvergens-fördröjning ~72h** efter deploy (N=3 runs över 3 dygn). Accepterat — alternativet (N=1) är dyrare att backa vid transient JobTech-hicka.
- **Domain-event-bortfall för bulk-archive** — `JobAdArchivedDomainEvent` raisas EJ vid bulk-Archive. **Verifierat: 0 subscribers idag.** Vid framtida subscriber-tillkomst: lyft som amendment, ej tyst-fix (kan kräva per-item-loop eller event-republish-mekanism).
- **Ny tabell `job_ad_snapshot_misses`** — bookkeeping, ej cursor. Frånvaro-räknare ortogonal mot konvergens-katch-up. Inte exponerad via `IAppDbContext` (ISP).
- **`/jobb`-UX-räkning hoppar ~56k → ~40k i samma deploy** som filter + retention aktiveras (Klas-STOPP-flaggad i CTO-dom; Klas valde Variant 1: konsistent state över alla läs-ytor från deploy-tillfället).

### Implementations-trail

**Application-lager:**

- `src/JobbPilot.Application/JobAds/Abstractions/IJobAdSnapshotMissTracker.cs` (NY — port + `SnapshotMissUpdateResult`-record)
- `src/JobbPilot.Application/JobAds/Abstractions/SnapshotOutcome.cs` (NY — record `(ParsedTotal, Attempts, TruncatedAndExhausted)`)
- `src/JobbPilot.Application/JobAds/Abstractions/SnapshotOutcomeRecorder.cs` (NY — single-write mutable; explicit passering)
- `src/JobbPilot.Application/JobAds/Abstractions/IJobSource.cs` (ÄNDRAD — `FetchSnapshotAsync(SnapshotOutcomeRecorder, ct)`)
- `src/JobbPilot.Application/JobAds/Abstractions/JobSourceRetentionOptions.cs` (ÄNDRAD — `SnapshotMissThreshold`, `SnapshotAbsoluteFloor`, `SnapshotRelativeFloorRatio`)
- `src/JobbPilot.Application/Common/Auditing/SystemAuditEvent.cs` (ÄNDRAD — nytt `JobAdsRetentionCompleted`-record, event-type `System.JobAdsRetentionCompleted`)
- `src/JobbPilot.Application/JobAds/Jobs/SyncPlatsbanken/SyncPlatsbankenSnapshotJob.cs` (ÄNDRAD — seen-set, outcome-läsning, floor-check, miss-tracker-anrop)
- `src/JobbPilot.Application/JobAds/Jobs/RetainPlatsbankenJobAds/RetainPlatsbankenJobAdsJob.cs` (NY)
- `src/JobbPilot.Application/JobAds/Jobs/ExpireJobAds/ExpireJobAdsJob.cs` (NY)

**Infrastructure-lager:**

- `src/JobbPilot.Infrastructure/JobAds/SnapshotMisses/JobAdSnapshotMiss.cs` (NY — entitet)
- `src/JobbPilot.Infrastructure/JobAds/SnapshotMisses/JobAdSnapshotMissTracker.cs` (NY — `IJobAdSnapshotMissTracker`-impl, parametriserat Postgres `INSERT ... ON CONFLICT`)
- `src/JobbPilot.Infrastructure/Persistence/Configurations/JobAdSnapshotMissConfiguration.cs` (NY)
- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/PlatsbankenJobSource.cs` (ÄNDRAD — `outcome.Record()` vid båda `yield break`-paths)
- `src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs` (ÄNDRAD — `source.Where(j => j.Status == JobAdStatus.Active)` SPOT-filter; ADR 0062 Beslut 1 query-mekanik bevarad)
- `src/JobbPilot.Infrastructure/DependencyInjection.cs` (ÄNDRAD — `IJobAdSnapshotMissTracker` + 2 nya jobs scoped)
- `src/JobbPilot.Infrastructure/Persistence/Migrations/20260523144102_F6P5SnapshotMisses.cs` (NY)

**Worker-lager:**

- `src/JobbPilot.Worker/Hosting/RetainPlatsbankenJobAdsWorker.cs` (NY — `[DisableConcurrentExecution(300)]`)
- `src/JobbPilot.Worker/Hosting/ExpireJobAdsWorker.cs` (NY — `[DisableConcurrentExecution(300)]`)
- `src/JobbPilot.Worker/Hosting/RecurringJobRegistrar.cs` (ÄNDRAD — 2 nya `AddOrUpdate` per Beslut 1.F)
- `src/JobbPilot.Worker/Program.cs` (ÄNDRAD — DI för wrappers)

**Test-svit:**

- Domain 399 / Application 542 (inkl 4 nya retention-handler-tester + 3 nya snapshot-edge-cases för floor-check + truncated-skip) / Architecture 78 (inkl 5 nya `JobAdRetentionLayerTests` — konsumentlistor låsta, `JobAdSnapshotMiss` ej exponerad via `IAppDbContext`, bulk-update-mekanik i Infrastructure) — alla gröna 2026-05-23.
- Integration-suite körs i CI (Docker ej igång lokalt vid implementation).

### Referenser

- senior-cto-advisor 2026-05-23 (`a8e277380b446bb02`) — Q1/Q2/Q3/Q4/Q5/Q6-domar
- dotnet-architect 2026-05-23 (`a10f8271fe298246c`) — port-design, cron-schema, `ExecuteUpdateAsync`-mekanik, arch-test-låsning
- Klas Olsson 2026-05-23 — Variant 1-val (filter + retention samma release)
- Robert C. Martin, *Clean Architecture* (2017), kap. 11 (ISP — `job_ad_snapshot_misses` ej via `IAppDbContext`), kap. 13 (REP — samma release-enhet som 2026-05-16-amend)
- Saltzer/Schroeder, *The Protection of Information in Computer Systems* (1975) — fail-safe defaults (defense-in-depth-motivering)
- Kent Beck, *XP Explained* (1999) — YAGNI (`Expired` förblir dead code)
- Vaughn Vernon, *Implementing DDD* (2013) — Aggregate Consistency Boundary (`Archived` är *en* terminal-state)
- Martin Fowler, *Refactoring* 2nd ed (2018) — Single Point of Truth (`Status=Active` i ApplyCriteria)
- Humble/Farley, *Continuous Delivery* (2010) — operability (audit-rad per pass även 0-count)
- Microsoft Learn — *EF Core ExecuteUpdate/ExecuteDelete* (.NET 10, bulk-UPDATE utan change-tracker; global query-filter respekteras)
- ADR 0032 §3 (snapshot-schema), §5 (dedup + child-scope), §6 (`JobAd.Archive()`), 2026-05-16-amend (snapshot-trunkerings-resiliens)
- ADR 0035 (`ISystemEventAuditor` system-event-pipeline)
- ADR 0048 (cross-aggregat join vs port — ortogonal mot detta beslut)
- ADR 0049 (TD-13 C2 `IUserDataKeyStore`-port-paritet)
- ADR 0062 (FTS-hybrid + `IJobAdSearchQuery`-port — ApplyCriteria-filter dokumenteras i ADR 0062-amendment 2026-05-23)
- TD-86 not 2026-05-23 — korpus-storlek-delen (recall-gap punkt 1 m.fl.) löses indirekt av denna amendment via retention; TD-86 förblir öppen för övriga sök/filter-fynd

---

## Amendment 2026-07-12 — §8's retention model is not what the code implements (#824)

**Status:** Accepted. **Trigger:** #824 (DPIA truth-sync) surfaced three defects in §8's own account of
itself, all verified empirically against real Postgres (Testcontainers). This amendment records the
truth; the code conformance work is tracked in #841/#842/#845 and is **not** done here.

### A1 — "indefinitively för sanitized fields" is false. Seven of them self-destruct after 30 days.

> ✅ **FIXED BY #841 (`da29255f`, PR #882 — migration `MaterialiseJobAdSourceFacets`).** The seven are no longer
> derived in the database. They are ordinary columns written in C# at the single ingest funnel
> (`JobAd.SetSourcePayload`, atomically with the payload they are parsed from), so they OUTLIVE
> `raw_payload` and §8's stated retention model is true for the first time. The rule is now
> executable rather than aspirational: `JobAdRawPayloadDerivationGuardTests` fails the build if any
> durable column is derived from `raw_payload` again (`search_vector` and `extracted_lexemes` remain
> legitimate — their base columns carry no TTL), and `JobAdFacetsSurvivePurgeTests` runs the purge
> against real Postgres and asserts all seven survive it.
>
> **Two things this amendment did NOT fix, and they are not it:** (1) an ad purged AND delisted
> before #841 shipped has no payload to re-derive from and cannot be re-fetched — its facets are
> permanently gone, which is why #824's honest-degradation mandate survives the schema fix; (2) the
> retention of `organization_number` is now INDEFINITE, which is the written policy but changes the
> Art. 17 obligation — see ADR 0087 D8(a) amendment 2026-07-13.

§8 states the retention model as *"30 dagar för `raw_payload`, **indefinitively för sanitized fields**"*
and reasons that total null-out is safe because only *debug value* is at stake (CTO-rond 2026-05-13,
punkt 8).

**Seven "sanitized fields" are STORED generated columns derived from `raw_payload`.** Postgres
**recomputes** a stored generated column on every UPDATE of its base, so `PurgeStaleRawPayloadsJob`
nulls all seven. They do not survive indefinitely; they survive 30 days.

**The blast-radius question was never asked — across four separate migrations, over 48 days.** The
columns did not arrive together, and it matters that the record is exact here (an earlier draft of this
amendment claimed they landed in one migration "the same day" as the CTO ruling; that was transcribed
from a review without verification and is **false** — precisely the failure mode this whole issue exists
to correct, so it is corrected in place rather than quietly):

| Migration | Date | Generated columns added |
|---|---|---|
| `20260513111555_F2P9JobAdSearchColumns` | 2026-05-13 | `ssyk_concept_id`, `region_concept_id` |
| `20260608155047_F6P6JobAdKlass1SearchColumns` | 2026-06-08 | `occupation_group_concept_id`, `municipality_concept_id` |
| `20260608205054_F6P7JobAdKlass2SearchColumns` | 2026-06-08 | `employment_type_concept_id`, `worktime_extent_concept_id` |
| `20260630144631_AddJobAdOrganizationNumber` | 2026-06-30 | `organization_number` |

The first two landed the **same day** as the 2026-05-13 ruling that chose total null-out on the grounds
that only debug value was at stake. The remaining five — including `organization_number`, the column
that *causes* #824 — landed **26 and 48 days later**. So this is not one unlucky coincidence: **each new
durable projection was hung off a base column already known to be purged, and nobody re-asked the
question.** That is the real lesson, and it is a worse one.

**Consequences (all proven, not inferred):** an ad that is **still Active** but published >30 days ago
disappears from facet-filtered search and from per-user background matching (both filter on those
columns), is missed by the company-watch location filter (#834), and can no longer be attributed to an
employer in the application-history projection (#824). Root cause + fix: **#841** — materialise the
seven as ordinary, C#-written ingest columns, exactly as `extracted_terms` already is (twelve lines
below them in `JobAdConfiguration`, and it survives the purge).

**Rejected remedy, recorded so it is not re-proposed:** exempting Active/still-listed ads from the
purge. That subordinates a GDPR minimisation control to a search-correctness need, and ADR 0049
Beslut 3 leans on this purge to justify excluding `raw_payload` from the DEK envelope. *You do not
weaken a data-protection control to paper over a schema mistake* (senior-cto-advisor, 2026-07-12).

#### A1.1 — The seven are NOT one loss. The asymmetry is the most useful fact in this amendment.

**The six facet columns are only ever read under `Status == JobAdStatus.Active`** — verified at every
consumer: `JobAdSearchComposition.cs:65`, `PerUserJobAdSearchQuery.cs:307,368`,
`BackgroundMatchingJob.cs` (ScanUserAsync's windowed candidate query; line anchor dropped after the
#751 scope refactor), `CompanyWatchScanJob.cs:156`,
`ListCompanyWatchesQueryHandler.cs:99`, `SuggestJobAdTermsQueryHandler.cs:39`. An ad that leaves the
Platsbanken feed is **Archived at 03:15** by `RetainPlatsbankenJobAdsJob` (ADR 0032-amendment
2026-05-23) — *before* the 04:30 purge destroys its columns. **So for a DELISTED ad, the facet loss is
inert: no code path will ever read those columns again.**

**`organization_number` is different, and it is the only real loss.**
`GetEmployerApplicationHistoryQueryHandler` `GroupJoin`s `db.JobAds` with **no `Status` predicate** —
an archived ad still joins, so its org.nr is genuinely load-bearing for #444/#446 employer
attribution. When the purge nulls it on a delisted ad, that attribution is **gone forever** (no
backfill exists — see the recovery paths closed in
`docs/reviews/2026-07-12-824-dpia-archived-ad-architect.md`).

**Two distinct defects follow, and they must not be conflated:**

| | Affected ad | Consequence |
|---|---|---|
| **Live degradation** (the reason #841 is P1) | **ACTIVE**, published >30d ago, still in the feed | Facets NULL ~21.5h/day → the ad drops out of facet-filtered search + matching. **Self-heals at 02:00, breaks again at 04:30, daily.** |
| **Permanent loss** | **DELISTED** (Archived), published >30d ago | Six facet columns: **inert** (no reader). `organization_number`: **irrecoverable** — employer attribution for that application is lost forever. |

**Consequence for scheduling (senior-cto-advisor bind, 2026-07-12):** the permanent loss accrues nightly,
but in a currency that is worthless until real users exist — the applications it would attribute are
dev/test rows. **#841 is therefore P1, NOT P0 — gated on production launch:** it **must** merge before the
first `v*` tag / before any real user can submit an application. `ALTER TABLE … DROP EXPRESSION` (PG13+)
converts the columns in place, **preserving whatever is populated on landing day**, so #841 *is* its own
salvage and an interim salvage table was rejected (it would replicate a possibly-personnummer org.nr into
a second at-rest location — the same Art. 5(1)(c) ground on which #445 was downgraded — to rescue data
with no current reader).

### A2 — the retention rule is "30 days after the ad leaves the feed", not "30 days after publication".

> **SUPERSEDED IN PART, 2026-07-26 (#845):** the replacement rule named in this heading is **also
> false**, in both directions; the arithmetic is worked once in the **Amendment 2026-07-26 §C1**. The
> rule's home is now **§C2**. This section's diagnosis (the documented rule is not the
> implemented one) stands; its replacement wording does not.

`SyncPlatsbankenSnapshotJob` (cron `0 2 * * *`) is a **daily full backfill** and
`UpsertExternalJobAdCommandHandler` has **no unchanged/hash short-circuit** — it always calls
`UpdateFromSource`, which rewrites `RawPayload`. So for any ad still present in the Platsbanken feed the
02:00 sync **restores** the payload that the 04:30 purge nulled, indefinitely.

Retaining the sanitized payload of a currently-live, publicly-listed ad has a live purpose, so the
**behaviour is substantively defensible** under Art. 5(1)(e). What is not defensible is that §8 documents
a rule the code does not implement (Art. 5(2)/24 accountability). Tracked: **#845**.

*Note:* this daily rewrite is also why the A1 defect presents as a ~21.5h/day outage rather than a
permanent one — the sync accidentally heals the seven columns for ~2.5h each night. **Do not "fix" that
by suppressing the rewrite:** it would make the columns NULL *permanently* after the first purge,
converting an intermittent defect into a permanent one.

### A3 — both PII mitigations §8 relies on are largely ineffective against the PII they target.

> **SUPERSEDED IN PART, 2026-07-26 (#845).** **This banner covers every retention-duration, shipping
> and GATE claim in this section and in the rest of this 2026-07-12 amendment** - a scope, not a list,
> because the first draft of this banner enumerated *"two things"* when three had moved. What to know:
> the rule lives in **§C2** and is not a duration, so *"this 30-day purge"* names the control by
> something that is not the rule, and the purge is not a PII control at all; A3's finding was correct
> when written and is **no longer the whole picture** - the two controls it judges are still as weak
> as it says, but they are no longer the only ones, since Tier A (`daa4b51d`) and Tier B (`269a4603`)
> shipped and per-control reach is tabulated in **§C4**; and **the prod gate's status is owned solely
> by ADR 0106**, so no gate sentence in this amendment states it - see **§C6**. Do not quote A3 as the
> current state of our PII defences in either direction.

§8's risk register presents two controls against recruiter PII: the ingest **allowlist sanitizer** and
this **30-day purge**. Neither works against the form the PII actually takes.

- The sanitizer strips the **structured key** (`employer.contact_email`) but the allowlist
  **deliberately retains every free-text surface** (`description`, `description_text`, `text`,
  `company_information`, `needs`, `requirements`, `salary_description`). A recruiter's address written
  into the ad body ("Skicka CV till anna@acme.se" — the exact case `PlatsbankenJobSource`'s own
  SECURITY-NOTE describes) survives sanitisation. **It strips the field, not the address.**
- The purge nulls `raw_payload` — but the identical free text also lives in the **ordinary column
  `job_ads.description`**, which nothing purges, and remains FTS-searchable via `search_vector`
  (generated from `title || description`, not from `raw_payload`). The purge deletes a **duplicate** and
  leaves the original.

**Consequence:** `RecruiterPiiPurger.RedactByEmailAsync` — the *only* Art. 17 erasure path for recruiter
PII — probes `raw_payload @> {"employer":{"contact_email": …}}`, a key ingest **guarantees is absent**.
It returns `rowsAffected = 0` structurally, always. Tracked: **#842** (P1, **launch-gate** — no `v*` prod
tag until fixed; the real fix is at **ingest**, Art. 25, not at the erasure path).

**Knock-on:** ADR 0087 D8(a) justifies plaintext org.nr partly on *"raw_payload är redan plaintext,
ADR 0032 §8 — PII stripped at ingest"*. **That supporting pillar is false.** D8(a)'s accept-risk survives
on its own merits (org.nr is genuinely public employer data), but the false pillar is withdrawn from the
reasoning rather than silently retained.

### Referenser
- Reviews: `docs/reviews/2026-07-12-824-dpia-archived-ad-security.md` · `-cto.md`
- Issues: #824 (DPIA truth) · #841 (root cause) · #842 (Art. 17 erasure) · #845 (retention rule) · #843 (test-fiction pattern)
- DPIA #456 §8 (amended 2026-07-12) · ADR 0087 D1/D8(a) · ADR 0049 Beslut 3 · ADR 0090 D1

---

## Amendment 2026-07-13 — the Art. 17 erasure path is 100 % vacuous; the prod gate is RE-IMPOSED (#842)

**Status:** Accepted. **Decision-maker:** senior-cto-advisor 2026-07-13, BOUND —
`docs/reviews/2026-07-13-842-erasure-contract-cto.md` (not advice; executable without further GO except
its six STOPP items).
**Evidence:** `docs/research/2026-07-13-842-erasure-evidence-pack.md` — every `file:line` below was
re-verified at HEAD `64e4c654`, and every number below was **measured** against the real dev Postgres on
2026-07-13 (PG 18.3, 93 469 ads, all ingested through the real JobTech path). **There is no prod DB, so
that corpus is the whole world of ingested ads.**
**Governing contract from here on:** **ADR 0106** — *"Recruiter-PII erasure contract: ingest minimisation
+ provable record removal"* (local/gitignored per ADR 0072). This amendment corrects §8's record; ADR 0106
carries the new mechanism.
**Relation to the 2026-07-12 amendment (#824):** A2 and A3 above already state parts of this truth. This
amendment does **not** re-argue them — it **propagates** them into the six passages of the ADR body that
still contradict them, and it re-imposes the gate that one of those passages released.

> **Line-number note:** the `:NNN` references below are as at HEAD `64e4c654`, i.e. **before** the inline
> "⚠ superseded" pointers this amendment adds shifted them.

### B1 — `:540` is WITHDRAWN. The prod gate it released was RE-IMPOSED (2026-07-13; status now owned by ADR 0106 — see §C6).

> **SUPERSEDED IN PART, 2026-07-26 (#845):** the gate's sole written condition **as stated in this
> section** — Tier B shipped with green provable-erasure tests — has been met (`269a4603`; dates in
> §C6). Note that ADR 0106 states the gate as more than this one leg, which is why the status is not
> derivable from this section. Whether the gate is therefore lifted is **ADR 0106's call, not this
> ADR's**; see ADR 0032 Amendment 2026-07-26 §C6.
>
> **This banner covers every GATE claim in B1's body, not only its shipping claims** - the
> *"THE GATE (this replaces `:540`)"* paragraph included. A gate sentence anywhere in this ADR is the
> record of what the gate **was**; its status is stated **only in ADR 0106**. (Scope, not a list - the
> same discipline §C6 states for ship dates and §C3 for the retention arithmetic.)

> *"v0.2-prod-tag är inte längre gated på TD-73. **PurgeStaleRawPayloadsJob + audit-wire +
> Email-only-erasure tillsammans täcker GDPR Art. 5/17/30 för rekryterar-PII i raw_payload.**"* — `:540`

**All three named controls are falsified.** This is the single most load-bearing false sentence in the
repo for this issue: it is what converted an unfinished GDPR control into a released launch gate.

**(1) Email-only-erasure — 100 % vacuous, not approximately vacuous.**
`RecruiterPiiPurger.RedactByEmailAsync` (`RecruiterPiiPurger.cs:31-52`) was the **only** Art. 17 erasure
path for recruiter PII. It matched rows by jsonb containment on `{"employer":{"contact_email": …}}` and
nulled `raw_payload` — nothing else. That key **cannot exist in any ingested row**, under two independent
locks:

- the wire POCO cannot emit it — `JobTechEmployer` declares exactly `name` + `organization_number`
  (`JobTechSearchResponse.cs:125-143`), and `raw_payload` is `JsonSerializer.Serialize(hit)`
  (`PlatsbankenJobSource.cs:238`);
- the sanitizer's default-deny allowlist would drop it anyway (`JobTechPayloadSanitizer.cs:62-64`,
  `:107-108`), at the one and only write path into `raw_payload`.

**Measured 2026-07-13:** rows with `raw_payload->'employer' ? 'contact_email'` = **0 of 93 469**.
`rowsAffected = 0` was its **only possible outcome**, structurally, always — and the endpoint returned
**200 OK** regardless (`AdminJobAdsEndpoints.cs:54-61`, `RedactRecruiterPiiCommandHandler.cs:48-50`):
there was no code path that distinguished "erased nothing" from "erased something". Same defect class as
#805-3 (`JobAd.DeletedAt` never written → vacuous filter).

**(2) `PurgeStaleRawPayloadsJob` — erases a duplicate and leaves the original.**
It only does `SetProperty(j => j.RawPayload, _ => null)` (`PurgeStaleRawPayloadsJob.cs:93-97`). It never
touches `job_ads.description`, where the address actually lives — while its own doc comment (`:18-20`)
claimed it removed *"rekryterar-PII som överlever sanitizer:n (free-text-yta i description)"*. **It
claimed to erase exactly the PII it cannot reach.** Add A2 above: the 02:00 full backfill restores the
payload nightly anyway (#845).

**(3) The audit-wire — no Art. 30 coverage for recruiter-PII erasure exists.**
`:517` promises one audit row per request with payload `{ identifier, type, rowsAffected }`.
`AuditLogEntry.Create` hard-codes `payload: null` (`AuditLogEntry.cs:81-92`), so nothing is recorded about
*what* was erased; and `AuditBehavior.cs:35-38` skips audit on `Result.Failure`, so a **rejected** request
left no trace it was ever received (Art. 12(3) exposure). The runbook's own verification query selected a
column that is always NULL.

**Where the PII actually is, and that it is searchable today.** The ad body is stored verbatim (`.Trim()`
only — `PlatsbankenJobSource.cs:207` → `JobAd.cs:50`/`:156`) in plaintext `job_ads.description`.
`search_vector` is a STORED generated column over `title || description`
(`JobAdConfiguration.cs:174-179`), and the FTS branch runs for **any** non-blank query
(`JobAdSearchComposition.cs:137, 175-191`). **Proven against real Postgres:**
`search_vector @@ websearch_to_tsquery('swedish', '<recruiter email>')` returns a hit — so **any
authenticated user can reverse-look-up a recruiter by typing the address into /jobb**. The recruiter's
**name** hits independently via ordinary word lexemes.

**Scale, measured on the corpus (not estimated):** **27 077 of 93 469 ads (29 %)** carry a well-formed
email in the ad body; **13 134** carry a phone number; only **17** use textual obfuscation. This is not an
edge case; it is roughly three ads in ten.

**Why this is not yet a harm, and why that is not a reason to wait.** `audit_log` rows matching
`%RecruiterPiiRedact%` = **0** (measured): **the endpoint has never been called, so no data subject has
yet received a false confirmation.** That is why #842 is P1 and not P0 — and it is why the correction is
**cheap**, not why it is deferrable.

**THE GATE (this replaces `:540`):** **any `v*` prod tag IS gated on #842.** The gate lifts **only** when
ADR 0106 **Tier B** (provable erasure on request — PR3) is merged with green tests. Tier A alone (PR2)
does **not** lift it: ingest scrubbing makes us hold far less, but leaves no working Art. 17 path for the
detector's misses (CTO STOPP-6). Independently, the DPIA cannot be signed until the Hetzner-phase
backup/PITR retention window is filled in by Klas (CTO STOPP-4) — CC must not invent it.

### B2 — `:240-242` (§8's heading): true for OUTBOUND only.

> *"### 8. GDPR: **PII-fri externtrafik** + sync-audit-events … **Inga PII skickas till JobTech.**"* — `:240-242`

The outbound claim stands: search-params are public metadata. But the heading reads as a blanket PII-free
guarantee for §8, and the **inbound** surface was only bolted on by the 2026-05-12 amendment (`:374`) and
then covered by a control that does not reach it (B3). §8 makes **no** valid claim about inbound recruiter
PII. `PlatsbankenJobSource.cs:199-207` says so in-file — it records that `description.text` may carry
recruiter PII, accepts it under Art. 6(1)(f), and defers the remedy: *"Regex-baserad PII-redaction kan
lyftas som Trigger-TD vid faktiskt klagomål."* **An Art. 17 request IS that "faktiskt klagomål", and the
mitigation it defers to never existed.** The inbound control is ADR 0106 Tier A — **bound, not yet
shipped** (PR2).

### B3 — `:380-382`: it strips the FIELD, not the ADDRESS.

> *"**1. PII-stripping vid ingest (P8b-leverans)** … en `JobTechPayloadSanitizer` som **strippar kända
> PII-keys** före persistering."* — `:380-382`

The sanitizer is a **key-name filter that never examines a value**, and its allowlist **deliberately
retains every free-text key**: `headline`, `description`, `description_html`, `description_text`, `text`,
`text_formatted`, `company_information`, `needs`, `requirements` (`JobTechPayloadSanitizer.cs:33-35`) and
`salary_description` (`:55`); values are `DeepClone()`d unexamined (`:99`).

**This retention is not an oversight. It is what the sanitizer is for** — an ad without its body is not an
ad. **PII in free text was never a gap in the design; it IS the design.** The correction is therefore not
"fix the sanitizer": the sanitizer **stays exactly as it is**, as defense-in-depth for structured keys
(CTO V3). What changes is (a) this false claim, (b) the sanitizer's own false XML doc
(`JobTechPayloadSanitizer.cs:7`, *"Strippar PII (rekryterar-namn, email, telefon, firmatecknare)"* —
truth-synced in PR1), and (c) the addition of a free-text control that does not exist yet (Tier A).

### B4 — `:404-407` + `:413`: both halves of the retention rule are false, plus a cron drift.

> **SUPERSEDED IN PART, 2026-07-26 (#845):** B4(a) repeats A2's replacement wording, which is itself
> false. See **Amendment 2026-07-26 §C2** for the rule.

> *"`raw_payload` null:as via Hangfire-job **30 dagar efter** `job_ads.published_at`"* … *"lagringstid
> (**30 dagar för raw_payload, indefinitively för sanitized fields**)"* — `:404-407`, `:413`

- **(a) "30 days after `published_at`" is not the implemented rule.** The 02:00 full backfill rewrites
  `RawPayload` unconditionally (`JobAd.cs:155-159`; no hash short-circuit in
  `UpsertExternalJobAdCommandHandler`), so for any ad still in the feed the payload is **restored** each
  night. The real rule is **"30 days after the ad leaves the feed"** — see A2 above (#845, **doc-only**:
  A2 explicitly forbids "fixing" this by suppressing the nightly rewrite, which would convert #841's
  intermittent column-NULL defect into a permanent one).
- **(b) "indefinitively för sanitized fields" is false.** Seven of the nine STORED generated columns are
  derived from `raw_payload` and are nulled with it — see A1 above (#824/#841).
- **(c) Cron drift.** The ADR says **03:00**; the code registers `30 4 * * *`
  (`RecurringJobRegistrar.cs:98-101`).
- **(d) And none of it is about the right surface.** The entire retention model governs `raw_payload`. The
  recruiter's address is in `job_ads.description`, which **nothing** purges, expires or redacts.

The Art. 30 register (`docs/runbooks/gdpr-processing-register.md`) inherits (a), (b) and (d); it is a
gitignored local file (ADR 0072) and is corrected outside this PR.

### B5 — `:524`: TD-75's rationale is falsified. TD-75 is CLOSED AS VOID.

> *"Name-baserad sökning defererad till TD-75 … YAGNI tills faktisk request finns. **Email är primär
> rekryterar-identifier i JobTech-payloads.**"* — `:524`

The premise is destroyed by the two locks in B1: **the email is never a structured key in storage at
all.** The deferral therefore deferred the **only branch that could ever have worked** (free text) and
shipped the one that **provably never matches** — while `RecruiterIdentifierType.Name` returned
`NameNotSupportedYet` for the identifier that §1.5 of the pack proves is independently FTS-searchable.

**The rationale is WITHDRAWN, not re-scoped. TD-75 is closed as VOID** (CTO V17). Under ADR 0106 the
erasure command takes a **single free-text identifier**; `kind` survives only as operator-supplied audit
metadata, **never as a matching switch** — matching is over free text either way, so a discriminator that
changes the query is a distinction without a difference and a place for the next bug to hide.

*Flagged, not ruled here:* TD-76 (GIN index on `raw_payload`, `:526`) rests on the same premise — that the
erasure query is a jsonb search over `raw_payload`. With the purger deleted (PR1) that query has no
caller. #842's owner should re-triage it; this amendment does not close it.

### B6 — `:530-536`: five of the seven TD-73 closure boxes are re-opened.

Seven `[x]` boxes — including *"Security-auditor verify-pass innan v0.2-prod-tag"* — sat above controls
that do not do what the checklist says. They are **kept as the historical record and annotated per item**
in place (see the checklist above): the sanitizer box, the AllowedKeys box, the purge-job box, the Art. 30
register box, the ADR 0024 cross-ref box and the security-verify box are **RE-OPENED**; the
`SystemAuditEvent.RawPayloadPurged` box **stands** for the purge job's system event, but the *erasure
command's* audit payload it is read as covering was never written (B1(3)). **TD-73's closure no longer
supports a prod tag.**

The ADR 0024 cross-ref deserves its own line, because it is where an auditor looks first: the Art. 17
cascade registry (`ADR 0024:467-472`) lists **only** `raw_payload` and never `job_ads.description`. A
dated in-file amendment to ADR 0024 ships with #842, as does one to ADR 0049 (`:148-184`), which declined
to bring `raw_payload` into the DEK envelope partly in order **not to break the `JsonContains` Art. 17
mechanism** — a mechanism that was already structurally broken. Its conclusion may survive on other
merits; its stated justification cannot.

### B7 — What replaces §8's PII story: ADR 0106's two-tier contract (BOUND, **not yet shipped**).

> **SUPERSEDED IN PART, 2026-07-26 (#845):** both tiers have shipped — **Tier B** in `269a4603` and
> **Tier A** in `daa4b51d`, with the command, handler, validator and `ErasureCascadeRegistry` under
> `src/Jobbliggaren.Application/JobAds/Commands/EraseRecruiterAds/`. **The ship dates live in one
> place — Amendment 2026-07-26 §C6** (the dates are what went stale; the commit hashes do not).
> The "UNSHIPPED" tense below, and B8's *"no automated erasure path exists"*, are **false from the
> earlier of those two commits onward**. **The prod gate's status is owned solely by ADR 0106 and is
> not restated here** — this entry neither declares it lifted nor re-asserts it. See §C6, and the
> Art. 12(3)/17 routing risk flagged to Klas on PR #1089.

> **Tense discipline, deliberately:** neither tier exists in the code today. Describing a control we do
> not have is the exact defect this amendment corrects. **Tier A and Tier B are BOUND (CTO 2026-07-13) and
> UNSHIPPED.** What has already landed on this branch is PR1 only: the endpoint now returns **501** with a
> truthful problem detail, `RecruiterPiiPurger` + `IRecruiterPiiPurger` + the `RedactRecruiterPii`
> command/handler are **deleted**, the test fiction is rewritten (#843), and the source docs are
> truth-synced. **PR1 is containment and truth. It is not a fix.**

**Tier A — Art. 25 (everyone, no request needed, heuristic, DISCLOSED).** We do not *store* recruiter
contact details: email and phone are stripped from the ad body **at ingest, as a `JobAd` aggregate
invariant** (`RecruiterContactRedactor`, deterministic, `GeneratedRegex`, **no LLM** — ADR 0071 /
CLAUDE.md §5), and replaced with a marker pointing to the canonical ad at Arbetsförmedlingen. Placement in
the aggregate (`JobAd.Import` + `JobAd.UpdateFromSource`) is what makes it durable: the nightly backfill
and the 10-minute stream both rewrite through `UpdateFromSource`, so the scrub is re-applied on **every**
write — **no suppression ledger, no tombstone column, no migration.** Detection is imperfect **and we say
so**. Ships in PR2, with a measured precision/recall number for the DPIA (gold set is a **local, never
committed** artefact — this repo is public, ADR 0072).

**Tier B — Art. 17 (on request, PROVABLE, no detector involved).** On a valid request we **remove the
entire ad record** (`JobAdStatus.Erased` — a fourth SmartEnum value on the existing string column, **zero
migration**) and **block its re-import** (`UpdateFromSource` refuses on `Erased`, keyed by the existing
`(source, external_id)` UNIQUE tuple; the detail route returns **410 Gone**). It deletes the **carrier**,
not the **string**: `description`, `search_vector`, `extracted_terms`, `extracted_lexemes`, `raw_payload`
and the seven derived columns all go together. **No recall question, no obfuscation question, no
image-embedded question — and it covers the recruiter's NAME**, which no regex can reach. Ships in PR3.

**Each tier is what makes the other honest.** Tier A alone leaves no honest answer to a request (Art.
17(1) is textually **unqualified**; the *"reasonable steps / available technology"* language lives **only**
in Art. 17(2), which governs informing **other** controllers — not erasure from our own store — and no
EDPB/IMY/DPA authority accepts best-effort erasure of unstructured free text). Tier B alone would leave us
hoarding 27 077 recruiters' contact details that we do not need at all. And Art. 12(3) is why the vacuous
path had to be hard-failed **today**: a mechanism that reports success while erasing nothing manufactures
a false statement to a data subject — an **independent** breach on top of the Art. 17 failure.

**Google Spain (C-131/12) cuts both ways, and both halves are load-bearing:** it legitimises *"we erase
our copy; we cannot erase Arbetsförmedlingen's"*, and it **forecloses** refusing a request on the ground
that the ad is already published — which is precisely the argument `PlatsbankenJobSource.cs:203` leans on.

**Bound disclosure wording** (substance bound by the CTO; Swedish per CLAUDE.md §10 — "du", no em-dash, no
exclamation marks, no emoji):

- *Privacy policy (Tier A):* "Vi hämtar annonstexter från Platsbanken. Innan en annons sparas tar vi
  automatiskt bort e-postadresser och telefonnummer ur annonstexten. Kontaktuppgifterna finns kvar i
  originalannonsen hos Arbetsförmedlingen, som vi länkar till. Borttagningen är regelbaserad och kan missa
  uppgifter som skrivits på ovanliga sätt eller som ligger i en bild."
- *Erasure contract (Tier B):* "Om du begär radering av dina kontaktuppgifter i en annons vi har hämtat tar
  vi bort hela annonsen ur våra system och hindrar att den hämtas in igen. Vi kan inte ta bort annonsen hos
  Arbetsförmedlingen, som är den som publicerat den."

**No migration, either tier** (CTO V10): `job_ads.status` has no CHECK constraint and no PG enum type, and
`audit_log.payload` (jsonb) already exists. ⇒ **#842 does not touch, block or wait on the #821/#841
migration lane**, and `db-migration-writer` is deliberately not invoked.

### B8 — Shipping order and what lifts the gate

> **SUPERSEDED IN PART, 2026-07-26 (#845) — read this before acting on anything below.** Every
> shipping claim in this section is false: **Tier A shipped** in `daa4b51d` and **Tier B shipped** in
> `269a4603` — **ship dates in §C6, which is their one home** — with the command,
> handler, validator and `ErasureCascadeRegistry` under
> `src/Jobbliggaren.Application/JobAds/Commands/EraseRecruiterAds/`. **Tier + commit is the only
> unambiguous identification** — the commit subjects number the two tiers in the reverse order from
> this section's table, so the PR ordinals are deliberately not restated here.
>
> **An Art. 17 request must NOT be routed to the manual workaround this section prescribes.** The
> sentence *"no automated erasure path exists"* is untrue, and the runbook it cites no longer says
> it. Use `EraseRecruiterAdsCommand` (dry-run, then confirm) — it is provable
> and blocks re-import. Routing a live request to a manual workaround on the strength of this section
> is an Art. 12(3)/17(1) failure, which is why this pointer sits here and not only in the amendment.
>
> **This banner covers every GATE claim below, not only the shipping claims** — the `Gate` column and
> the closing *"Until PR3 is merged … no `v*` prod tag"* sentence included. **The prod gate's status
> is owned solely by ADR 0106** and is not restated in this ADR; see Amendment 2026-07-26 §C6.

| PR | Scope | Status | Gate |
|---|---|---|---|
| **PR1** | Containment + truth: endpoint → **501**; purger + command **deleted**; test fiction rewritten (#843); source docs + this amendment truth-synced | **Committed on this branch** | Does not lift |
| **PR2** | **Tier A** — ingest scrub as a `JobAd` aggregate invariant + backfill of all 93 469 ads + measured recall + privacy-policy disclosure | **Bound, not shipped** | Does not lift |
| **PR3** | **Tier B** — `JobAdStatus.Erased`, re-import block, 410 Gone, dry-run-first erasure command, HMAC-SHA256 audit payload (never md5) | **Bound, not shipped** | **LIFTS the gate** |

Until PR3 is merged with green provable-erasure tests: **no `v*` prod tag.** An Art. 17 request arriving
before then is escalated to Klas and handled manually — the runbook (PR1) now says exactly that, and says
plainly that no automated erasure path exists.

### Referenser
- **Ruling:** `docs/reviews/2026-07-13-842-erasure-contract-cto.md` (senior-cto-advisor, BOUND — V1/V3/V5/V10/V14/V17, STOPP-1..6)
- **Evidence:** `docs/research/2026-07-13-842-erasure-evidence-pack.md` (§1 the defect, §2 surface inventory, §5 false doc claims, §9 measurements on 93 469 ads)
- **Prior review:** `docs/reviews/2026-07-12-824-dpia-archived-ad-security.md`
- **Governing contract:** **ADR 0106** (local/gitignored per ADR 0072) · amendments shipped with #842: **ADR 0024** (`:467-472`, Art. 17 cascade registry) · **ADR 0049** (`:148-184`, DEK-envelope exclusion) · **ADR 0087** D8(a) withdrawal (drafted verbatim in A3 above)
- **Law:** GDPR Art. 5(1)(c)/(2), 12(3), 17(1)/(2), 25(2), 30 — https://gdpr-info.eu (accessed 2026-07-13) · CJEU **C-131/12** *Google Spain* (13 May 2014)
- **Issues:** #842 (this) · #843 (test fiction) · #845 (retention rule, doc-only) · #824/#841 (generated-column blast radius) · #821 (`DeletedAt` never written)

## Amendment 2026-07-26 - the retention rule, stated once and correctly (#845)

**Status:** Accepted - does **not** supersede A2/B4(a) wholesale. They were right that the documented
rule was false, and wrong about what replaces it. This entry is the **rule's home**; **C3 defines the
sweep that is supposed to keep it the only one, and is deliberately the only place that says anything
about coverage.** Three drafts of this amendment asserted completeness here and were falsified by the
next review pass - the last of them while six live claims stood in `tests/`. A completeness claim in
the record's own status line is the defect this amendment exists to remove, written in the register
that removes it.

### C1 - A2's and B4(a)'s replacement rule is also false

A2 and B4(a) write *"30 days after the ad leaves the feed"*. That is closer than the `published_at`
rule and still wrong, in **both** directions:

- an ad **delisted on day 10** is purged on day 30, while that rule claims day 40 - **ten days
  before** it;
- an ad **listed for a year** loses its payload about a day after delisting - **twenty-nine days
  before** it.

**This is the single home of that arithmetic.** Every banner correcting a retention wording elsewhere
in this ADR, and the Art. 30 register's retention entry, point here rather than restating a day count -
the restatement is what produced four versions of one rule. Note the trap the first two attempts both
fell into: *"the purge happens twenty days after the ad left the feed"* is a true statement about **when
the purge fires**, and is **not** the error in the rule. The error is the gap between what the rule
**claims** (day 40) and what the code **does** (day 30), which is ten.

The rule was never "N days after event X". Writing it in would have given this ADR a **fourth** false
retention sentence, and the wording had already spread well past the three files #845 named - into
source, ADRs, the ROPA, the runbooks and the test suites alike.

### C2 - the rule

> `raw_payload` is nulled by `PurgeStaleRawPayloadsJob` (registered as
> `RecurringJobIds.PurgeStaleRawPayloads` in `RecurringJobRegistrar`; cron times are UTC, Hangfire
> default) on every ad older than `RawPayloadRetentionDays` (default 30, measured from
> `published_at`). **The purge is not durable while the ad is being re-ingested:** every re-ingest
> route writes through `JobAd.UpdateFromSource`, which reassigns the payload. **The payload is
> therefore durably absent only from the first purge run after BOTH (a) the ad is older than the
> retention window AND (b) nothing is re-ingesting it** - whichever comes later. Equivalently:
> **effective erasure = `max(published_at + threshold, last re-ingest + one purge period)`.** An
> Art. 17 erasure short-circuits both clauses: `JobAd.Erase` nulls the payload
> immediately and `UpdateFromSource` refuses on `Erased`, so nothing restores it.

A grep for `0 2 * * *` finds no registration (the
snapshot is registered as `Cron.Daily(2)`), and an enumeration of writers goes stale the moment a
fifth one is added; clause (b) is written to cover any re-ingest route, named or not.

**The generalisable rule - apply this, do not just copy the paragraph above:**

> **A retention claim about a column that a periodic job REWRITES must state the rewrite as part of
> the rule. The effective deletion date of a re-written column is `max(purge-eligibility-date,
> last-write + purge-period)`, never the purge predicate alone.**

It discriminates rather than alarming everywhere. For `audit_log` partitions and `ParsedResume`
staging rows no ingest cron rewrites the column: the rewrite term is zero, `max()` collapses to the
purge predicate, and the rule is correctly silent. For `job_ad_snapshot_misses` - written on the
nightly sync, consumed by `RetainPlatsbankenJobAdsJob` - it fires.

**Purpose, because Art. 5(1)(e) needs something to measure necessity against.** `raw_payload` exists
for debug and replay of ingest and matching determinism **while the ad is live and searchable**. That
purpose ends at delisting, which is exactly why the rule's long regime ends about a day after the last
re-ingest rather than thirty days later. Retaining the sanitized payload of a currently-published,
publicly-listed ad is necessary for a stated purpose. Retaining it past delisting is not, and the
rule's **long** regime does not - it ends about a day after the last re-ingest. Its **short** regime
does retain past delisting, by up to the threshold: an ad delisted on day 10 keeps its payload until
day 30. That window is bounded, stated here rather than glossed, and it is the honest limit of the
necessity argument.

**The code does not change.** A2's original ground for refusing a hash short-circuit is dead - #841
shipped, so suppressing the rewrite no longer converts an intermittent defect into a permanent one -
but the ground is **replaced, not repealed**: a short-circuit breaks ADR 0106 D4's durability
argument, because the ingest scrub is structural only while every rewrite unconditionally reaches
`ApplyContactRedaction`. It would also freeze `ExtractedTerms` against a changing taxonomy (#874) and
misread `Remote`'s PRESERVE semantics (#551). A documentation defect traded for an architecture defect
- rejected.

### C3 - one home; `src/` carries pointers

**Scope of the done-condition, written down because getting it wrong is how this defect survives:**
the claim-shaped grep runs over `src/`, `tests/`, `docs/decisions/` **and** `docs/runbooks/`. The
first pass of this amendment measured only the first and third and reported "zero live false claims"
on that basis - scoped-true and globally false, with live claims left in `tests/` and in the ROPA.

**The grep is the authority, never a file list.** Search for the claim's *shape* - any co-occurrence
of `raw_payload`/`RawPayload` with a duration, or with `purge`/`retention` - and require zero
un-truth-synced hits at your HEAD. **Do not record a site count here.** Every count this amendment's
drafts wrote was falsified by the next pass, including one that claimed the sweep was complete while
`tests/` still carried six live claims - three inside architecture-guard messages, which a green
suite cannot catch because they are prose. The grep is the record.

**Never put a retention duration back into a `src/` comment.** A rule living in thirteen places goes
stale in twelve of them, and that is precisely how this ADR accumulated four different false versions
of one sentence. State the *consequence* locally where the local reader needs it; name this section
for the rule.

### C4 - the risk register: the defect is the inverse of the one #845 reports

#845 says both mitigations *"are ineffective"* and asks for that to be recorded honestly. **That is no
longer true at HEAD** - #842 closed 2026-07-17 (Tier A `daa4b51d`, Tier B `269a4603`), so the
aggravating circumstance the issue rests on does not hold, and writing "no effective mitigation" would
have created a **new** false sentence. The honest form is reach per control:

| Control | Reaches | Does NOT reach | Covered by |
|---|---|---|---|
| `JobTechPayloadSanitizer` (allowlist) | structured contact **keys** | every **value**; free-text keys are retained by design | defense-in-depth, never a free-text control |
| `PurgeStaleRawPayloadsJob` | `raw_payload`, per C2 | every other surface carrying the same text - `description`, `search_vector`, `extracted_terms`, and the frozen `applications.snapshot_*` columns among them | not a PII control - payload retention, which is all it ever was |
| `RecruiterContactRedactor` at ingest (Tier A) | detected **email and Swedish phone** spans in `Title`/`Description`/`RawPayload`, on every write, in every status | recruiter **names** (no NER - ADR 0106 D5); obfuscated forms such as `anna(at)acme.se`; contacts inside images | Tier B |
| Whole-record erasure (Tier B) | the **carrier**, name included; provable; re-import blocked | applicants' frozen `AdSnapshot` (ADR 0106 D9, recorded exception); requires a **request** | nothing - this is the floor |

**Written residual.** Names and obfuscated forms are outside Tier A **by design**; Tier B is
request-driven. Two further residuals that are ours and owned: (a) `AdSnapshot` copies frozen
**before 2026-07-17** hold un-scrubbed contact text and are reached by neither tier - Tier A never
rewrites them, Tier B exempts them per D9; (b) a nulled payload survives in database backups for the
backup retention period. Neither is "no mitigation", and neither is "covered".

### C5 - the axis stands. Ruled, not deferred.

`RawPayloadRetentionDays` measured from `published_at` is kept, and the doc's **verb** was the defect,
not the arithmetic: the value is a purge-**eligibility threshold**, never a guaranteed lifetime. Both
options classes now say so. #845 task 4 is therefore **closed**, not open.

The argument for keeping it is the purpose test in C2, **not** "the error direction is always less
retention". That comparative claim was in this amendment's first draft and is withdrawn: a rule is not
lawful under Art. 5(1)(e) because it could have been worse, and in the regime where retention is
*longest* - a long-listed ad - the binding term is `last-write + purge-period`, not the axis at all.
Defending the threshold as though it were the rule contradicts C2, which proves it is not.

### C6 - what #842 actually shipped, and who owns the gate

B7 and B8 still describe both tiers as **BOUND and UNSHIPPED** and state that *"no automated erasure
path exists"*. Measured against `git log`: `269a4603` shipped Tier B (Art. 17 whole-record erasure,
durable against re-import) on **2026-07-15** and `daa4b51d` shipped Tier A (Art. 25 at ingest) on
**2026-07-17**. #842 itself closed 2026-07-17 - the issue date, which this amendment's first draft
wrongly attached to both commits. The shipped surface is
`src/Jobbliggaren.Application/JobAds/Commands/EraseRecruiterAds/`, which carries the command, handler,
validator and `ErasureCascadeRegistry`; `RecruiterErasureIngestTests` and `ErasedAdReadPathTests` run
green against Testcontainers Postgres (34 tests, re-verified 2026-07-26).

**This is the authoritative home of the ship dates, and the rule is a rule, not a claim about the
tree: prefer tier + commit hash wherever a ship is referenced, and cite this section for the date.**
A hash is a stable identifier and cannot go stale; the date is precisely what did - *"both shipped
2026-07-17"* survived two review rounds because one date sat in several homes.

**No count and no file list appears here, deliberately.** Four consecutive drafts of this amendment
asserted that some enumerated set of sites had been cleaned, and the tree falsified every one of them
- the fourth while the same commit was adding fresh dated mentions elsewhere in its own diff. C3
states this discipline for the retention arithmetic and gives the reason in its own words - **"The grep
is the authority, never a file list."** It is quoted rather than reworded on purpose: two phrasings of
one rule is the defect this amendment exists to remove. C1 holds the arithmetic's single home.

This entry records only what shipped. It does **not** declare the prod gate lifted and does **not**
re-assert it: **ADR 0106 is the gate's sole owner** and its status is described only there. The reason
this matters today rather than at a future tag is that B8 directs whoever receives an Art. 17 request
to escalate and handle it manually *"because no automated erasure path exists"* - so a false record in
the governing ADR routes a live rights request away from the automated path that exists. Art. 5(2)/24
with an Art. 12(3)/17 consequence. **Klas ruled on the gate's status on 2026-07-26 (PR #1089); the
ruling is recorded in ADR 0106, which is gitignored per ADR 0072 - not restated here.**
