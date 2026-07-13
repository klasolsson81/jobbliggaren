# Recruiter PII erasure (GDPR Art. 17) — operational procedure

> What a human does on the day a recruiter asks us to erase their contact
> details from an ad we imported from Platsbanken.
> Contract: **ADR 0106** (two-tier). Issue: **#842**.

**Last updated:** 2026-07-13 (#842 PR1 — containment + truth)
**Applies to:** the state of the system **today** (post-PR1, pre-PR2/PR3).

---

## 0. STATUS — read this before anything else

**There is no working automated erasure path. There never was.**

- `POST /api/v1/admin/job-ads/redact-recruiter-pii` now returns **501 Not
  Implemented** with a truthful problem detail
  (`src/Jobbliggaren.Api/Endpoints/AdminJobAdsEndpoints.cs:70-79`). The route is
  kept, not deleted, so that ADR 0024's Art. 17 cascade registry does not
  silently dangle and so an operator holding an older copy of this runbook is
  told the truth instead of being served a lie.
- `RecruiterPiiPurger`, `IRecruiterPiiPurger` and the `RedactRecruiterPii`
  command/handler are **deleted**. Dead code that impersonates a safety control
  is worse than no code.
- **The previous version of this runbook was wrong in a way that mattered.** It
  told the operator that `rowsAffected = 0` "är OK" and then told the operator to
  *"Återkoppla rekryterar — bekräfta att radering är genomförd."* **That
  confirmation has never been true. Not once.** Anyone who followed the old
  procedure and told a recruiter "raderat" made a false statement to a data
  subject (Art. 12(3)) on top of a failure to erase (Art. 17).
- **Measured, dev Postgres, 2026-07-13:** the endpoint has been called **0
  times** (`audit_log` rows matching `%RecruiterPiiRedact%` = **0**). As far as
  the audit log shows, **no data subject has yet received a false confirmation**,
  and no notification duty is live. That is why the correction is cheap. It is
  not a reason to have waited.

### Why the old mechanism could never work (all measured or quoted)

| Claim in the old runbook | Reality |
|---|---|
| The erasure searches `raw_payload` for `{"employer":{"contact_email": …}}` | That key **cannot exist**. The wire POCO cannot emit it (`JobTechSearchResponse.cs:125-143`) and the ingest sanitizer's default-deny allowlist drops it (`JobTechPayloadSanitizer.cs:62-64, :107-108`). **Measured: 0 of 93 469 ingested ads carry it.** `rowsAffected = 0` was the only outcome the code could produce. |
| "Sanitizer strips PII at ingest" | The sanitizer strips the **field**, not the **address**. It is a key-name filter that never examines a value, and it **deliberately retains every free-text key** (`description`, `text`, `company_information`, `needs`, `requirements`, `salary_description` — `JobTechPayloadSanitizer.cs:33-35, :55`). **PII in free text was never a gap in the design. It IS the design.** |
| "Free-text remnants are purged after 30 days" | `PurgeStaleRawPayloadsJob` only nulls `raw_payload` (`PurgeStaleRawPayloadsJob.cs:93-97`). It **never touches `description`**. It claimed to erase exactly the PII it cannot reach. |
| The manual Name fallback | Its search SQL looked in **both** `raw_payload` and `description`, and its erasure SQL updated **only `raw_payload`** — leaving the address in `description` and in `search_vector`. Even the reserve path was incomplete by construction. **That recipe is deleted from this runbook. Do not reach for it.** |
| TD-75: *"Email är primär rekryterar-identifier i JobTech-payloads"* | **Falsified, not stale.** The email is **never** a structured key in storage. **TD-75 is closed as VOID** (CTO V17). |

### Where the data actually is

The recruiter's address sits **verbatim, in plaintext, in `job_ads.description`**
(`PlatsbankenJobSource.cs:199-207` → `JobAd.cs:50` / `:156`), and it is
**full-text searchable by any user today**: `search_vector` is a STORED generated
column over `title || description` (`JobAdConfiguration.cs:174-179`), queried on
every non-blank `/jobb` search (`JobAdSearchComposition.cs:175-191`). Proven
against real Postgres: `search_vector @@ websearch_to_tsquery('swedish',
'<email>')` returns a hit. **The recruiter's name is independently searchable
too**, and no regex can reach it.

**Measured on the real corpus (93 469 ads, 2026-07-13):** **27 077 (29 %)** carry
a well-formed email in the ad body, **13 134** carry a phone number, and only
**17** use textual obfuscation.

---

## 1. If a request arrives RIGHT NOW

1. **Do not improvise SQL.** Nothing in this runbook is a self-service erasure
   button. There isn't one.
2. **Escalate to the data controller (Klas).** Every step below runs under his
   sign-off. He decides what is done and he approves what is said back.
3. **Record the request and the date it was received.** Art. 12(3) starts a
   **one-month clock** for informing the data subject of the action taken (or of
   the reasons for taking none, plus the right to lodge a complaint with IMY and
   to seek a judicial remedy). If the compliant remedy (PR3, §3) will not land
   inside that window, the extension or interim-communication decision is
   **Klas's, not the operator's**.
4. **Do not put the requester's email, phone number or name into the repo, into a
   commit message, into a log line, or into an issue.** This repo is public
   (ADR 0072) and CLAUDE.md §5 forbids logging sensitive data. The identifier
   lives in the controller's case record, outside the repo.
5. **Verify identity** before anything else: the requester must demonstrate
   control of the address/number in question (a request from the same address, or
   a written request).

---

## 2. Interim manual procedure (today's only complete remedy)

**Be clear about what this is.** The only complete remedy available today is to
**remove the whole ad record** for the matching ads. There is no supported
surgical redaction: clearing `description` alone leaves `extracted_terms`
(C#-written, not generated) intact, and every one-shot `UPDATE` is undone anyway
(see the durability warning below).

This is a **manual, controller-supervised** procedure. It is not a substitute for
PR3; it is what we do while PR3 is being built.

### 2.1 Search — two channels, both required

Run both. FTS will not find an obfuscated address; a substring scan finds
everything and over-matches. Over-match, then let a human confirm (fail-safe).

```sql
-- Channel 1: literal substring scan (catches obfuscated and partial forms).
-- Run once per identifier: email, phone number, and the recruiter's NAME.
SELECT id, external_source, external_id, status, published_at, expires_at, url,
       left(description, 240) AS description_excerpt
FROM job_ads
WHERE description ILIKE '%' || :identifier || '%'
   OR title ILIKE '%' || :identifier || '%'
   OR raw_payload::text ILIKE '%' || :identifier || '%';

-- Channel 2: FTS probe — the channel a user would actually use against /jobb.
-- The config MUST be 'swedish' or the probe misses the GIN index behind
-- search_vector (JobAdConfiguration.cs:174-179).
SELECT id, external_source, external_id, status
FROM job_ads
WHERE search_vector @@ websearch_to_tsquery('swedish', :identifier);
```

Notes:
- `:identifier` is PII. Pass it as a psql variable; do not leave it in shell
  history, in a script committed to the repo, or in a log.
- Search the **name** as well as the address. The name is what the address-shaped
  searches will miss, and it is separately searchable by users.
- There is **no soft-delete axis to hide behind**: `JobAd.DeletedAt` is never
  written by anything (#821), so a "deleted" ad is a fiction. Do not use it.

### 2.2 Human confirmation — mandatory, before anything destructive

A substring match is not a match. Common names produce false positives, and a
company inbox is not necessarily the requester's personal address. **A human
reads each candidate ad body and confirms it is the requester's data.** Record
which `external_id`s were confirmed and which were rejected.

### 2.3 Klas signs off before any destructive statement is executed

**No destructive statement is composed by an operator and no destructive
statement is executed without the data controller's explicit sign-off.** The
statement is written with him, against the confirmed id list, with these
constraints on the table:

- **FK: `applications.job_ad_id` references `job_ads`.** A naive `DELETE` either
  fails or cascades into users' own applications. The shipped remedy (PR3) is a
  **tombstone**, not a hard delete, for exactly this reason.
- **Durability: a one-shot write is undone.** The nightly full backfill
  (`sync-platsbanken-snapshot`, 02:00 UTC) and the 10-minute stream both funnel
  into `UpdateFromSource`, which **unconditionally** reassigns `Title`,
  `Description`, `Url` and `RawPayload` (`JobAd.cs:155-159`) and re-runs the term
  extractor. **There is no re-import block until PR3 ships.** Therefore:
  - **Ad no longer published at Arbetsförmedlingen** (check its `url`): it has
    left the feed, the sync will not rewrite it, and a removal is **durable**.
  - **Ad still published at Arbetsförmedlingen**: any removal is **undone within
    ≤10 minutes (stream) or ≤24 hours (nightly backfill)**. **Do not confirm
    erasure to the data subject in this case.** Klas decides between an interim
    holding reply (§4, template C) and any other controller-level action. Do not
    invent one.
- **Blast radius:** nulling `raw_payload` also nulls the seven generated columns
  derived from it (#824/#841). On a removed ad this is irrelevant — the row is
  excluded from every read path by the `Status == Active` filters — but it is not
  irrelevant on a live one.
- **Postgres residue:** an `UPDATE`/`DELETE` does not remove the old row version
  from disk until `VACUUM`, and copies remain in WAL/backups. **Do not make any
  statement to the data subject about backups or residual copies.** The
  backup/PITR retention window is not yet decided (CTO STOPP-4). Do not invent
  one.

### 2.4 Art. 30 audit trail — keep this step, but know what it does today

`audit_log.payload` (jsonb) **exists** as a column, but
`AuditLogEntry.Create` **hard-codes `payload: null`**
(`src/Jobbliggaren.Domain/Auditing/AuditLogEntry.cs:92`). So every audit row the
application writes today has an empty payload, and the old runbook's verification
query (`SELECT … payload FROM audit_log …`) returns an **empty column**. It looked
like a verification. It verified nothing.

For a manual action, write the audit row by hand — **carrying the external ids
and the counts, and no recruiter identifier at all**. The `external_id`s are not
PII and they are the accountability spine: they let a future auditor verify that
the erasure actually happened. The identifier itself stays in the controller's
case record.

```sql
-- Executed under the same sign-off as the destructive statement.
INSERT INTO audit_log (
  id, occurred_at, correlation_id, event_type, aggregate_type,
  aggregate_id, payload
)
VALUES (
  gen_random_uuid(), now(), gen_random_uuid(),
  'Admin.RecruiterAdsErasedManual',
  'System.RecruiterPiiErasure',
  gen_random_uuid(),
  jsonb_build_object(
    'matchedExternalIds', jsonb_build_array('<external_id1>', '<external_id2>'),
    'erasedCount', 2,
    'operator', '<operator>',
    'reason', 'GDPR Art. 17 request, case <case-ref>, received <YYYY-MM-DD>'
  )
);
```

**Never `md5()` the identifier.** The previous runbook suggested it. An md5 of an
email address is reversible by dictionary in milliseconds; it is not a pseudonym,
it is a fig leaf. **CTO V16 rejected it.** PR3 wires the audit payload properly,
with **HMAC-SHA256 over the server pepper** (the house primitive and precedent —
ADR 0090 D5).

---

## 3. What replaces this procedure (bound, NOT yet shipped)

ADR 0106 binds a **two-tier** contract. **Neither tier exists in the code yet.**
Do not describe either of them to a data subject as a control we have. Writing
about a control we do not have is the exact defect this whole issue is about.

- **Tier A — Art. 25, everyone, no request needed, heuristic, disclosed (PR2).**
  We stop **storing** recruiter contact details: email and phone are stripped from
  the ad body **at ingest, as a `JobAd` aggregate invariant**
  (`RecruiterContactRedactor`, deterministic, no LLM), and replaced by a marker
  pointing to the canonical ad at Arbetsförmedlingen. Detection is imperfect and
  **we say so** in the privacy policy.
- **Tier B — Art. 17, on request, provable, no detector involved (PR3, lifts the
  launch gate).** On a valid request we **remove the entire ad record**
  (`JobAdStatus.Erased`, zero migration) and **block its re-import**. It deletes
  the **carrier**, not the **string**: `description`, `search_vector`,
  `extracted_terms`, `extracted_lexemes`, `raw_payload` and the seven derived
  columns go together. No recall question, no obfuscation question, no
  image-embedded question — **and it covers the recruiter's name**, which no regex
  can reach.

**The operator-facing shape of Tier B**, which is what will replace §2:

- `EraseRecruiterAdsCommand(identifier, dryRun)` with **two-channel fail-safe
  matching** (FTS **plus** substring over `title`/`description`/`raw_payload`) —
  the same two channels §2.1 runs by hand.
- **A dry-run is mandatory before the destructive call.** A rule engine never
  rewrites silently (CLAUDE.md §5), and this is the one operation in the system
  that destroys content for every user.
- **An explicit outcome discriminator in the response body**, never a bare
  `rowsAffected`:
  - `NoMatchingDataHeld` — we hold nothing matching this identifier.
  - `AdsErased(count, externalIds)` — this is what we removed.
  - `DryRun(matches)` — this is what we would remove.
  **This is the whole point.** `rowsAffected: 0` is exactly what a broken
  mechanism and an empty result set look like to an operator, and they must never
  again be indistinguishable.
- The ad detail endpoint returns **410 Gone** for an erased ad; a resync does not
  resurrect it.

**Until PR3 lands, the launch gate stays closed: no `v*` prod tag** (CTO STOPP-6).

### Out of scope, recorded (not omitted)

**Tier B does not reach `applications.snapshot_description`** — an applicant's
frozen copy of the ad she applied to (ADR 0086 exists precisely so the snapshot
outlives the ad; nulling it would destroy her own record of what she applied to).
This is a **recorded decision** (CTO V13); the legal ground (Art. 17(3)(e)) is
Klas's to affirm (STOPP-3). Measured: the dev DB currently holds **0 non-null
`snapshot_description` rows**. If a reply to a data subject needs to mention it,
**Klas decides the wording** — the operator does not.

---

## 4. Reply templates (drafts for Klas — he approves and sends)

Swedish, "du", no exclamation marks, no emoji, no em-dash (CLAUDE.md §10).
**Never claim more than was actually done.** Do not add sentences about backups or
retention (CTO STOPP-4).

**A. Acknowledgement, on receipt.**

> Tack, vi har tagit emot din begäran om radering. Vi återkommer med besked om
> vilken åtgärd vi har vidtagit inom en månad från det att begäran kom in. Du når
> oss under tiden på <kontaktadress>.

**B. Completion — only after a Klas-signed removal of an ad that has LEFT the
feed.** (If the ad is still published at Arbetsförmedlingen, the removal is not
durable today. Use template C instead.)

> Vi har tagit bort hela annonsen ur våra system. Annonstexten, sökindexet och de
> uppgifter vi har härlett ur annonsen är borttagna hos oss. Vi kan inte ta bort
> annonsen hos Arbetsförmedlingen, som är den som har publicerat den. Vill du att
> uppgifterna tas bort även där behöver du vända dig till Arbetsförmedlingen eller
> till arbetsgivaren som publicerade annonsen.

**C. Holding reply — the ad is still in the feed and no durable remedy exists
yet.** Art. 12(3) requires the reasons and the right to complain.

> Vi har tagit emot din begäran. Annonsen hämtas löpande in på nytt från
> Arbetsförmedlingens öppna gränssnitt, och vi kan i dag inte hindra att den
> hämtas in igen. Vi bygger om raderingsvägen så att vi kan ta bort hela annonsen
> och blockera ny inhämtning, och vi återkommer med besked om vidtagen åtgärd. Du
> har rätt att lämna klagomål till Integritetsskyddsmyndigheten och att vända dig
> till domstol.

**The bound Tier-B wording (ADR 0106), for use once PR3 has shipped** — it says
"hindrar att den hämtas in igen", which is **not true today**:

> Om du begär radering av dina kontaktuppgifter i en annons vi har hämtat tar vi
> bort hela annonsen ur våra system och hindrar att den hämtas in igen. Vi kan
> inte ta bort annonsen hos Arbetsförmedlingen, som är den som publicerat den.

---

## 5. Cross-references

- **ADR 0106** — the erasure contract: ingest minimisation (Tier A) + provable
  record removal (Tier B). The binding document.
- **ADR 0032 §8** (amended 2026-07-13) — the JobTech ingest sanitizer. The
  amendment withdraws the claim that the sanitizer + purge + email-erasure
  together covered Art. 5/17/30.
- **ADR 0024** — audit retention and the Art. 17 cascade registry. Its cascade
  scope listed only `raw_payload` and never `job_ads.description`.
- **ADR 0086** — the applicant's ad snapshot (why Tier B does not cascade into it).
- **ADR 0090 D5** — HMAC-SHA256 over the server pepper (the audit-payload
  primitive; not md5).
- **Issue #842** — this defect. **#843** (test fiction), **#845** (the purge job's
  false retention claim), **#821**/**#841** (`DeletedAt` / generated columns).
- `docs/research/2026-07-13-842-erasure-evidence-pack.md` — the proven facts and
  the measurements quoted above (local, gitignored per ADR 0072).
- `docs/reviews/2026-07-13-842-erasure-contract-cto.md` — the binding CTO ruling
  (local, gitignored).
- `docs/runbooks/gdpr-processing-register.md` — Art. 30 register.
