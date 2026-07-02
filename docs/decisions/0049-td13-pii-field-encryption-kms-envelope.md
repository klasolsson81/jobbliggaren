# ADR 0049 — TD-13 PII-fält-kryptering via KMS-envelope (per-användare-DEK + crypto-erasure)

**Status:** Accepted
**Datum:** 2026-05-18
**Kontext:** FAS 3.5 STOPP D — pre-FAS-4-blocker (TD-13)
**Beslutsfattare:** Klas Olsson (Proposed→Accepted-grind, GO 2026-05-18); senior-cto-advisor (5 designval, §9.6 decision-maker)
**Relaterad:** TD-13 (`docs/tech-debt.md:77-108`); ADR 0009 (ingen Repository — EF-bridge i Infrastructure); ADR 0024 (Art. 17-cascade + backup/retention — **komplementär, ej supersession**); ADR 0032 §8 (JobTech raw_payload sanitizer/PII); ADR 0039 (taxonomi-sök-SPOT); ADR 0042 (sök-yta multi-värde-kriterier). Underlag: `docs/reviews/2026-05-18-td13-design-decisions-cto.md`, `docs/reviews/2026-05-18-td13-pii-encryption-discovery.md`, `docs/reviews/2026-05-18-pre-fas4-audit-validation-cto.md`

> **Livscykel-not:** Denna ADR skrevs som STOPP D-utkast och flippades
> `Proposed→Accepted` av Klas (Klas-GO 2026-05-18; ej adr-keeper, ej CC).
> Prosan är omformulerad från utkast-presens/futurum till beslutad form;
> besluts-substansen är oförändrad. Implementation (STOPP I) får startas
> efter denna flipp.

---

> **Not 2026-06-06 (ADR 0066 — lokal envelope-provider):** Efter AWS-avveckling
> (ADR 0066) introducerades `LocalDataKeyProvider` som ett andra
> `IDataKeyProvider`-impl bredvid `KmsDataKeyProvider`, valt via config-switch
> `FieldEncryption:Provider` ("Kms" default / "Local"). Local-grenen wrappar
> per-användar-DEK:en med en lokal AES-256-GCM master-nyckel
> (`FieldEncryption:LocalMasterKeyBase64`, gitignored) istället för KMS
> `GenerateDataKey`/`Decrypt`. **Hela denna ADR:s besluts-substans är oförändrad:**
> envelope-strukturen (per-JobSeeker wrapped-DEK i `user_data_keys`),
> owner-AAD-bindningen, fail-closed-invarianten och `IFieldEncryptor`
> (AES-256-GCM-primitiv) är identiska — bara DEK-wrap-mekanismen byter. KMS-impl
> + paket BEHÅLLS som referens. Self-managed-nyckelns prod-skyddsmodell + rotation
> för Hetzner är **TD-102** (Major, Hetzner-deploy) och kräver ADR-amendment/
> superseder + security-auditor-granskning innan riktig PII. Lokal dev kräver det
> inte. Verifierat denna session: `KmsEnvelopeEncryptor` har noll AWS-import
> (ren BCL `AesGcm`); enda AWS-touchpoint var `KmsDataKeyProvider`.

---

## Kontext

Fem databaskolumner lagrar PII-känsligt innehåll (BUILD.md §13.1 "Känsligt")
som klartext i Postgres. RDS ger AES-256 disk-encryption via KMS, men app-side
envelope encryption — ett extra lager utöver RDS — saknas för dessa fält.
Berörda kolumner (verifierade on-disk i discovery, HEAD `8474c06`):

- `applications.cover_letter` — TEXT, klartext, `TODO(GDPR)` → Fas 2
- `application_notes.content` — TEXT, klartext, `TODO(GDPR)` KMS-VC
- `follow_ups.note` — TEXT (nullable), klartext, `TODO(GDPR)` KMS-VC
- `resume_versions.content` — JSONB, klartext, redan JSON-`ValueConverter` +
  `ValueComparer` (`ResumeVersionConfiguration.cs:41-59`) — krypto måste
  komponeras *runt* den befintliga JSON-converter:n, ej ersätta den
- `job_ads.raw_payload` — JSONB, klartext, **load-bearing** för tre oberoende
  Postgres-side-mekanismer (STORED generated columns, taxonomi-sök-SPOT,
  Art. 17 `JsonContains`-redaction) — se Beslut 3

**Krafter som spelar in:**

- **GDPR Art. 32/17 + EDPB CEF 2025 (rapport 2026-02):** RDS disk-at-rest
  skyddar inte mot snapshot-share, automated-backup-export (default 7d, max
  35d) eller IAM-komprometterad DB-läsning. ADR 0024:s Art. 17-story stänger
  live-data + app-logg, men RDS automated-backups bär klartext-PII under
  overwrite-fönstret. EDPB CEF 2025: backup-exklusion utan motivering = fynd;
  backup-overwrite *med* dokumenterad motivering = accepterat; crypto-erasure
  = medel, ej ursäkt.
- **Fas-sekvensering (prejudikat, redan Klas-GO):** TD-13 reklassas Fas 2 →
  "FAS 3.5 (pre-FAS-4-blocker)" och implementeras sekventiellt FÖRE FAS 4.
  Drivkraften är arkitektonisk divergens-risk: FAS 4 BYOK-key-storage kräver
  exakt samma `ValueConverter<T,string>` + KMS-envelope. Att bygga FAS 4:s
  envelope före TD-13 skapar två divergerande implementationer (DRY-brott på
  knowledge-nivå, Hunt/Thomas 1999; Fowler 2018 "Duplicated Code"). Detta
  prejudikat omprövas **inte** här — `docs/reviews/2026-05-18-pre-fas4-audit-validation-cto.md`
  §2 bär det och kräver redan Klas-GO som inhämtats.
- **Inget KMS-bruk existerar:** discovery verifierar att `AWSSDK.KMS` ej finns
  i `Directory.Packages.props` (endast `AWSSDK.SecretsManager`,
  `AWSSDK.SimpleEmailV2`, `AWSSDK.Core`). Ingen envelope-impl, ingen converter,
  ingen migration finns ännu. Secrets Manager-mönstret (`Migrate/Program.cs`:
  klient-init + ARN-via-env-var, fail-fast `RequiredEnv`) är precedens för
  KMS-CMK-ARN-bindning via `IOptions`/env-var.
- **Clean Architecture-gräns (ADR 0009):** krypto-laget är ett
  Infrastructure-bekymmer. `ValueConverter` bor i EF-config i Infrastructure;
  Domain förblir orört (Evans 2003 — persistensartefakt läcker ej in i
  aggregatet).

Denna ADR avgör de fem interna designvalen som senior-cto-advisor fattat
(§9.6 decision-maker); TD-13 är CC-direkt-implementerbart efter Klas:s
Proposed→Accepted-grind (GO 2026-05-18).

---

## Beslut

JobbPilot inför KMS-backed envelope encryption som ett extra app-side-lager
ovanpå RDS-at-rest för de fyra **user-ägda** PII-kolumnerna, med
**per-användare-DEK** och **crypto-erasure** för Art. 17-backup-täckning.
`job_ads.raw_payload` **exkluderas** medvetet ur envelope-scopet. Fem beslut:

### Beslut 1 — DEK-granularitet: per-användare-DEK för de fyra user-ägda kolumnerna

`cover_letter`, `application_notes.content`, `follow_ups.note` och
`resume_versions.content` krypteras med en **DEK per `JobSeeker`** — en
data-encryption-key per användare, wrappad av CMK och lagrad i en
`user_data_keys`-tabell (eller på JobSeeker-aggregatet). DEK-livscykeln följer
aggregatets, inte den fysiska raden: de fyra kolumnerna lever och dör med
JobSeeker (Art. 17).

**Motivering:** DDD aggregate-ägande (Evans 2003; Vernon *IDDD* 2013 kap. 10)
— DEK-livscykeln binds till ägaren. En DEK per JobSeeker gör Beslut 2
(crypto-erasure) möjlig och billig. SRP (Martin *Clean Architecture* 2017
kap. 7): per-användare-DEK har en change-reason (kontoradering), ej N×M
nyckelpunkter. KISS/key-rotation: O(användare) re-wrap vid CMK-rotation.

### Beslut 2 — Crypto-erasure JA, som dokumenterad förstärkning ovanpå ADR 0024 backup-overwrite (ej ersättning)

Kontoradering kastar användarens DEK → backup-resident ciphertext blir
omedelbart olesbar. ADR 0024:s backup-overwrite-story (RDS automated 7–35d)
**kvarstår** som primär Art. 17-motivering; crypto-erasure stänger
klartext-fönstret *under* overwrite-perioden. ADR 0024 (live + applog) och
ADR 0049 (backup-PII-lager) är **komplementära** — relationen dokumenteras
som **cross-ref, ej ADR 0024-amendment**.

**Motivering:** EDPB CEF 2025 (rapport 2026-02): crypto-erasure är ett medel,
ej en ursäkt — det får ej åberopas som ersättning för en retention-story; båda
måste samexistera i ADR-texten. ADR 0024 delbeslut 1/7 täcker
`audit_log` + CloudWatch men **inte** RDS automated-backup-PII; crypto-erasure
stänger exakt det gapet. Defense-in-depth (OWASP; Microsoft Learn —
encryption-at-rest/key-hierarchy): kollapsar 7–35d klartext-fönster till
"tiden att kasta en nyckel". YAGNI-kontroll: per-användare-DEK byggs ändå
(Beslut 1) → crypto-erasure = litet tillägg, ej separat system.

**Trade-off:** restore av en backup med sedan-raderad användare ger olesbar
ciphertext (önskat — restore återupplivar ej raderat innehåll). Key-rotation
bevarar icke-raderade användares wrapped DEK:er.

### Beslut 3 — `raw_payload` EXKLUDERAS ur envelope-scope; (b)-omstrukturering avvisas

`job_ads.raw_payload` krypteras **inte** av TD-13-envelopet. Exklusionen
dokumenteras med tre-lagers befintlig motivering: JobTech-payloaden är redan
saniterad (`JobTechPayloadSanitizer` allowlist, ADR 0032 §8-amendment),
self-purgande (30d, `PurgeStaleRawPayloadsJob`) och Art. 17-null-out:ad
(`RecruiterPiiPurger`). Envelope ovanpå tre befintliga kontroller på
redan-saniterad icke-user-PII ger noll additionell GDPR-vinst men bryter tre
Postgres-side-mekanismer:

1. **STORED generated columns** (`ssyk_concept_id`, `region_concept_id`,
   `JobAdConfiguration.cs:74-80`) — Postgres beräknar `raw_payload->...` vid
   write; ciphertext (ej giltig JSONB) → `->`-operatorn kraschar.
2. **Taxonomi-sök-SPOT** (`JobAdSearch.cs:39-49`, ADR 0039 Beslut 1; delas av
   `ListJobAdsQueryHandler` + `RunSavedSearchQueryHandler`, jfr ADR 0042) —
   beror transitivt på (1).
3. **Art. 17-redaction** (`RecruiterPiiPurger.cs:38-41`,
   `EF.Functions.JsonContains` = Postgres `@>` direkt mot raw_payload) —
   ciphertext → `@>` matchar ej → Art. 17-radering bryts.

Alternativ (b) — extrahera ssyk/region till klartext-icke-PII-kolumner +
ersätta `JsonContains`-Art.17-mekanismen, sedan kryptera raw_payload —
**avvisas**: negativ ROI (schema-omstrukturering + JsonContains-ersättning +
SPOT-omskrivning + jsonb→text + migration/test för noll additionell
GDPR-vinst), scope-creep förklädd till grundlighet. Eftersom (a) valdes entydigt
utlöstes **ingen Klas-STOPP-eskalering** (uppdragets (b)-eskaleringstrigger
inträffade ej; ingen raw_payload-kodändring sker).

**Motivering:** YAGNI + KISS (Hunt/Thomas 1999; Martin 2017 kap. 22).
Component cohesion/CRP (Martin 2017 kap. 13): raw_payload är funktionellt
kohesivt med giltig JSONB (generated columns → taxonomi-sök-SPOT ADR 0039 +
JsonContains-Art.17). SRP-skillnad i change-reason: TD-13 = "skydda user-ägd
Känsligt-PII vid backup-läckage"; raw_payload = "JobTech-ingest-artefakt med
egen sanitering/retention" (ADR 0032/0039-domän). Risk/värde (Fowler *PoEAA*
2002): (b) negativ ROI.

**Trade-off:** raw_payload förblir klartext-JSONB at-app-rest (skyddad av RDS
KMS + sanitizer + 30d-purge + Art. 17-null-out). Medveten dokumenterad
exklusion (EDPB CEF 2025: exklusion *med* motivering = accepterat).
**Future-watch-antagande:** om någon av de fyra user-ägda kolumnerna får en
WHERE/LIKE-konsument bryts kryptering rakt-av och frågan om
searchable-encryption återöppnas (utanför scope, YAGNI idag).

### Beslut 4 — Migrering: hybrid lazy encrypt-on-write (primär) + bounded idempotent backfill-job

En lazy `ValueConverter` krypterar vid write och dekrypterar vid read.
Read-path tål både klartext-legacy och ciphertext via ett versions-/sentinel-
prefix (t.ex. `v1:` + base64) som bär DEK-version för key-rotation och
disambiguerar legacy vs krypterat. Ett idempotent, batchat,
cancellation-bart Hangfire-backfill-job (samma chassi som
`PurgeStaleRawPayloadsJob` / `HardDeleteAccountsJob`) driver deterministiskt
till 100% ciphertext.

**Motivering:** TD-13-spec mandaterar icke-destruktiv migrering. Ren lazy =
obegränsad klartext-svans (besegrar FAS 3.5-syftet). Ren backfill big-bang =
downtime. Ford/Parsons/Kua 2017: migration utan deterministiskt slut =
permanent dual-state; backfill = fitness-funktion
(`COUNT(*) WHERE ej-ciphertext = 0`). Cryptographic agility (OWASP):
sentinel-prefixet behövs ändå för key-rotation → ej additiv komplexitet.
CCP (Martin 2017 kap. 13): återanvänd Hangfire-kohesion.

**Mekanik-not (senior-cto-advisor-triage 2026-05-18, STOPP I — gäller Beslut 4
+ Beslut 5):** ordalydelsen "`ValueConverter`" ovan var en
implementeringsförväntan, inte besluts-substans. En ren `ValueConverter` är
statiskt registrerad i `OnModelCreating`, ser endast kolumnvärdet och kan per
Microsoft Learn — *Value Conversions* (ingen `DbContext`-referens, single-
column; dotnet/efcore #13947, #31234) **inte** nå radens `JobSeekerId` för
per-användare-DEK-uppslag (Beslut 1). Ordalydelsen är därmed tekniskt
ogenomförbar mot Beslut 1. Den implementeras istället via paret
`FieldEncryptionSaveChangesInterceptor : ISaveChangesInterceptor`
(encrypt-on-write) + `FieldDecryptionMaterializationInterceptor :
IMaterializationInterceptor` (decrypt-on-read), som via `ChangeTracker`
navigerar entitet→`JobSeekerId`→DEK med en scoped cache per `SaveChanges`-enhet
(ingen ambient/`AsyncLocal`-state — CLAUDE.md §5.1; ingen cross-user-batch-
läcka). De **fyra substans-invarianterna är oförändrade**: lazy
encrypt-on-write, sentinel-/versionsprefix, bounded idempotent backfill,
legacy-tolerans på read-path. Detta är en mekanik-precisering tvingad av
EF Core-doktrin — **ingen substans-ändring, ingen formell ADR-amendment, ingen
Klas-STOPP** (CTO entydig mot principer, §9.6 p.5). Konsekvens för Beslut 5
nedan: JSON-`ValueConverter` bevaras **endast om** den empiriska C4-gaten
(integrationstest mot Npgsql/Testcontainers, ej InMemory) bekräftar att
`IMaterializationInterceptor` ser det JSON-serialiserade strängvärdet (efter
VC på write, före VC på read — ej normativt garanterat i Microsoft Learn). Om
gaten är röd flyttas JSON-transformen in i interceptor-paret (samma mekanik som
de tre TEXT-kolumnerna; ingen VC-komposition med service-locator — det vore
återinförande av det avvisade ambient-state-antimönstret). `ValueComparer` på
klartext-`ResumeContent` bevaras oavsett utfall (annars trasas
change-tracking).

**Mekanik-not 2 (senior-cto-advisor-triage 2026-05-18, STOPP I batch C2 —
Approach D, gäller fail-closed-startup):** ordalydelsen ovan + i
`FieldEncryptionOptions`-doc om att "tom CmkKeyId ska validera bort vid
startup (.ValidateOnStart())" var en **implementeringsförväntan om mekanism**,
inte besluts-substans. Substansen är: fält-PII får aldrig
krypteras/dekrypteras mot saknad/ogiltig CMK (fail-closed). Den invarianten
är **oförändrad** — `KmsDataKeyProvider`:s runtime-guard (KMS avvisar tom
KeyId, ingen klartext-fallback) bär den i ALLA miljöer. En global
`.Validate(Func)` ser per .NET-design inte `IHostEnvironment` och applicerade
en Production-invariant på ~6 KMS-fakande integ-test-hostar → J3-broken main
(regression införd i C1 `78958ce`). Omimplementerad via
`IValidateOptions<FieldEncryptionOptions>` (kanonisk .NET-form, Microsoft
Learn): hård fail-fast i Production/Staging (där KMS måste fungera — tom CMK
= deploy-fel), warning utan boot-block i Development/Test (fail-closed
kvarstår via runtime-guard; boot-checken var alltid redundant defense-in-depth
meningsfull endast där KMS måste fungera). `.ValidateOnStart()` behålls
(triggar `IValidateOptions` vid boot — prod-fail-fast 100 % bevarad).
**Ingen substans-ändring, ingen formell ADR-amendment, ingen Klas-STOPP**
(CTO entydig mot principer, §9.6 p.5; paritet med Mekanik-not 1:s
`ValueConverter`→interceptor-precedens). Klas informeras i STOPP-rapport och
kan override:a till formell amendment om miljö-villkoret bedöms vara
besluts-substans.

**Mekanik-not 3 (senior-cto-advisor-triage 2026-05-18, STOPP I batch C3 —
Approach B, gäller decrypt-on-read DEK-anskaffning):** ordalydelsen
"decrypt-on-read via `IMaterializationInterceptor`" (not 1) var en
implementeringsförväntan om var radens DEK *anskaffas*, inte besluts-substans.
EF Core 10:s `IMaterializationInterceptor.InitializedInstance(...)` är
synkron (ingen async-overload — dotnet/efcore; Microsoft Learn *Interceptors*).
En ren läs-scope har ingen förcachad DEK → första decrypt kräver async
KMS-unwrap, omöjlig i synkron `InitializedInstance` utan sync-over-async
(CLAUDE.md §3.5 — förbjudet, analyzer-enforced). Substansen — decrypt-on-read
med per-användare-DEK, legacy-tolerans, fail-closed — är **oförändrad**.
Mekaniken preciseras: en additiv `DecryptionKeyPrefetchBehavior :
IPipelineBehavior` (pipeline-ordning: efter Authorization, före UnitOfWork)
förladdar ägar-DEK (ADR 0031 `currentUser → JobSeekerId`) till
`ScopedUserDataKeyCache` (async, samma scoped-cache som encrypt-on-write —
CCP-återanvändning) innan handlerns query materialiserar.
`IMaterializationInterceptor.InitializedInstance` blir då en ren synkron
cache-hit + symmetrisk AES-Decrypt (noll I/O — ingen §3.5-konflikt). Ingen
ambient/`AsyncLocal`-state (CLAUDE.md §5.1; scope-bunden, `ZeroMemory` vid
dispose). De **fyra substans-invarianterna oförändrade**. Mekanik-precisering
tvingad av EF Core 10-doktrin + §3.5 — **ingen substans-ändring, ingen formell
ADR-amendment, ingen Klas-STOPP** (CTO entydig mot principer, §9.6 p.5;
paritet med Mekanik-not 1/2). Klas informeras i STOPP-rapport och kan
override:a till formell amendment om pipeline-additionen bedöms vara
besluts-substans.

**Mekanik-not 4 (senior-cto-advisor-triage 2026-05-18, STOPP I batch C3 —
Approach A, gäller decrypt-on-read interceptor-träffbarhet):** ordalydelsen
"decrypt-on-read via `IMaterializationInterceptor`" (not 1) bar en
implementeringsförväntan om *att interceptorn alltid träffar*. EF Core 10:s
`IMaterializationInterceptor` triggar **endast när shapern producerar en
entitetsinstans** (Microsoft Learn *Interceptors* / *IMaterializationInterceptor*
efcore-10.0; dotnet/efcore #33614, #15911). En SQL-projektion av en krypterad
kolumn rakt till en DTO (`.Select(... new Dto(a.CoverLetter, ...))`)
materialiserar ingen entitet → interceptorn kringgås → ciphertext når DTO:n
oläst. Substansen — decrypt-on-read med per-användare-DEK, legacy-tolerans,
fail-closed — är **oförändrad**. Mekaniken preciseras: read-handlers som rör
de krypterade kolumnerna **materialiserar entiteten** (ej SQL-projektion av
det krypterade fältet) så att interceptor-paret (not 1) + prefetch-behavior
(not 3) faktiskt träffar. Omfång verifierat minimalt: enda berörda handler är
`GetApplicationByIdQueryHandler` (skrivs om till entitets-materialisering +
in-memory-map; JobAd förblir projicerad left-join — ADR 0048 cross-aggregat-
del orörd). `GetResumeByIdQueryHandler` (C4) är redan konform (`Include` +
in-memory `ToDetailDto()`). `GetApplications`/`GetPipeline` projicerar inga
krypterade kolumner. En arch-test-spärr (Approach D-komplement) förhindrar
framtida SQL-projektion av de fyra krypterade kolumnerna. De **fyra substans-
invarianterna oförändrade**. Mekanik-precisering tvingad av EF Core 10-doktrin
— **ingen substans-ändring, ingen formell ADR-amendment, ingen Klas-STOPP för
mekaniken** (CTO entydig, §9.6 p.5; paritet not 1–3). Klas-GO inhämtad
2026-05-18 på den utökade C3-scopen (handler-materialisering + arch-test;
not 3 var nödvändig men ej tillräcklig). Klas kan override:a not 4 till
formell amendment om interceptor-träffbarhet bedöms vara besluts-substans.

**Mekanik-not 5 (dotnet-architect + senior-cto-advisor-triage 2026-05-18,
STOPP I batch C3 — re-entrancy-fix Approach A + system-scope-passthrough #3
(iv)):** två mekanik-preciseringar som tillsammans sluter C3:s fyra
scope-kvadranter. **(a) Re-entrancy (Approach A, reviderar Mekanik-not
1:s ruling 1):** write-interceptorn fick anropa `IUserDataKeyStore
.GetOrCreateDataKeyAsync` inifrån `SavingChangesAsync` → `UserDataKeyStore`
gjorde `db.SaveChangesAsync()` på SAMMA DbContext → EF
concurrency-detector-deadlock (DbContext icke-re-entrant, Microsoft Learn).
Precisering: `FieldEncryptionSaveChangesInterceptor` blir en ren synkron
cache-konsument (speglar decrypt-interceptorn) — anropar aldrig store/KMS;
DEK värms av `FieldEncryptionKeyPrefetchBehavior` i ett eget pipeline-steg
före UnitOfWork (write-commands bär `IRequiresFieldEncryptionKey`). Markören
omdöpt `IRequiresDecryptedFields`→`IRequiresFieldEncryptionKey` (write+read-
symmetrisk); behavior omdöpt `DecryptionKeyPrefetchBehavior`→
`FieldEncryptionKeyPrefetchBehavior`. **(b) System-scope-passthrough #3 (iv):**
`FieldDecryptionMaterializationInterceptor` fyrar på all entitets-
materialisering; system/Hangfire-vägar (MarkGhosted, AccountHardDeleter)
materialiserar krypterade aggregat men är medvetet ej `IAuthenticatedRequest`
(ingen DEK-prefetch möjlig) och läser aldrig plaintext-fältet. Precisering:
scope-differentierad fail-closed — autentiserad ägar-scope
(`ICurrentDataOwner.JobSeekerId` satt) + ingen cachad DEK → kasta
(oförändrat); system-scope (ingen `ICurrentDataOwner`/auth) → lämna
ciphertext orört, kasta ej (drift får ej krascha; konfidentialitet bevarad —
ciphertext exponeras aldrig som plaintext; encrypt-interceptorn
idempotent-skippar re-save). Arch-test spärrar system-commands från att läsa
krypterade plaintext-fält. De **fyra substans-invarianterna oförändrade**;
fail-closed-substansen ("returnera ALDRIG klartext-fallback") bokstavligt
bevarad (passthrough är striktare, ej svagare). Mekanik-precisering tvingad
av EF Core 10-doktrin + drift-robusthet — **ingen substans-ändring, ingen
formell ADR-amendment, ingen Klas-STOPP för mekaniken** (architect+CTO
entydiga, §9.6 p.5; paritet not 1–4). **CTO-flagg:** #3 (iv) rör
fail-closed-*villkorets* scope-differentiering (närmare substans än not 3/4);
**Klas kan override:a Mekanik-not 5(b) till formell amendment** om
scope-differentierad fail-closed bedöms vara besluts-substans — flaggas i
STOPP V-rapporten (ej Klas-STOPP före STOPP V per Klas-direktiv 2026-05-18).

**Mekanik-not 5c (dotnet-architect-triage 2026-05-18, Microsoft Learn-
verifierad rev 2026-02-26):** Interceptor-paret auto-discoveras INTE av EF
Core från application-DI (empiriskt falsifierat: utan `AddInterceptors` kör
de aldrig → klartext persisteras). Kanonisk EF Core 10-mekanik: **singleton-
registrerade `ISingletonInterceptor`-implementationer** (`ISaveChangesInterceptor`/
`IMaterializationInterceptor` ÄR singleton-interceptorer i EF) +
`(sp,options).AddInterceptors(sp.GetRequiredService<...>())` — stabil
singleton-instans → identisk options-cache-nyckel → EN intern EF-provider →
ingen `ManyServiceProvidersCreatedWarning` (en **prod-reell** resursläcka med
scoped interceptor-instanser, ej test-artefakt; EF default `WarningBehavior
.Throw`). Scoped state (`IFieldEncryptor`/`ScopedUserDataKeyCache`/
`ICurrentDataOwner`) nås via `eventData.Context.GetService<T>()` resp.
`MaterializationInterceptionData.Context.GetService<T>()` vid invocation
(samma scope som AppDbContext = samma scope som prefetch-behaviorn värmde),
INTE via konstruktorinjektion. `ICurrentDataOwner` förblir Scoped.
ApiFactory:s re-AddDbContext speglar `(sp,options).AddInterceptors`.
Approach A/CTO #3 (iv)-semantiken är oförändrad (interceptorerna förblir rena
synkrona cache-konsumenter; re-entrancy-fri; scope-differentierad fail-closed
rad-för-rad bevarad). Ersätter den felaktiga "auto-discovery"-formuleringen i
tidigare not 1/5 + DI-kommentar. **Ingen substans-ändring** (mekanik-precisering
tvingad av EF Core 10-doktrin, paritet not 1–5; §9.6 p.5). dotnet-architect
flaggade detta som potentiell ADR-amendment; per Klas-direktiv 2026-05-18
(non-stop, CTO/architect-kedja, inga Klas-stopp före STOPP V) appliceras det
som mekanik-not — **flaggas i STOPP V-rapporten; Klas kan override:a till
formell amendment**.

**Mekanik-not 6 (dotnet-architect-triage 2026-05-19, Microsoft Learn-
verifierad; C4 RÖD-grenens EF-mekanik-korrektion):** C4.0-gaten kördes
empiriskt (Testcontainers/Npgsql) → **utfall RÖD bekräftat**:
`ValueConverter.ConvertFromProvider` kör FÖRE
`IMaterializationInterceptor.InitializedInstance` (normativt per Microsoft
Learn — InitializedInstance anropas efter att EF satt property-värden). Den
villkorade RÖD-grenens tidigare pre-spec (Mekanik-not 1 / Beslut 5:
"`ResumeVersionConfiguration` slutar använda contentConverter; ValueComparer
bevaras via `.Metadata.SetValueComparer`") visade sig vara **ogiltig EF
Core 10-mekanik** — en custom CLR-typ (`ResumeContent`-record) mot en
`text`-kolumn saknar `ProviderClrType` utan `ValueConverter` och kan ej
mappas; en `ValueComparer` ger ingen store-typ (Microsoft Learn *Value
Conversions* §Overview/§Limitations; VC kan ej referera DbContext, #12205).
**Korrigerad låst konstruktion (#1c):** `ResumeVersion.Content`
`builder.Ignore(rv => rv.Content)` (EF-persisterar den EJ) + en
string-shadow-property `ContentEnc` → kolumn `content_enc text`.
Interceptor-paret äger hela transformen på shadow-strängen: write —
SaveChangesInterceptorn serialiserar `Content`→JSON (delad
`ContentJsonOptions`), krypterar, sätter `entry.Property("ContentEnc")
.CurrentValue`; read — MaterializationInterceptorn läser shadow-ciphertext,
dekrypterar, JSON→`ResumeContent`, sätter `Content` via private-setter-
reflection (befintlig Form B-väg). `EncryptedFieldRegistry` får en Form B-map
(`JsonSerializedVoField(DomainProperty, ShadowProperty, ToJson, FromJson)`).
ValueComparer-frågan **upphör** (Content är ej EF-tracked → ingen comparer
behövs/kan sättas; change-tracking sker på shadow-strängen). RÖD-ordningen
är nu en invariant-regressionsvakt (`ResumeContentMaterializationProbeTests`,
1 [Fact]). Backfill-fönstret (Beslut 5 steg 2): C4.2 mappar BÅDE legacy
`content jsonb` + `content_enc text` som shadows tills cutover; read väljer
`content_enc` (om sentinel) annars legacy `content` (klartext-JSON, ingen
decrypt); ingen `content`-drop i C4.2 (separat cutover/drop = Beslut 5 steg
3–4, Klas-STOPP). De **fyra substans-invarianterna oförändrade** (lazy
encrypt-on-write, sentinel-prefix, bounded backfill, legacy-tolerans);
mekanik-precisering tvingad av EF Core 10-doktrin (paritet not 1–5c, §9.6
p.5) — **ingen substans-ändring, ingen formell amendment, ingen Klas-STOPP
för mekaniken** (architect entydig). **Flaggas i STOPP V-rapporten; Klas
kan override:a till formell amendment** om dual-property-shadow-
konstruktionen bedöms vara besluts-substans. C4.2-impl villkorad av mini-
gate C4.2a (empirisk verifiering av shadow-läsning i `InitializedInstance`
under `AsNoTracking`, paritet C4.0-disciplin).

**Mekanik-not 6 — implementeringsutfall & reconciliation (2026-05-19, STOPP V;
CC-utkast med Klas §9.4-undantag, Klas granskar):** C4.2→C6 levererat
(`89545aa`, full svit grön, security-auditor + code-reviewer GO). Tre
preciseringar av Not 6:s pre-implementerings-prosa, alla **inom #1c:s fyra
substans-invarianter** (architect/CTO entydiga, §9.6 p.5 — ingen
substans-ändring):

1. **C4.2a-gaten GREEN** (Microsoft Learn EF Core 10.0-verifierad):
   `MaterializationInterceptionData.GetPropertyValue<T>(string)` läser
   shadow-property under `AsNoTracking` utan ChangeTracker-entry → Form B-read
   genomförbar som låst.
2. **`ResumeContentMaterializationProbeTests` raderad** (ej längre "invariant-
   regressionsvakt" enligt rad 362–364). #1c eliminerade JSON-`ValueConverter`:n
   (`builder.Ignore(rv => rv.Content)`) → probens load-bearing-premiss
   (prod-modellen applicerar VC:n; probe-only-context observerar
   VC↔interceptor-ordning) **föll bort av #1c:s egen låsta design** — ingen VC
   kvar att regressera mot. #1c:s faktiska read-ordnings-invariant
   (`GetPropertyValue`-shadow-läsning under `AsNoTracking`) bärs nu empiriskt
   av `ResumeContentEncryptionTests` (C4.4) mot riktig Postgres +
   produktions-interceptorerna (starkare skydd än testprojekt-probe mot
   raderad VC). Subsumering, ej täckningsförlust (senior-cto-advisor 2026-05-19
   Approach A, paritet C4.2a-gate-retirement). Likaså raderades
   unit-testet `GetResumeByIdQueryHandlerTests.Handle_WhenResumeExists`
   (handlern dereffererar Content ovillkorligt via `ToDetailDto`; bare
   InMemory utan interceptor NRE:ar — subsumerad av
   `ResumesEndpointsTests.GET_resume_by_id_returns_detail_with_master_version`,
   Api-integ). §7-coverage ej sänkt (flyttad till rätt lager).
3. **Dual-shadow-konstruktionen preciserad** (architect 2026-05-19): `ContentEnc`
   mappas **nullable** (ej `.IsRequired()` — legacy-only-rader har
   `content_enc IS NULL` tills C5-backfill); legacy `content` mappas som
   **read-only rå `string`-jsonb-shadow `ContentLegacyJson`** med
   `PropertySaveBehavior.Ignore` på before+after-save (EF skriver ALDRIG
   `content` → ingen klartext-write-back under dual-state-fönstret; striktare,
   ej svagare). Dessutom krävde en ny ResumeVersion-write utan `content`
   (NOT NULL on-disk) en **expand-fas-migration `ALTER COLUMN content DROP
   NOT NULL`** (icke-destruktiv metadata-only, Beslut 5 steg 2 — ingen
   content-drop, ingen ALTER TYPE; drop = Beslut 5 steg 3–4 separat Klas-STOPP).

Dessa tre + Not 5b/5c är **STOPP V-flaggade**: Klas kan override:a
dual-property-shadow-konstruktionen, den nullable/read-only-precisionen,
eller `ALTER COLUMN content DROP NOT NULL` till formell ADR-amendment om
någon bedöms vara besluts-substans snarare än EF Core 10-doktrin-tvingad
mekanik-precisering. Default (ingen override): mekanik-noter, ingen amendment.

**Mekanik-not 7 (senior-cto-advisor-bind + dotnet-architect-CONFIRM 2026-07-02,
audit-epik #480/#500 — encrypt-on-write skip-predikat):** extern revision
(2026-07-02) fann att encrypt-on-write-interceptorns skip-villkor grindades på
**innehåll** (`IFieldEncryptor.IsEncrypted(plaintext)`, regex `^v\d+:`) i stället
för **proveniens**. Användarlevererad klartext som råkar börja med sentinel-
mönstret (t.ex. en anteckning "v1: ringde rekryteraren…") felklassades som
redan-krypterad → skippades → persisterades i **klartext** at-rest; läsvägen såg
sedan `IsEncrypted==true`, fail-closade på Decrypt och 500:ade raden permanent
(backfiller-fitnessen `LIKE 'v1:%'` grön-klassade dessutom raden som ciphertext).
Innehåll kan aldrig skilja vår ciphertext från användarklartext som liknar den.
Precisering: skip-villkoret grindas på proveniens —
`IsEncrypted(v) && State != EntityState.Added && !property.IsModified` (genom-
passering av vår EGEN oförändrade ciphertext, t.ex. system-scope-re-save per
not 5b). En `Added`-entitet eller en modifierad property är användarlevererad →
krypteras alltid; kortslutningen `State != Added` gör att `IsModified` aldrig
läses för `Added` (ospecificerad EF-per-property-semantik undviks). Klartext-at-
rest blir **strukturellt omöjligt**: enda skip-vägen kräver `!IsModified`, och EF
skriver aldrig en oförändrad property i UPDATE SET → on-disk-ciphertext bevaras
oavsett interceptor↔Npgsql-snapshot-ordning (den egenskap ADR 0049 kräver
eftersom ordningen inte är normativt garanterad, not 1). De **fyra substans-
invarianterna oförändrade** — idempotensen bevaras men blir *striktare* (bara
äkta genom-passering skippas, inte användar-klartext som liknar sentinel), vilket
ÅTERSTÄLLER den avsedda invarianten "all användar-PII krypterad at-rest" som den
innehållsbaserade kontrollen bröt. Empirisk verdikt (paritet C4.0-disciplin,
InMemory förbjudet): två Testcontainers-round-trip-regressioner (Added-anteckning
"v1:…", "v2:"-cover letter) + 61 gröna Security-integ-tester. Mekanik-precisering
tvingad av EF Core 10-doktrin — **ingen substans-ändring, ingen formell ADR-
amendment, ingen Klas-STOPP** (senior-cto-advisor + dotnet-architect entydiga,
§9.6 p.5; paritet not 1-6). Redan-korrupta rader (skrivna av den gamla buggen)
är INTE app-reparerbara utan att bryta fail-closed → forward-fix; detektions-/
saneringsuppföljning spårad som **#524**. **Flaggas i STOPP-rapporten; Klas kan
override:a till formell amendment** om proveniens-predikatet bedöms vara besluts-
substans.

### Beslut 5 — jsonb→text-skifte via expand/contract; aldrig in-place ALTER TYPE

Gäller `resume_versions.content` (raw_payload berörs ej — Beslut 3). Ciphertext
är inte giltig JSONB → kolumntypen måste skifta `jsonb → text`. Skiftet sker
via parallel-change i fyra steg:

1. **Additiv:** `content_enc text NULL` (noll-risk, ingen lock).
2. **Backfill:** Beslut 4-jobbet populerar `content_enc` lazy + batch;
   read-path prioriterar `content_enc`, fallback `content`.
3. **Cutover:** vid 100% (`COUNT(*) WHERE content_enc IS NULL = 0`) flippas
   EF-mappningen till `content_enc`; `content` blir read-only legacy.
4. **Drop:** en separat senare migration (egen commit, efter
   prod-verifiering) droppar gamla `content` JSONB.

**Motivering:** expand/contract/parallel-change (Fowler *Refactoring* 2e 2018;
Ford/Parsons/Kua 2017) — typ-skifte med befintlig data aldrig in-place
destruktivt; varje steg reverterbart med egen `down()`. DDD: befintlig
JSON-`ValueConverter` (`ResumeVersionConfiguration.cs:41-59`) bevaras —
krypto komponeras *runt* (`ResumeContent → JSON → ciphertext → content_enc`) —
**villkorat av C4-gaten enligt mekanik-noten under Beslut 4**; om gaten är röd
äger interceptor-paret JSON+krypto-transformen direkt. `ValueComparer` opererar
fortsatt på klartext-`ResumeContent` oavsett utfall (annars trasas
change-tracking). Idempotent (`IF [NOT] EXISTS`, ADR 0024-precedens).

---

## Konsekvenser

### Positiva

- De fyra user-ägda Känsligt-PII-kolumnerna får ett app-side-lager utöver
  RDS-at-rest — skyddar mot snapshot-share, automated-backup-export och
  IAM-komprometterad DB-läsning.
- Crypto-erasure stänger ADR 0024:s backup-PII-gap under
  overwrite-fönstret; Art. 17-täckning blir omedelbar vid kontoradering.
- Per-användare-DEK ger en enda change-reason per nyckel och O(användare)
  key-rotation — samma infrastruktur återanvänds av FAS 4 BYOK-key-storage,
  vilket eliminerar divergens-risken som drev fas-sekvenseringen.
- raw_payload-exklusionen bevarar generated columns, taxonomi-sök-SPOT
  (ADR 0039/0042) och `JsonContains`-Art.17 orörda — ingen sök-regression.
- Domain förblir orört (ADR 0009 — krypto i Infrastructure-EF-config).

### Negativa

- Restore av en backup med sedan-raderad användare ger olesbar ciphertext.
  Detta är önskat beteende men måste dokumenteras i restore-runbooks så att
  drift inte tolkar det som dataförlust.
- Krypterade kolumner är inte WHERE/LIKE-bara. Verifierat att de fyra
  user-ägda kolumnerna saknar WHERE/LIKE idag (discovery §4) — men en framtida
  sökkonsument på dessa fält kräver searchable-encryption (Beslut 3
  future-watch).
- raw_payload förblir klartext-JSONB at-app-rest. Medveten, motiverad
  exklusion (RDS KMS + sanitizer + 30d-purge + Art.17-null-out), men det är ett
  accepterat defense-in-depth-tak, ej fullt envelope.
- Ny top-level-dependency `AWSSDK.KMS` + ny `user_data_keys`-yta + jsonb→text-
  parallel-change ökar Infrastructure-komplexitet och migrations-scope
  (CTO-estimat 1.5–2.5 v).
- Dual-state (klartext-legacy + ciphertext) existerar tills backfill når 100%
  — mitigeras av sentinel-prefix + deterministisk fitness-funktion.

### Mitigering

- Restore-beteendet dokumenteras explicit i ADR-texten och i FAS 3.5-
  implementationens runbook.
- Sentinel-prefix (`v1:`) gör read-path-disambiguering deterministisk;
  backfill-jobbets `COUNT(*) WHERE ej-ciphertext = 0` är fitness-gate mot
  permanent dual-state.
- `AWSSDK.KMS` + converter + EF-config + DI registreras i samma commit
  (memory `feedback_di_with_handlers_same_commit`).
- jsonb→text via expand/contract — varje steg reverterbart, drop i separat
  senare migration efter prod-verifiering.

---

## Alternativ övervägda

**Beslut 1 — DEK-granularitet:**

- **Uniform per-rad-DEK:** avvisad — bryter billig-Art.17 (crypto-erasure
  kräver O(rader) nyckelhantering) + SRP (N×M nyckelpunkter).
- **Uniform per-aggregat-DEK:** avvisad — döljer att `applications` /
  `resume_versions` är olika aggregat under samma owner.
- **Uniform per-användare inkl. raw_payload:** avvisad — JobAd har ingen
  ägande-användare; per-användare semantiskt omöjlig → primitive obsession på
  nyckelnivå, bryter bounded context.

**Beslut 2 — Crypto-erasure:**

- **NEJ / enbart backup-overwrite:** avvisad — Mastercard-testet: 90%-kontroll
  som stannar; lämnar 7–35d klartext-fönster oåtgärdat när per-användare-DEK
  ändå byggs.
- **Crypto-erasure som ersättning för retention-story:** avvisad — bryter
  EDPB-normen (crypto-erasure får ej åberopas som ursäkt för avsaknad
  retention-story; båda måste samexistera). Skulle felaktigt motivera en
  ADR 0024-amendment i stället för cross-ref.

**Beslut 3 — raw_payload:**

- **(b) Schema-omstrukturering + JsonContains-ersättning, sedan kryptera:**
  avvisad — negativ ROI (Fowler *PoEAA* 2002): schema-omstrukturering +
  JsonContains-ersättning + SPOT-omskrivning + jsonb→text + migration/test för
  noll additionell GDPR-vinst. Scope-creep förklädd till grundlighet.

**Beslut 4 — Migrering:**

- **Ren lazy encrypt-on-write:** avvisad — obegränsad klartext-svans, ej
  bounded (besegrar FAS 3.5-syftet).
- **Ren backfill big-bang:** avvisad — downtime, onödigt då converter ändå
  byggs för lazy-write.

**Beslut 5 — jsonb→text:**

- **In-place `ALTER COLUMN TYPE text USING ...`:** avvisad — destruktiv,
  ingen `down()`, table-lock.
- **Ciphertext lagrad i jsonb-kolumn:** avvisad — typ-lögn (bryter
  schema-som-domänsanning, Evans 2003) + onödig JSONB-overhead på opak data.

---

## Implementationsstatus

**Accepted 2026-05-18; implementation (STOPP I) påbörjad efter Klas-GO.**

Vid Accepted-flippen var inget av detta implementerat. Discovery (HEAD
`8474c06`) verifierade att `AWSSDK.KMS`-paketet, envelope-converter:n,
`user_data_keys`-ytan och samtliga migrationer **saknades** i kodbasen. De
fem berörda EF-configarna bär explicita `TODO(GDPR)`-kommentarer som
deferrar hit.

Klas godkände `Status: Proposed → Accepted` 2026-05-18; implementation
(STOPP I) påbörjas därmed och följer de fem besluten ovan i
split-batch-struktur (prejudikat-domens scope-realism: 1.5–2.5 v CC-tid, med
jsonb→text-parallel-change + crypto-erasure-restore-runbook som största
enskilda osäkerheter).

## Validering

- **Backfill-fitness:** `COUNT(*) WHERE ej-ciphertext = 0` per berörd kolumn
  (Beslut 4) — deterministisk gate mot permanent dual-state.
- **jsonb→text-cutover:** `COUNT(*) WHERE content_enc IS NULL = 0` innan
  EF-mappning flippas (Beslut 5).
- **Sök-icke-regression:** taxonomi-sök-SPOT (ADR 0039/0042) + generated
  columns + `JsonContains`-Art.17 (`RecruiterPiiPurger`) verifieras gröna
  efter implementation — Beslut 3 garanterar att de inte rörs.
- **Crypto-erasure:** integrationstest som raderar JobSeeker, kastar DEK och
  verifierar att backup-resident ciphertext blir olesbar utan att
  icke-raderade användares wrapped DEK:er påverkas.

## Relaterade beslut

- **ADR 0009** — krypto-`ValueConverter` bor i Infrastructure-EF-config;
  Domain orört. Denna ADR respekterar EF-bridge-gränsen.
- **ADR 0024** — Art. 17-cascade + backup/retention. ADR 0049 är
  **komplementär**: ADR 0024 täcker live-data + applog; ADR 0049 lägger
  backup-PII-lagret via crypto-erasure. **Cross-ref, ej amendment** —
  ADR 0024:s text ändras inte.
- **ADR 0032 §8** — JobTech raw_payload sanitizer/PII-stripping. ADR 0049
  Beslut 3 motiverar raw_payload-exklusionen delvis på ADR 0032:s
  sanitizer-allowlist + 30d-purge.
- **ADR 0039** — taxonomi-sök-SPOT. ADR 0049 Beslut 3 bevarar SPOT:en orörd
  genom raw_payload-exklusionen.
- **ADR 0042** — sök-yta-IA (multi-värde-kriterier). Konsument av samma
  generated columns / SPOT som Beslut 3 skyddar.
- **TD-13** (`docs/tech-debt.md:77-108`) — denna ADR är TD-13:s mandaterade
  designval-ADR; TD-13 stängs/uppdateras vid FAS 3.5-implementationens
  slutförande (separat TD-livscykel-touch, §9.7).

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP), 13 (CCP/CRP),
  22 (KISS)
- Eric Evans, *Domain-Driven Design* (2003) — aggregate-ägande,
  schema-som-domänsanning
- Vaughn Vernon, *Implementing DDD* (2013) — kap. 10 (aggregat)
- Martin Fowler, *Refactoring* 2nd ed (2018) — Parallel Change / "Duplicated
  Code"; *PoEAA* (2002) — risk/värde
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — kap. 7 (DRY/YAGNI)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) —
  fitness functions, deterministisk migration
- Microsoft Learn — encryption-at-rest / key-hierarchy; OWASP —
  defense-in-depth / cryptographic agility
- EDPB CEF 2025 right-to-erasure-rapport (2026-02) + blockchain-guidelines
  2025 — backup-overwrite-motivering, crypto-erasure som medel ej ursäkt
- AWS KMS developer guide — `GenerateDataKey` / envelope encryption /
  encryption context
- `docs/reviews/2026-05-18-td13-design-decisions-cto.md` (5 designval) ·
  `docs/reviews/2026-05-18-td13-pii-encryption-discovery.md` (kod-verbatim) ·
  `docs/reviews/2026-05-18-pre-fas4-audit-validation-cto.md` (fas-sekvensering)
- ADR 0009 / 0024 / 0032 / 0039 / 0042 · CLAUDE.md §2.1, §9.6, §9.7
