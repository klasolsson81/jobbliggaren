# CTO-beslut — FAS 3 STOPP 3a provider-divergens (read-handler join-nyckel)

**Datum:** 2026-05-17
**Roll:** senior-cto-advisor (decision-maker, feedback_cto_decides_multi_approach)
**Scope:** read-only beslut + implementations-spec. CC implementerar.
**Klas-STOPP:** **NEJ** (se §6 — entydigt motiverat mot principer, ingen
strategisk blast-radius, ingen fas-/ADR-amendment).

---

## 1. Beslut

**3:e väg (i), variant: rå-Guid-join via separat shadow-FK-property.**

Konkret: bryt converter-invokationen på *join-nyckeln* genom att låta EF
mappa `Application.JobAdId` med en **icke-konverterad shadow-Guid-property**
som join-nyckel, medan domän-property:t `JobAdId : JobAdId?` behålls för
skriv-/domän-sidan. Read-handlers joinar på shadow-Guid:en (rå `Guid?`),
aldrig på det konverterade VO-property:t.

Detta löser **båda providers** (InMemory + Npgsql), kräver **ingen testflytt**
((B) avvisad), **ingen factory-ändring** ((C) avvisad), behåller **EN LEFT
JOIN** (ADR 0048), och är **minsta-ingrepps**-lösningen (YAGNI/KISS).

---

## 2. Rotorsak — kod-exakt

`ApplicationConfiguration.cs:26–29`:

```csharp
builder.Property(a => a.JobAdId)
    .HasConversion(
        id => id == null ? (Guid?)null : id.Value.Value,
        value => value == null ? (JobAdId?)null : new JobAdId(value.Value));
```

`Application.JobAdId` = `JobAdId?` (`JobAdId.cs:3` = `readonly record struct
JobAdId(Guid Value)`). De tre read-handlers gör:

```csharp
.GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, ...)
```

- **Relationell provider (Npgsql):** översätter converter + join till SQL,
  null hanteras i SQL-semantik → GRÖN.
- **EF InMemory:** har ingen SQL — invokerar converter-lambdan
  `id => id == null ? (Guid?)null : id.Value.Value` *klient-sida på varje
  join-nyckel*. För rader med `JobAdId == null` är `id` en
  `Nullable<JobAdId>` utan värde; uttrycket når `id.Value.Value` →
  `InvalidOperationException: Nullable object must have a value`. → 18 RÖDA.

Architectens (D)-fix korrigerade **projektionen** (join-härledd `JobAdGuid`
via `j != null ? (Guid?)j.Id.Value : null`, se handler-rad 58/42/46) men
**join-nyckeln** `a => a.JobAdId` är fortfarande det konverterade
nullable-struct-property:t. (D) löste fel halva av samma converter-problem.

**Detta är architectens fördefinierade Klas-STOPP-villkor** ("join-härledd
form ej GRÖN i båda providers → äkta provider-divergens → (B)/(C)
strategiskt"). Men architecten begränsade lösningsrymden till B/C; den 3:e
vägen undanröjer själva STOPP-premissen — det finns ingen genuin
provider-divergens i *domänlogiken*, bara i en *EF-mappnings-detalj* på
join-nyckeln. Att fixa mappningen är in-block (CLAUDE.md §9.6, default =
fixa in-block; ingen annan-fas/saknad-dependency → ingen TD).

---

## 3. Vald väg — implementations-spec (kod-exakt)

### 3.1 Princip

EF Core stödjer **shadow properties** + **alternativ FK-mappning**. Vi
introducerar en shadow-Guid `JobAdRef` (rå `Guid?`, ingen converter) som
EF fyller från samma kolumn `job_ad_id`. Domän-property:t `JobAdId`
(`JobAdId?`) behålls oförändrat för skriv-sidan och invarianter. Join sker
på den råa shadow-Guid:en — converter invokeras aldrig på join-nyckeln,
varken under InMemory eller Npgsql.

> **Varför inte `EF.Property<Guid?>(a, "JobAdId")` rakt av (ren väg (i)):**
> `JobAdId` är redan ett *mappat CLR-property med converter* — `EF.Property`
> mot dess namn refererar samma converted property, InMemory invokerar
> samma lambda. Det löser inte brottet. En **separat icke-konverterad
> shadow-FK** krävs. Detta är fortfarande väg (i) i andan (rå-Guid-join,
> ingen testflytt/factory-ändring) men preciserad till det som faktiskt
> är tekniskt sunt.

### 3.2 `ApplicationConfiguration.cs` — lägg till shadow-FK

Behåll rad 26–29 oförändrad. **Lägg till** efter den:

```csharp
// Shadow-FK för read-side join. Domän-property JobAdId (JobAdId?) bär
// converter för skriv-sidan; converter-invokation på join-NYCKELN bröt
// EF InMemory (Nullable<JobAdId>.Value på null-rad). Denna icke-
// konverterade rå-Guid mappar SAMMA kolumn (job_ad_id) och används som
// join-nyckel i read-handlers — provider-neutral (InMemory + Npgsql).
builder.Property<Guid?>("JobAdRef")
    .HasColumnName("job_ad_id");
```

**Kritiskt:** både `JobAdId` (converter) och shadow `JobAdRef` pekar på
kolumn `job_ad_id`. EF Core tillåter detta endast om **en** av dem är
"read-only ur skrivflödet". Två writable properties mot samma kolumn ger
`InvalidOperationException` vid `SaveChanges` (duplicate column mapping).
Lös med **`.AfterSaveBehavior` / metadata**:

```csharp
var jobAdRef = builder.Property<Guid?>("JobAdRef")
    .HasColumnName("job_ad_id");
jobAdRef.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
jobAdRef.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
```

Skriv-sidan går via `JobAdId`-converter (oförändrad). `JobAdRef` är
read-only spegel — EF materialiserar den vid query, ignorerar vid persist.
Ingen migration behövs (samma kolumn, ingen DDL — `db-migration-writer`
behöver inte re-invokeras; verifiera med `dotnet ef migrations list` att
ingen ny migration genereras av model-diff).

### 3.3 De tre read-handlers — byt join-nyckel

I `GetApplicationsQueryHandler.cs:49`,
`GetPipelineQueryHandler.cs:35`,
`GetApplicationByIdQueryHandler.cs:39`:

Byt:

```csharp
.GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
```

Till:

```csharp
.GroupJoin(
    db.JobAds,
    a => EF.Property<Guid?>(a, "JobAdRef"),
    j => (Guid?)j.Id.Value,
    (a, ja) => new { a, ja })
```

- Vänster nyckel: rå `Guid?` shadow → ingen converter-invokation.
- Höger nyckel: `j.Id` är `JobAdId` (non-null struct, `JobAdConfiguration:12`
  converter `id => id.Value`). `(Guid?)j.Id.Value` är säkert (icke-nullable
  struct, ingen `.Value` på `Nullable<>`). Under InMemory invokeras
  `j.Id.Value` på faktiska JobAd-rader (alltid har värde) → inget brott.
  Under Npgsql översätts till SQL → grön (redan bevisat).
- `null == null` ⇒ EF/LINQ GroupJoin matchar **inte** null-nycklar (LEFT
  JOIN ON-semantik), vilket är önskat: ansökan utan JobAd → `ja` tom →
  `DefaultIfEmpty()` → `j == null` → ManualPosting/null-fallback (ADR 0048,
  oförändrad branch-logik rad 50–77 / 36–61 / 40–78).

Projektionen (`JobAdGuid = j != null ? (Guid?)j.Id.Value : null`,
architectens (D)-fix) **behålls oförändrad** — den var korrekt, det var
bara join-nyckeln som kvarstod.

### 3.4 Verifiering CC ska producera i STOPP-rapport

1. `dotnet build` 0 fel.
2. `dotnet test tests/JobbPilot.Application.UnitTests` — 18 tidigare RÖDA
   nu GRÖNA, inkl. `ReadHandlerManualPostingFallbackTests` (12 fakta),
   `GetApplications/GetPipeline/GetApplicationByIdQueryHandlerTests`.
3. `dotnet test` full svit grön (Npgsql/Testcontainers-integration
   fortsatt grön — regressions-vakt mot väg (i)).
4. `dotnet ef migrations list` + `dotnet ef migrations has-pending-model-changes`
   (eller motsv.) → **ingen ny migration** (bevisar shadow-FK = samma
   kolumn, ADR 0048 schema oförändrat).
5. `git diff --stat` — ändrade filer ⊆ {ApplicationConfiguration.cs,
   3 read-handlers}. Noll testfiler ändrade, noll factory-ändring.

---

## 4. Avvisade alternativ

**(B) Flytta 3 read-handlers + ReadHandlerManualPostingFallback →
Testcontainers/Npgsql-integration.**
Avvisad. (a) Bryter test-pyramiden (Fowler 2018, kap. 2 / Cohn 2009):
handler-logik som *kan* enhetstestas flyttas till långsammare integrations-
lager pga en mappnings-detalj — fel nivå att betala. (b) Rör Fas-1-testfiler
(`GetApplications/GetPipeline/GetApplicationByIdQueryHandlerTests` är
befintliga) → J3-blast-radius, architecten avrådde själv. (c) Maskerar
problemet i stället för att fixa det: join-nyckeln vore fortfarande en
converter-fälla för nästa handler som joinar `Application.JobAdId`. Botar
symptom, inte orsak (Martin 2017, kap. 17 — gränser ska skydda, inte gömma
defekter).

**(C) Byt `TestAppDbContextFactory` → SQLite-in-memory.**
Avvisad. 44 filer refererar factoryn (greppat, §verifierat). Att byta
provider-semantik (converter-, owned-type-, DateTimeOffset-, query-filter-
beteende) under ~40 orелaterade testfiler är en strategisk regressions-
risk helt utan koppling till STOPP 3a:s scope. Bryter
feedback_di_with_handlers_same_commit-andan (atomisk J3, ingen broken
state) och YAGNI (Martin 2017, kap. 22 — inför inte tvärgående
infrastruktur-skifte för ett lokalt mappnings-problem). Architecten
avrådde som default; CTO bekräftar.

**Ren väg (i) `EF.Property<Guid?>(a, "JobAdId")` mot befintligt
converted-property.** Avvisad — tekniskt osund (§3.1-not): refererar samma
converted property, InMemory invokerar samma trasiga lambda. Preciserad
till shadow-FK-varianten i §3.

**Väg (ii) "justera JobAdId-converter så null-fallet ej rör `.Value`".**
Avvisad. Convertern `id => id == null ? (Guid?)null : id.Value.Value` är
*redan* null-safe i C#-runtime — brottet är att EF InMemory invokerar
hela uttrycket på join-nyckeln inkl. när `id` är `Nullable<JobAdId>`
utan värde. Det går inte att skriva en converter på `JobAdId? → Guid?`
som EF InMemory kan invokera klient-sida på en HasValue==false-nyckel
utan att röra `.Value` (man *måste* unwrappa structen). Convertern är
inte felet; att den används som join-nyckel är. Fel angreppspunkt.

**Väg (iii) explicit relationell `Join` på `a.JobAdId.Value`-projektion.**
Avvisad. `a.JobAdId.Value` är exakt `Nullable<JobAdId>.Value` → samma
InMemory-brott, plus att INNER `Join` skulle tappa ansökningar utan JobAd
(bryter ADR 0048 EN LEFT JOIN + 3-grens-fallback). Sämre på alla axlar.

---

## 5. Trade-offs accepterade

- **Två CLR-properties mot en kolumn** (`JobAdId` converter +
  `JobAdRef` shadow read-only). Lätt EF-mappnings-komplexitet, men
  väldokumenterad EF Core-pattern (shadow property + `PropertySaveBehavior
  .Ignore`). Acceptabelt: alternativet (B/C) har större och *strategisk*
  komplexitet. SRP bevaras — skriv-väg (JobAdId-converter, domän-invariant)
  och read-join-väg (rå FK) får var sitt property med var sitt ansvar
  (Martin 2017, kap. 7).
- En kommentar-rad krävs i 3 handlers + config för att förklara varför
  join-nyckeln är shadow-FK, inte VO-property. Self-documenting; billigt
  mot framtida förvirring.
- `EF.Property<Guid?>(a, "JobAdRef")` är en magisk sträng. Mitigerat med
  förklarande kommentar; acceptabelt då EF shadow-access kräver det
  (ingen typsäker access finns för shadow). Ej §5.1 magic-string-brott
  (det gäller domän-konstanter, inte EF-shadow-namn — vedertaget mönster).

---

## 6. Klas-STOPP — explicit bedömning

**NEJ, inget Klas-slut-GO krävs.** Motivering mot §9.6 / §9.2:

- Entydigt motiverat mot principer (test-pyramid, YAGNI, SRP, ADR 0048
  bevarad, minsta ingrepp).
- Ingen fas-skifte, ingen ADR-amendment (ADR 0048 EN LEFT JOIN +
  query-filter-disciplin **bevaras** — IgnoreQueryFilters ej rört,
  schema oförändrat, ingen ny migration).
- Ingen strategisk blast-radius: 4 produktionsfiler, noll testfiler, noll
  factory, atomisk J3 bevarad.
- Ej deploy-beslut.

CTO går direkt till implementation-spec; CC slutför per §3 och rapporterar
verifiering §3.4 i STOPP-rapport för Klas parallell-granskning (ADR 0019
mekanism 3–4). Klas har alltid override — men detta är ingen genuin
strategisk korsning, så default = CC kör (CLAUDE.md §9.6 p.5).

**Re-invokera CTO om:** §3.4 p.4 visar att en **ny migration genereras**
(då är shadow-FK-mappningen inte kolumn-identisk → annan teknisk premiss,
återvänd till beslut), eller om Npgsql-sviten regresserar (väg (i) ej
provider-neutral som antaget).

---

## 7. Referenser

- Robert C. Martin, *Clean Architecture* (Prentice Hall, 2017) — kap. 7
  (SRP), kap. 17 (gränser skyddar, gömmer inte defekter), kap. 22 (YAGNI/
  inga spekulativa infrastruktur-skiften).
- Martin Fowler, *Refactoring* 2nd ed. (2018) / Mike Cohn,
  *Succeeding with Agile* (2009) — test-pyramiden: enhetstestbar logik
  hör på unit-nivå.
- Microsoft Learn — EF Core *Shadow and Indexer Properties*;
  *Value Conversions* (converter invokeras klient-sida under InMemory);
  `PropertySaveBehavior` (read-only spegel-property mot delad kolumn).
- ADR 0048 (EN LEFT JOIN, query-filter-disciplin, IgnoreQueryFilters
  förbjudet) — bevarad, ej amenderad.
- CLAUDE.md §3.6 (query-hygien), §7 (test-disciplin), §9.6 (in-block-fix
  default, ingen TD då varken annan-fas eller saknad-dependency), §9.2
  (CTO decision-maker).
- Architect-design 2026-05-17-fas3-stopp3a-architect-design.md +
  -inmemory-fix-architect.md (väg (D) projektions-fix — korrekt men
  ofullständig; join-nyckeln kvarstod).
