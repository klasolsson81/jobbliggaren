# Recruiter PII erasure — operativ procedur

> Operativ runbook för GDPR Art. 17 right-to-erasure av rekryterar-PII i
> `job_ads.raw_payload`. Per ADR 0032 §8 amendment 2026-05-13 + ADR 0035.

**Senast uppdaterad:** 2026-05-13 (TD-73 prod-gating-batch)

---

## Bakgrund

JobbPilot importerar platsannonser från Arbetsförmedlingens JobTech-API.
`raw_payload` (jsonb på `job_ads`) lagrar sanerad JobTech-payload för
debug/replay. Sanitizer (per ADR 0032 §8 amendment 2026-05-12) strippar
kända PII-paths vid ingest, men fri-text-rester (`description.text`) kan
fortfarande innehålla rekryterar-namn eller e-post.

`PurgeStaleRawPayloadsJob` (cron `30 4 * * *` UTC) null:ar
`raw_payload` efter 30 dagar. Right-to-erasure-flödet adresserar fönstret
mellan publication och purge.

---

## Email-baserad radering (auto-flöde)

Primär identifier är e-post. Auto-flöde levererat i TD-73 prod-gating-batch.

### Steg

1. **Begäran inkommer** (via support, DPO, eller direkt e-post till klas@jobbpilot.se).
2. **Verifiera identitet** — rekryteraren ska bevisa kontroll över e-postadressen
   (e-post-verifikation från samma adress, eller skriftlig begäran).
3. **Anropa admin-endpoint:**

   ```bash
   curl -X POST https://dev.jobbpilot.se/api/v1/admin/job-ads/redact-recruiter-pii \
     -H "Authorization: Bearer <ADMIN_SESSION_ID>" \
     -H "Content-Type: application/json" \
     -d '{
       "identifier": "alice@example.com",
       "type": "Email"
     }'
   ```

4. **Tolka respons:**

   ```json
   {
     "requestId": "<guid>",
     "rowsAffected": <int>
   }
   ```

   `rowsAffected = 0` är OK — kan betyda att payload redan är null:ad
   (purge-jobbet har hunnit före) eller att email-strängen aldrig
   förekommit i sanerad payload.

5. **Bekräfta audit-rad** — `requestId` matchar `audit_log.aggregate_id`:

   ```sql
   SELECT event_type, aggregate_type, occurred_at, payload
   FROM audit_log
   WHERE aggregate_id = '<requestId>';
   ```

   Förväntad rad: `event_type = 'Admin.RecruiterPiiRedacted'`,
   `aggregate_type = 'System.RecruiterPiiRedaction'`.

6. **Återkoppla rekryterar** — bekräfta att radering är genomförd.
   Inkludera `requestId` + `rowsAffected` i återkopplingen för spårbarhet.

### Mekanik (för referens)

- Söker `raw_payload` via `EF.Functions.JsonContains` med probe
  `{"employer":{"contact_email":"<email>"}}`.
- Email normaliseras till lower-case (RFC 5321 case-insensitive practice).
- Total null-out av matchande rows via `ExecuteUpdateAsync` (CTO Q2-decision —
  KISS + Saltzer/Schroeder default-deny + GDPR Art. 5(1)(c) data-minimisation).
- IgnoreQueryFilters — soft-deletade rader inkluderas i scope:n.
- En aggregerad audit-rad per request (CTO Q3-decision, ADR 0024 D4-precedens).

---

## Name-baserad radering (manuell procedur)

Name-baserad erasure är defererad till **TD-75** (auto-flöde levereras vid
första faktiska request). Tills dess: manuell procedur via DB-admin.

### När använda

- Rekryteraren kan inte tillhandahålla e-post (sällsynt — JobTech-payloads
  innehåller normalt minst en kontakt-email).
- Email-baserad körning gav `rowsAffected = 0` trots att rekryterar-PII finns
  i annonser (verifierat via manuell granskning av `description.text`).

### Steg

1. **Verifiera identitet** (som för Email-flödet).
2. **Manuell sökning** mot dev/prod Postgres via psql:

   ```sql
   -- Sök efter rekryterar-namn i raw_payload jsonb (fritext-region)
   SELECT id, external_source, "external_id", published_at
   FROM job_ads
   WHERE raw_payload::text ILIKE '%Alice Anka%'
     OR description ILIKE '%Alice Anka%';
   ```

3. **Manuell granskning** — verifiera att hittade rader faktiskt är samma
   rekryterare (false positives möjliga vid vanliga namn).
4. **Manuell sanering:**

   ```sql
   -- Null:a matchande raw_payload-rader
   UPDATE job_ads
   SET raw_payload = NULL
   WHERE id IN ('<id1>', '<id2>', ...);
   ```

5. **Skriv audit-rad manuellt** (eftersom auto-endpoint inte täcker Name):

   ```sql
   INSERT INTO audit_log (
     id, occurred_at, correlation_id, event_type, aggregate_type,
     aggregate_id, payload
   )
   VALUES (
     gen_random_uuid(), now(), gen_random_uuid(),
     'Admin.RecruiterPiiRedactedManual',
     'System.RecruiterPiiRedaction',
     gen_random_uuid(),
     jsonb_build_object(
       'identifier_hash', md5('Alice Anka'),
       'type', 'Name',
       'rowsAffected', 2,
       'operator', 'klas@jobbpilot.se',
       'reason', 'GDPR Art. 17 request 2026-05-XX'
     )
   );
   ```

6. **Återkoppla rekryterar** med audit-rad-ID som spårbarhet.

### TD-75-trigger för auto-flöde

Vid första faktiska Name-baserade begäran: lyft från manuell till
auto-flöde (TD-75 i `docs/tech-debt.md`). Auto-flöde kräver:

- Multi-path jsonb-sökning (`employer.contact_name`, `recruiter.name`,
  ev. fler keys upptäckta vid första faktiska request)
- Eventuell full-text-search på `description.text` (Postgres `tsvector` +
  GIN-index)
- Återanvänd `RedactRecruiterPiiCommand` med utvidgad `Type = Name`-branch

---

## Cross-ref

- ADR 0032 §8 amendment 2026-05-13 (auto-flöde Email + TD-75 Name)
- ADR 0035 (system-event audit-pipeline för sync + purge)
- ADR 0024 D4 (aggregerad audit-rad per handling — precedens)
- `docs/runbooks/gdpr-processing-register.md` (Art. 30 register)
- `src/JobbPilot.Application/JobAds/Commands/RedactRecruiterPii/` (auto-impl)
- `src/JobbPilot.Infrastructure/JobSources/RecruiterPiiPurger.cs` (jsonb-search-impl)
