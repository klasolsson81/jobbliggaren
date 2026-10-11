# The count before a job ad is published — S2-P3 (#2090, ADR 0160 D9.1)

**What it is.** A count of personnummer-shaped tokens in the title, the description and the company name of the **listed**
job ads, taken on the production box before the public job page goes live. **Counts only** (CLAUDE.md §9.2 (a)): no id, no text and no
token is ever printed, and nothing is written down but the numbers and the date.

**The gate.** `validated_ads` / `any` must read **0**. A personnummer on a public, indexed page is Blocker-class (ADR 0160 D9.1,
dataskyddslagen 3 kap. 10 §), and a sole trader's organisation number is their personnummer (#841). More than 0 means the page does not
go live: wait for the next nightly snapshot (`sync-platsbanken-snapshot`, 02:00) and count again. The snapshot upserts every record and
`JobAd.ApplyContactRedaction` masks a personnummer in title and description each time, so no backfill is needed for a listed ad (Klas,
2026-10-11, waived S2-P2). A count that stays above 0 after a nightly run is a defect in the scrub (#2123 is the known one), and its PR
merges first.

**When.** After the first nightly snapshot that ran on a release containing #2120 (merged 2026-10-11), and **on the day of the merge**
that makes the page live. A reading is never inherited from an earlier row of this table.

## Who may run it, and how

Klas's standing rule (CLAUDE.md §9.2): an agent session may read the box over SSH when what it reads is counts. This query prints
counts grouped by a field name and nothing else. Run it so that nothing else can reach the transcript: a failed run prints **nothing**
and the session reports the failure, not an error message (a database error can echo a value).

```bash
ssh jp-vps 'sudo docker exec -i jobbliggaren-postgres psql -U postgres -d jobbliggaren -tA -F " | " -q -v ON_ERROR_STOP=1' \
  < docs/runbooks/public-job-page-publication-count.sql 2>/dev/null
```

The statement is read-only, takes a `statement_timeout` of 120 s, and reads each listed ad's three text fields once per shape.

## What it counts

- **listed** is the publication predicate's (`JobAdPublication.Classify`, ADR 0160 D2): imported from the published source
  (`external_source = 'Platsbanken'`, never the user-settable `source`), `status = 'Active'`, and `expires_at` null or `>= now()`.
- **shaped_ads**: an ad with a token of a personnummer *shape*: 10 or 12 digits, with the separators the Domain scanner accepts
  (`-`, `+`, a Unicode dash, U+2212) and the #2123 forms (spaces around the separator, `.`, `/`, `_`, split groups such as
  `81 12 18-9876` and `811218-98 76`). Fullwidth digits are folded to ASCII first, as the scanner does. The context number.
- **validated_ads**: the same, and the token passes the Domain's own test, `Personnummer.TryParse`: month 01-12, day 01-31 or 61-91
  (samordningsnummer), Luhn over the 10 significant digits. **The gate.** It is computed with string comparison and `ascii()`
  arithmetic on digits alone, so no cast can fail on a data value.
- **listed_ads_link_host_not_arbetsformedlingen**: informative. The importer picks an ad's address from three fields, so it is not always
  an Arbetsförmedlingen page; the page labels its link by host (`adSourceLink`) and says "hos Arbetsförmedlingen" only when it is one.

**What it does not see** (so a 0 is a floor of evidence, not a proof): a token split by a line break (the Domain deliberately does not
bridge one, `PersonnummerGapProfile`), a gap of three or more columns (ADR 0134 D7/R3, accepted), a token inside an image, and any
shape not listed above. The scrub and its tests are the primary control; this is the measurement of what reached the table anyway.

**Dry run.** The query was run on 2026-10-11 against an ephemeral Postgres 18 holding 20 fixture rows (valid plain, spaced, 12-digit,
dot, slash, underscore, split groups, fullwidth with U+2212, a phone number in front of a valid one; a Luhn-invalid number, an
organisation number, a 13-digit run, an archived, an expired and a non-imported ad). It counted the 17 listed rows and 10 validated
description tokens, 1 in a title and 1 in a company name (12 ads), and excluded the rest. Re-run it against such a fixture after any
change to the query.

## The query

`docs/runbooks/public-job-page-publication-count.sql`

## Record

| Date and time (UTC) | Release / sync run | listed | shaped (any) | validated (any) | title / description / company | link host not AF | Taken by |
|---|---|---|---|---|---|---|---|
| _not yet taken_ | | | | | | | |

The record is also posted on [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) with the same date.
