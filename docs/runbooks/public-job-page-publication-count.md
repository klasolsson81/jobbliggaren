# The count before a job ad is published — S2-P3 (#2090, ADR 0160 D9.1)

**What it is.** A count of personnummer-shaped tokens in the four values the public job page prints for a **listed** job ad (the
title, the description, the company name and the link), taken on the production box before the page goes live. **Counts only**
(CLAUDE.md §9.2 (a)): no id, no text and no token is ever printed, and nothing is written down but the numbers and the date.

**The gate.** `validated_ads` / `any` must read **0**. A personnummer on a public, indexed page is Blocker-class (ADR 0160 D9.1,
dataskyddslagen 3 kap. 10 §), and a sole trader's organisation number is their personnummer (#841). More than 0 means the page does not
go live: wait for the next nightly snapshot (`sync-platsbanken-snapshot`, 02:00 UTC) and count again. The snapshot upserts every record and
`JobAd.ApplyContactRedaction` masks a personnummer in title and description each time, so no backfill is needed for a listed ad (Klas,
2026-10-11, waived S2-P2). A count that stays above 0 after a nightly run is a defect in the scrub, and its PR merges first. **The link is never masked at import**, so a token there stays until the ad leaves the listed set; if one is counted, that is a finding of its own,
not a reason to wait for the next run.

**The gate measures the stock, not the flow.** It reads what is in the table on the day. An ad imported after go-live with a token in a
form #2123 describes (a space around the hyphen, `.`, `/`, `_`) is published unmasked and nothing here sees it. `security-auditor` therefore
requires #2123 merged and live, and a nightly run after it, before the page goes live, and this count taken on that release (ADR 0166 D4 W3, D11).

**When.** After the first nightly snapshot that ran on a release containing #2123 and #2120, and **on the day of the merge** that makes the
page live. A reading is never inherited from an earlier row of this table.

## Who may run it, and how

Klas's standing rule (CLAUDE.md §9.2): an agent session may read the box over SSH when what it reads is counts. This query prints
counts grouped by a field name and nothing else. Run it so that nothing else can reach the transcript: a failed run prints **nothing**
and the session reports the failure, not an error message (a database error can echo a value).

```bash
ssh jp-vps 'sudo docker exec -i jobbliggaren-postgres psql -U postgres -d jobbliggaren -tA -F " | " -q -v ON_ERROR_STOP=1' \
  < docs/runbooks/public-job-page-publication-count.sql 2>/dev/null
```

The statement is read-only, takes a `statement_timeout` of 180 s, and reads each listed ad's four fields once per shape.

## What it counts

- **listed** is the publication predicate's (`JobAdPublication.Classify`, ADR 0160 D2): imported from the published source
  (`external_source = 'Platsbanken'`, never the user-settable `source`), `status = 'Active'`, and `expires_at` null or `>= now()`.
  A reading with `listed_ads` near 0 is not a reading: it means the table is not the one that was meant.
- **shaped_ads**: an ad with a token of a personnummer *shape*: a run of digits, with the separators the Domain scanner accepts
  (`-`, `+`, a Unicode dash, U+2212, U+FF0D) and the #2123 forms (up to three space or zero-width characters around the separator, up to two separators in a row as in
  `811218 -- 9876` (#2137), `.`, `/`, `_`, split groups such as `81 12 18-9876` and `811218-98 76`). Fullwidth digits are folded to ASCII first, as the scanner does. The context number.
- **validated_ads**: the same, and the token passes the Domain's own test, `Personnummer.TryParse`: month 01-12, day 01-31 or 61-91
  (samordningsnummer), Luhn over the 10 significant digits. **The gate.** It is computed with string comparison and `ascii()`
  arithmetic on digits alone, so no cast can fail on a data value.
- **listed_ads_link_host_not_arbetsformedlingen**: informative. The importer picks an ad's address from three fields, so it is not always
  an Arbetsförmedlingen page; the page labels its link by host (`adSourceLink`) and says "hos Arbetsförmedlingen" only when it is one.

**What it does not see** (so a 0 is a floor of evidence, not a proof): a token split by a line break (the Domain deliberately does not
bridge one, `PersonnummerGapProfile`), a gap wider than three of the listed space and zero-width characters (ADR 0134 D7/R3, accepted), a
character class outside those listed, a token inside an image, and any shape not listed above. Three spellings the scanner's own pattern masks are measured
not to be counted (dry run 2026-10-12): a separator, a space and a separator again (`811218 - -9876`, `811218-- --9876`), and a
zero-width character between two separators (`811218-<ZWSP>-9876`); the query's separator run cannot straddle a gap. The scrub
and its tests are the primary control; this is the measurement of what reached the table anyway.

**Dry run.** The query was last run on 2026-10-12 against an ephemeral Postgres 18.6. The pre-change query first, on the 26-row fixture
of 2026-10-11 (valid plain, spaced, 12-digit, dot, slash, underscore, split groups, fullwidth with U+2212, thin space, fullwidth hyphen,
zero-width and narrow no-break-space around the hyphen, a phone number in front of a valid one, a token in the link; a Luhn-invalid
number, an organisation number, a 13-digit run, an archived, an expired and a non-imported ad): it counted the 23 listed rows and 14
validated description tokens, 1 in a title, 1 in a company name and 2 in a link (18 ads, 20 shaped), and excluded the rest, as on
2026-10-11. The widened query counted the same on the same 26 rows. Then one row per spelling, pre-change query against widened:
`811218--9876`, `811218 -- 9876`, `811218 --9876`, `811218-- 9876`, `811218.-9876`, `811218-/9876`, `19811218 -- 9876`,
`811218<ZWSP>--<ZWSP>9876` and an en dash followed by U+2212 each read 0 shaped and 0 validated before and 1 and 1 after;
`811218--9877` (Luhn-invalid) read 0 and 0 before and 1 shaped and 0 validated after; `811218---9876`, `811218 --- 9876` and
`811218 - - 9876` (three separators, or three visible columns, which the mask does not bridge either) read 0 and 0 in both.
Re-run it against such a fixture after any change to the query.

## The query

`docs/runbooks/public-job-page-publication-count.sql`

## Record

| Date and time (UTC) | Release / sync run | listed | shaped (any) | validated (any) | title / description / company / url | link host not AF | Taken by |
|---|---|---|---|---|---|---|---|
| 2026-10-12 03:54-03:55 | Release 1627 (`e41eac850`, holds #2137) applied 01:49:35Z; the 02:00 snapshot ran on it, evidenced by `job_ads` autovacuum 02:05:51Z and the miss-tracking write 02:05:51Z (the worker was recreated 03:49Z by 1628, so its log is gone). Query `23cc0a4b9` (blob `297bfe6`) | 39 390 | 177 | **0** | shaped 0 / 177 / 0 / 0; validated 0 / 0 / 0 / 0 | 0 | Claude Code |
| 2026-10-12 04:27 | Release 1628 (`9ca862013`, holds #2137), `--status` consistent before (04:27:09Z) and after (04:27:31Z); the 02:00 snapshot ran on 1627 (evidence above). Query blob `8c02e37` (comment-only change from `297bfe6`: the length claim on shaped_ads removed) | 39 409 | 177 | **0** | shaped 177 in the description, no row printed for the others; validated: no row printed (0) | 0 | Claude Code |

The record is also posted on [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) with the same date.

## After the merge — what is read, and what is not

The page is live for everyone, and every successful view of it is a `job_ads` row. A check that prints a title, an `og:` value or an `<h1>` is
a read of §9.2 (b) data, so the live check prints **none** of them.

1. Three pipelines, each printing only what the step allows (the canonical href holds only the uuid; titles, descriptions and company names are
   counted with `grep -c`, never printed):
   - `curl -s -o /dev/null -D - https://jobbliggaren.se/jobb/<uuid> | grep -iE '^(HTTP/|cache-control|x-robots-tag)'`
   - `curl -s https://jobbliggaren.se/jobb/<uuid> | grep -o 'rel="canonical" href="[^"]*"'`
   - `curl -s https://jobbliggaren.se/jobb/<uuid> | grep -c '<h1'`
2. **The uuid is chosen by Klas** (copied from his own signed-in view, an ad he picks) or comes from a query that returns an id and nothing else.
   An agent never lists ads to find one.
3. A random uuid must answer **404**, and `/robots.txt` must show the two training crawlers blocked and `/jobb/` not disallowed.
4. **URL Inspection (the live test) is Klas's, in his own browser**, never an agent's, and never with an account signed in on the box (CLAUDE.md §9.2 (d)).
