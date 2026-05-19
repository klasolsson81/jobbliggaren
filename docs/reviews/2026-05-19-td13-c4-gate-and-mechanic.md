# TD-13 C4 — gate-resultat + låst #1c-mekanik (nästa-session-spec)

**Datum:** 2026-05-19
**On-disk:** `ceab97e` + uncommittad C4.0/C4.1 (denna commit)
**Architect:** agentId a946e2a66200dc5b8 (C4.2-mekanik), af92815b78c2fb817 (C4-design)

## C4.0-gate — KÖRD, utfall RÖD bekräftat
Empiriskt (Testcontainers/Npgsql, probe-interceptor): `ValueConverter
.ConvertFromProvider` kör FÖRE `IMaterializationInterceptor.InitializedInstance`
→ interceptorn ser `ResumeVersion.Content` som redan-deserialiserat
`ResumeContent`-objekt, ej JSON-string. Microsoft Learn-normativt verifierat
(architect a946e2a66200dc5b8). Probe konverterad till permanent invariant-
regressionsvakt `ResumeContentMaterializationProbeTests` (1 [Fact], grön 1/1).

## C4.1 — additiv migration KLAR
`20260519060041_AddResumeVersionContentEnc` — `ADD COLUMN content_enc text NULL`
på `resume_versions`. Icke-destruktiv, metadata-only, Down=DropColumn (säker
pre-backfill). Snapshot medvetet OFÖRÄNDRAD (EF-modellen saknar property i C4.1
— C4.2:s dual-property-mappning diffas mot denna snapshot, ingen manuell synk).

## C4.2 — LÅST #1c-mekanik (Microsoft Learn-verifierad, CC implementerar rakt av)

**Block-korrektion:** RÖD-pre-spec ("VC tas ur persistens, ValueComparer via
SetValueComparer") = ogiltig EF Core 10 (custom CLR→text saknar ProviderClrType
utan ValueConverter; ValueComparer ger ingen store-typ; VC kan ej referera
DbContext #12205). Korrekt = #1c. ADR 0049 Mekanik-not 6 dokumenterar.

**#1c-konstruktion:**
- `ResumeVersionConfiguration`: `builder.Ignore(rv => rv.Content)` (EF
  persisterar EJ Content) + `builder.Property<string>("ContentEnc")
  .HasColumnName("content_enc").IsRequired()` (shadow-string). Behåll legacy
  `content jsonb` mappad som shadow tills cutover (backfill-fönster).
- `EncryptedFieldRegistry` Form B: andra map `Dictionary<Type,
  JsonSerializedVoField[]>`; `JsonSerializedVoField(string DomainProperty,
  string ShadowProperty, Func<object,string> ToJson, Func<string,object>
  FromJson)` — delegater kapslar delad `ContentJsonOptions`. C3 TEXT-kolumner
  = Form A oförändrad.
- **Write** (`FieldEncryptionSaveChangesInterceptor` Form B-gren):
  `json = JsonSerializer.Serialize(rv.Content, ContentJsonOptions)` →
  `cipher = fieldEncryptor.Encrypt(json, dek)` →
  `entry.Property("ContentEnc").CurrentValue = cipher`. Ägar-resolution:
  ResumeVersion skugg-FK `"ResumeId"` → spårad `Resume` i ChangeTracker →
  `Resume.JobSeekerId` (mönster = C3 ApplicationNote/FollowUp→Application).
- **Read** (`FieldDecryptionMaterializationInterceptor` Form B-gren): läs
  shadow `ContentEnc`; om sentinel+owner → decrypt → `FromJson` → sätt
  `rv.Content` via private-setter-reflection (befintlig PropertyCache).
  Owner: ResumeVersion saknar JobSeekerId → `ICurrentDataOwner` (C3-mönster,
  scope-diff fail-closed Mekanik-not 5b ärvs). Backfill-fallback: om
  `ContentEnc` null/ej-sentinel → legacy `content` (klartext-JSON, ingen
  decrypt) → `FromJson`.
- ValueComparer-frågan UPPHÖR (Content ej EF-tracked).
- Snapshot: C4.2 `dotnet ef migrations add` diffar modell↔C4.1-snapshot,
  emitterar shadow+mapping-delta. INGEN `content jsonb`-drop i C4.2.

**3 GO-villkor (architect):**
1. **C4.2a mini-gate** (paritet C4.0): empiriskt verifiera shadow-property-
   läsning i `InitializedInstance` under `AsNoTracking` (`context.Entry` finns
   ej vid AsNoTracking → verifiera `MaterializationInterceptionData`-value-
   accessor; testa båda tracking-lägen). NO-GO Form B-read tills grön.
2. C4.2 mappar BÅDE legacy `content` + `content_enc` som shadows under
   backfill; read prefererar content_enc(sentinel) annars legacy. Ingen
   content-drop (Beslut 5 steg 3–4 = separat cutover/drop, Klas-STOPP).
3. STOPP V-flagg: Mekanik-not 6 (#1c dual-property-shadow) — Klas kan
   override:a → formell amendment.

**Major (nästa session):** (M1) C4.2a shadow-read-AsNoTracking-gate;
(M2) dual-shadow legacy+content_enc under backfill-fönster.
**Minor:** registry bimodal → arch-test (Content `Ignore`:ad om JsonVoField);
ContentJsonOptions → delad intern statisk (SPOT); private-setter-reflection
på ResumeContent (paritet C3, OK).

## Nästa session
C4.2a mini-gate → C4.2 impl (#1c, ovan) → C4.3 markörer (CreateResume/
UpdateMasterContent/GetResumeById; verifiera DeleteResume*) + arch-test →
C4.4 testsvit → gates → commit. Sedan C5/C6/STOPP I/V.
