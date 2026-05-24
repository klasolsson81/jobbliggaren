# dotnet-architect-rond — STEG 6 backfill-design (Approach A)

**Datum:** 2026-05-24
**Agent:** dotnet-architect (agentId `a2f3999e4202b89eb`)
**Triggrad av:** CC post-discovery (Hypotes 1 falsifierad; smoking gun: snapshot trunkerar ~10k/dag).
**Källa-bilagor:** [`2026-05-24-steg6-fas-a-discovery.md`](./2026-05-24-steg6-fas-a-discovery.md), [`2026-05-24-steg6-cto-multiapproach.md`](./2026-05-24-steg6-cto-multiapproach.md)

---

## Arkitektur-analys

### Sammanfattning

Behöver åtgärdas — designen behöver justeras innan implementation. **S2 är rätt riktning** men med tre konkreta korrigeringar. S1 är otillräcklig (probabilistisk coverage, ej idempotent verifierbar). S3 är ADR 0032-amendment-veto och avvisas. Tidsbudget 4-6h håller om scope begränsas strikt till backfill-jobbet utan att utvidga `JobAd`-aggregatet eller skapa nya domain-koncept.

### Fynd

---

**[Kritiskt — designval]** Strategi-val: S2 vinner, S1 avvisas, S3 vetoas

**Vad:** Tre strategier presenterade. Endast S2 löser problemet inom ramverket.

**Varför:**

- **S1 (manuell snapshot-trigger × 4-5):** Bryter mot DoD §8 punkt 1 ("Implementerad enligt acceptance criteria") — "garanterat full coverage" är acceptanskriterium för en sync-rot-fix. Probabilistisk overlap är inte deterministisk coverage. 35k pre-2026-05-20-fix-rader saknar `occupation`-key i `raw_payload` — re-fetch via snapshot förutsätter att JobTech `/snapshot` faktiskt råkar inkludera just dessa IDs i sin trunkerade prefix. Det är inte design, det är hopp. Dessutom: STORED column re-evaluerar bara vid `UPDATE` av `raw_payload` — om snapshot-pipelinens `existing.UpdateFromSource` skickar samma payload (utan occupation, för att JobTech-stream inte returnerar occupation för redan-publicerade jobb) blir det no-op. **Avvisas.**

- **S2 (dedikerad backfill-job mot `/search`):** Enda strategi med deterministisk coverage. JobTech `jobsearch.api.jobtechdev.se/search` har redan port (`IJobTechSearchClient`) — den är minimerad (TD-70 noterar utbyggnad pending). Återanvänder befintlig `UpsertExternalJobAdCommand`-pipeline → ADR 0032 §5 race-safety, audit, miss-tracking-isolering (backfill kör inte miss-tracking — viktigt, se nedan) bevaras kostnadsfritt. **Vald.**

- **S3 (snapshot-loop-utvidgning):** Direkt ADR 0032-amendment 2026-05-16-veto. "Bounded retry kapar kostnaden (~3 min vid JobTech 1 req/min) — resten fylls av hybrid stream-katch-up + nästa cron." Att höja `MaxSnapshotAttempts` eller lägga en re-fetch-pass underminerar amendment-disciplinen som etablerades efter Hangfire-storm-incidenten (60 starts / 0 completes). **Vetoas.**

**Föreslagen åtgärd:** Implementera S2. Detaljer nedan.

---

**[Kritiskt — port-design]** Fråga 2: `FetchAdByIdAsync` ska vara metod på befintlig `IJobTechSearchClient`, INTE ny port

**Vad:** Klas frågar om `FetchAdAsync(externalId)` ska vara ny separat port eller utvidga `IJobTechSearchClient` / `IJobTechStreamClient`.

**Varför:** Tre tekniska skäl + ett arkitekturellt:

1. **API-host-grouping (CLAUDE.md §2.1 + DIP):** `IJobTechSearchClient` är Refit-bundet till `jobsearch.api.jobtechdev.se`. `IJobTechStreamClient` är bundet till `jobstream.api.jobtechdev.se`. Per-ID-fetch (`GET /ad/{id}`) ligger på **jobsearch**-hosten, inte jobstream. Att lägga den på `IJobTechStreamClient` skulle tvinga andra base-URL eller två HttpClients per port — bryter Refit-konventionen som etablerades i ADR 0032 §2.

2. **Single Responsibility (Robert Martin 2017 kap. 7):** `IJobTechSearchClient` har redan ansvar för "hitta annonser via jobsearch-API:t". `SearchAsync` är fritextsökning; `GetByIdAsync` är direktslagning — samma ansvarsområde, olika query-shape. Att skapa ett tredje interface enbart för backfill är YAGNI.

3. **Minimal yta:** TD-70 noterar att `IJobTechSearchClient` ska byggas ut med filter. Att lägga `GetByIdAsync` här nu pre-positionerar TD-70-arbetet utan kostnad.

4. **Återanvändning:** Om någon framtida feature behöver "slå upp en specifik annons från JobTech" (t.ex. user delar en länk till en JobTech-annons som ännu inte finns i vår DB) har vi porten redan.

**Föreslagen åtgärd:**

```csharp
// I IJobTechSearchClient.cs
[Get("/ad/{id}")]
Task<JobTechHit?> GetAdByIdAsync(
    string id,
    CancellationToken cancellationToken = default);
```

`JobTechHit` är redan rätt shape (returneras direkt, inte wrappad i `JobTechSearchResponse`). Refit deserialiserar 404 → `null` om interface returnerar nullable + `[Get]` utan `ApiResponse<T>`. Verifiera mot Refit-version i `Directory.Packages.props` — om Refit kastar `ApiException` vid 404 istället, fångas i wrapper (se Fynd 3).

---

**[Kritiskt — Application-port]** Backfill ska gå via ny Application-port, inte direkt mot Infrastructure-interface

**Vad:** `IJobTechSearchClient` är `internal` i Infrastructure (per ADR 0032 §2). Application-lagret (där `BackfillJobAdSsykJob` ska bo per §2.1 — det är en Hangfire-job-orchestrator precis som `SyncPlatsbankenSnapshotJob`) får inte se den. Behövs en `IJobSource`-utvidgning eller ny port.

**Varför:** CLAUDE.md §2.1: "Application beror på Domain, definierar alla interfaces som Infrastructure implementerar." Att låta backfill-jobbet anropa JobTech direkt skulle bryta Clean Arch-gränsen.

**Föreslagen åtgärd:** Utvidga `IJobSource` (eller skapa kompletterande `IJobAdRefetcher`-port — välj per nedan):

**Variant A — utvidga `IJobSource`:**
```csharp
// I JobbPilot.Application.JobAds.Abstractions.IJobSource
Task<JobAdImportItem?> RefetchByExternalIdAsync(
    string externalId, CancellationToken cancellationToken);
```

`PlatsbankenJobSource` implementerar genom att anropa `IJobTechSearchClient.GetAdByIdAsync` + `TryConvertToImportItem` (redan privat metod, line 159). Återanvänder `JobTechPayloadSanitizer`, samma URL-fallback-logik, samma defensive skip-villkor. **Noll dubblering.**

**Variant B — separat `IJobAdRefetcher`-port:**

Endast om `IJobSource` har andra implementationer än Platsbanken där per-ID-refetch är meningslöst. Idag är `PlatsbankenJobSource` enda implementation → YAGNI mot Variant B.

**Variant A vinner.** Returnerar `null` för 404 (annons borttagen från JobTech). Caller (backfill-jobbet) avgör semantik vid `null` — se Fynd 4.

---

**[Viktigt — rate-limit]** Fråga 3: Polly-pipeline räcker, men separat throttle behövs för human-pace

**Vad:** Klas frågar om befintlig Polly-pipeline på HttpClient räcker.

**Varför:** Polly-pipeline (retry + CB + rate-limit) i `AddJobSources` skyddar mot **transient fel och burst-skydd** — inte mot ett medvetet 35k-sekventiellt drag. JobTech jobsearch-API:s rate-limit är inte web-verifierad för `/ad/{id}` (snapshot-doc anger ~1 req/min för `/v2/snapshot`; `/search` brukar tillåta ~100-500 req/min per app-key men jag har inte verifierat detta i denna context — det MÅSTE web-search:as innan implementation).

Klas-uppskattning 200ms per request × 35k = 7000s = ~2h. Det är 5 req/s = 300 req/min. Om JobTech-limit ligger under det utan att Polly:s rate-limiter är konfigurerad för det specifikt → 429-storm + CB-trip.

**Föreslagen åtgärd:**

1. **Web-search obligatoriskt innan kod:** "JobTech jobsearch API rate limit" + "jobtechdev.se /ad endpoint rate limit". Verifiera datum på källan. Bifoga URL i STOPP-rapport.

2. **Backfill-jobbet kör sekventiellt med explicit `Task.Delay`** mellan items istället för att lita på Polly. Skäl: Polly:s rate-limiter köar — vid 35k items skulle kö-djupet bli enormt och timeout-risken hög. Explicit `Task.Delay(rateInterval, ct)` mellan iterations är simpler och read-as-written:

```csharp
// I BackfillJobAdSsykJob (Application-lagret)
// Konfigurerbar via IOptions<BackfillOptions> med default = 200ms
await Task.Delay(options.Value.PerItemDelay, cancellationToken);
```

3. **Polly behålls för transient-fel-skydd** (5xx, network) — som idag. Ingen ny resilience-konfig.

4. **CancellationToken propageras hela vägen** (CLAUDE.md §3.5) → om Klas behöver stoppa backfill mid-run via Hangfire-dashboard fungerar det.

---

**[Kritiskt — 404-semantik]** Fråga 5: 404 från JobTech `/ad/{id}` ska markera jobb som expired, inte deletea

**Vad:** Annons borttagen från Platsbanken (404) — hur hanteras den i backfill?

**Varför:** ADR 0032-amendment 2026-05-23 etablerade retention-disciplin via miss-tracking-tabell. Backfill-jobbet är INTE samma flöde som retention — det vore fel att låta backfill mass-arkivera 5-10k annonser baserat på 404 från enskild GET. Skäl:

- JobTech `/ad/{id}` kan returnera 404 transient (cache-miss på deras CDN) — inte konfirmerad borttagning
- Retention-flödet använder snapshot-baselin (max_7d × ratio) + miss-counter över flera körningar för att skydda mot exakt detta
- Backfill mass-arkivering vid 404 skulle skapa den falska-arkivering-risk som CTO-rond 2026-05-23 Q5 floor-skydd specifikt motverkar

**Föreslagen åtgärd:** Backfill-jobbet hanterar 404 som **"skip, log, count"** — inget delete, ingen archive, ingen miss-tracking-påverkan.

```csharp
// I BackfillJobAdSsykJob.RunAsync, per-item
var refetched = await jobSource.RefetchByExternalIdAsync(externalId, ct);
if (refetched is null)
{
    counts.NotFoundOnSource++;
    LogBackfillItemNotFound(logger, externalId);
    continue;
    // Inget annat. Retention-flödet hanterar arkivering via sin egen
    // baseline-disciplin. Backfill rör INTE den tabellen.
}
```

Räkneverk `NotFoundOnSource` rapporteras i `SyncCounts`-motsvarighet (`BackfillCounts`) så Klas kan se efter körning hur många som var verkligt borta. Om talet är högt (>5% av input) → manuell utredning innan retention-jobbet ev. arkiverar dem nästa körning.

---

**[Viktigt — race vs snapshot]** Fråga 4: Idempotens-strategi

**Vad:** `WHERE ssyk_concept_id IS NULL`-filter inte race-säkert mot samtidig snapshot.

**Varför:** Snapshot kör 02:00 UTC dagligen. Backfill kör manuellt. Sannolikheten för verklig kollision är låg, men inte noll om Klas råkar trigga backfill mid-snapshot-fönster. Race-mönstret:

1. Backfill query:ar IDs med `ssyk_concept_id IS NULL` → får 35 384 IDs
2. Snapshot uppdaterar samtidigt en delmängd → deras `ssyk_concept_id` blir satta
3. Backfill iterar IDs och GET:ar mot JobTech även för redan-fixade

Detta är inte korruption — det är onödigt arbete. `UpsertExternalJobAdCommand`-pipelinen är race-säker via UNIQUE-index (ADR 0032 §5) och `UpdateFromSource` är idempotent om payload är samma.

**Föreslagen åtgärd:** **Acceptera race som no-op-overhead** (Variant A) ELLER **paginera per körning med ID-cursor** (Variant B).

- **Variant A:** Lås ej. Worst case = backfill gör några hundra onödiga GETs mot JobTech. Acceptabelt eftersom snapshot endast kör 1x/dag och Klas kan välja tidpunkt.
- **Variant B:** Kör backfill i batches om 100 med `.OrderBy(j => j.Id).Skip(offset).Take(100)` + per-batch re-query av `IS NULL`-filter. Reduces wasted work om snapshot kör samtidigt. **Lägger till komplexitet utan tydligt värde.**

**Variant A vinner.** Klas väljer tidpunkt för backfill (söndag eftermiddag, snapshot kör 02:00 UTC nästa natt). Pre-check i jobbet kan logga warning om snapshot-job är aktiv:

```csharp
// Optional pre-flight check
var activeSnapshotJob = await /* Hangfire-API */;
if (activeSnapshotJob) LogBackfillStartedDuringSnapshot(logger);
// Fortsätt ändå — race är no-op, inte korruption.
```

Eller skip pre-flight helt — YAGNI tills race faktiskt observeras.

---

**[Viktigt — query och paginering]** Backfill-query måste vara stream-baserad, inte materialiserad

**Vad:** 35 384 rader är för många för `.ToListAsync()` upfront — bryter ADR 0045 Worker-mem soft cap 512 MiB om kombinerat med andra workers.

**Varför:** CLAUDE.md §3.6: "Paginering via `.Skip().Take()` med total count i separate query." Plus minneshygien.

**Föreslagen åtgärd:** EF Core `IAsyncEnumerable` via `.AsAsyncEnumerable()` ELLER explicit cursor-paginering:

```csharp
// I BackfillJobAdSsykJob
// AsAsyncEnumerable - streamar utan materialisering
var query = db.JobAds
    .Where(j => EF.Property<string?>(j, "SsykConceptId") == null
                && j.External != null
                && j.External.Source == JobSource.Platsbanken)
    .OrderBy(j => j.Id)
    .Select(j => j.External!.ExternalId)
    .AsNoTracking()
    .AsAsyncEnumerable();

await foreach (var externalId in query.WithCancellation(ct))
{
    // Per-item: child-scope + RefetchByExternalIdAsync + UpsertExternalJobAdCommand
    // (samma mönster som SyncPlatsbankenSnapshotJob.cs:65-104)
}
```

Återanvänder child-scope-mönstret från `SyncPlatsbankenSnapshotJob` exakt — ADR 0032 §5 single-command-scope-antagandet bevaras.

**Varning:** Verifiera att Npgsql + EF Core 10 streamar `AsAsyncEnumerable` korrekt (inte buffrar hela result-set i klientminne). MEMORY.md noterar `EF Core 10 + Npgsql Contains med strongly-typed VO`-translation-fel — sanity-check här att enkla `Where`/`OrderBy` mot shadow-property fungerar. Om problem: cursor-paginering med `Skip().Take(1000)` per batch som fallback.

---

**[Viktigt — pipeline-bypass]** Backfill får INTE påverka `IJobAdSnapshotMissTracker`

**Vad:** Per-item-upsert i backfill kommer gå genom samma `UpsertExternalJobAdCommand` → ingen miss-tracking-påverkan eftersom miss-tracking sker i `SyncPlatsbankenSnapshotJob`, inte i handler.

**Varför:** Bra design — handler är clean. Men `BackfillJobAdSsykJob` får INTE av misstag anropa `missTracker.ApplyAsync` även om det är "samma flöde i övrigt".

**Föreslagen åtgärd:** `BackfillJobAdSsykJob` har INTE `IJobAdSnapshotMissTracker` som constructor-dependency. Endast: `IJobSource`, `IServiceScopeFactory`, `IAppDbContext` (för IS NULL-query — kan injiceras direkt i job-scope), `IDateTimeProvider`, `ISystemEventAuditor`, `ILogger<...>`, `IOptions<BackfillOptions>`. Audit-rad skrivs (separat `JobAdsBackfilled`-event vs återanvändning av `JobAdsSynced` med `JobType: "backfill"` — välj återanvändning för att inte uppfinna nytt audit-koncept under MVP-press).

---

**[Viktigt — Hangfire-registrering]** Job ska INTE vara RecurringJob — kör som fire-and-forget

**Vad:** `BackfillJobAdSsykJob` är engångs-operation, inte cron.

**Varför:** Idempotent men tidsbunden till MVP-demo. Att lägga en cron-schedule skulle skapa permanent operativ skuld.

**Föreslagen åtgärd:**

- INTE registrerad i `RecurringJobRegistrar.cs` som cron
- Admin-endpoint (`POST /admin/jobs/backfill-ssyk` med `JobAdAdministrationPolicy`-auth per ADR 0032 §3-konvention) enqueueas via `IBackgroundJobClient.Enqueue<BackfillJobAdSsykJob>(j => j.RunAsync(CancellationToken.None))`
- `DisableConcurrentExecution(timeoutInSeconds: 7200)` attribute så Klas inte kan trigga två parallella körningar
- Worker-Hosting class (motsvarande `SyncPlatsbankenSnapshotWorker.cs`) behövs inte — fire-and-forget körs av befintlig Hangfire-worker-pool

---

**[Nice-to-have — rapportering]** `BackfillCounts` ska exponeras i admin-response

**Vad:** Klas vill se efter 2h körning hur det gick.

**Föreslagen åtgärd:**

```csharp
public sealed class BackfillCounts
{
    public int Fetched { get; set; }          // hämtade från DB (= 35 384)
    public int RefetchAttempted { get; set; } // anropade JobTech
    public int Updated { get; set; }          // SSYK satt efter update
    public int NotFoundOnSource { get; set; } // 404 från JobTech
    public int Errors { get; set; }
    public int RateLimitRetries { get; set; } // Polly transient-räknare
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
```

Hangfire-state-snapshot via dashboard räcker för MVP — ingen ny endpoint behövs för att läsa counts. Klas följer i `LogCompleted`-loggar (CloudWatch /aws/ecs/jobbpilot-dev/worker).

---

### Svar på Klas frågor (kondenserat)

1. **S2.** S1 probabilistisk, S3 ADR-veto.
2. **Utvidga `IJobTechSearchClient` med `GetAdByIdAsync`.** Ny `IJobSource.RefetchByExternalIdAsync`-metod i Application-lagret. Variant A.
3. **Polly räcker INTE för 35k sekventiellt.** Explicit `Task.Delay` per item + web-search-verifiering av JobTech `/search`-rate-limit innan implementation.
4. **Acceptera race som no-op-overhead.** Worst case = onödig nätverkstrafik. Snapshot 02:00 UTC, Klas väljer backfill-tidpunkt.
5. **404 = skip + log + count.** Inget delete, ingen archive, ingen miss-tracking-påverkan. Räkneverk `NotFoundOnSource`.
6. **Scope ÄR inom "trivial" relativt 1-dags-tröskel.** ~6h estimerat (port-utvidgning 30 min, backfill-job 2h, tests 1.5h, DI + admin-endpoint 1h, web-search + verifiering 1h). STOPP C till Klas före push, inte före kod.
7. **MaxSnapshotAttempts ändras INTE.** ADR 0032-amendment-disciplin bevaras. Backfill är operativ engångs-fix för pre-existing data-skuld från före `JobTechHit.Occupation`-fixen 2026-05-20. Ny TD vid behov: TD-NN "ta bort `BackfillJobAdSsykJob` när 30 dagar utan körning verifierat".

### Implementations-checklista (för Klas att verifiera mot CC-pushar)

1. `IJobTechSearchClient.GetAdByIdAsync(string id, ct)` tillagd — Refit, nullable return för 404
2. `IJobSource.RefetchByExternalIdAsync(string externalId, ct)` tillagd i Application
3. `PlatsbankenJobSource.RefetchByExternalIdAsync` implementerad — återanvänder `TryConvertToImportItem` (privat metod redan finns)
4. `BackfillJobAdSsykJob` i Application — child-scope per item, sekventiell `Task.Delay` mellan iterations
5. `BackfillOptions` (`PerItemDelay`, default 200ms) — `IOptions<T>` per CLAUDE.md anti-pattern-listan
6. Admin-endpoint i Api — enqueueas via `IBackgroundJobClient`, `JobAdAdministrationPolicy`-skyddad
7. `BackfillJobAdSsykJobTests` — happy path, 404-skip, cancellation propagation, race-no-op
8. Audit-rad via befintlig `JobAdsSynced` med `JobType: "backfill"` — INGET nytt audit-koncept
9. `[DisableConcurrentExecution(7200)]` på `RunAsync`
10. `RecurringJobRegistrar.cs` rörs EJ
11. Web-search-källa (JobTech rate-limit) bifogas STOPP-rapport med URL + datum
12. `IJobAdSnapshotMissTracker` är INTE constructor-dependency på `BackfillJobAdSsykJob`

### Referenser

- `CLAUDE.md` §2.1 (Clean Arch), §3.5 (async), §3.6 (paginering), §5.1 (DateTime, magic strings, IOptions), §9.2 (architect-rond), §9.6 (in-scope-fix vs TD)
- `docs/decisions/0032-jobtech-integration.md` (§2 port-design, §3 cron, §5 race-safety, amendment 2026-05-16 bounded retry, amendment 2026-05-23 miss-tracking)
- `docs/decisions/0045-performance-budget-and-fitness-functions.md` (Beslut 3 Worker-mem 512 MiB)
- `src/JobbPilot.Application/JobAds/Jobs/SyncPlatsbanken/SyncPlatsbankenSnapshotJob.cs` line 65-104 (child-scope-mönster att kopiera)
- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/PlatsbankenJobSource.cs` line 159-211 (`TryConvertToImportItem` återanvänds)
- `src/JobbPilot.Application/JobAds/Commands/UpsertExternalJobAd/UpsertExternalJobAdCommandHandler.cs` (race-safe upsert återanvänds)
- Martin, R. C. (2017). *Clean Architecture* kap. 7 (SRP), kap. 11 (DIP)
- Fowler, M. (2018). *Refactoring* — idempotent operations

**STOPP-flagga för Klas:** Innan CC implementerar — verifiera (a) web-search-resultat för JobTech `/search` rate-limit (om strängare än 5 req/s måste `PerItemDelay` justeras + körnings-tid revideras mot 4-6h-budget), och (b) att backfill-körning söndag eftermiddag inte krockar med Vercel-deploy eller demo-prep. Övriga designval är CTO-entydiga mot principer per §9.6 → CC implementerar direkt efter web-search-verifiering.
