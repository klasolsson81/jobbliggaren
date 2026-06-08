# Security-audit — Platsbanken sök-paritet Fas B2 (employment_type / working_hours_type / scope_of_work)

**Status:** ✓ GO — inga blockers, inga major, inga minor
**Granskat:** 2026-06-08
**Agent:** security-auditor (agentId `abab6e7abef9c8608`)
**Auktoritet:** GDPR Art. 5, 6(1)(f), 32 + CLAUDE.md §5.4 + ADR 0032 §8 (sanitizer)
**Scope:** Areas 1 (PII), 4 (GDPR), 5 (third-country), 6 (logging)

## Verifierat

**1. PII-yta (Area 1):** De nya fälten är klassifikations-taxonomi, inte persondata. `employment_type` + `working_hours_type` deserialiseras till POCO:er med exakt samma shape som de redan godkända occupation-typerna (`concept_id`, `label`, `legacy_ams_taxonomy_id`) — taxonomi-pekare + statiska svenska labels ("Heltid", "Vanlig anställning"). `scope_of_work` (om den tagits) = `{min, max}` heltals-procent. Inget identifierar en fysisk person. Samma bedömning som B1:s ssyk/region/kommun-koder — icke-PII publik data.

**2. Sanitizer-allowlist (Area 1):** Inget vidgas. POCO-tillägget deserialiserar **endast** de specifika concept_id/label/min/max-fälten → `JsonSerializer.Serialize(hit)` producerar bara dessa keys, inte godtycklig payload. Sanitizern är default-deny (Saltzer/Schroeder) — extra nästlade fält droppas om de inte står på listan. employment_type/working_hours_type/scope_of_work/duration/min/max/label/concept_id står redan (rad 44, 54) → no-op-passering. PII-tunga kontaktfält (application_details.email, employer.contact_email, phone_number) saknas medvetet → fortsätter droppas. Tillägget kan inte oavsiktligt börja persistera ett PII-fält.

**3. Re-ingest 44k rader (Area 6):** Re-serialiseringen går genom samma SanitizeForStorage → raw_payload-väg. Nya värdena (concept_id/label/procent) är icke-PII → ingen klartext-PII-yta i loggar. Logg-anropen loggar externalId (annons-ID, icke-PII), inte payload-innehåll. Befintlig SECURITY-NOTE (PlatsbankenJobSource.cs:199-205) om fri-text-PII i description.text oförändrad, utanför scope (B2 rör inte fri-text).

**4. GDPR (Areas 4, 5):** Ny PII? Nej (ingen DPIA-trigger, ingen ny ADR). Lawful basis oförändrad (Art. 6(1)(f) legitimt intresse, publik annonsdata). Third-country transfer: ingen (JobTech = svensk myndighetsdata, EU; inget AI-anrop, ADR 0051 ej i scope). Retention: oförändrad (ärver job_ads-radens livscykel + snapshot-cron miss-tracking).

## Praise
- POCO-tillägget återanvänder etablerad concept-typ-shape — konsistent, divergens-tåligt.
- Default-deny-sanitizern gör tillägget säkert by construction.
- Kontaktfält-uteslutningen dokumenterad och håller (defense-in-depth bevarad).

## Sammanfattning
Säkerhets- och GDPR-mässigt mergeklar. Publik klassifikations-data, ingen PII-yta vidgas, sanitizer default-deny intakt. Ingen eskalering till Klas. CTO:s scope_of_work-beslut påverkar inte säkerhetsdomen ({min,max}-procent är icke-PII oavsett).
