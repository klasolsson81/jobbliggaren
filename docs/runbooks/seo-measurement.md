# Runbook — measuring search visibility for jobbliggaren.se

**Purpose:** the one home for *what* is measured about search visibility, *who* reads it, *how often*, and *where* the result is written down.
**Rules for this file:**
- It holds commands and a dated record table. It never holds a live number outside a dated row (AGENTS.md §5, Comments).
- Procedures that already have a home are pointed to, never restated:
  - Core Web Vitals and Lighthouse: `performance-measurement.md` §F.
  - The job sitemap's crawl readings: ADR 0174 D5.
  - The personnummer gate on listed ads: `public-job-page-publication-count.md`.

## Rules that bind every row below

- **Reading the production box** follows CLAUDE.md §9.2: counts only, grouped by fields that name no person, and a failed run prints nothing.
- **Nothing here pushes a job-ad URL or ad content to a third party.** ADR 0166 D10 trigger 5 keeps the following out of this runbook until a step proposes them and Klas takes ADR 0166 again: all of the Search Console API (read-only calls included), URL Inspection by script, Bing URL Submission, IndexNow and the Indexing API.
- **No URL that carries ad data goes into a third-party tool**, such as PageSpeed Insights, an SEO crawler or a commissioned site audit. Those URLs are:
  - `/jobb/<uuid>`;
  - `/lediga-jobb` with every `?sida`, and any place page under it;
  - the job sitemap parts;
  - `/sitemap-sidor.xml`, once it lists `/lediga-jobb`.

  Sending one is trigger 5, and only Klas's re-take of ADR 0166 opens it.
- **Query-level data from any search tool leaves that tool as aggregates by page type only.** That covers Search Console, Bing Webmaster Tools (search queries and AI grounding queries) and any SERP reading. Never copy a query string into a repository file, an issue or a transcript, because a query can name a person (CLAUDE.md §9.2 (c)). The fixed query list in §2 is ours, so recording positions for it is allowed.
- **No signed-in request** to the site, ever (CLAUDE.md §9.2 (d)).

## §1 What is measured, who reads it, how often

| Signal | Who | When | How | Written to |
|---|---|---|---|---|
| Page indexing for job URLs: indexed vs not indexed, with the reasons, especially "Crawled – currently not indexed" and "Duplicate, Google chose different canonical" | Klas (Search Console UI) | weekly for the first four weeks after Google first fetches a job part, then monthly | Indexing → Pages, filtered to the sitemap `sitemap-jobb-*.xml`; counts per reason | §4 |
| Sitemaps report: discovered URLs per part, errors | Klas | as above | Indexing → Sitemaps | §4 |
| Performance by page type: clicks, impressions, CTR, average position for `/`, the marketing pages, `/jobb/` and (later) `/lediga-jobb` | Klas | monthly | Performance → Search results, page filter (regex) per type; totals only | §4 |
| Crawl stats | Klas | at the ADR 0174 D5 readings | Settings → Crawl stats | ADR 0174 D5 |
| Generative-AI visibility in Google: impressions in AI Overviews and AI Mode by page type; and the "Search generative AI features" setting, which should read Include | Klas | monthly | Search Console's generative-AI performance report; Settings → Search generative AI | §4 |
| Bing: index coverage, search performance, and AI performance where the property shows it | Klas | monthly | Bing Webmaster Tools UI | §4 |
| Real-user Core Web Vitals (CrUX, origin) | Klas, or the session he names | monthly | PageSpeed Insights web UI on `https://jobbliggaren.se/` ("Upptäck vad de faktiska användarna upplever"); record "no data" when it says so | §4 |
| Lighthouse CI (lab) | per `performance-measurement.md` §F, "Who reads the job, and when" | per `performance-measurement.md` §F | `build` workflow → job `lighthouse (observe-only)` | `performance-measurement.md` §F |
| Listed-ad supply per kommun (the input to any city-hub gate) | an agent session | when a hub step is proposed, then monthly while hubs exist | §3 | §4 |
| SERP spot checks for the fixed query list | Klas (Google, signed out, private window); Klas, or the session he names (Bing, sv-SE) | monthly | §2 | §4 (our position or "not in top 20", and the top-3 domains only) |
| Keyword volumes (optional) | Klas | when a hub pilot is chosen | Google Ads Keyword Planner, location Sweden, language Swedish | §4 (ranges per query) |

An agent session never answers a CAPTCHA or works around a rate limit. If a third-party page asks, the check is recorded as not taken.

## §2 The fixed query list

`lediga jobb` · `sök jobb` · `jobb göteborg` · `lediga jobb stockholm` · `jobb malmö` · `jobb uppsala` · `systemutvecklare göteborg` · `undersköterska jobb` · `sommarjobb` · `jobb utan erfarenhet`

Change the list only by a dated row in §4 saying why. A changed list breaks comparison with earlier rows.

## §3 Counting listed-ad supply per kommun

The query counts listed ads by taxonomy concept id. It uses the same publication predicate as `public-job-page-publication-count.sql`: imported from Platsbanken, Active, deadline empty or not passed. It prints concept ids, counts and one boolean (`s_ok`, the S test of the hub gate). It never prints an ad, a title, a company name, or a figure about any single employer. A kommun, län or yrkesgrupp gets its own row only when it has at least 25 distinct employers (the gate's E). Those below that are counted in a total row.

```bash
ssh jp-vps 'sudo docker exec -i jobbliggaren-postgres psql -U postgres -d jobbliggaren -tA -F "|" -q -v ON_ERROR_STOP=1' \
  < docs/runbooks/seo-supply-count.sql 2>/dev/null > <a file outside the repository and every worktree>
```

- Read the release first with `sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-reconcile.sh --status`, and record its sequence and verdict with the reading.
- Map the ids to names **offline**, against `src/Jobbliggaren.Infrastructure/Taxonomy/taxonomy-snapshot.json`, never against the box.
- Write the output file outside the repository and every worktree, for example into the session's scratchpad, since it is a measurement and not source. Delete it when the reading is recorded.

## §4 Record

One row per reading: date (UTC), what, the value, the release where the box was read, and who read it. Rows are never edited; a correction is a new row.

Record totals and gate outcomes only, in the form "<n> kommuner fail S". Never record a figure about a single employer, such as one kommun's largest employer's count or share. The repository is public, and its history keeps what is written here.

| Date (UTC) | Signal | Reading | Release | By |
|---|---|---|---|---|
| 2026-10-18 12:30 | Supply (§3) | listed 42 269; kommuner with ≥ 100 listed ads: 80; with ≥ 250: 35; with ≥ 500: 14; yrkesgrupper with ≥ 100: 112 | 1638, consistent | SEO/AIO audit session |
| 2026-10-18 12:32 | CrUX, origin | "Ingen data" (no field data) | – | SEO/AIO audit session |
| 2026-10-18 | Search Console, job URLs | no data yet; sitemap index resubmitted by Klas on 2026-10-18 | – | Klas |
