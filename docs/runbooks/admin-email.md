# Runbook — `/admin/e-post`: the allowance and Leverans

The page answers two questions. First, how this month's sent mail stands against the provider's free monthly allowance,
and what the mail beyond it costs (ADR 0178). Second, Leverans: how many mails the provider sent and failed per period, and
which sends failed recently (ADR 0179). This runbook is the one home for what the figures mean and how they are kept
true.

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
Scaleway took it) is in neither.

**Counted for the project, free tier per organisation.** The free tier is "300 per organization". The count is the project's.
They are the same while the organisation has one project sending through Transactional Email (measured 2026-10-19, ADR 0178).

**Not known, and never claimed on the page:**
- the instant at which the provider's month resets (UTC or Paris time);
- whether failed or cancelled sends are in the monthly count;
- how far `statistics` lags real time, or whether its window edges fall on time buckets. A 20-minute window around a
  sign-in code sent five minutes earlier read 0 on 2026-10-19 (ADR 0179 D3). The page shows when the figures were
  fetched, never that a period counts up to that moment.

The page names the month only where UTC and Stockholm agree on it; otherwise it reads "Utskick denna månad".

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
