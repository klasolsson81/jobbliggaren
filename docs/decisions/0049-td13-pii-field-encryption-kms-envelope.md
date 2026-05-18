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
