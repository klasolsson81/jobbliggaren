# ADR 0184 — DMARC aggregate reports are read by the Worker, read-only over IMAPS, bounded by construction, minimised, and kept twelve months

**Date:** 2026-10-24
**Status:** Accepted
**Decider:** Klas Olsson. His prompt of 2026-10-24 asked for DMARC aggregate reports (`rua`) on `/admin/e-post`, ingested
automatically. On 2026-10-24 he answered, verbatim: K1 "Ja, avgränsa (Rek.)" (the `release-checklist.md` mailbox sentence binds
the agents, not the Worker's read of `dmarc@`); K2 "Ja, MailKit 4.18.2 (Rek.)"; K3 "Ja, automerge (Rek.)" for exactly
`AddDmarcAggregateReports`; K4 "Ja, båda (Rek.)" (`hotspot:i18n` for PR L and PR 4b); K5 "Godkänn, egen PR nu (Rek.)" (the
privacy-policy text in its own PR); K6 "Ja, /32 per A-post (Rek.)"; K7 "Ja till a, b och c (Rek.)"; K8 "Ja, ordningen (Rek.)".
`senior-cto-advisor` ruled the plan in two rounds (`docs/reviews/2026-10-24-1981-pr4-plan-cto.md`, `-cto-r2.md`).
`security-auditor`, `dotnet-architect`, `db-migration-writer`, `design-reviewer`, `test-writer`, an independent planner and a
measurement agent read it; `security-auditor` and `dotnet-architect` re-checked the ruling.
**Related:** [#1981](https://github.com/klasolsson81/jobbliggaren/issues/1981) (PR 4a; PR L and PR 4b are its siblings) ·
ADR 0050 · 0123 · 0124 · 0150 · 0154 · 0172 · 0178 · 0179 · 0181
**Measured against:** `origin/main` `5ad51c9c1`, read 2026-10-24.

> **Provenance.** Written by the driving session from the rulings above. `docs/runbooks/admin-email.md` is the one home for
> what the figures mean, how they are read on the box, and how the password is placed.

---

## Context

`_dmarc.jobbliggaren.se` has published `rua=mailto:dmarc@jobbliggaren.se` since 2026-10-17. The mailbox is a STRATO mailbox
(STRATO GmbH is the processor and its DPA is signed). On 2026-10-24 Klas read 8 messages there, all aggregate reports from
Google, the first received 2026-10-19 (`vps-deploy-stack.md` row 38). Nobody should have to open the mailbox to know whether
mail sent as the domain authenticates.

Measured at pickup (2026-10-24):
- From the box, TCP 993 to `imap.strato.de` times out while 443 to an outside host connects: the edge policy `jbl-egress`
  allows 443, 80, 53 and 123 only (`vps-base-hardening.md` §6.3).
- `imap.strato.de:993` speaks TLS 1.3 with a chain to "T-TeleSec GlobalRoot Class 2" and advertises IMAP4rev1,
  `AUTH=PLAIN` and `AUTH=LOGIN` before login. One password covers IMAP, SMTP and webmail; SPF includes STRATO and STRATO's
  DKIM selectors are published, so that password can send DMARC-passing mail as `dmarc@`.
- The base library has no IMAP client and no MIME body parser. MailKit 4.18.2 (MIT, .NET Foundation) does. Its IMAP
  `BODYSTRUCTURE` parser recurses with no depth bound (4.18.1 and master, read 2026-10-24), and a stack overflow cannot be
  caught: one attacker-built message could crash the Worker on every run, and the Worker also runs the erasure and retention
  jobs.
- DMARCbis was published in May 2026 (RFC 9989, 9990, 9991). Reporters send the RFC 7489 form, often without a namespace, and
  the RFC 9990 form with `urn:ietf:params:xml:ns:dmarc-2.0`. Google sends `.zip`; Microsoft and Yahoo send `.xml.gz`.
- Anyone can mail the report address. A report is the sender's own statement.

## Decision

### D1 — The Worker reads the mailbox, read-only, over IMAPS

The Worker reads `dmarc@jobbliggaren.se` over IMAPS on `imap.strato.de:993` with implicit TLS. Host, port and username are code
constants. Certificate and revocation validation are the defaults and are never relaxed: no validation callback, no pinned
bypass. The mailbox is opened with `EXAMINE` and every body read is a `BODY.PEEK`, so no message is ever marked read, moved,
flagged or deleted. The mailbox keeps its own retention, which is Klas's manual routine, and it stays the authoritative copy:
if the database copy and the mailbox disagree, the mailbox wins.

Exactly one authentication command is sent per run, with a constant SASL mechanism (`PLAIN`, read anonymously from the
server's capabilities on 2026-10-24 at 18:35 UTC: `IMAP4rev1 … AUTH=PLAIN AUTH=LOGIN …`, no SASL-IR). If the server does not
offer it, the run ends without sending the password. After signing in, MailKit itself sends `NAMESPACE` and two `LIST`
commands; they read folder names only and are on the adapter's tested command list.

### D2 — MailKit, in a Worker-only project, and never MimeKit's parser on a sender's structure

MailKit 4.18.2 (Klas, K2) is referenced only by the new project `Jobbliggaren.Infrastructure.Mailbox`, which references
Application and nothing else from this repository; only the Worker references it. The Api image never carries MailKit,
MimeKit or BouncyCastle, and "the Api never builds the mailbox" is a compile-time fact.

No part of a message's structure reaches a recursive parser in our process. The adapter never requests `BODYSTRUCTURE` or
`ENVELOPE` and never calls MimeKit's message or entity loaders. It lists with flat `FETCH` items, reads at most 8 KiB of the
top-level header and at most 4 KiB of each of the first four top-level part headers, unfolds those headers with its own
non-recursive code, and parses one unfolded `Content-Type` or `Content-Disposition` value at a time. A header block that does
not end inside its cap is skipped for good, never parsed truncated. Nested multiparts and `message/rfc822` parts are never
descended into. Architecture tests pin the package declarations, the project references and the banned calls.

### D3 — Listing and fetching are bounded whatever the mailbox holds

After `EXAMINE`, the message count and UIDVALIDITY are captured once. The first message above the cursor is found by bisection
with single-number `FETCH k (UID)` probes (at most 32), and one window of at most `PerRunMessageCap` (100) messages is
fetched with explicit numbers for UID, size and receipt time. The adapter never sends a `*` message range, `SEARCH`,
`UID SEARCH`, `SORT` or `THREAD`, whose answers grow with the mailbox. A UID at or below the cursor is dropped (RFC 3501 §6.4.8).

Per message: a message over `MaxMessageBytes` (2 MiB) is skipped unread; a forensic report
(`multipart/report; report-type=feedback-report`) is skipped after its top-level header; exactly one part must be the report
(by media type or file extension), else the message is skipped; that part is fetched with a partial fetch of at most 1 MiB plus
one byte, and transfer-decoded (base64, quoted-printable, 7bit, 8bit, binary). The filename is used for the match only and is
never stored or logged. Every bound is named in `DmarcIngestionLimits`.

### D4 — Decoding is bounded; acceptance refuses the whole report

The decoder uses the base library only. A zip must hold exactly one unencrypted `.xml` entry whose declared size is within the
cap; gzip may hold concatenated members. Decompression is capped at 8 MiB through a counting stream, and the XML is read only
through `XmlReader` with DTDs prohibited and no resolver (the settings shared with the CV extractor), elements matched by local
name, at most 32 levels deep and at most 5 000 records. The content digest is SHA-256 over the decompressed XML.

`DmarcAggregateReport.Accept` is the one place a report's bounds are decided. Any bound broken by any record refuses the whole
report, because a partly accepted report would show a subset as if the reporter had said it: another policy domain; a
reporter name or report id that is blank, longer than 255 characters or carries a control, format or unassigned
character (bidi controls included); a source that is not exactly one IPv4 address in dotted-quad form or one unscoped IPv6
address (IPv4-mapped addresses become IPv4); a header-from that is not LDH labels equal to or under `jobbliggaren.se`; a count
outside 1..10 000 000; no records or more than 5 000; a period that is not `0 < end − begin ≤ 7 days` with
`end ≤ receipt + 1 day`. An inclusive day end (`begin + 86399`) becomes exclusive.

### D5 — Claim before content, and every retry is bounded

Each new message is claimed before any of its content is fetched: one transaction writes its ledger row `InProgress` and moves
the cursor. Failure classes decide what happens to it:
- **A session fault** (connect, TLS, authentication, socket, timeout, BYE, an unusable connection, a tagged BAD) hands the
  message in hand back as `RetryPending` and ends the run with that fault in the state row.
- **A refusal** of one message (a tagged NO on a usable connection) makes it `RetryPending` and the run continues.
- **A content failure** (skip, malformed, rejected) is terminal for the message.
- **A missing part** during the header probe ends the probe.

Retries run after new messages, within the same per-run cap, and a message is abandoned after 168 caught retries. A row still
`InProgress` when a run starts counts one crash; two crashes abandon it. A caught failure, a session fault and a graceful
shutdown count none: on host shutdown the claim in hand is handed back under a fresh 5-second token. A run has a 5-minute
deadline; reaching it is a `Timeout` fault. After a refused login, no run connects again for 24 hours, and a backed-off run
writes nothing (STRATO's lockout policy is unmeasured, and the same password is Klas's webmail login). A changed UIDVALIDITY
starts a new epoch: the cursor goes to position 0 and the old epoch's ledger rows are deleted in one transaction; stored
reports stay, and the content key keeps a rescan from storing one twice. The Hangfire wrapper never retries automatically.

### D6 — What is stored, and the caps on it

Migration `AddDmarcAggregateReports` (Klas, K3) adds four tables and changes none:
- `dmarc_reports`: reporter, reporter's report id, content digest, period, receipt time; unique on reporter, report id and
  digest.
- `dmarc_report_rows`: per record the source (`inet`), message count, header-from domain and the two aligned results.
- `dmarc_mailbox_messages`: the ledger by epoch and position, with receipt time, outcome, crash attempts and retries.
- `dmarc_mailbox_state`: one row with the latest attempt, its outcome, the latest success and the cursor; never seeded, so a
  missing row is "never attempted".

Never stored: the reporter's contact address, extra contact information, envelope identifiers, raw authentication results
(DKIM domains and selectors, SPF domains), disposition, override reasons, comments, errors, generator and extensions; the
message's sender, subject, body and filename. `source_ip` is stored in plaintext: no non-user data key exists, and anonymising
it would destroy what identifies an unauthorised sender. The plaintext-exposure registry sweeps it as an address column.

Per UTC day of receipt at most 100 reports and 5 000 records are stored; past either, the message is `VolumeCapped` and
counted. With the 12-month retention the total is bounded by those caps times (366 days + the days to the next retention run),
plus at most 100 × 24 ledger rows a day. The numbers are revisited against the first 30 days live (8 reports in 5 days so far).
A database refusal of an accepted report is a typed catch that records `RejectedByStore`; `Accept` checks every bound the
database enforces first, so that outcome is declared unreachable.

### D7 — Two versions of one report are counted, never summed

A report id is the reporter's, and anyone can write one. When one reporter and report id are stored with more than one content
digest, none of their versions counts towards any figure; the conflict is counted once on the page. The residual: a forger who
predicts an id can keep a genuine report out of the figures, visibly and countably; the mailbox stays authoritative.

### D8 — Retention is twelve calendar months from receipt

Klas decided on 2026-09-04 that DMARC reports are kept twelve months from receipt. Receipt is the mailbox's own delivery time,
never a sender-written header, and never later than the clock that reads it. `DmarcRetention.CutoffOf` is the one rule:
ingestion skips a message received before the cutoff (it advances the cursor and writes no ledger row), and the daily
`dmarc-report-retention` job (04:20 UTC) deletes every report (with its records) and ledger row received before it. A row
therefore lives at most twelve months plus the time to the next successful run, except while a rollback across
`AddDmarcAggregateReports` lasts: the older code neither ingests nor deletes, so rows past the bound stay until the roll
forward, `migrate` refuses to start until `MIGRATE_ALLOW_SCHEMA_AHEAD` names the migration, and Down would drop the reports, the
ledger and the cursor (lapse trigger 3). The state row holds no personal data and is never deleted. Backups follow the processing register's backup entry. A schedule change goes back to `security-auditor`.

### D9 — The read, and what the page may claim

`GET /api/v1/admin/email/dmarc` (Admin policy, `admin-read` rate limit, `private, no-store`) is read by the Api alone from the
database; the Api never holds the password or the mailbox. The window is a fixed 7 × 24 hours: a report is in it when its
period overlaps the window, half-open, and the covered span is returned with the figures. The state comes from the state row
alone: never attempted, not configured, misconfigured, failed before any success, or — once a run has succeeded — observed or
no report in the window, with the latest failure and a staleness flag beside it. An unknown figure is null, never 0
(ADR 0150 D2). Sources are grouped by address and header-from domain, each with its count of messages that failed DMARC
(neither DKIM nor SPF passed aligned), which flags the source when it is above zero; at most 25 are shown, with the total named.

DMARC reports show **authentication**, not inbox or spam placement, and they are the reporters' own unverified statements. The
region on `/admin/e-post` is PR 4b's, with its own ADR 0150 amendment, and it keeps this third source apart from the
provider's figures and our own handover count (ADR 0178 D1, ADR 0181 D6).

### D10 — The password and the egress rule

The password lives in `/run/jobbliggaren/worker-secrets/Dmarc__Mailbox__Password`, a directory only the Worker mounts, behind the
pointer `Dmarc__Mailbox__Password_FILE`, which only the Worker's compose block carries. Klas sets a new random password used for
`dmarc@` only and injects it from his own terminal; no agent ever types, reads or prints it. Without the key the job records
`NotConfigured`; with a blank value it records `Misconfigured`; neither stops the Worker. A pointer set to a missing file stops
the Worker at boot, which `--check` reports. The lifecycle row is in `master-key-ops.md`.

Egress gets one rule (Klas, K6): TCP 993 to one `/32` per A record of `imap.strato.de`, applied in Netcup's console after merge.
A port-only rule would open IMAPS from every container to every host.

**The residual Klas accepted as controller (K7, not a §9.6 acceptance):** the password can send DMARC-passing mail as `dmarc@`
and delete the reports, the same risk class as the Scaleway sending key the Worker already holds.

### D11 — Personal data

The reports' `source_ip` is personal data when a server is a person's own (C-582/14 *Breyer*); the basis is legitimate interest,
Art. 6(1)(f) and recital 49: detecting mail sent in our name without authority. We cannot identify the people concerned
(Art. 11); Articles 15–21 apply once someone supplies the address. An Art. 17 erasure is manual: Klas deletes every report that
holds the address, with every version of its report id, and the messages in the mailbox; the address never enters chat or a
transcript, and the erasure is recorded as a date and a count. The processing is described publicly under Art. 14(5)(b) by
PR L; the sentences saying the service reads and deletes the reports automatically follow no later than one month after the
first successful run. There is no soft delete and no `created_by`/`updated_by`: the rows end by hard deletion at twelve months
and no user writes them; the ledger outcomes and the count logs are the write trail. Logs carry event ids, outcomes and counts
only, never an address, reporter name, report id, UID or the password. What the box may be read for is listed in
`admin-email.md`.

## Alternatives considered

- **A third-party report receiver with an API.** Postmark (US) would add a Chapter V transfer for an admin convenience page, and
  its DPA is not confirmed to cover its DMARC product. URIports (EU) would add a processor, a DPA, a policy recipient, a paid plan
  and a second external copy for 30–90 days. Rejected while D1 is available; URIports is the fallback if D1 ever is not.
- **Our own IMAP and MIME code over `SslStream`.** No dependency, but our own parser for a network protocol and attacker input.
  With D2 it gains nothing. Rejected.
- **Waiting for MailKit 5.0.** Unreleased, and its `BODYSTRUCTURE` parser is still unbounded on master. Rejected.
- **MailKitLite.** Drops BouncyCastle, which our call path never reaches, but its maintainer lists it as obsolete and it trails
  releases. Rejected.
- **MailKit inside `Jobbliggaren.Infrastructure`.** The Api would carry MailKit, MimeKit and BouncyCastle it never uses. Rejected.
- **`BODYSTRUCTURE`, or MimeKit with a small `MaxMimeDepth`.** The first sends the sender's nesting into an unbounded recursion;
  the second rests on a library setting rather than on construction. Rejected.
- **Deleting or marking messages after ingestion.** It would take over Klas's manual mailbox retention and make the mailbox stop
  being the authoritative copy. Rejected.
- **The page's own period links for this region.** Reports cover whole UTC days and arrive a day late, so "24 tim" would almost
  always be empty. Rejected for a fixed window.

## Consequences

### Positive
- The reports are read without anyone opening the mailbox, and a failing source is visible on the admin page.
- No sender-built structure reaches a recursive parser; every byte, record, message and stored row has a named bound.
- The Api cannot reach the mailbox or its password, by compilation and by mount.

### Negative and risks
- A standing dependency: an advisory in MailKit, MimeKit or BouncyCastle fails restore repo-wide until it is pinned.
- One start tag with hundreds of thousands of attributes fits in a 1 MiB part and costs about 190 MB and 2.7 s of CPU in the
  reader (measured 2026-10-24); no reader setting bounds it. The per-run cap and the run deadline bound how often it can recur.
- A flood of valid-looking forged reports can use a day's storage cap; genuine reports are then counted `VolumeCapped`.
- The ledger keeps only epoch and position, so Klas cannot find a skipped message by its ledger row.
- After a refused login, ingestion waits 24 hours even once the password is fixed.
- If STRATO renumbers `imap.strato.de`, ingestion reads `Unreachable` until the egress rule is edited.

## Lapse triggers

Each goes back to the reviewer named.
1. Mozilla removes "T-TeleSec GlobalRoot Class 2" from website trust on **2027-04-15**; once a Worker image carries a
   ca-certificates bundle without it, the state reads `TlsFailed`. Never fixed by relaxing validation. (`security-auditor`)
2. The A records of `imap.strato.de` change (the state reads `Unreachable`). (Klas, egress rule)
3. A rollback across `AddDmarcAggregateReports`: retention stops until the roll forward (D8). (`db-migration-writer`)
4. A new stored field, raw authentication results included; `ruf=`; a retention or schedule change; a second holder of the
   password; a third-party receiver; any automated action on a report (alert, attention item, notification, blocklist,
   export); display outside `/admin`. (`security-auditor`)
5. MailKit 5.0, or any change to the adapter's command set or fetch caps. (`security-auditor`, `dotnet-architect`)
6. The first 30 days of measured volume, against the per-day caps. (`senior-cto-advisor`)

## Implementation

- Application, `Admin/Email/Dmarc`: the vocabulary, `DmarcIngestionLimits`, `DmarcRetention`, `ReportedSourceAddress`,
  `DmarcAggregateReport.Accept`, the ports, the evaluator and `GetDmarcReportsQuery`; `Admin/Email/Jobs`: the ingestion and
  retention jobs.
- Infrastructure, `Email/Dmarc`: the entities, `SqlDmarcReportStore`, `SqlDmarcReportsSource`, `SqlDmarcReportRetention`, the
  decoder (`Decoding/XmlDmarcReportDecoder`); `Common/IO`: the byte budget, the capped stream and the hardened XML settings shared
  with the CV extractor.
- `Jobbliggaren.Infrastructure.Mailbox`: `ImapDmarcMailbox` and its selection.
- Worker: `dmarc-report-ingestion` (`23 * * * *`) and `dmarc-report-retention` (`20 4 * * *`). Api: the endpoint above.
- Deploy: the worker-only secret directory, its pointer, `--check`, and the egress rule in `vps-base-hardening.md` §6.3.
