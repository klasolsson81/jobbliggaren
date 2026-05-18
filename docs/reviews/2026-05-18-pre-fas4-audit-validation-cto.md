# CTO-validering — extern pre-FAS-4-helhetsaudit

**Datum:** 2026-05-18
**Roll:** senior-cto-advisor (decision-maker, §9.6)
**Uppdrag:** oberoende verifiering av extern helhetsaudit (lärarens CC-setup, mot
`22338ea`) + entydigt sekvenseringsbeslut för TD-13. Read-only. Ingen rubber-stamp.
**On-disk HEAD vid validering:** `ad4758c` (origin/main, ren)

---

## 0. HEAD-diskrepans (flaggas först)

Externa auditen kördes mot `22338ea`. On-disk HEAD är `ad4758c`. `22338ea` är
**ancestor** till HEAD, 9 commits bak. De 9 mellanliggande commitsen är
docs + web-UI-polish (RSC-slot-fix, design-reviewer Area 5, ADR 0046 FAS-3-stängning)
— **ingen rör backend-PII-config**. Auditens blocker-substans (DB-kolumner) är
därför oförändrad och giltig mot nuvarande HEAD. Diskrepansen är benign men noterad:
auditen är inte stale för det den blockerar på.

---

## 1. Blocker-verifiering — TD-13 (kod-exakt)

### 1.1 De 5 klartext-kolumnerna — VERIFIERAT SANT

Alla 5 kolumner lästa on-disk. Ingen krypterande `ValueConverter`. Varje config
bär ett explicit `TODO(GDPR)` som deferrar till Fas 2 / TD-13:

| Kolumn | Fil:rad | Status on-disk |
|--------|---------|----------------|
| `applications.cover_letter` | `ApplicationConfiguration.cs:31-32` | Klartext, `TODO(GDPR)` → Fas 2 |
| `application_notes.content` | `ApplicationNoteConfiguration.cs:18-19` | Klartext, `TODO(GDPR)` KMS-VC |
| `follow_ups.note` | `FollowUpConfiguration.cs:27-28` | Klartext, `TODO(GDPR)` KMS-VC |
| `resume_versions.content` | `ResumeVersionConfiguration.cs:52-59` | Klartext JSONB (JSON-VC ej krypto), `TODO(GDPR)` → TD-13 |
| `job_ads.raw_payload` | `JobAdConfiguration.cs:24-29` | Klartext JSONB, envelope skjuts till TD-13 |

Auditens påstående om exakt dessa 5 kolumner: **korrekt, verbatim mot kod.**

### 1.2 TD-13 existens/scope — VERIFIERAT, MED EN KORRIGERING

TD-13 finns i `docs/tech-debt.md:77-108` + översiktstabell rad 20. Innehåller
KMS-backed `ValueConverter<T,string>` envelope-encryption-spec, icke-destruktiv
lazy-migration, samt en explicit crypto-erasure-övervägning för Art. 17/backups.

**Korrigering av auditen:** TD-13 är klassad **Major / Fas 2**, *inte* "Fas 3.5".
"Fas 3.5-mini-batch" är auditens *omsekvenserings-förslag*, inte en egenskap hos
den existerande TD:n. Det ändrar inte besluts-substansen men formuleringen
"auditen påstår TD-13 är Fas 3.5" ska läsas som rekommendation, inte fynd.

### 1.3 BUILD.md §13.1-klassningen — DELVIS ÖVERSTÄLLD AV AUDITEN

- §13.1 (BUILD.md:1273): "CV-innehåll, cover letters … → **Kryptera at rest, logga
  aldrig**". RDS AES-256/KMS uppfyller "kryptera at rest" *bokstavligt*.
- §13.2 (BUILD.md:1282): app-side **envelope encryption med KMS (extra lager
  utöver RDS)** mandateras **explicit endast för OAuth-tokens och BYOK-nycklar** —
  *inte* för de 5 innehållskolumnerna.

Slutsats: spec-internt är app-fält-envelope för cover_letter/note/resume-content
**inte** en hård §13-mandatering idag — det är TD-13 själv som *föreslår* att
utöka envelope-lagret dit. Auditens framing "BUILD.md §13.1 kräver KMS-envelope
för dessa fält" är **överställd**. Den korrekta framingen: §13.1 klassar fälten
Känsligt; §13.2 ger RDS-at-rest men envelope-gapet är en medveten defense-in-depth-
förstärkning som TD-13 äger. Detta **nedgraderar inte** beslutet — men "policy-
avvik / GDPR Art. 32-brott *idag*" är för starkt. Det korrekta språket är
**defense-in-depth-gap inför en fas som vidgar PII-exponeringsytan**.

### 1.4 "RDS-only räcker ej" — TEKNISKT KORREKT, men inte ett akut-brott

ADR 0024 + §13.2 bekräftar: RDS KMS = disk-at-rest. Det skyddar inte mot
snapshot-share, automated-backup-export (7–35d default), eller IAM-komprometterad
DB-läsning — allt korrekt. ADR 0024:s Art. 17-story stänger live-data + app-logg
(30d) men **backups (7–35d) bär klartext-PII efter radering** tills de roterar.
Defense-in-depth-argumentet är tekniskt sunt. Det är dock ett **förstärknings-gap**,
inte ett produktionsstoppande compliance-brott i nuläget (RDS-at-rest + ADR 0024-
retention ger en försvarbar Art. 32-baslinje för Fas 1-volym med 0 prod-användare).

---

## 2. CTO-BESLUT — TD-13-sekvensering

### Beslut

**TD-13 implementeras som dedikerad FAS 3.5-batch FÖRE FAS-4-featurearbete.
Reklassas Fas 2 → "Fas 3.5 (pre-FAS-4-blocker)" i tech-debt.md. Sekventiellt,
ej parallellt med FAS 4.**

Detta **bekräftar auditens slutsats**, men på korrigerad grund: drivkraften är
inte "akut Art. 32-brott idag" utan **arkitektonisk divergens-risk + GDPR-yt-
expansion när FAS 4 öppnas**.

### Motivering mot principer

- **§9.6 fas-regel (lyft ej TD om den hör till nuvarande fas):** TD-13 var
  legitimt Fas 2-deferrad så länge ingen ny konsument tvingade fram krypto-
  infrastrukturen. FAS 4 *är* den tvingande konsumenten — BYOK-key-storage
  kräver exakt samma `ValueConverter<T,string>` + KMS-envelope (BUILD.md §8.4,
  §13.2). Att bygga FAS 4 BYOK-envelope *före* TD-13 skapar två divergerande
  envelope-implementationer → **DRY-brott på knowledge-nivå, ej kod-nivå**
  (Hunt/Thomas 1999; Fowler 2018 "Duplicated Code"). Det är inte en TD som ska
  skjutas vidare — det är arbete vars fas *just blev nu*.
- **GDPR Art. 32/17 (defense-in-depth):** FAS 4 AI-export skickar samma
  klartext-PII (cover_letter, resume content) till Bedrock. Att öppna det
  dataflödet ovanpå okrypterad at-app-vila vidgar exponeringsytan medvetet
  innan baslinjen är höjd. Sekvensering före = minimera fönstret (Microsoft
  Learn — Security; OWASP defense-in-depth).
- **Klas-doktrin kvalitet > tempo (CLAUDE.md §1, §9.6):** vid tvekan vinner
  in-scope-kvalitet. Mastercard-testet: en extern arkitekt som ser FAS 4 AI-PII-
  flöde byggas ovanpå 5 okrypterade Känsligt-kolumner blir *inte* imponerad.
- **memory `feedback_td_lifting_discipline`:** jag har pressat detta hårt mot
  fas-regeln. Skälet är **inte** "scope-disciplin/+Xh" (illegitimt). Skälet är
  **konkret funktion-dependency**: FAS 4 BYOK + AI-export kan inte byggas
  rent utan denna infrastruktur. Det passerar fas-regelns legitima kriterium.

### Avvisade alternativ

- **"Öppna FAS 4 nu + TD-13 parallellt":** avvisat. BYOK-envelope och TD-13-
  envelope skulle byggas samtidigt av olika spår → garanterad divergens
  (samma fynd som auditen). Parallellitet sparar inget eftersom FAS 4:s
  *första* säkerhetskänsliga PR (BYOK key-storage) ändå blockeras på samma
  KMS-VC. Att då bygga den ad-hoc i FAS-4-spåret är värre än en ren FAS 3.5.
- **"RDS-only, defer TD-13 till efter FAS 4":** avvisat. Skjuter envelope-
  infrastrukturen *bakom* den punkt som behöver den. Lämnar AI-PII-flödet
  byggt på okrypterad vila. Bryter §9.6 (en TD vars fas blivit nu) +
  Mastercard-test.
- **"Lyft som NY TD för FAS 4-scope":** avvisat. TD-13 finns redan, väl-
  speccad. Att splittra/duplicera vore TD-bloat (§9.7-anti-pattern).
  Åtgärd = reklassa befintlig TD-13:s Fas-fält, inte skapa nytt ID.

### Trade-offs accepterade

FAS 4-start fördröjs med TD-13-batchens varaktighet. Acceptabelt: kvalitet >
tempo är skriven doktrin, och fördröjningen är arbete som ändå måste göras
före FAS 4:s första BYOK/AI-PR — den är inte additiv, den är tidigarelagd.

### Scope-realism — ~1 v-estimatet är OPTIMISTISKT

Auditens "~1 v CC-tid" underskattar. Verifierad scope-yta:

1. KMS-backed `EncryptedConverter<T>` (DEK-strategi: per-rad vs per-aggregate —
   eget designval, ska ha ADR).
2. 5 kolumner, 3 typer (TEXT ×3, JSONB ×2). `resume_versions.content` har
   **redan** en JSON-`ValueConverter` + `ValueComparer` (`ResumeVersionConfiguration.cs:41-50`)
   — krypto-lagret måste komponeras *runt* den, inte ersätta den.
   `job_ads.raw_payload` har **STORED generated columns** (`ssyk_concept_id`,
   `region_concept_id`, `JobAdConfiguration.cs:74-80`) som läser
   `raw_payload->...` i Postgres — **krypterad raw_payload bryter dessa
   generated columns och tillhörande partial-index/sök**. Detta är **dold
   komplexitet auditen missar helt** och kan ensamt kosta mer än en dag
   (omdesign av JobTech-sök eller exkludera raw_payload från envelope-scopet).
3. Befintlig klartext-data-migrering (lazy encrypt-on-write + ev. back-fill-job).
4. Sök/filter-konsekvenser: krypterade kolumner är inte WHERE/LIKE-bara. Verifiera
   att ingen query filtrerar på cover_letter/note/content (sannolikt OK — men
   `raw_payload`-generated-columns är ett bekräftat problem, se 2).
5. Nyckelrotation + crypto-erasure-koppling till Art. 17 (TD-13:102-108).
6. Migrations + integrationstester (Testcontainers) per kolumn.

**CTO-estimat:** 1.5–2.5 v CC-tid realistiskt, med `raw_payload`/generated-column-
interaktionen som största enskilda osäkerhet. **Rekommendation till Klas:** låt
TD-13-batchen inledas med en discovery-PR (ADR-utkast: DEK-strategi +
raw_payload-generated-column-beslut) innan implementation — det är där den
dolda komplexiteten avgörs.

### Kräver Klas-GO (strategiskt, §9.2)

JA. Detta är ett fas-sekvenseringsbeslut (FAS 3.5 infogas före FAS 4) +
~2 v scope-commit + TD-13-reklassning. Det är en strategisk transition,
inte en in-block-fix. **CTO:s motivering är entydig mot principer, men
beslutet är av den klass (§9.6 p.5) som kräver explicit Klas-GO innan
implementation startar.** CC ska STOPPA och invänta Klas-GO.

---

## 3. Filnamn-kanon (PascalCase vs kebab)

### Verifierat — auditens detalj är INVERTERAD

CLAUDE.md §4.2: "Komponenter i `PascalCase.tsx`". On-disk:
- PascalCase (§4.2-compliant): `LoginForm.tsx`, `RegisterForm.tsx`,
  `WaitlistForm.tsx` — **minoriteten**.
- kebab (§4.2-icke-compliant): `me-profile-form.tsx`,
  `resume-content-form.tsx`, `add-note-form.tsx`, m.fl. — **majoriteten**.

Auditens "rename 3 forms-filer till kebab" innebär alltså att riva ut de
**enda §4.2-följsamma filerna** för att matcha en de-facto-konvention som
bryter spec. Det är ett legitimt val (konsekvens > spec-bokstav när spec
divergerat från verkligheten), men det är **per definition ett spec-edit**.

### Beslut

**Detta är ett Klas-spec-edit-beslut, inte ett CC- eller CTO-beslut.**
CLAUDE.md-edit kräver explicit Klas-instruktion (CLAUDE.md §9.2, §13) och
hård-blockas av approve-spec-edit-klassificeraren (memory
`feedback_spec_edit_approve_classifier_block`). CC får **inte** editera §4.2
och **inte** rename:a filer på eget bevåg.

**Rider det till efter FAS 4?** JA. Filnamn-kasus är ren hygien utan
funktion-dependency mot FAS 4 (AI-layer bryr sig inte om forms-filnamn).
Auditens "måste avgöras innan FAS 4" är **avvisat** — det finns ingen
teknisk koppling. Rekommendation: Klas tar §4.2-beslutet när det passar
(förslagsvis: blessa kebab i §4.2 + rename 3 filer i en isolerad
`refactor(web)`-touch), men det **blockerar inte FAS 4**.

---

## 4. Resten — triage mot fas-regeln

| Fynd | Verifierat | Klass | Beslut |
|------|-----------|-------|--------|
| `session-provider.tsx:26-35` useEffect-fetch | SANT — §5.2-brott ("useEffect för datahämtning") verbatim | Major | **In-block-fix, tidig FAS 4** (ej pre-FAS-4-blocker). Det är hydration-pattern, inte säkerhets/dataintegritets-blocker. Fixas i FAS 4:s första web-touch (server-component/SSR-prop). Lyfts EJ som TD (§9.6: rätt fas = nu→snart, ingen dependency saknas). |
| `TaxonomyReadModel.cs:58` `.Result` | SANT att raden finns — **MEN auditens §3.5-framing är FEL** | Ej fynd | **Avvisat.** `.Result` är guard:ad av `IsCompletedSuccessfully: true` (rad 57) — läser endast redan-komplett task, blockerar aldrig. Detta är den dokumenterade lås-fria cached-task-fast-path:en (kommentar rad 60-65), inte den deadlock-`.Result` §3.5 förbjuder. Att "fixa" raden vore en regression. **Disagreement med extern audit — flaggat.** Ingen åtgärd. |
| Filnamn-kanon | Se §3 | Spec | Klas-spec-edit, rider efter FAS 4 |
| JWT-rester `[Obsolete]` dead-code | Ej djup-verifierad (auditen: Minor) | Minor | Opportunistisk. Lyfts EJ som TD. Städas vid naturlig auth-touch. |
| IDOR-ownership pipeline-behavior saknas (17 `[Authorize]` ad-hoc) | Ej fil-verifierad denna rond | Major (om sant) | **Eskaleras till Klas som öppen fråga.** Om 17 handlers gör ad-hoc ownership-check utan centraliserad behavior är det en genuin säkerhets-arkitektur-fråga. Kräver egen security-auditor-rond + ev. ADR. **Inte** ett tyst CC-fix. Klas: begär riktad security-auditor-validering innan beslut. Lyfts EJ som TD förrän verifierad — men flaggas explicit som potentiell pre-FAS-4-Major. |
| Design-polish (text-tertiary/text-sm/px) | Auditen: Minor | Minor | Opportunistisk, ej blocker. design-reviewer-domän. |
| DRY `lib/actions/` | Auditen: Minor | Minor | Opportunistisk. Pressas mot §9.6: ingen fas-dependency → ingen TD. Fixas vid naturlig touch. |
| TD-26 (AI-kostnadstak) | Verifierat finns, Fas 4 | — | Korrekt FAS-4-intrinsiskt. EU-Bedrock-routing likaså = första AI-PR-gate, ej pre-FAS-4-blocker. Auditen korrekt här. |

**Inga nya TDs skapas.** Default = ej TD (§9.6). Inget fynd passerar fas-regelns
legitima kriterium utom TD-13 (som redan finns och bara reklassas).

---

## 5. Sammanfattning till Klas — vad som kräver ditt beslut

1. **TD-13 → FAS 3.5 före FAS 4 (sekventiellt).** CTO-beslut entydigt mot
   principer. Kräver **Klas-GO** (strategisk fas-sekvensering + ~1.5–2.5 v
   scope, §9.2/§9.6 p.5). CC STOPPAR tills GO. Rekommendation: starta med
   discovery/ADR-PR (DEK-strategi + `raw_payload`-generated-column-beslut).
2. **Filnamn-§4.2:** Klas-spec-edit-beslut, **rider efter FAS 4**, blockerar
   inte. CC får ej röra det självständigt.
3. **IDOR-ownership-behavior:** potentiell pre-FAS-4-Major — **Klas begär
   riktad security-auditor-validering** innan beslut. Ej CC-tyst-fix.
4. **`session-provider.tsx`:** in-block-fix i tidig FAS 4, ej blocker, ej TD.
5. **`TaxonomyReadModel.cs:58`:** CTO **avvisar** auditens fynd — falskt
   positivt (guard:ad fast-path). Ingen åtgärd.

### Avvikelser från externa auditen (explicit)

- **Håller med:** TD-13 som blocker + sekvensering FÖRE FAS 4 + BYOK-divergens-
  argument + EU-Bedrock = FAS-4-intrinsiskt.
- **Korrigerar:** TD-13 är Fas 2 (ej "Fas 3.5") i nuläget; framingen "BUILD.md
  §13.1 kräver KMS-envelope idag" är överställd (§13.2 mandaterar envelope
  endast för tokens/nycklar) — rätt framing är defense-in-depth-gap, ej
  akut Art. 32-brott; ~1 v-estimatet är optimistiskt (1.5–2.5 v, dold
  `raw_payload`/generated-column-komplexitet).
- **Underkänner:** `TaxonomyReadModel.cs:58` `.Result` som §3.5-fynd — det är
  en korrekt guard:ad lås-fri fast-path; "fix" vore regression.
- **Inverterar:** filnamn-detaljen — PascalCase är minoriteten/spec-compliant,
  ej tvärtom; och det blockerar **inte** FAS 4.

---

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) — §9.6 fas-disciplin, divergens-undvikande
- Hunt/Thomas, *The Pragmatic Programmer* (1999), kap. 7 "DRY" (knowledge-nivå)
- Martin Fowler, *Refactoring* 2nd ed (2018) — "Duplicated Code" (divergerande envelope)
- Microsoft Learn — Security / encryption-at-rest layering; OWASP defense-in-depth
- CLAUDE.md §1, §4.2, §5.2, §9.2, §9.6, §9.7, §13
- BUILD.md §13.1 (dataklassificering), §13.2 (encryption-layering), §8.4 (BYOK-KMS)
- ADR 0024 (Art. 17-cascade + backup/retention), ADR 0032 §8 (JobTech PII)
- Verifierad kod: `ApplicationConfiguration.cs:31-32`, `ApplicationNoteConfiguration.cs:18-19`,
  `FollowUpConfiguration.cs:27-28`, `ResumeVersionConfiguration.cs:41-59`,
  `JobAdConfiguration.cs:24-29,74-80`, `session-provider.tsx:26-35`,
  `TaxonomyReadModel.cs:54-69`, `docs/tech-debt.md:77-108`
