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

> **Not 2026-07-12 (#802 — KMS-providern borttagen, Local-only):** AWS-exiten är
> nu slutförd för fält-krypteringen. `KmsDataKeyProvider` +
> `CmkKeyId`/`AwsRegion`-options + `AWSSDK.KeyManagementService`/`AWSSDK.Core` är
> **borttagna** (0 Amazon-paket i lösningen); `LocalDataKeyProvider` är den enda
> `IDataKeyProvider`. Provider-default är nu `"Local"`; ett explicit icke-Local-
> värde fail-fastar i DI (`AddPersistence`) — aldrig en tyst fallback. Den
> AWS-fria `IFieldEncryptor`-primitiven är omdöpt `KmsEnvelopeEncryptor` →
> `AesGcmFieldEncryptor` (truth-in-naming; wire-format oförändrat, pinnat av det
> frysta ciphertext-testet). Detta **ersätter** 2026-06-06-notens "KMS-impl +
> paket BEHÅLLS som referens". Besluts-substansen (envelope-struktur, owner-AAD-
> bindning, fail-closed-invariant, AES-256-GCM-primitiv) är **fortsatt
> oförändrad** — bara den döda KMS-wrap-grenen försvinner. Prod-master-nyckelns
> skyddsmodell + rotation på Hetzner kvarstår **TD-102** (Major, Hetzner-deploy),
> självständig från den borttagna KMS-providern och en senare separat
> 0049-amendment.

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

---

> **AMENDMENT 2026-07-13 (#842) — Beslut 3: the stated justification for excluding
> `raw_payload` is WITHDRAWN. The conclusion is NOT reversed. The `JsonContains`
> constraint is VOID.**

**Scope of this amendment.** It touches **Beslut 3 only**. Beslut 1, 2, 4 and 5
(per-user DEK, crypto-erasure, hybrid lazy encrypt-on-write, jsonb→text
expand/contract) and Mekanik-not 1-7 are unaffected. It **withdraws a stated
justification**; it does **not** reverse the decision that justification supported, and
it does **not** decide whether `raw_payload` should now be encrypted. That question is
re-opened on the merits and left **open** (§D below). The false pillar is being removed
from the reasoning rather than silently retained — which is the whole discipline #842
exists to enforce.

#### A. What Beslut 3 stated (verbatim)

The exclusion rested on a *"tre-lagers befintlig motivering"* (`:148-152`):

> "`job_ads.raw_payload` krypteras **inte** av TD-13-envelopet. Exklusionen
> dokumenteras med tre-lagers befintlig motivering: JobTech-payloaden är redan
> saniterad (`JobTechPayloadSanitizer` allowlist, ADR 0032 §8-amendment),
> self-purgande (30d, `PurgeStaleRawPayloadsJob`) och Art. 17-null-out:ad
> (`RecruiterPiiPurger`). Envelope ovanpå tre befintliga kontroller på
> redan-saniterad icke-user-PII ger **noll additionell GDPR-vinst** men bryter tre
> Postgres-side-mekanismer:"

The third of those Postgres-side mechanisms was the Art. 17 erasure path itself
(`:162-164`):

> "3. **Art. 17-redaction** (`RecruiterPiiPurger.cs:38-41`,
>    `EF.Functions.JsonContains` = Postgres `@>` direkt mot raw_payload) —
>    ciphertext → `@>` matchar ej → **Art. 17-radering bryts**."

And the trade-off was booked as a bounded, defensible one (`:182-184`):

> "**Trade-off:** raw_payload förblir klartext-JSONB at-app-rest (skyddad av RDS
> KMS + sanitizer + 30d-purge + Art. 17-null-out). Medveten dokumenterad
> exklusion (EDPB CEF 2025: exklusion *med* motivering = accepterat)."

#### B. What is false, layer by layer

**Layer 1 — *"redan saniterad"*: FALSE as an inference, and false in the direction that
matters.** `JobTechPayloadSanitizer` is a **key-name filter that never examines a
value**. Its default-deny allowlist drops the PII *keys*
(`JobTechPayloadSanitizer.cs:107-108`) but **deliberately retains every free-text key** —
`headline`, `description`, `description_html`, `description_text`, `text`,
`text_formatted`, `company_information`, `needs`, `requirements` (`:33-35`) and
`salary_description` (`:55`) — whose values are `DeepClone()`d unexamined (`:99`).
**It strips the field, not the address.** PII in free text was never a gap in the
sanitizer's design; it **is** the design. Consequently `raw_payload` carries the
recruiter's email in its retained free-text keys, in plaintext, at app-rest — the exact
class of exposure this envelope exists to close. Measured on the real corpus
(2026-07-13, dev Postgres 18.3, 93 469 ads): **27 077 ads (29 %) carry a well-formed
email in the ad body and 13 134 carry a phone number.** *"Redan saniterad"* was read as
*"contains no recruiter PII"*. It never meant that.

**Layer 2 — *"self-purgande (30d)"*: materially false.** `PurgeStaleRawPayloadsJob` does
exactly one thing — `SetProperty(j => j.RawPayload, _ => null)` (`:93-97`) — and it
**never touches `description`**, while its own doc comment (`:18-20`) claims it erases
*"rekryterar-PII som överlever sanitizer:n (free-text-yta i description)"*. It claims to
erase precisely the PII it cannot reach. And the 30-day clock does not run as documented:
the nightly full backfill (`SyncPlatsbankenSnapshotJob`) and the 10-minute stream both
funnel through `UpdateFromSource`, which **unconditionally reassigns `RawPayload`**
(`JobAd.cs:155-159`), so a purged payload is **restored within ≤24 h for any ad still in
the feed** (#845; already recorded at ADR 0032 A2 `:1090-1092`).

> **Corrected 2026-07-26 (#845) — two claims in the paragraph above are no longer true.**
> **(1)** The deletion rule for `raw_payload` lives in exactly one place — **ADR 0032 Amendment
> 2026-07-26 §C2** — and is neither *"30 days after publication"* nor *"30 days after the ad leaves
> the feed"*; both are false. Do not restate a duration here.
> **(2)** `UpdateFromSource` no longer reassigns the payload **unconditionally**: it **refuses on
> `Erased`** (`JobAd.cs:382-384`), and `JobAd.Erase` nulls the payload outright (`JobAd.cs:267`) —
> which is precisely what makes an Art. 17 erasure durable against re-ingest.

**Layer 3 — *"Art. 17-null-out:ad (`RecruiterPiiPurger`)"*: FALSE, completely.**
`RecruiterPiiPurger` probed jsonb containment on `{"employer":{"contact_email": …}}`
(`RecruiterPiiPurger.cs:31-52`) — a key the ingest path **guarantees is absent**. Two
independent locks: the wire POCO declares only `name` + `organization_number` and cannot
emit it (`JobTechSearchResponse.cs:125-143`), and the sanitizer's default-deny allowlist
would drop it anyway. **Measured: 0 of 93 469 ingested ads carry that key.**
`rowsAffected = 0` was its **only possible outcome**. It was not approximately vacuous;
it was **100 % vacuous**. It has been deleted (#842, PR1), together with
`IRecruiterPiiPurger` and the `RedactRecruiterPii` command; the admin endpoint now
returns **501** with a truthful problem detail.

**Stale by-catch, recorded not re-litigated:** the Trade-off's *"skyddad av RDS KMS"* leg
also no longer exists. AWS was retired (ADR 0066) and the KMS provider removed (#802) —
see the 2026-06-06 and 2026-07-12 notes at the head of this ADR. The Hetzner-phase
disk-at-rest and master-key protection model remains **TD-102** and is unbuilt. This
amendment does not re-open it; it is flagged only because the Trade-off sentence still
reads as if all four protective legs stand. **One of the four remains: the sanitizer, and
only for the structured keys it actually filters.**

#### C. The irony, stated plainly

Field encryption was declined for `raw_payload` in part **in order to avoid breaking an
Art. 17 mechanism that was already structurally incapable of erasing anything**. The
reasoning at `:162-164` — *"ciphertext → `@>` matchar ej → Art. 17-radering bryts"* — is
literally true and completely worthless: encryption would indeed have broken the `@>`
probe, and the `@>` probe matched nothing, could match nothing, and had matched nothing
in 93 469 ads. **We protected a no-op from encryption.** Worse, the mechanism we protected
was cited as evidence that there was nothing left to protect (Layer 3 of the same
justification), while the data it was supposed to erase sat in plaintext in the very
column the envelope was declined for.

The practical harm to date is bounded and should be stated as fairly as the defect:
`audit_log` holds **0 rows** for the erasure endpoint — it has **never been called**, so
**no data subject has yet received a false confirmation**. That bounds the damage. It does
not excuse the reasoning.

#### D. Verdicts

**WITHDRAWN — the three-layer justification.** *"Envelope ovanpå tre befintliga kontroller
på redan-saniterad icke-user-PII ger noll additionell GDPR-vinst"* is withdrawn in full.
Two of its three controls are falsified (Layers 2 and 3) and the third does not do what
the sentence assumes it does (Layer 1). The premise *"there is no recruiter PII in
`raw_payload` worth encrypting"* was **factually wrong at the time it was written**.

**VOID — the `JsonContains` constraint (`:162-164`).** There is **no `@>` erasure
mechanism left to protect**: `RecruiterPiiPurger` is deleted and the replacement contract
(ADR 0106) uses **no jsonb-containment probe of any kind**. Mechanism 3 of Beslut 3, and
the *"JsonContains-ersättning"* cost item in the rejection of alternative **(b)**
(*"Alternativ övervägda — Beslut 3"*), are **void and must never again be cited as a
reason against encrypting `raw_payload`.** The cost they priced has already been paid: the
mechanism is gone.

**NOT REVERSED — the decision itself.** Beslut 3's conclusion (`raw_payload` stays outside
the DEK envelope) **survives, for now, on reasoning that is independent of the withdrawn
pillar**:

1. **The generated-column constraint is intact and is broader than the ADR recorded.**
   `job_ads` carries **9 STORED generated columns, 7 of which derive from `raw_payload`**
   (`organization_number`, `ssyk_concept_id`, `region_concept_id`,
   `municipality_concept_id`, `occupation_group_concept_id`, `employment_type_concept_id`,
   `worktime_extent_concept_id`) — the ADR named only two (`:156-158`). Postgres computes
   `raw_payload->…` at write time; **ciphertext is not valid JSONB and the `->` operator
   cannot be computed over it at all.** Mechanism 1 (and mechanism 2, the taxonomy-search
   SPOT, which depends on it transitively) stands **unchanged and unweakened**. This is a
   real, still-valid, load-bearing constraint — and it, not the falsified PII pillar, is
   what actually holds the decision up.
2. **Post-ADR-0106 there will be materially less to protect.** Under Tier A the ad body is
   scrubbed of detected contact details **at ingest**, and the redactor is applied to the
   `rawPayload` string as well — redacting values **in place**, never nulling, with a
   replacement token carrying no JSON-structural character, so the document stays valid
   JSONB and the seven generated columns keep computing. **Tense discipline: Tier A is
   BOUND but NOT YET SHIPPED (PR2). Today `raw_payload` still holds those addresses.**
   ⚠ **Corrected 2026-07-26 (#845): Tier A SHIPPED** (`daa4b51d`), with the corpus backfill —
   so *"today"* in the sentence above means 2026-07-13, not now. Ship dates and commits are
   recorded once, in ADR 0032 Amendment 2026-07-26 §C6.

**OPEN — whether `raw_payload` should now be encrypted is re-openable on the merits, and
is NOT decided by this amendment.** It is genuinely live, for reasons the original Beslut
3 could not have weighed:

- One of the two costs that priced alternative **(b)** has already evaporated (the
  `JsonContains` replacement is done — see VOID above).
- **#841** would materialise the seven `raw_payload`-derived columns as **C#-written
  ingest columns**, which changes the shape of the single surviving constraint against
  encryption. If those columns stop being computed by Postgres over the jsonb, the
  strongest remaining argument for the exclusion is no longer the same argument.
- Even after Tier A, `raw_payload` retains what the detector misses (obfuscation,
  image-embedded addresses, and the recruiter's **name**, which no regex reaches).

**This amendment takes no position on that question. It is open.** Whoever re-opens it must
argue it on (1) the generated-column constraint as it stands after #841, and (2) the
residual PII in the free-text keys after Tier A — **and must not resurrect the withdrawn
three-layer pillar or the void `JsonContains` constraint.**

#### E. Passages in this ADR that inherit the withdrawn pillar

Cited by section (not by line) because this amendment shifts line numbers below it. The
original prose is **left standing as the historical record**; **this amendment overrides
it** wherever they conflict:

| Section | Inherited claim | Status |
|---|---|---|
| **Beslut 3**, `:148-152` | *"tre-lagers befintlig motivering … noll additionell GDPR-vinst"* | **Withdrawn** (§D) |
| **Beslut 3**, `:162-164` | Mechanism 3, *"Art. 17-radering bryts"* | **Void** (§D) |
| **Beslut 3 — Motivering**, `:174-180` | *"raw_payload är funktionellt kohesivt med giltig JSONB (generated columns → taxonomi-sök-SPOT ADR 0039 + JsonContains-Art.17)"* | Read **without** the `JsonContains-Art.17` conjunct; the generated-column/SPOT half stands |
| **Beslut 3 — Trade-off**, `:182-184` | *"skyddad av RDS KMS + sanitizer + 30d-purge + Art. 17-null-out"* | Three of four legs gone (§B). The exclusion is **no longer a documented-and-motivated** one in the EDPB CEF 2025 sense until re-argued (§D OPEN) |
| **Konsekvenser — Positiva** | *"raw_payload-exklusionen bevarar generated columns, taxonomi-sök-SPOT (ADR 0039/0042) och `JsonContains`-Art.17 orörda"* | Drop the `JsonContains`-Art.17 conjunct — there is nothing left to preserve |
| **Konsekvenser — Negativa** | *"Medveten, motiverad exklusion (RDS KMS + sanitizer + 30d-purge + Art.17-null-out)"* | Same correction as the Trade-off |
| **Alternativ övervägda — Beslut 3 (b)** | *"negativ ROI … + JsonContains-ersättning + SPOT-omskrivning …"* | The `JsonContains-ersättning` cost item is **void** — already paid (§D) |
| **Validering** | *"`JsonContains`-Art.17 (`RecruiterPiiPurger`) verifieras gröna efter implementation"* | **Void** — the mechanism is deleted; there is no green to verify. The generated-column and SPOT non-regression checks stand |
| **Relaterade beslut — ADR 0032 §8** | *"ADR 0049 Beslut 3 motiverar raw_payload-exklusionen delvis på ADR 0032:s sanitizer-allowlist + 30d-purge"* | Both cited grounds are falsified (§B Layers 1-2). ADR 0032 carries its own dated amendments A2/A3 for the same drift |

#### F. The replacement contract (BOUND, NOT YET SHIPPED)

> **SUPERSEDED IN PART, 2026-07-26 (#845).** Both tiers shipped: Tier B in `269a4603` on 2026-07-15,
> Tier A in `daa4b51d` on 2026-07-17. The statements below that neither tier exists, and that the
> launch gate stays closed until PR3 lands, are false as of those dates. **The gate's status is owned
> solely by ADR 0106** and is neither declared lifted nor re-asserted here — see ADR 0032 Amendment
> 2026-07-26 §C6. Do not route an Art. 17 request to a manual workaround on the strength of this
> section.

The Art. 17 recruiter-PII contract is now **ADR 0106** (local per ADR 0072), a two-tier
design. **Neither tier is shipped yet — do not read this ADR as describing a control we
have.** That failure mode is the exact defect #842 exists to correct.

- **Tier A (Art. 25, everyone, no request needed, heuristic, disclosed) — PR2, not yet
  shipped.** Email and phone are stripped from the ad body at ingest as a `JobAd`
  aggregate invariant (`RecruiterContactRedactor`, deterministic, no LLM per ADR 0071),
  replaced by a marker pointing to the canonical ad at Arbetsförmedlingen. Detection is
  imperfect and the privacy policy says so.
- **Tier B (Art. 17, on request, provable, no detector involved) — PR3, not yet shipped;
  the launch gate stays closed until it lands.** A valid request removes **the entire ad
  record** (`JobAdStatus.Erased`, zero migration) and blocks its re-import. It deletes the
  **carrier**, not the **string**, so `description`, `search_vector`, `extracted_terms`,
  `extracted_lexemes`, `raw_payload` and the seven derived columns go together — and it
  covers the recruiter's **name**, which no regex can reach.

Why the contract had to change shape at all, in one line: **Art. 17(1) is textually
unqualified.** The *"reasonable steps / available technology"* language lives only in Art.
17(2), which governs informing **other** controllers, not erasure from our own store —
so there is no instrument that lets us soften a promise about our own copy, and a
mechanism that reports success while erasing nothing is an independent **Art. 12(3)**
breach on top of the Art. 17 failure.

#### G. Sources for this amendment

- `docs/research/2026-07-13-842-erasure-evidence-pack.md` — §1 (the vacuous probe, with
  file:line), §2 (surface inventory), §3 (what the code does and does not do), §5 (the
  table of falsified doc claims; this ADR is **item 8**), §9 (measurements against the
  real dev corpus, 2026-07-13).
- `docs/reviews/2026-07-13-842-erasure-contract-cto.md` — the binding CTO ruling; **V19**
  mandates this dated in-file amendment, **V3/V5** bind Tier A/Tier B, **V10** confirms
  #842 takes zero migrations.
- ADR 0032 amendments **A2** (`:1083-1092`, #845) and **A3** (`:1099-1122`, #842) — the
  same drift, already recorded at source. ADR 0024 `:467-472` (the Art. 17 cascade
  registry) carries its own #842 amendment.
- Code, at HEAD: `JobTechPayloadSanitizer.cs:33-35, :55, :99, :107-108` ·
  `JobTechSearchResponse.cs:125-143` · `RecruiterPiiPurger.cs:31-52` (deleted in PR1) ·
  `PurgeStaleRawPayloadsJob.cs:18-20, :93-97` · `JobAd.cs:155-159` ·
  `PlatsbankenJobSource.cs:199-207`.

---

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

---

## Amendment 2026-08-09 (#198) — prod master-key protection model, B-1 discharge, rotation cadence (M-2/M-3)

**Status:** Accepted (amendment to ADR 0049). The protection model (§1–§2) ships in this PR
(`feat/master-key-protection-198`, #198 PR-1). The rotation mechanism (§5) is a bound design;
the `migrate rewrap-master-key` dispatch arm **shipped in #198 PR-2** (2026-08-09); §5 is now
descriptive rather than forward-looking — the same discipline this ADR's own #845/#842 corrections
already apply elsewhere in this document.
**Date:** 2026-08-09
**Decision-makers:** Klas Olsson — self-managed master key over an external KV/HSM, and the
scale trigger in §4, both binding inputs not renegotiated by this amendment
(`docs/reviews/2026-08-09-198-masterkey-cto.md:8-10`); senior-cto-advisor — decision-maker
(CLAUDE.md §9.2) for gates B-1 (Blocker), M-2, M-3 (Major) and M-7's key-access half (Major,
escalated to Klas, not resolved here).
**Source:** `docs/reviews/2026-08-09-198-masterkey-cto.md` (the bound ruling, Q1–Q8). That file
is gitignored (`.gitignore:123`) and absent from a fresh worktree or a peer session unless
docs-sync ran — this amendment restates every fact it depends on so it stands alone.
**Trigger:** #198 (formerly TD-102) — the ADR 0050 pre-beta-data gate this ADR's own
2026-06-06 and 2026-07-12 head-notes both deferred: *"Self-managed-nyckelns prod-skyddsmodell +
rotation för Hetzner ... kräver ADR-amendment ... innan riktig PII"* (this file, line 28).

> **This amendment is authoritative for the prod master-key protection model and is written to
> be read alone**, per the precedent ADR 0050 set for itself in its own
> `Amendment 2026-08-04` (`docs/decisions/0050-deployment-migration-aws-exit-hetzner.md:683-685`):
> its CTO source is gitignored, so a fresh worktree without docs-sync loses the rationale, never
> the gate.

### 1. Protection model: files on tmpfs, never container environment

The field-encryption master key and the three pseudonymisation peppers reach `api` and
`worker` as **files on a RAM-backed tmpfs mount**, never as container environment values and
never as a value in `deploy/.env`.

- **Host side:** `/etc/tmpfiles.d/jobbliggaren.conf` creates `/run/jobbliggaren/secrets`
  (mode `0700`) at every boot (`deploy/systemd/jobbliggaren-tmpfiles.conf:20-21`). An operator
  runs `deploy/systemd/jobbliggaren-inject-secrets.sh` after each boot, which measures the
  container runtime UID **out of the api image** (`docker run --rm --entrypoint id <image> -u`,
  never hardcoded — `jobbliggaren-inject-secrets.sh:76-93`) and writes each value `0400`,
  owned by that UID.
- **Container side:** `deploy/docker-compose.yml`'s `x-app-secrets-mount` anchor bind-mounts
  the same host directory read-only into both `api` and `worker` at `/run/app-secrets`
  (`docker-compose.yml`, the `x-app-secrets-mount` anchor).
- **Code side:** the four values arrive as `<KEY>_FILE` environment variables whose *value is
  a path*, resolved by `EnvFileSecretsConfiguration.cs` into ordinary `IConfiguration` keys
  (`__` → `:`), registered last in both hosts (`Api/Program.cs`, `Worker/Program.cs`) so the
  file outranks any stray environment variable. This is the **same convention
  `Jobbliggaren.Migrate` already used** — `MigrateEnv.Resolve`
  (`src/Jobbliggaren.Migrate/MigrateEnv.cs:24-38`) implements the identical `<NAME>_FILE`-first
  policy for the one host with no configuration pipeline. One spelling, three executables.
- **Dev is unaffected.** With no `*_FILE` variables set, the provider contributes zero keys;
  `appsettings.Local.json` works exactly as before (CLAUDE.md §11) — no new mandatory dev key,
  no template change.
- **There is no encrypted-at-rest copy of the master key anywhere on this disk.** That is a
  decision, not an omission (§2). An off-box escrow is therefore the only recovery path — and
  it is **undecided as of this writing, not delivered**. The CTO ruling escalates it to Klas
  and binds it as a hard cutover prerequisite
  (`docs/reviews/2026-08-09-198-masterkey-cto.md:118-121`); §9.6 makes a risk acceptance his to
  grant and never a session's to claim. An earlier draft of this amendment recorded it as
  delivered fact, which would have let a cutover proceed past an open gate. It covers all four
  secrets, not only the master key (§6).

### 2. B-1 discharge and its evidence form

**Two plaintext-on-disk surfaces were measured, not one.** `deploy/.env` (root-owned, `0600`)
was the obvious one. The second was found on the box on 2026-08-05 (#1240): Docker persists a
container's environment in its own on-disk state, and `docker inspect` returns the value
**even after the container has exited** (`docker-compose.yml:45-48`). A sweep that only
checked `.env` and running containers would have missed it.

**The gate's own parenthetical is exhausted, and that must be written down rather than
silently worked around.** B-1 reads *"(systemd-credentials TPM-bunden el.
sops+age→tmpfs)"* (ADR 0050:566) — written 2026-06-08 against the Hetzner CAX31 host, since
superseded by Netcup (ADR 0050 `Amendment 2026-08-04`; ADR 0122). Measured on the actual box,
2026-08-09: `systemd-analyze has-tpm2` reports `partial` with no `/dev/tpm0` and no `libtss2`
— the TPM branch is closed. `sops` is absent from apt on Debian 13 (trixie) — the second
branch would arrive through a non-apt channel, against this project's own precedent of
pinning to what Debian maintains. **The gate's requirement is "never plaintext on disk"; its
parenthetical is an illustrative enumeration, not an exhaustive one.** Discharging B-1 with
operator-injected RAM-only files — a mechanism the parenthetical does not name — is not a
deviation from the gate. It is written here explicitly so a later reader scores B-1 met
rather than open for lacking a named mechanism.

**Evidence form.** The check must run over `docker ps -aq` — every container, **including
exited ones** — because the exited-container surface is exactly the one a running-only sweep
would miss (that is how #1240 was found in the first place):

```bash
K=$(sudo cat /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64)
sudo docker inspect $(sudo docker ps -aq) | grep -cF "$K"   # expect 0
unset K
```

**The check greps for the VALUE, not the variable name, and an earlier draft of this
amendment had that wrong** — it matched on `FieldEncryption|Pepper` and called an empty
result the evidence. That test can never pass on a correctly configured box: the anchor
deliberately leaves five `*_FILE=` **pointers** in the environment, so the name-based grep
matches by design. An operator following the wrong form would have concluded B-1 was still
open. Corrected against `vps-deploy-stack.md` row 21, which is authoritative for the command.

The structural half is separate and also required: no environment entry may carry a crypto
**value** — only `*_FILE=` path lines.

That, together with `deploy/.env` containing none of the four values and
`jobbliggaren-inject-secrets.sh --check` reporting all five files present and the directory
traversable, **is** the discharge evidence. (Five, not four: the key identity travels as a
file alongside the key bytes.)

**B-1 is satisfied when those verification-log lines exist against the real box, not when
this PR merges.** This PR delivers the mechanism; it cannot prove the instance, because the
session driving it has read-only SSH (`docs/reviews/2026-08-09-198-masterkey-cto.md:8`) and
the injection is Klas's own ops action. The gate closes on his verification, not on merge.

### 3. Accepted in-memory residual — described here, granted nowhere

The master key lives in `api`/`worker` process memory for the whole process lifetime
(`LocalDataKeyProvider.cs:76,116-118` — `_masterKey`, *"lever singleton-instansens
livstid"*). Anyone with root on the box can read it out of that memory. Under this host's
configuration, key theft **is** root: `NOPASSWD` sudo for `jpadmin` plus a passphrase-less
operator key together mean whoever holds the operator key has unrestricted root (ADR 0123).

**This amendment states that exposure and cites ADR 0123 for its acceptance. It grants
nothing.** ADR 0123's own status line is explicit: *"this is a risk acceptance, and CLAUDE.md
§9.6 makes that Klas's to grant, never a session's to claim"*
(`docs/decisions/0123-nopasswd-sudo-with-a-passphrase-less-operator-key-means-key-theft-is-root.md:3`),
and as of this writing **ADR 0123 is `Proposed`, not `Accepted`** (same file, line 1). A
reader who cites ADR 0123 as closed authority for the in-memory residual is reading a document
ahead of its own status line. This amendment does not create a second, competing acceptance of
the same risk — the memory-residual risk named in ADR 0050's gate M-2 and ADR 0123's
root-theft risk are the **same risk**, and it has exactly one place to be granted.

### 4. Ratified scale trigger for external KV/HSM

**Klas decision, 2026-08-09:** self-managed key now; no external KV/HSM; no §9.5 web research
commissioned for this decision (`docs/reviews/2026-08-09-198-masterkey-cto.md:9-10`).

The trigger to revisit fires on **any** of:
- a second box,
- a paid hosting tier,
- **≥100 real users with encrypted PII**,
- a compromised box, or
- a second key holder.

When any one fires, **Klas decides then** — this amendment does not pre-commit to a
mechanism, only to the moment of reconsideration.

**HSM is assessed as not legally required at beta scale.** Art. 32 sets a proportionality
standard, not an absolute HSM requirement, for an opt-in-testers, low-volume phase
(`docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-security.md:26`). AWS KMS specifically is
closed by `NoAmazonReferenceTests`' allow-list
(`tests/Jobbliggaren.Architecture.Tests/NoAmazonReferenceTests.cs:16-31`) — reopening it is a
rule change, not a config flip. A non-AWS external KV (Vault, Infisical) is **not** touched by
that test, but is graded YAGNI at current scale and is exactly what the trigger above exists
to reconsider.

### 5. Ratified rotation cadence (gate M-3)

**Cadence: at least annual, plus event-driven** — box compromise, offboarding of anyone with
box access, or any known exposure of the key (ADR 0050:568; consistent with the earlier
security dom, `docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-security.md:51-54`).

**Mechanism (SHIPPED, #198 PR-2, 2026-08-09):** an offline
`rewrap-master-key` dispatch arm in `Jobbliggaren.Migrate`. Migrate has no DI and builds
`AppDbContext` without interceptors, so the operation runs with no audit side-effects and no
DEK-bearing aggregate materialised in a system job.

> **Delivered as a separate `migrate-rewrap` service under `profiles: ["ops"]`**, rather than a
> mount on `migrate`. `schema` runs on every `up`, so a mount there would hand the master key
> hourly to a container that needs no crypto material; the profile keeps the declaration in the
> compose file without putting it on the default path.

> **The FIRST rotation does not need this arm at all.** Measured 2026-08-09 with raw SQL:
> `user_data_keys` holds **0 rows**, so there is nothing to re-wrap and rotating the master key
> is simply injecting different bytes. **B-1's cutover is therefore not chained to PR-2.**
> Re-measure before relying on it — the first registered user creates the first row.

- **Idempotency marker:** `user_data_keys.cmk_key_id`, stamped from
  `FieldEncryptionOptions.LocalMasterKeyId` (`UserDataKeyStore.cs:79`) — configurable rather
  than hardcoded specifically so that rows written after a rotation are not mis-stamped with
  the retired key's identity (`FieldEncryptionOptions.cs:42-60`). A rotation moves it
  `local-v1` → `local-v2`.
- **`dek_version` is untouched.** The #501 single-version invariant governs DEK rotation and
  explicitly carves out this operation: *"ej TD-102:s master-nyckel-re-wrap, som behåller
  dek_version"* (`UserDataKeyStore.cs:44-45`). #501's own axis is a different hazard — a
  versionsblind read path silently decrypting a `dek_version=2` row with the wrong DEK — pinned
  by `ResolveDek_WhenHigherDekVersionExists_FailsClosed`
  (`tests/Jobbliggaren.Worker.IntegrationTests/Security/UserDataKeyStoreIntegrationTests.cs:280-324`,
  "Scenario 10"). A master-key re-wrap never inserts a second `dek_version` row, so it never
  exercises that guard.
- **The wire header (`0x01`) does NOT bump.** An earlier version of this file's own code
  comment said a master-key rotation would bump it — that was wrong, and is now corrected in
  the code itself: layout is unchanged across a rotation, and identity lives in `cmk_key_id`,
  not the wrapped-DEK header (`LocalDataKeyProvider.cs:41-48`, "corrected 2026-08-09, #198").
  This also **corrects an older security review's recommendation** of a `0x01`→`0x02`
  version-byte progression (`docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-security.md:54`),
  which predates this bind and must not be read as still current.
- **Write path:** `ExecuteUpdateAsync` with compare-and-swap on
  `(JobSeekerId, DekVersion, CmkKeyId == oldKeyId)`; `affected != 1` fails loud. No mutator is
  added to `UserDataKey` — it is an Infrastructure-internal persistence type, not a Domain
  aggregate (§2.2 does not govern it), and a mutator would route the write through the change
  tracker, losing the atomic CAS.
- **One transaction over all rows.** At beta scale `user_data_keys` is a handful of rows. A
  crash rolls back to an untouched database; a re-run after success selects 0 rows and exits
  0 — that exit code **is** the idempotence proof required to close M-3.
- **Verification must reach field ciphertext, not only the DEK.** A rewrap that generates a
  *new* DEK instead of re-wrapping the existing one passes every DEK-level check while
  destroying all field data. Both a CI-level field-ciphertext round-trip test and an app-level
  read after the real rotation are required before M-3 is called closed.
- **A drill against a copy is required before any real rotation** — steered by
  `MIGRATE_APP_CONNECTION_STRING`, never `MIGRATE_DB_NAME` (which only reaches the
  master-credential path). Procedure: `docs/runbooks/master-key-ops.md` §4.

**Why the current key rotates at cutover rather than merely relocates.** The key has been
plaintext on disk since 2026-08-05, on a host where key theft is root (ADR 0123). Relocating
it to tmpfs does not erase the copies already made: `sed -i` on `.env` writes a new file and
renames, leaving the old bytes in freed disk blocks, and deleting the exited containers' state
frees more — no in-guest procedure can reach physical NVMe blocks a wear-levelling controller
has already reassigned. **Rotation is the only operation that makes every such copy worthless
simultaneously; relocation makes none of them worthless.** The current key is rotated as part
of the #198 cutover for exactly this reason, once the mechanism above ships.

**Named scale trigger for the mechanism itself:** when the `user_data_keys` row count makes
the single-transaction offline window unacceptable, the shape above is revisited. Not fired
today.

### 6. The three peppers rode a shared change-reason — never a B-1 requirement

**All four secrets move onto the tmpfs mechanism, and — as corrected below — all four get new
bytes at cutover.**

**B-1 names the master key, singular.** The peppers travel because `docker-compose.yml`'s
`x-app-secrets` anchor exists so that `api` and `worker` cannot structurally diverge on any of
the four values (`docker-compose.yml:56-64`); splitting one value out would leave that
guarantee resting on two mechanisms — a file mount and `${...:?}` interpolation — instead of
one. **This must be read as defence in depth riding a shared change-reason, not as a B-1
requirement.** A future reader must not conclude that the gate demanded moving the peppers; it
did not.

#### The peppers are ROTATED too, and the reasoning that said otherwise was a generalisation

The first version of this section had the peppers **moved byte-identically**, on the ground that
a pepper is generate-once. That ground was `deploy/.env.example`'s single sentence covering all
four values — **a template sentence standing in for a measurement**, and it was wrong in three
different ways at once. security-auditor graded the omission Major; the CTO bind was re-opened
and corrected on 2026-08-09.

Rotatability is per secret, and only one of the three actually depended on whether the database
was empty:

| Secret | Rotatable? | Why |
|---|---|---|
| `AuditPseudonymization` | **Any time**, at one named cost | No read path matches against it — verified by tracing the consumers, not taken from the DI comment that calls it *"rotation-tolerant"*: the query handler filters on `OccurredAt`/`UserId`/`EventType`/`AggregateType`, and the only `Pseudonymize(` call site outside the interface is a write. The cost is forensic rather than functional: the pseudonym's purpose is to link one erased subject's Art. 17 audit records to each other, and a rotation breaks that linkage **across the rotation boundary**, permanently. Immaterial at 13 rows; state it rather than claiming an unqualified "even against a full database", which is broader than the evidence. |
| `CvReviewFingerprintPseudonymization` | **While `resume_finding_statuses` is empty** | The fingerprint is recomputable, but rotating makes every stored `target_fingerprint` mismatch, so every Ignored/Resolved finding silently reverts to Open. |
| `CompanyWatchPseudonymization` | **Only while `company_watches` has no rows** | `BackfillCompanyWatchOrgNrTokenJob` destroyed the plaintext organisation number in place, so an existing token cannot be recomputed under a new pepper — not expensively, but mathematically. |

**Measured on the box 2026-08-09, raw SQL inside the postgres container (not through EF, so
soft-delete query filters cannot hide rows):** `resume_finding_statuses` **0**,
`company_watches` **0**, `user_data_keys` **0**. `audit_log` holds 13 rows, which is immaterial
for the reason in the table.

All three windows are therefore open, and **the company-watch window closes at the FIRST row,
not at "first real data"** — one test user following one company would lock that pepper
permanently. The peppers carry the same disk exposure as the master key (same `.env`, same
container state, since 2026-08-05), and the argument for rotating rather than relocating the
master key applies to them unchanged: freed disk blocks, deleted container state, operator
scrollback and any provider snapshot are unreachable by any procedure — **only new bytes make
them worthless**.

Two code comments are qualified rather than reversed in the same change
(`DependencyInjection.cs`, `BackfillCompanyWatchOrgNrTokenJob.cs`): *"permanent/non-rotatable"*
is precise only as **"non-rotatable once any row exists"**. Without the condition, the next
reader takes it as a prohibition on what was just done.

**This must still be read as defence in depth riding a shared change-reason, not as a B-1
requirement.** B-1 names the master key, singular. The peppers travel because
`docker-compose.yml`'s `x-app-secrets` anchor exists so `api` and `worker` cannot structurally
diverge on any of the four values; splitting one out would leave that guarantee resting on two
mechanisms instead of one. And escrow (§1) covers all four: losing a pepper has the same effect
as rotating it after rows exist.

### 7. Relation to the mandatory second security review, and to ADR 0093 / DPIA R-F4

ADR 0050 requires *"en andra security-auditor-granskning av den faktiska
prod-konfigurationen (master-nyckel-injektion, backup-kryptering, TLS-topologi, härdning) ...
innan första beta-data laddas"* (ADR 0050:578-581). This amendment states explicitly that the
obligation splits into three parts, none of which substitutes for another:

1. **The PR-time mechanism review** — code-reviewer/security-auditor reading this diff
   (CLAUDE.md §9.2), against no live box.
2. **A post-ops signing of the master-key leg specifically**, against the *measured state of
   the real box* after Klas's injection (§2's evidence form). This leg is what closes B-1, M-2
   and M-3 for #198.
3. **The remaining legs** — backup encryption (#197), TLS topology (M-5a/M-5b, #196), the
   hardening baseline including a re-review of ADR 0123 — stay open until first real-data
   corpus load, independent of this amendment.

**No PR-time APPROVE of this diff should be read as satisfying ADR 0050:578-581 in full.** It
satisfies leg 1, for the master-key mechanism only.

**ADR 0093 (Fas4b CV motor v2) and DPIA finding R-F4 are not raised or altered by this
amendment.** R-F4 is Minor 3 of the #659 DPIA review — a cross-reference gap between the Form
C binary-blob deploy gate (cited there as "TD-102", now #198) and the backup-residency gate
(#197/TD-107) (`docs/reviews/2026-07-10-659-dpia-security-auditor.md:103-112`). That
cross-reference is a separate, still-open editorial gap on a different document; closing it is
not this amendment's scope and must not be inferred from it.

### 8. Premise-conflict register

Two premises this work started from do not survive contact with the measured facts, and are
recorded rather than quietly worked around:

(i) **#198's acceptance criteria said "env/secret injection, never a file."** That is
contradicted by both B-1's own text and the 2026-08-05 measurement (§2): environment injection
**is** the `docker inspect`-after-exit leak, and a file on a RAM-backed tmpfs mount is RAM,
not disk. The AC text predates the measurement; B-1's requirement plus the measurement wins,
and "never a file" is not followed here.

(ii) **"Hetzner PROD"** — the host named in this ADR's 2026-06-06/2026-07-12 head-notes and in
the 2026-06-08 security dom cited throughout this amendment — **is a revoked premise.** The
production host is Netcup RS 1000 G12, Nuremberg (ADR 0122; ADR 0050 `Amendment 2026-08-04`).
Every host-specific fact measured in this amendment (TPM, apt, tmpfs size) is measured against
Netcup, not Hetzner.

### 9. Out of scope, named

- **The `v1:` sentinel ↛ `dek_version` mapping** — the naive-rotation trap
  `ResolveDek_WhenHigherDekVersionExists_FailsClosed` ("Scenario 10",
  `UserDataKeyStoreIntegrationTests.cs:280-324`) pins against — is **#501's axis, not this
  one.** The master-key re-wrap in §5 preserves `dek_version` throughout; it does not touch
  the sentinel-to-version mapping #501 owns.
- **Key-ACCESS detection (M-7's key-access half) is dispositioned to #1201, not delivered
  here.** `auditd` is absent from the box, measured 2026-08-09. Under this protection model
  every illegitimate read of the tmpfs secret file is, by construction, a root action — a
  strict subset of host root-activity detection, which ADR 0050:574 already assigns to
  #196/#1201. #198 cannot deliver a detection capability its own threat model resolves into
  someone else's scope. **What #198 does deliver is ABSENCE detection** —
  `jobbliggaren-secrets-present.timer` running `--check` at boot and every ten minutes,
  landing a missing key on `systemctl --failed`
  (`deploy/systemd/jobbliggaren-secrets-present.service:1-33`, `.timer:1-22`;
  `docs/runbooks/master-key-ops.md` §2-§3). Absence detection and access detection must not
  be read as one capability; they are not.

  > **Amendment 2026-08-13 (#1329) — the delivered capability is conditional in operation, and
  > the paragraph above states it unconditionally.** `--check` is one predicate over more than
  > the crypto directory: it demands #197's host-only `Backup__RcloneConfigBase64` too, so a
  > timer enabled before that credential exists fails on every fire — and **where
  > `jobbliggaren-heartbeat.timer` is armed and reaching its expecter**, that makes P1
  > (`systemctl --failed` is clean) continuously false, so it pages every run and carries no
  > information for as long as it stands. `master-key-ops.md` §2 therefore makes `enable --now`
  > conditional on the credential being injectable. **So the unit is shipped and the capability
  > is real, but on a box where enabling was deferred there is no absence detection in
  > operation.** Enabling restores the local surface; the outbound page additionally needs M-7's
  > alerting half, which is [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201)'s —
  > re-homed there by Klas 2026-08-06 when #196 closed without delivering it (ADR 0050's dated
  > note; pointing at the closing issue would have retired the obligation by accident). The window's outer bound is unowned and is Klas's to set or
  > to accept explicitly — it is named in §10 below rather than left in a PR body.

  > **Amendment 2026-08-13 (#1334) — the amendment above is SUPERSEDED IN ITS MECHANISM, and is
  > kept because it is what the box measured on the day it was written.** `--check` no longer
  > reads #197's `Backup__RcloneConfigBase64`: the predicate was split per set, and the host-only
  > half moved to `--check-host` behind its own
  > `jobbliggaren-host-secrets-present.{service,timer}`. So the conditionality the previous
  > amendment records is gone — `master-key-ops.md` §2 now enables
  > `jobbliggaren-secrets-present.timer` in the same visit as the install, and the crypto absence
  > detection §9 claims is unconditional in operation from the moment the crypto secrets exist.
  > **What is NOT restored by this is the outbound page**, which still needs M-7's alerting half
  > (#1201) exactly as the previous amendment says; and a deferral is still possible for the
  > host-only timer, where its cost is the nightly backup and not the box. §10's unmeasured
  > premise below is unaffected in substance: the reboot series still begins at `enable`, but that
  > moment no longer waits on #197.

### 10. Unmeasured premises carried forward

Eight premises behind this amendment were checked against the tree rather than assumed; six
held (the CTO source's §0 carries the full table). Two of the open ones matter enough to
restate here so a reader does not treat this amendment as resting on more certainty than it
has:

- **The frequency of unplanned reboots is unmeasured.** `last reboot` returned no readable
  history on this host as of 2026-08-09. `jobbliggaren-secrets-present.timer` (§9) is the
  instrument that starts converting this into an actual series — every firing after a
  secrets-loss event timestamps one in the journal. Until that series exists, the availability
  cost of the no-at-rest-copy model in §1 is bounded by nothing firmer than "reboots are
  manual today."
  **Amended 2026-08-13 (#1329): the series starts at `enable`, not at install, and §9's
  amendment makes that conditional on #197. A deferral extends this premise by exactly its own
  length. HOW LONG THE WINDOW MAY RUN IS UNOWNED — nothing sets a date for #197's ops half and
  nothing re-measures it. The gate that must close it is ADR 0050's mandatory second
  security-auditor review, which is required BEFORE the first beta data — so the window must be
  closed or explicitly accepted there, not "if it runs past first real data", which would key the
  condition to fire after the forum that grades it. Substantively this is already M-7's, which
  escalates to Blocker at first real data. Klas's call, not a session's.**
- **Whether any host snapshot was taken since 2026-08-05**, and **whether the hosting
  provider's snapshot facility captures guest RAM at all**, are both unmeasured. Either would
  mean a copy of the plaintext key already exists outside this box's disk entirely — part of
  why §5 rotates the current key rather than merely relocating it.
- **Compose semantics were measured on Compose 2.40.3; the box runs v5.4.0.** Bind-mount and
  `${...}`-interpolation behaviour — both load-bearing for §1 — should be re-measured against
  the running binary before the real cutover.
- **`ExecuteUpdate` translation of the strongly-typed `JobSeekerId`** inside the §5
  compare-and-swap predicate is unverified; this repository has a measured EF translation trap
  on this value-object family elsewhere, so it must be proven at build time in PR-2 rather
  than assumed to translate.
- **Crash-loop self-heal after injection** (`restart: unless-stopped` recovering api/worker
  with no `compose up`) is designed, not yet observed against the real box.

## Amendment 2026-08-09 (#197) — Beslut 2's claim held only by an unwritten premise, now written down

**Trigger:** #197 (nightly encrypted offsite backup + restore drill). **Source:**
`docs/reviews/2026-08-09-197-cto.md` (gitignored, so restated here) and **ADR 0125** (local-only,
the mechanism this amendment names).

Beslut 2 states, above: *"restore av en backup med sedan-raderad användare ger olesbar
ciphertext"* (this file, `:142-144`). `content-legal.json:139` publishes the same claim to users.
**Both were true only by an unwritten premise.** `user_data_keys` is an ordinary table with no
special handling in a `pg_dump`: a full dump carries every user's wrapped DEK next to the
ciphertext it unwraps, and the master key survives on the box (this file's 2026-06-06/2026-07-12
head-notes; ADR 0123). Restoring such a dump makes an erased user's field-encrypted columns
readable again — the opposite of what Beslut 2 claims.

The claim holds **only where the DEK table's contents are excluded from the artefact the
ciphertext travels in.** **ADR 0125** binds that as the nightly mechanism: a split `pg_dump` —
a main artefact with `user_data_keys` present but empty, and a separate DEK artefact of which
exactly one verified generation is retained — restored by pairing any main artefact within the
retention window against the *current* DEK artefact. That mechanism is this claim's guardian.

**This amendment is required even though the mechanism now makes the sentence true.** A ratified
claim whose truth condition is documented nowhere is one refactor away from being false again —
the next reader who adds a convenience single-command full-dump script would silently reintroduce
exactly the failure this amendment names, with no ADR text anywhere to stop them.

**No copy change follows from this.** `content-legal.json:139`'s claim is scoped to the four
field-encrypted columns — the paragraph immediately before it (`:138`) names them: *"ditt cv, dina
personliga brev, dina anteckningar och dina uppföljningar"*. Under the split dump the sentence
needed no edit. It would have needed one under a single full dump, which is why ADR 0125 binds the
split design rather than the simpler alternative (its Decision §2, Ground 2).

**Not touched by this amendment:** a restore from a main artefact taken *before* a user's deletion
*request* still resurrects that user as live, with the request lost. That is bounded to 30 days
and inherent to backups of any shape; it is disclosed in `docs/runbooks/backup-restore.md` and
ADR 0125, and its adjudication belongs to security-auditor, not to this file.

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
