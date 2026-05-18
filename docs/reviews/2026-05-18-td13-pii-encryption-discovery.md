# Discovery — TD-13 PII-fält-kryptering (KMS-envelope), FAS 3.5 STOPP D

**Datum:** 2026-05-18
**Roll:** Claude Code (discovery, read-only — ingen produktkod)
**On-disk HEAD:** `8474c06` (origin/main, ren)
**Uppdrag:** kartlägg de 5 PII-kolumnernas EF-config, `raw_payload`-komplexiteten,
befintligt KMS/Secrets-mönster, sök/filter-konsekvenser. Underlag för
senior-cto-advisor multi-approach-beslut + ADR 0049-utkast.

> §9.4: rå verbatim-källor i kodblock, inga trunkeringar. Filer lästa
> on-disk denna session.

---

## 1. De 5 PII-kolumnerna — EF-config verbatim

### 1.1 `applications.cover_letter` — TEXT, klartext

`ApplicationConfiguration.cs:31-32`:

```csharp
// TODO(GDPR): CoverLetter är känsligt innehåll (BUILD.md §13.1) — kryptera kolumnen i Fas 2
builder.Property(a => a.CoverLetter).HasMaxLength(10_000);
```

Ingen converter. Ren `string?`-property → TEXT-kolumn. **Ingen** WHERE/LIKE-query
filtrerar mot `CoverLetter` (grep-verifierat: enbart write i
`CreateApplicationCommandHandler`, läs för display i `ApplicationDetailDto`).
→ Kan krypteras rakt av med en `ValueConverter<string,string>`.

### 1.2 `application_notes.content` — TEXT, klartext

`ApplicationNoteConfiguration.cs:18-19`:

```csharp
// TODO(GDPR): kryptera med KMS-backed value converter innan prod-release
builder.Property(n => n.Content).HasMaxLength(5000).IsRequired();
```

Ingen converter. Ingen WHERE/LIKE. → Kan krypteras rakt av.

### 1.3 `follow_ups.note` — TEXT, klartext

`FollowUpConfiguration.cs:27-28`:

```csharp
// TODO(GDPR): kryptera med KMS-backed value converter innan prod-release
builder.Property(f => f.Note).HasMaxLength(2000);
```

`string?` (nullable). Ingen converter. Ingen WHERE/LIKE. → Kan krypteras rakt av;
converter måste hantera `null` (ingen Note) ≠ krypterad tom sträng.

### 1.4 `resume_versions.content` — JSONB, klartext, **redan converter+comparer**

`ResumeVersionConfiguration.cs:41-59` (verbatim):

```csharp
var contentConverter = new ValueConverter<ResumeContent, string>(
    content => JsonSerializer.Serialize(content, ContentJsonOptions),
    json => JsonSerializer.Deserialize<ResumeContent>(json, ContentJsonOptions)!);

var contentComparer = new ValueComparer<ResumeContent>(
    (left, right) => JsonSerializer.Serialize(left, ContentJsonOptions)
        == JsonSerializer.Serialize(right, ContentJsonOptions),
    content => JsonSerializer.Serialize(content, ContentJsonOptions).GetHashCode(StringComparison.Ordinal),
    content => JsonSerializer.Deserialize<ResumeContent>(
        JsonSerializer.Serialize(content, ContentJsonOptions), ContentJsonOptions)!);

// TODO(GDPR): Content innehåller känsligt CV-innehåll (BUILD.md §13.1: PersonalInfo,
// Experiences, Educations, Skills) — kryptera kolumnen med KMS-backed value converter
// i Fas 2. Idag lagras klartext-JSONB; samma status som applications.cover_letter,
// application_notes.content, follow_ups.note. Spårning: TD-13.
builder.Property(rv => rv.Content)
    .HasConversion(contentConverter, contentComparer)
    .HasColumnType("jsonb")
    .IsRequired();
```

**Komplexitet:** krypto-lagret måste komponeras *runt* den befintliga
JSON-converter+comparer:n, inte ersätta den. Pipeline blir
`ResumeContent → JSON-sträng → ciphertext-sträng → kolumn`. **Ciphertext är
inte giltig JSONB** → kolumntyp måste skifta `jsonb → text`. Ingen WHERE/LIKE
mot `Content` (grep: läs i `ResumeMappingExtensions`, write i
`UpdateMasterContentCommandHandler`). Comparer:n måste fortsatt operera på
klartext-JSON (annars trasas change-tracking).

### 1.5 `job_ads.raw_payload` — JSONB, klartext — **LOAD-BEARING, se §2**

`JobAdConfiguration.cs:24-29`:

```csharp
// ADR 0032 §4 — raw_payload som jsonb för debug/replay-artefakter.
// PII-yta: JobTech-payload kan innehålla rekryterar-PII (namn, email,
// telefon, firmatecknare). Encryption-at-rest täcks av AWS RDS KMS;
// envelope encryption (app-side) skjuts till TD-13 (Fas 2 Major).
// PII-stripping vid ingest dokumenterad i ADR 0032 §8-amendment 2026-05-12.
builder.Property(j => j.RawPayload).HasColumnType("jsonb");
```

`string?` (nullable) mappad till jsonb. **Tre separata Postgres-side-beroenden
av att kolumnen är giltig JSONB — se §2.**

---

## 2. `raw_payload` — load-bearing-ytan (KRITISK, CTO-flaggad)

`raw_payload`-kryptering bryter **tre** oberoende Postgres-side-mekanismer
eftersom ciphertext inte är querybar/JSON-parsbar:

### 2.1 STORED generated columns

`JobAdConfiguration.cs:74-80` (verbatim):

```csharp
builder.Property<string?>("SsykConceptId")
    .HasColumnName("ssyk_concept_id")
    .HasComputedColumnSql("raw_payload->'occupation'->>'concept_id'", stored: true);

builder.Property<string?>("RegionConceptId")
    .HasColumnName("region_concept_id")
    .HasComputedColumnSql("raw_payload->'workplace_address'->>'region_concept_id'", stored: true);
```

Migration `20260513111555_F2P9JobAdSearchColumns.cs` skapar dem + två partial
B-tree-index (`ix_job_ads_ssyk_concept_id`, `ix_job_ads_region_concept_id`,
`WHERE … IS NOT NULL`). **Postgres beräknar `raw_payload->...` vid write.**
Krypterad `raw_payload` (text-ciphertext, ej jsonb) → `->`-operatorn
misslyckas → migration/insert kraschar.

### 2.2 Taxonomi-sök (`JobAdSearch.cs:39-49`) — SPOT, ADR 0039

```csharp
if (ssyk.Count > 0)
{
    var ssykValues = ssyk;
    source = source.Where(j => ssykValues.Contains(EF.Property<string?>(j, "SsykConceptId")));
}

if (region.Count > 0)
{
    var regionValues = region;
    source = source.Where(j => regionValues.Contains(EF.Property<string?>(j, "RegionConceptId")));
}
```

Delas av `ListJobAdsQueryHandler` + `RunSavedSearchQueryHandler` (ADR 0039
Beslut 1 SPOT). Beror transitivt på 2.1. q-filtret (rad 61-63) filtrerar
mot `j.Title`/`j.Description` — **inte** mot raw_payload eller någon av de
5 PII-kolumnerna.

### 2.3 GDPR Art. 17-redaction — `RecruiterPiiPurger.cs:38-41`

```csharp
.Where(j => j.RawPayload != null
            && EF.Functions.JsonContains(j.RawPayload, probe))
    .ExecuteUpdateAsync(
        s => s.SetProperty(j => j.RawPayload, _ => (string?)null),
```

`EF.Functions.JsonContains` = Postgres `@>` JSONB-containment **direkt mot
raw_payload**. Detta är Art. 17-enforcement för rekryterar-PII
(`RedactRecruiterPiiCommand`, admin-scopad). Krypterad raw_payload → `@>`
kan inte matcha → **Art. 17-radering bryts**.

### 2.4 Vad som *fungerar* på ciphertext

- `PurgeStaleRawPayloadsJob.cs:57-59`: `.Where(j => j.RawPayload != null …)` +
  `SetProperty(j => j.RawPayload, _ => null)` — null-check + null-out
  oberoende av innehåll. **OK på ciphertext.**
- `RedactRecruiterPiiCommandHandler`: total null-out. **OK.**
- Ingest-pipeline: `JobTechPayloadSanitizer` (allowlist) strippar redan
  rekryterar-PII innan persistering (ADR 0032 §8-amendment); `raw_payload`
  purgeas dessutom efter `RawPayloadRetentionDays` (default 30).

**Slutsats raw_payload:** kryptering rakt-av är inte möjlig utan att bryta
2.1+2.2+2.3. Lösning kräver antingen (a) exkludering ur envelope-scope med
motivering (sanitizer+30d-purge+null-out-Art.17 = befintlig kontroll), eller
(b) schema-omstrukturering (extrahera ssyk/region till klartext-icke-PII-
kolumner FÖRE kryptering + ersätta `JsonContains`-Art.17-mekanismen). **(b)
rör sök/taxonomi-funktion → Klas-STOPP-flagga per uppdraget.** CC ger ingen
egen rekommendation (§9.6) — senior-cto-advisor avgör.

---

## 3. Befintligt KMS/Secrets-mönster

### 3.1 Inget KMS-bruk existerar

Grep `kms|GenerateDataKey|AWSSDK` över hela `src/`: enbart TODO-kommentarer +
`AWSSDK.SimpleEmailV2` (Infrastructure) + `AWSSDK.SecretsManager` (Migrate).
**Inget `AWSSDK.KMS`-paket, ingen envelope-impl.** `Directory.Packages.props`:

```
AWSSDK.SecretsManager  4.0.4.20
AWSSDK.SimpleEmailV2   4.0.5.8
AWSSDK.Core            4.0.6.1
```

→ `AWSSDK.KMS` måste läggas till i `Directory.Packages.props` + Infrastructure
(`feedback_di_with_handlers_same_commit` — converter+config+DI+pkg samma commit).

### 3.2 Secrets Manager-mönstret (precedens för KMS-klient-init)

`Migrate/Program.cs` (Phase D/E): klient-init + ARN-via-env-var-mönster:

```csharp
var awsRegion = RequiredEnv("AWS_REGION");
var secretsClient = new AmazonSecretsManagerClient(
    Amazon.RegionEndpoint.GetBySystemName(awsRegion));
// ARN:er via RequiredEnv("MIGRATE_APP_CONN_SECRET_ARN") etc.
```

`RequiredEnv` kastar `InvalidOperationException` vid saknad env-var (fail-fast).
Region: `AWS_REGION` env-var (eu-north-1 i dev/prod per infra). KMS-CMK-ARN
bör följa samma `IOptions`/env-var-mönster. Fail-closed-precedens finns i
`FetchMasterCredsAsync` (kastar vid tom/ogiltig secret).

---

## 4. Sök/filter-konsekvenser — sammanfattning

| Kolumn | WHERE/LIKE idag? | Krypterbar rakt av? |
|--------|------------------|----------------------|
| `applications.cover_letter` | Nej | Ja (`VC<string,string>`, nullable) |
| `application_notes.content` | Nej | Ja |
| `follow_ups.note` | Nej | Ja (nullable-hantering) |
| `resume_versions.content` | Nej | Ja, men komponera runt befintlig JSON-VC+comparer; jsonb→text |
| `job_ads.raw_payload` | **Ja, indirekt ×3** (§2) | **Nej — CTO-beslut krävs** |

Inga av de 4 första kolumnerna queryas med WHERE/LIKE → kryptering bryter
ingen sökväg. `resume_versions.content`/`raw_payload` skiftar `jsonb→text`
(ciphertext ej giltig JSONB) → migration måste hantera kolumntyp-skifte.

---

## 5. Web-search-grund (§9.5, 2026-05-18)

- **AWS KMS envelope (.NET):** `GenerateDataKey` → plaintext-DEK krypterar
  fält, `CiphertextBlob` (wrapped DEK) lagras med data; plaintext-DEK
  zeroas ur minne direkt; `Decrypt` unwrappar vid läsning. Encryption
  context (CMK-bundet AAD) rekommenderas starkt.
  Källa: <https://docs.aws.amazon.com/kms/latest/developerguide/kms-cryptography.html>,
  <https://docs.aws.amazon.com/kms/latest/APIReference/API_GenerateDataKey.html>
- **EF/Npgsql VC↔generated column:** EF kan inte translatera converter-logik
  till SQL; queries mot konverterade värden kan inte nyttja index. STORED
  generated column beräknas Postgres-side vid write — kräver giltig JSONB-
  källkolumn. Bekräftar §2.1-analysen.
  Källa: <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions>,
  <https://www.npgsql.org/efcore/modeling/generated-properties.html>
- **GDPR Art. 17 / EDPB CEF 2025 (rapport 2026-02):** controllers som
  exkluderade backup-data ur erasure *utan motivering* var ett fynd; "raderas
  när backup överskrivs" accepteras *med* dokumenterad motivering. Crypto-
  erasure adresseras i EDPB blockchain-guidelines men "teknisk omöjlighet
  kan inte åberopas för non-compliance" — crypto-erasure är ett *medel*,
  inte en *ursäkt*. Underlag för ADR 0049 crypto-erasure-ställningstagande.
  Källa: <https://www.edpb.europa.eu/system/files/2026-02/edpb_cef-report_2025_right-to-erasure_en.pdf>,
  <https://www.edpb.europa.eu/system/files/2025-04/edpb_guidelines_202502_blockchain_en.pdf>

---

## 6. Öppna beslutspunkter för senior-cto-advisor (CC ger ej egen rek, §9.6)

1. **DEK-granularitet:** per-rad vs per-aggregat vs per-användare.
   Per-användare kopplar direkt till crypto-erasure (kasta user-DEK vid
   kontoradering → backup-PII olesbar).
2. **Crypto-erasure för Art. 17/backups:** ja/nej. ADR ska ta ställning
   per TD-13-spec. EDPB CEF 2025: backup-overwrite OK med motivering;
   crypto-erasure ger omedelbar täckning men komplicerar restore.
3. **raw_payload-lösning (störst osäkerhet, Klas-STOPP om (b)):**
   (a) exkludera ur envelope-scope — motivera via sanitizer-allowlist +
   30d-purge + Art.17-null-out (befintlig defense), eller
   (b) extrahera ssyk/region → klartext-icke-PII-kolumner + ersätta
   `JsonContains`-Art.17-mekanism, sedan kryptera raw_payload.
4. **Migrerings-strategi:** lazy encrypt-on-write vs backfill-job (TD-13-spec
   nämner båda; ADR ska besluta).
5. **`jsonb→text`-kolumnskifte** för resume_versions.content + (ev.)
   raw_payload — migration-mekanik (icke-destruktiv, idempotent, down).

---

*Discovery klar. Ingen produktkod skriven. Nästa: senior-cto-advisor-beslut
→ ADR 0049-utkast (Proposed) → Klas-STOPP.*
