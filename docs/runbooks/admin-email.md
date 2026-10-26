# Runbook — `/admin/e-post`: the allowance, Leverans, our own counts per mail kind, and the DMARC reports

The page answers four questions. First, how this month's sent mail stands against the provider's free monthly allowance,
and what the mail beyond it costs (ADR 0178). Second, Leverans: how many mails the provider sent and failed per period, and
which sends failed recently (ADR 0179). Third, under Leverans, how many attempts of each mail kind we handed over to
Scaleway and how many failed (ADR 0181). Fourth, from the DMARC aggregate reports the receiving mail services send us, which
servers sent mail as `jobbliggaren.se` and whether it authenticated (ADR 0185). This runbook is the one home for what the
figures mean and how they are kept true.

## What the figures are

**The allowance (this month):**

| Shown | Source | Meaning |
|---|---|---|
| Skickade | Scaleway, `project-consumption` → `monthly_emails_count` | Mail sent this month **in the project**, counted by the provider. Each recipient is one mail. |
| Ingår gratis | `EmailAllowanceOptions.IncludedMonthlyEmails` | The plan's free tier. Shown only when the plan Scaleway names is the configured one. |
| Beräknad kostnad inkl. moms | computed | `max(0, sent − included) × price / 1000`, plus Swedish VAT, at the configured kronor-per-euro rate, rounded **up** to the whole öre. An estimate, never the invoice: a card is charged at its own rate on the day. |
| Sändgränsen | `EmailAllowanceOptions.MonthlySendLimit` | The project's monthly quota, shared with the sign-in codes. Above half of it is ADR 0161 D4 trigger 1. |
| Webhooks | Scaleway, `project-consumption` → `webhooks_count` | None should exist (see the processing register). A line appears only when one does, or when the count could not be read. |

**Leverans (per period: 24 tim, 3 dygn, 7 dygn, chosen in the URL as `?period=h24|d3|d7`):**

| Shown | Source | Meaning |
|---|---|---|
| Skickade | Scaleway, `statistics` → `sent_count` | Mails created in the period that the provider handed to the receiving mail system. Not proof of inbox placement. |
| Misslyckade | Scaleway, `statistics` → `failed_count` | Mails created in the period that the receiving system refused with a final error. |
| Senaste misslyckade utskick | Scaleway, `emails?statuses=failed` | At most ten failed sends from the period, newest first: time, one of seven reasons, the recipient masked to its first character and domain. |
| Kräver uppmärksamhet (overview) | the 24-hour Misslyckade | Above 0 it raises the card; 0 says there were no failed sends at the provider. |

The two "Skickade" are different measures: the month's count from `project-consumption`, and a rolling period's count
from `statistics`. They are not reconciled on the page. A mail the provider never accepted (our send failed before
Scaleway took it) is in neither; our own count below shows it as a failure.

**Utskick per mejltyp (our own count, per period, ADR 0181):**

| Shown | Source | Meaning |
|---|---|---|
| Överlämnade | `email_send_daily_counts`, outcome Accepted | Attempts of the kind that Scaleway answered 2xx: handed over, not proof of delivery. |
| Fel | the same table, every other outcome | Attempts that failed at the handover: refused (4xx, our own 401/403 included), over the rate limit (429), Scaleway not reached, an error at Scaleway (5xx), no answer in time, or another failure. The last three may still have been delivered. |
| Senaste fel | the same table | The latest date in the period with a failure of the kind, and every failure class seen that date. A daily count does not know which came last. "Inget" when there was none. |

- **The unit is the attempt.** A feedback notice the provider refused is retried, so it can count as several failures and then one handover.
- **The dates.** The table covers every whole Swedish date the rolling period touches, from the first date the note names through today, so it may span more than the period. It is counted by Jobbliggaren in the Scaleway send path and is never Scaleway's statistics.
- **Partial.** Counting began when the migration ran (`email_send_counting.started_at`). A period reaching further back is shown from that instant, which the note names; the figures are exact for that span.
- **The gate.** When our count is zero for the whole period while Scaleway's own read of the period shows a sent mail, the table says so and shows no figure ("Jobbliggarens räkning visar inga utskick under perioden, fast Scaleway har skickat."). Look for Warning 3009 (`[EmailSendTally] … was not confirmed as counted`) in the api and worker logs, by event id and count only. The gate does nothing without the read key, and catches a total loss, not a partial one.
- **Not counted:** a template fault before the send, a send cancelled at shutdown, and every mail held back before the sender (no recipient, a closed gate, a spent budget). "No recipient" is not observable from the send path (ADR 0181 D9).
- **Kept** no longer than eight Swedish dates plus the time to the next successful scheduled run: `email-send-count-retention` runs daily at 04:40 UTC and deletes every count dated before the first date the seven-day period reads. A failed run leaves the rows for the next one.
- **Never a send log** in the sense of CLAUDE.md §9.6 (3)(ii), and never a measurement of "no reached bearer" or of ADR 0133's bound: a count is not a set of recipients.

The kinds, in the table's order:

| Row | Kind (logs, exceptions, the wire) |
|---|---|
| Inloggning och koder | `login-challenge` (sign-in, new-account, re-authentication and address-change codes, and the replies about a pending deletion or closed registration) |
| Adressbyte begärt | `account-email-change-requested-notification` |
| Adressbyte genomfört | `email-changed-notification` |
| Matchningar | `match-notification` (Toppmatch and the digests) |
| Bevakade företag | `followed-company-notification` |
| Ny feedback | `feedback-received-notification` |
| Svar på feedback | `feedback-reply` |

**Reading on the box (CLAUDE.md §9.2).** The rows of `email_send_daily_counts` are personal data (§9.2 (b)): a day's count of
a rare kind can be linked to our own records per person (ADR 0181 D8). Only these are (a):
- that both tables exist;
- `count(*) > 0` and `max(day) =` today's Swedish date, each printed as a boolean;
- one whole-table `sum(attempts)`;
- `email_send_counting.started_at`;
- the migrate tool's output;
- one number: the union of log events 3005 and 3006 in the api and worker container logs together since `started_at`, counted
  in one command and matched on event id and category, never on message text. It means something only while both containers
  started after `started_at` and were neither recreated nor rotated, and within seven days. A difference from the sum is a
  hint to read the page, never a reason to read rows;
- a count of event 3009 lines, on the same terms.

Never printed: a row, any grouping by kind, outcome or day, a total of failures, or anything from Seq or the log archive. The
table per kind is read on the page, by Klas.

**After a rollback across `AddEmailSendDailyCounts`** (run under `MIGRATE_ALLOW_SCHEMA_AHEAD`): older code counts nothing, so
after the roll-forward the dates in between would read as zeros under a start that predates them. Klas empties the counts and
re-takes the start in one box write under his GO (ADR 0154), once the counting release runs again (ADR 0181 lapse trigger 7):
`DO $$ BEGIN DELETE FROM email_send_daily_counts; UPDATE email_send_counting SET started_at = now(); END $$;`

**Counted for the project, free tier per organisation.** The free tier is "300 per organization". The count is the project's.
They are the same while the organisation has one project sending through Transactional Email (measured 2026-10-19, ADR 0178).

**Not known, and never claimed on the page:**
- the instant at which the provider's month resets (UTC or Paris time);
- whether failed or cancelled sends are in the monthly count;
- how far `statistics` lags real time, or whether its window edges fall on time buckets. A 20-minute window around a
  sign-in code sent five minutes earlier read 0 on 2026-10-19 (ADR 0179 D3). The page shows when the figures were
  fetched, never that a period counts up to that moment.

The page names the month only where UTC and Stockholm agree on it; otherwise it reads "Utskick denna månad".

## DMARC aggregate reports (ADR 0185)

**What they are.** Receiving mail services (only Google so far) send a daily aggregate report to `dmarc@jobbliggaren.se`, a
STRATO mailbox. The Worker reads that mailbox every hour at :23, read-only, and stores the accepted reports;
`GET /api/v1/admin/email/dmarc` serves them over a fixed seven-day window, and the DMARC-rapporter section on `/admin/e-post`
shows them. The figures
are the reporters' own statements, never verified by us, and they are a third source: never Scaleway's figures and never our
own handover count. A source is flagged when any of its messages failed DMARC, meaning neither DKIM nor SPF passed aligned.

**The states** come from the one state row the ingestion writes: no row (never attempted), `NotConfigured` (no password),
`Misconfigured` (pointer set, password blank), `Failed` (no run has succeeded yet; the latest outcome says why), and, once a run
has succeeded, `Observed` or `NoReports`, with the latest failure and `stale` (the latest run failed, or the latest success is
more than three hours old) beside them.

**What the section says** (ADR 0150 Amendment (4)):
- The note's span is the covered period of the included reports, from the earliest start to the latest end. The end is
  exclusive: Google's report for a UTC day reads "02:00 till 02:00" in summer time.
- At most 25 rows, failing sources first; "Visar 25 av N rader." when more were counted. The figures count every source.
- "… kunde inte läsas som rapport" counts the ledger's `Skipped*` and `Rejected*` outcomes; "… kunde inte sparas" counts
  `VolumeCapped`; "… efter upprepade försök" counts `Abandoned`; all three over mail received in the window. "… väntar på
  ett nytt försök" counts every `RetryPending`, whenever received. Tell them apart with the `count(*)` by `outcome` reading
  below.
- "… finns i flera versioner …" counts the reports in the window whose reporter and report id were stored with other
  content; none of their versions is in any figure (ADR 0185 D7).

**Reading on the box (CLAUDE.md §9.2).** Every row of the four tables is personal data or points at it (§9.2 (b)). Only these
are (a):
- the migrate tool's output;
- that each of `dmarc_reports`, `dmarc_report_rows`, `dmarc_mailbox_messages` and `dmarc_mailbox_state` exists, as a boolean;
- from `dmarc_mailbox_state`: `last_attempt_at`, `last_success_at`, `last_outcome`, and `last_uid IS NOT NULL` as a boolean;
- `count(*)` of `dmarc_mailbox_messages` grouped by `outcome`;
- whole-table `count(*)` of `dmarc_reports` and `dmarc_report_rows`, and one whole-table `sum(message_count)`;
- `max(received_at) > now() - interval '48 hours'` and `min(received_at) >= now() - interval '12 months' - interval '2 days'`
  on `dmarc_reports`, each as a boolean;
- counts of the job's log event ids (3120–3124 ingestion and retention, 3130 store, 3135–3139 mailbox) in the worker's log;
- the inject script's `--check` output, and `stat -c '%a %U %G' /run/jobbliggaren/worker-secrets`.

Never read: a row of any of the four tables; `source_ip` in any form (no `DISTINCT`, min/max, grouping, filter or probe for one
address); a reporter, report id, header-from or an individual report's period; any grouping by reporter or day; the ledger's
UIDs or the cursor's values; the password file in any form, `cat`, `wc` or a hash included; the mailbox; Seq beyond a count.

**Placing the password the first time** (after the release that carries #1981 PR 4a is on the box; every step with Klas's GO in
chat, one step per message):
1. CC advances the box to the release (`vps-deploy-stack.md` §3b): `merge --ff-only`, then installs the changed tmpfiles and
   units (`sudo install -m 0644 /opt/jobbliggaren/deploy/systemd/jobbliggaren-tmpfiles.conf /etc/tmpfiles.d/jobbliggaren.conf`,
   `sudo systemd-tmpfiles --create`, the observe unit with the install block's `cp`, `chmod` and `daemon-reload`), then starts
   the reconcile unit, which also creates `/run/jobbliggaren/worker-secrets` and lets `migrate` apply `AddDmarcAggregateReports`.
   Read `--status`.
2. Klas adds `jbl-egress` rule 6 in Netcup's console: resolve `imap.strato.de` (A records only), one rule per address, TCP,
   destination port 993, destination `/32`, ACCEPT; read the policy back (`vps-base-hardening.md` §6.3 and §6.5). Then, from
   the box and with no login, a TLS handshake to `imap.strato.de:993` must complete and a connection to another host on 993
   must time out; the outcome goes into the GO record.
3. Klas sets a new random password for `dmarc@` only in STRATO's customer login, and writes down with the date whether the
   mailbox can be barred from sending or webmail and whether it has two-factor sign-in.
4. Klas runs `sudo JBL_INJECT_DMARC_MAILBOX=1 /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh` in his own
   terminal and pastes the password at the prompt. It never enters chat.
5. In `/opt/jobbliggaren/deploy/.env`, set `DMARC_MAILBOX_PASSWORD_FILE=/run/worker-secrets/Dmarc__Mailbox__Password`.
6. Run the script's `--check`; it must pass.
7. Recreate the worker under the reconcile lock in the form `deploy/.env.example` gives (`--status`, then
   `up -d --pull never worker`). Never `docker restart`: a restart does not re-read `.env`.
8. Wait for the next :23 run, or Klas triggers `dmarc-report-ingestion` from `/admin`. CC cannot sign in (§9.2 (d)).
9. CC takes the (a) readings above. Klas checks in webmail that no message was marked read or moved.

**Rotating it** on any trigger in `master-key-ops.md` (R1–R3): set a new password in STRATO, remove the old file
(`sudo rm /run/jobbliggaren/worker-secrets/Dmarc__Mailbox__Password`, because the script skips a file that is present), inject
the new one as in step 4, recreate the worker as in step 7, and read the next run's `last_success_at`. The same password is the
mailbox's webmail login, so it changes there too.

**Removing it:** clear `DMARC_MAILBOX_PASSWORD_FILE` in `.env`, remove the file, recreate the worker. The next run records
`NotConfigured`. The stored reports stay until the retention job deletes them twelve months after receipt.

**An Art. 17 erasure by address** (ADR 0185 D11) is Klas's, by hand, with his GO. It deletes every report whose records hold the
address, and every version of those reports' ids; the records go with their reports by cascade, and the ledger holds no
address. The address is typed only into his own `psql` session as a variable, never into chat or a file, and the erasure is
recorded as a date and a count:

```sql
\prompt 'Address: ' addr
SELECT pg_input_is_valid($1, 'inet') AS addr_ok \bind :addr \gset
\if :addr_ok
DELETE FROM dmarc_reports r
 USING (SELECT DISTINCT s.reporter, s.reporter_report_id
          FROM dmarc_reports s JOIN dmarc_report_rows w ON w.report_id = s.id
         WHERE w.source_ip = $1::inet) hit
 WHERE r.reporter = hit.reporter AND r.reporter_report_id = hit.reporter_report_id
\bind :addr \g
\else
\echo 'Not an address'
\endif
```

He also deletes the messages that carry those reports from the mailbox in webmail.

**When the state reports a failure:**

| `last_outcome` | What to do |
|---|---|
| `NotConfigured` | Expected in dev, CI and the harness. On the box: place the password (above). |
| `Misconfigured` | The pointer is set but the password file is blank: remove the file and inject again. |
| `Unreachable` | The egress rule is missing, or STRATO has renumbered `imap.strato.de`: re-resolve and edit rule 6 with Klas's GO. Never a port-only rule. |
| `TlsFailed` | The certificate chain fails. Expected once a worker image lacks "T-TeleSec GlobalRoot Class 2" (ADR 0185 lapse trigger 1). Never fixed by relaxing validation. |
| `AuthRefused` | The password was refused. No run connects again for 24 hours, so a fixed password is picked up after that; the page shows the earliest next sign-in. Rotate it (above). |
| `AuthUnconfirmed` | The password was sent and no answer came: a timeout, a BYE or a reset. No run connects again for 24 hours; the page shows the earliest next sign-in. It does not say the password is wrong; if it recurs, read STRATO's status before rotating. |
| `Timeout` | Transient. A lasting one: STRATO's status. |
| `Protocol` | The server answered outside the protocol, or refused a command as malformed. Count the 3135–3139 events; a code change. |
| `Incomplete` | The first run did not finish. Count the 3120–3123 events in the worker log. |

**Not known, and never claimed:**
- whether a mail reached the inbox or the spam folder. The reports show authentication only; the feedback notice of 2026-10-15
  passed DKIM, SPF and DMARC and still landed in a Gmail spam folder;
- whether a report is genuine: anyone can mail `dmarc@`, so the figures are the reporters' statements;
- who operates a source address;
- whether every receiving service sends reports (only Google has so far).

After signing in, MailKit itself sends `NAMESPACE` and two `LIST` commands that read folder names only; they are on the adapter's
tested command list.

## Re-reading the price list

The terms are code defaults on `src/Jobbliggaren.Application/Admin/Email/EmailAllowanceOptions.cs`, with no appsettings entry.

1. Read <https://www.scaleway.com/en/pricing/managed-services/>, the "Transactional Email" table, for the plan in force. The page
   states prices before tax.
2. If the free tier, the price per 1 000 mails or the plan's name has changed, change the defaults in a PR, and set
   `PriceReadOn` to the day you read it.
3. Re-read the rate at the same time: the ECB's euro reference rate for SEK
   (<https://www.ecb.europa.eu/stats/policy_and_exchange_rates/euro_reference_exchange_rates/html/eurofxref-graph-sek.en.html>).
   Set `SekPerEur` and `ExchangeRateOn`, and change `VatRate` if Swedish VAT on the service changes.
4. If nothing changed, nothing is committed. The dates on the page then show how old the readings are.

**When the plan is changed in the console:** change `Plan` (and the terms) in the same PR or before it. Until then the page shows
the mismatch and computes no cost.

## The two keys

Every read on this page uses the **read key**, never the sending key. The two keys belong to two IAM applications, each
with one rule scoped to the project Jobbliggaren and nothing broader:

| Application | Permission sets | Used for |
|---|---|---|
| `Jobbliggaren`, policy `jobbliggaren-tem-send` (the sending key) | `TransactionalEmailEmailApiCreate`, `TransactionalEmailDomainReadOnly` | Sending, in the api and the worker. |
| `jobbliggaren-tem-read`, policy `jobbliggaren-tem-read` (the read key) | `TransactionalEmailEmailReadOnly`, `TransactionalEmailOfferSubscriptionReadOnly` | The allowance, `statistics` and the recent failures, in the api only. |

Never `TransactionalEmailReadOnly`, `*FullAccess`, `Webhook*` or `Blocklist*` on either. Never `EmailReadOnly` on the
sending key. The read key's only justification is the recent-failures read (ADR 0179 D1): if that read is removed, the read
key loses `TransactionalEmailEmailReadOnly` (ADR 0179 lapse trigger 1).

**`statistics` answers 200 with zeros to a key without `email:read`** (measured 2026-10-19 with the sending key). A zero
from a key that cannot read mails is not a measurement. That is why the page has a canary: if `statistics` shows no sends
at all over the last 32 days, every delivery figure is "not observable", never 0.

After any change to either application's policy, send a sign-in code to your own address and check that it arrives: the
sending application carries the sign-in codes. The dated console readings are ADR 0178 D7's and ADR 0179 D1's.

## Placing, rotating and removing the read key

The read key is a file in the api-only secrets directory, `/run/jobbliggaren/api-secrets/` on the box, which only the api
container mounts. The worker never sees it. The value passes only through your own terminal.

**Placing it** (after a release that carries #1981 PR 2 is on the box):
1. `sudo JBL_INJECT_SCALEWAY_READ=1 /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh` and paste the secret key
   at the prompt.
2. In `/opt/jobbliggaren/deploy/.env`, set `EMAIL_SCALEWAY_READ_SECRET_KEY_FILE=/run/api-secrets/Email__ScalewayRead__SecretKey`.
3. Run the script's `--check`; it must pass.
4. Recreate the api under the reconcile lock, in the form `deploy/.env.example` and `vps-deploy-stack.md` §3d give
   (`up -d --pull never api`). Never `docker restart`: a restart does not re-read `.env`.
5. Read `/admin/e-post`: the allowance and Leverans show figures, not "Läsnyckeln är inte installerad".

**The first time only (G5):** in the same sitting:
1. Remove `TransactionalEmailOfferSubscriptionReadOnly` from the policy `jobbliggaren-tem-send` on the application
   `Jobbliggaren`.
2. Read the policy back in the console: it holds exactly `TransactionalEmailEmailApiCreate` and
   `TransactionalEmailDomainReadOnly`. Write down the names only, with the date, in ADR 0179.
3. Send a sign-in code to your own address and check that it arrives.
4. Read `/admin/e-post` again: the allowance still shows figures.
5. Record the date in the processing register.

**Rotating it** (the key expires on its date in `master-key-ops.md`; nothing pages anyone before then):
1. In the console, generate a new API key for `jobbliggaren-tem-read` and delete the old one in the same visit, so the
   application has exactly one key. Note the new expiry.
2. Remove the old file, because the injection script skips a file that is already present:
   `sudo rm /run/jobbliggaren/api-secrets/Email__ScalewayRead__SecretKey`. Then place the new key as above.
3. Re-read the API reference for any read that returns message content, and re-run the schema probe in the form below
   (ADR 0179 lapse trigger 3). If a failed send exists, note its size against the cap in ADR 0179 D5.
4. Update the row in `master-key-ops.md`.

**Removing it:** delete the key (or the application) in the console, clear `EMAIL_SCALEWAY_READ_SECRET_KEY_FILE` in `.env`,
remove the file (`sudo rm /run/jobbliggaren/api-secrets/Email__ScalewayRead__SecretKey`), and recreate the api. The page then reads "Läsnyckeln är inte installerad" everywhere.

**Rollback across #1981 PR 2.** An image older than PR 2 reads the allowance with the sending key. Once the sending
application has lost `TransactionalEmailOfferSubscriptionReadOnly`, that image shows the allowance as refused (403) until
the set is given back to it.

## When a region reports a failure

| Line on the page | State | What to do |
|---|---|---|
| "Scaleway nekade läsnyckeln…" | Unauthorized (401) | The read key is refused: check its expiry in `master-key-ops.md`, then rotate it. Sending is not affected. |
| "Scaleway nekade läsningen…" | Forbidden (403) | A read permission is missing: check `jobbliggaren-tem-read`'s sets against the table above. |
| "Scaleway begränsar läsningarna…" | RateLimited (429) | Wait. Each read holds the failure for the provider's `Retry-After`, at most 15 minutes. |
| "Scaleway svarade inte…" | Timeout, Unreachable, ProviderError | Transient. A lasting one: check Scaleway's status page. |
| "Scaleways svar kunde inte läsas…" | Malformed | The API's answer changed shape, or the failures list broke its contract (a non-failed item, a wrong order, an item older than the period, more than ten, more than 64 KiB). A code change; the adapters are in `src/Jobbliggaren.Infrastructure/Admin/Email/`. |
| "Scaleways leveransstatistik visar inga utskick de senaste 32 dygnen…" | NotObservable | Either nothing was sent for 32 days, or the read key cannot see mails. Check the read key's sets; send a sign-in code to yourself and read the page again after a few minutes. |
| "Läsnyckeln är inte installerad…" | ReadKeyMissing | Place the read key (above). |
| "Räknas inte i den här miljön." | NotConfigured | Expected in dev, CI and the harness: `Email:Provider` is not Scaleway. |
| "Jobbliggarens räkning visar inga utskick under perioden, fast Scaleway har skickat…" | NotObservable (per mail kind) | Count event 3009 in the api and worker logs (above). |

The plan's own lines follow the same reasons, and say the cost is not computed.

## Re-measuring from the box

A read with a production key against the provider needs **Klas's GO for each run** (CLAUDE.md §9.2), in this form:

- The key and project id are read from their files into a curl config on tmpfs (`umask 077`), never into argv or the environment.
  A probe of the read key reads it from a root-only file under its own directory in `/run` (mode 0700), written by Klas over
  his own terminal with `read -rs` and deleted by the probe's exit trap.
- One GET per endpoint and filter, `-sS --max-time 10`, never `-v`, `--trace`, `--trace-ascii` or `set -x`. The body goes to a
  temporary file that is never printed.
- The config and the body files are deleted in the same command that wrote them.
- The output is a closed set of named lines: on a 200, the named counts, booleans and, for a schema check, the sorted top-level
  key names (whitelisted to `[a-z_]`) with the names outside the documented set; on anything else, the error's `type` and its
  `details` resource/action names, whitelisted to `[a-z_]`. Never the body, never an id, never an address.
- The GO question shows the exact command that will run, and the command actually run goes with its outcomes to
  security-auditor when they decide anything.

The deployed page performs the same reads. Klas reading it live is the measurement that needs no GO.

**Measuring how far `statistics` lags.** Read the 24-hour Skickade on the page, send a sign-in code to your own address, and
read the page again after each minute (the page holds an answer for 60 s) until the figure moves. Record the minutes, with
the date, in ADR 0179 D3. No freshness claim may be made before that.

**Closing the month's reset instant.** Paris midnight comes before UTC midnight: at 23:00 UTC in winter, at 22:00 UTC in summer.
Read the page (or, with a GO, the count) three times around a month's end: shortly before Paris midnight, between Paris midnight
and UTC midnight, and shortly after UTC midnight. The read at which the count falls back names the reset. Record it in ADR 0178,
and drop the unknown from this runbook.
