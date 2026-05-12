# GDPR Processing Register

> Krävs per GDPR Art. 30. Förteckning över alla person-data-behandlingar
> JobbPilot utför, deras rättsliga grund, retention, och sub-processors.
>
> **Underhåll:** uppdatera vid varje ny PII-källa, ändrad retention, eller
> ny sub-processor. Cross-ref från ADRs som introducerar processing.
>
> **Senast granskad:** 2026-05-12 (P8b-leverans, sec-Maj-2)

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
- `JobAdsSyncedDomainEvent` audit-events: 18 månader per ADR 0024 audit-retention.

**Sub-processors:**

- **Arbetsförmedlingen JobTech** (datakälla): Sverige, GDPR-omfattad. Vi är data controller från och med persistering; JobTech är annons-publicerare.
- **AWS RDS PostgreSQL** (eu-north-1 Stockholm): Sub-processor för persistering.

**Right-to-erasure (Art. 17):**

Vid rekryterar-begäran:

1. Identifiera rekryterar via fri-text-sökning över `job_ads.description` (jsonb-query mot eventuellt fritext-indexerade fält).
2. Manuell granskning + sanering av specifika rader.
3. Audit-event `JobAdRedactedDomainEvent` skrivs.

Implementation: planerad som TD-73-stängningsdelpunkt (P8c eller separat batch).

**PII-stripping-trail:**

- Allowlist: `src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechPayloadSanitizer.cs`
- Unit-tester: `tests/JobbPilot.Application.UnitTests/JobAds/Infrastructure/JobTechPayloadSanitizerTests.cs`
- Pipeline: `PlatsbankenJobSource.TryConvertToImportItem` anropar sanitizer innan items lämnar Infrastructure.

**Cross-ref:** ADR 0032 §8-amendment, TD-73, BUILD.md §9.1.

---

## Tillkommande behandlingar (placeholder)

Lägg till nya behandlingar i kronologisk ordning här när nya PII-källor introduceras (BYOK-AI, Gmail-sync, Calendar-sync, etc. — kommande faser).
