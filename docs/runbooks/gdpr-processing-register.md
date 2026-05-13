# GDPR Processing Register

> Krävs per GDPR Art. 30. Förteckning över alla person-data-behandlingar
> JobbPilot utför, deras rättsliga grund, retention, och sub-processors.
>
> **Underhåll:** uppdatera vid varje ny PII-källa, ändrad retention, eller
> ny sub-processor. Cross-ref från ADRs som introducerar processing.
>
> **Senast granskad:** 2026-05-13 (TD-73 prod-gating-batch, ADR 0035 + ADR 0032 amendment 2026-05-13)

---

## Behandling: JobTech-import (Platsbanken)

**Källa:** Arbetsförmedlingen JobTech API (`jobsearch.api.jobtechdev.se` + `jobstream.api.jobtechdev.se`)
**Datakategori:** Publicerad annons-metadata + potentiellt rekryterar-kontakt-PII
**Datafält:**

- **Säkert publika (no PII risk):** SSYK-koder, region/municipality-koder, anställningsform, lönintervall, kompetenskrav (taxonomy-koder), publication_date, last_publication_date, organization_number.
- **Företagsnamn** (`employer.name`): Publikt. Inte PII för juridisk person; PII för enskild firma — behandlas som publik metadata eftersom JobTech redan publicerat.
- **Fri-text-beskrivning** (`description.text` → `job_ads.description`): Kan innehålla rekryterar-PII i löpande text ("Skicka CV till anna@acme.se"). Persisteras klartext.
- **Annons-URL** (`source_links[0].url` → `job_ads.url`): Publik länk till annonsen på arbetsformedlingen.se. `mailto:`-URL:er filtreras bort i `PlatsbankenJobSource.FirstNonMailtoUrl` (sec-Min-1).
- **Sanerad payload** (`job_ads.raw_payload`, jsonb): Strippad via `JobTechPayloadSanitizer` allowlist (ADR 0032 §8-amendment). Kontaktfält (`employer.contact_email/name`, `application_details.email/phone`) blockeras explicit av default-deny.

**Syfte:** Matchning av användarens preferenser mot tillgängliga jobb + visning av platsannonser i JobbPilot-katalog.

**Rättslig grund:** Art. 6(1)(f) berättigat intresse — annonsen är redan publicerad av Arbetsförmedlingen för allmän indexering; vi tillhandahåller mervärdes-sök/matchning.

**Retention:**

- `job_ads.raw_payload`: **30 dagar** efter `published_at` (ADR 0032 §8-amendment, konfigurerbar via `JobTechOptions.RawPayloadRetentionDays`). Null:as via `PurgeStaleRawPayloadsJob` (P8c-leverans).
- `job_ads.title/description/url/company/external_*`: Bevaras "indefinitively" som arbetsmarknad-historik. Annonsen `Archive`:as när JobTech rapporterar removal-event (status → Archived, ingen radering).
- `audit_log` system-events (`System.JobAdsSynced`, `System.RawPayloadPurged`, `Admin.RecruiterPiiRedacted`): 90 dagar per ADR 0024 audit-retention (partition-DROP). Payload jsonb-kolumn aktiverad för Fas 2 system-events per ADR 0035.

**Sub-processors:**

- **Arbetsförmedlingen JobTech** (datakälla): Sverige, GDPR-omfattad. Vi är data controller från och med persistering; JobTech är annons-publicerare.
- **AWS RDS PostgreSQL** (eu-north-1 Stockholm): Sub-processor för persistering.

**Right-to-erasure (Art. 17):**

Vid rekryterar-begäran (per ADR 0032 §8 amendment 2026-05-13 + ADR 0035):

- **Email-baserad** (primär): admin-endpoint `POST /api/v1/admin/job-ads/redact-recruiter-pii` med body `{ identifier, type: "Email" }`. Söker `raw_payload` via `EF.Functions.JsonContains` mot probe `{"employer":{"contact_email":"<email>"}}` och null:ar matchande rows via `ExecuteUpdateAsync` (total null-out — CTO Q2). En aggregerad audit-rad per request (`Admin.RecruiterPiiRedacted`) per ADR 0024 D4-precedens. Detaljerad procedur: [`recruiter-pii-erasure.md`](./recruiter-pii-erasure.md).
- **Name-baserad** (defererad till TD-75): manuell procedur via DB-admin tills auto-flödet levereras. Trigger för TD-75-fix: första faktiska name-baserade begäran.
- **Audit-trail:** Email-flödet via `IAuditableCommand` → aggregerad audit-rad. System-events (sync-runs + purge-runs) via `ISystemEventAuditor` per ADR 0035.

Implementation: **levererad i TD-73 prod-gating-batch 2026-05-13** (commit-cykel kring tag `v0.2.4-dev`).

**PII-stripping-trail:**

- Allowlist: `src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechPayloadSanitizer.cs`
- Unit-tester: `tests/JobbPilot.Application.UnitTests/JobAds/Infrastructure/JobTechPayloadSanitizerTests.cs`
- Pipeline: `PlatsbankenJobSource.TryConvertToImportItem` anropar sanitizer innan items lämnar Infrastructure.

**Cross-ref:** ADR 0032 §8-amendment, TD-73, BUILD.md §9.1.

---

## Tillkommande behandlingar (placeholder)

Lägg till nya behandlingar i kronologisk ordning här när nya PII-källor introduceras (BYOK-AI, Gmail-sync, Calendar-sync, etc. — kommande faser).
