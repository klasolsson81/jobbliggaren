# ADR 0177 — Search Console is read weekly by one workflow on `main` through Workload Identity Federation as a Restricted service account: a permission check, five dimensionless queries, two sitemap lists, and the job summary only

**Date:** 2026-10-18
**Status:** Accepted 2026-10-18 (the CTO's rulings 4–10, with `security-auditor` co-deciding ruling 6, and Klas's three answers of the same day, which the ADR 0166 amendment records). The
ADR 0166 amendment of the same date, which carries the legal outcome, is Proposed until `security-auditor` signs it on PR B's final head. The merge waits for that signature.
**Deciders:** Klas Olsson (controller and product owner; three answers in chat on 2026-10-18, recorded in the ADR 0166 amendment) · `senior-cto-advisor`, "the CTO" below (decision-maker under
CLAUDE.md §9.2; report-only ruling on rulings 1–10, `docs/reviews/2026-10-18-seo-measurement-automation-cto.md`, local) · `security-auditor` (plan read, report-only,
`docs/reviews/2026-10-18-seo-measurement-automation-security-auditor.md`, local: her verdict, M1, m1–m3 and the escalation; she co-decides ruling 6, and where she differs from the CTO her position stands)
**Amends:** ADR 0166 (the trigger-5 re-take for this read path, by a dated amendment at the end of that file). Nothing above that amendment is rewritten.
**Related:** [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the epic) · [#2188](https://github.com/klasolsson81/jobbliggaren/issues/2188) (PR B, the issue this ADR ships under) ·
ADR 0174 (D5: the Search Console readings made by hand) · ADR 0072 (D2; local) · `docs/runbooks/seo-measurement.md` (what is measured, who reads it, and where it is written down) ·
`docs/runbooks/search-console-read.md` (setup, reading and revocation) · #1239 (the `github/codeql-action` exemption from Dependabot auto-merge, Klas 2026-08-08, the form D4 copies) ·
CLAUDE.md §9.2 (a)–(f) and §9.6
**Measured against:** the branch `ci/seo-search-console-read` in `C:/tmp/jbl-seo-gsc-read`, which contains S5's merge `182e002ff`. The workflow, the script, its suite, its samples, the Dependabot
arm and the runbook were read in that worktree on 2026-10-18.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1, D2 and D6 follow the CTO's rulings 4, 5, 7, 8 and 10. D3 and
> D4 follow ruling 6 as `security-auditor` co-decided it, and where she differs from the CTO her position stands. D5 is her determination. D7 records two places where the delivered form departs from
> a ruling. The two reports are local files, transcribed verbatim by the driving session from the agents' hand-backs. Facts about the code were read from the worktree on 2026-10-18; **no test
> result is recorded here**, and none was run for this ADR. The mandatory agents review the delivered diff in the PR. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README
> row (precedent 0167–0176).
>
> **Numbering.** The CTO's ruling 9 named 0177 as the next free number "at the time of writing", with the caveat that another lane might take it first. It was measured free on 2026-10-18 in this
> worktree, on the local `origin/main` ref, and in every open PR (`gh pr list`).

---

## Context

PR B of the SEO measurement automation (#2090, issue #2188) lets a scheduled job read Search Console instead of Klas reading it by hand. Nine facts shape the decision.

- **The aim, and the rule that bounds it.** Klas wanted as much of the measurement as possible automated ("så mycket som möjligt automatiserat", as the brief records it). The CTO's rule is
  narrower than the aim: automate what is deterministic, aggregate and push-free (ruling 10).
- **What the API has and lacks.** It has no method for the Page indexing report, so the phase-2 gate's four weekly readings stay Klas's in the Search Console UI (as the CTO's question to Klas states
  it; not re-measured here). It does have the month's Performance results per page type and the sitemaps' status, and both are aggregates that `seo-measurement.md` §4 already publishes by page type,
  under its own rule.
- **What the permissions allow, read from Google's pages on 2026-10-18.**
  - `urlInspection.index.inspect` accepts either `webmasters` or `webmasters.readonly` (last updated 2024-07-23).
  - `searchAnalytics.query` (last updated 2026-08-11): "If no dimensions are specified, all values will be combined into a single row"; and "If you filter or group by page, choose auto".
  - `sites.get` accepts `webmasters.readonly` and returns a `permissionLevel` that is one of `siteOwner`, `siteFullUser`, `siteRestrictedUser` and `siteUnverifiedUser` (last updated 2024-07-23).
  - The usage-limits page lists a per-project URL Inspection quota (last updated 2025-08-28). Whether a project can lower it is not stated and was not measured.
- **What the repository does today, measured by `security-auditor` on 2026-10-18.** `main` requires 0 approving reviews and no code-owner review (`enforce_admins: true`). Dependabot's patch and minor
  bumps of `github_actions` auto-merge on green `ci` with no agent and no human reading them. The auth action's defaults are `create_credentials_file: true` and `export_environment_variables: true`,
  and its WIF credentials file embeds the job's OIDC request token in a header (the action's source, branch `main`).
- **Trigger 5.** ADR 0166 D10 trigger 5 is "Any push of a posting URL or its content to a third party". The amendment of 2026-10-18 for S4 kept "the Search Console API" inside it, and the CTO's
  stage-B ruling B11 (local) says the API "stays inside trigger 5". A weekly read through the API is therefore Klas's to re-take, with `security-auditor`'s signature, and the ADR 0166 amendment of the
  same date is where he does.
- **The undocumented premise.** Google documents no service account as a Search Console user. Search Console's help (page 7687615, no date shown) says only that users need "a valid Google Account".
  Whether Search Console accepts the account is measured by Klas's setup (D3, item 10).
- **A job summary is not private.** GitHub shows it to any signed-in GitHub user (measured 2026-10-18). The CTO admitted that because the summary carries only what §4 already publishes (D1).
- **A summary is not a record.** `seo-measurement.md` §4 is the append-only record. A summary expires with the run's retention.
- **No agent may hold the credential or call the API,** whether from chat or from a cloud `/schedule`. The response would enter a transcript (ruling 10, CLAUDE.md §9.2 (f)). Sessions read only the
  summary.

## Decision

**One workflow, `.github/workflows/search-console-read.yml`, runs on `main` every Wednesday at 06:13 UTC and on `workflow_dispatch`. It logs in to Google through Workload Identity Federation as one
service account that holds no Google Cloud role and no key and is a Restricted user of `sc-domain:jobbliggaren.se`. It makes eight fixed calls: one permission check, five `searchAnalytics.query`
requests that carry no `dimensions`, and two `sitemaps.list` requests. It accepts a response only in the shapes it knows, and writes the named fields to its job summary and nowhere else. Every change
to the workflow, the script, its suite or its samples, and every bump of an action the job runs, is read by `security-auditor` before merge, and Dependabot does not auto-merge the auth action. The
legal outcome and the lapse triggers 13 and 14 have one home: the ADR 0166 amendment of the same date.**

**Scope.** This ADR covers the workflow, the script, its suite and samples, the Dependabot arm, the runbook and the records. URL Inspection sampling, the CrUX and Bing APIs, and a read of the Page
indexing report are out (D6). Each of them would be a new trust boundary, with a re-take of its own.

### D1 — Where the aggregates land: the run's job summary, and nowhere else (CTO ruling 4)

- **The job summary of the workflow run.** No artifact, no raw response anywhere, and no log line that carries a response body.
- **Who can read it.** GitHub shows a job summary to any signed-in GitHub user (measured 2026-10-18), and it expires with the run's retention. That is admissible. It carries what `seo-measurement.md` §4
  already publishes under its own rule, which is aggregates by page type, accepted by Klas with "Lokal + publik runbook". Our own traffic totals are not the competitive analysis that ADR 0072 D2
  (local) keeps hidden.
- **One outcome annotation, beside the summary, carrying no aggregate.** A run that is accepted writes `::notice::` with `accepted, permission siteRestrictedUser, window <start>..<end>`. A run that
  is refused writes `::error::` with `refused: <request> <class>`, and one line with the same two fields in the summary. Neither carries a value from a response: the level is printed as the constant
  it matched, the window is computed from the run date, the request name is a constant at its call site, and the class comes from the closed set in D2. The annotation exists so that a session can
  read the outcome without a GitHub sign-in. The aggregates stay in the summary, which a session does not need. GitHub shows a run's annotations, but not its summary, to a signed-out viewer
  (measured 2026-10-18 on #2184's run, in a browser with no GitHub login).
- **§4 is still written by hand,** from the summary, by Klas or the session he names. The summary is the instrument's output and the record is §4.

### D2 — The read path: eight fixed calls and one fail-closed parser (CTO ruling 5; `security-auditor` §7)

- **The calls, in order.** (1) `sites.get`, the permission check. (2–6) `searchAnalytics.query` for `home`, `job`, `catalogue`, `other` and `site`. (7–8) `sitemaps.list`, once for the property and
  once with `sitemapIndex` set to the fixed index address. The endpoints, methods and bodies are pinned in the ADR 0166 amendment, point 1, and byte for byte by the golden files
  `.github/scripts/samples/search-console/request-*.json`.
- **The partition.** Four page types and the site total. A partition maintains itself when a marketing page is added, and a list of paths would not.
  - `home`: `includingRegex` `^https://jobbliggaren\.se/$`.
  - `job`: `includingRegex` `^https://jobbliggaren\.se/jobb/`.
  - `catalogue`: `includingRegex` `^https://jobbliggaren\.se/lediga-jobb(?:[/?]|$)`. It reads zero until S5 is live.
  - `other`, meaning the marketing pages and every other page on the apex: `includingRegex` `^https://jobbliggaren\.se/.` together with `excludingRegex`
    `^https://jobbliggaren\.se/(?:jobb/|lediga-jobb(?:[/?]|$))`, in one `and` group.
  - `site`: no filter, `aggregationType` `byProperty`. That is the accurate count Google names.
- **Fixed fields, and no `dimensions`.** `type` is `"web"` and `dataState` is `"final"`. `aggregationType` is `"auto"` wherever a page filter is present, because Google forbids `byProperty` with one,
  and `"byProperty"` for the site total. No body carries `dimensions`: Google then combines everything into one row that has no keys, so by construction no query string and no page value can come back.
  That is stricter than `["date"]`, which still returns keyed rows.
- **The window.** The latest calendar month whose last day is at least four days before the run (UTC). Every run within a month prints the same final numbers, so no reader picks a run.
  `SEARCH_CONSOLE_RUN_DATE` exists for the fixture suite, and the suite pins that the workflow never sets it.
- **The cadence.** Weekly, Wednesday 06:13 UTC, a minute no other cron here uses. It matches the Sitemaps row of `seo-measurement.md` and gives redundancy against a dropped schedule.
- **What is printed.** The window and the permission level. For each type, clicks, impressions, CTR, position and `responseAggregationType`. For each sitemap file, its path without the host, its last
  download as a date only, `isPending`, `errors`, `warnings` and `submitted` for `contents[type=web]`. Never `indexed`, which is deprecated. The page types are aggregated by page, as in the UI, so they
  do not sum to the site total, and the summary says so once.
- **What a response must be to be accepted.** The set is closed, and a refusal names one class from it: `http <code>`, `permission`, `shape`, `keys`, `rows` or `path`.
  - Any HTTP status other than 200, and any transport failure, is `http <code>` (`000` for a transport failure).
  - `sites.get`: `permissionLevel` must be the string `siteRestrictedUser`. Any other string is `permission`, and the level is not printed. A level that is absent or not a string is `shape`.
  - A query: `rows` absent (no data, printed as dashes), or exactly one row that is an object whose `keys` is absent or an empty array and whose `clicks`, `impressions`, `ctr` and `position` are numbers.
    `responseAggregationType`, where present, must be `auto`, `byPage` or `byProperty`. Two or more rows is `rows`. A `keys` that is not an empty array is `keys`. Anything else is `shape`, an empty
    `rows` array included: only an absent `rows` means no data.
  - Sitemaps: `sitemap` absent (none listed), or an array of objects, each with a `path` that is a string. A `path` that does not match `^https://jobbliggaren\.se/sitemap[-a-z0-9]*\.xml$` is `path`,
    and that is judged before any other field is read. The other fields are type-checked: dates begin `YYYY-MM-DD`, `isPending` is a boolean, and the counts are non-negative whole numbers. Google types
    the counts only as `long`, so a number and a string of digits are both accepted. Anything else is `shape`.
  - The closed set of classes is what makes ADR 0166 trigger 14 observable. A refusal that named only a status could not tell a row carrying `keys` from a malformed body.
- **The mechanics.**
  - jq's stderr is discarded, at the script's one jq call site, because its diagnostics can echo input values (`security-auditor` m3).
  - Responses live only under `$RUNNER_TEMP`, are removed by `trap` and are never uploaded.
  - A late refusal leaves no aggregate and only its own line.
  - The token reaches curl on stdin as a config line and never in argv. There is no `-v`, no `--trace` and no `--fail-with-body`, no `set -x` and no `::debug::`.
  - `workflow_dispatch` takes no inputs.
- **Fixtures** (AGENTS.md §5, Tests). The success shapes follow Google's reference pages, which the suite's header names with their last-updated dates. The `*-hostile-*` samples are declared
  unreachable under the fixed bodies, and their cases assert only that the refusal names its class and that nothing of the response is printed.

### D3 — Auth: Workload Identity Federation through one service account, and no key (CTO ruling 6, co-decided; `security-auditor` items 1–8, 10 and 11, m1 and m2)

The item numbers are hers. Her item 9 is D4.

- **WIF through a service account (item 1).** A JSON key is rejected, and so is a hand-rolled STS exchange (*Alternatives considered*).
- **A dedicated Google Cloud project (item 2),** holding nothing else, with no billing account. The Search Console API is enabled, with only what impersonation needs (IAM Service Account Credentials
  and STS) and what Google's WIF page requires for creating the pool (D7, (ii)). The ids are neutral, because they are public. This does not stop URL Inspection, which is in the same API. It isolates
  the credential and makes "no role" measurable.
- **The provider (item 3).** The pool is `github-actions` and the provider is `search-console-read`. The attribute condition is stricter than the CTO's, and the workflow file's path was fixed before
  Klas created the provider:

  ```
  assertion.repository_owner_id == '229617477' && assertion.repository_id == '1214215182' && assertion.ref == 'refs/heads/main' && assertion.workflow_ref == 'klasolsson81/jobbliggaren/.github/workflows/search-console-read.yml@refs/heads/main' && assertion.event_name in ['schedule', 'workflow_dispatch'] && assertion.runner_environment == 'github-hosted'
  ```

  The numeric ids are used and not the names, because names can be re-registered. The attribute mapping carries `google.subject` and exactly the six claims the condition reads, and never `actor`.
  Google's WIF page (last updated 2026-10-13) says every field the condition reads must be mapped.
- **IAM (item 4).** The service account `sc-read` holds no project role and has no key. "No key" is a setup record and not an enforced property, because no organization policy forbids creating one
  later. Its only binding is `roles/iam.workloadIdentityUser`, granted to the `principalSet` keyed on `attribute.repository_id`, never pool-wide `/*`.
- **Search Console (item 5).** The account is a **Restricted** user of `sc-domain:jobbliggaren.se`, never Full and never Owner. **Every run starts with `sites.get`** and proceeds only on
  `permissionLevel == "siteRestrictedUser"`, and prints the level. With `webmasters.readonly` the scope is chosen by our code, and Restricted is the only limit Google enforces, so a yes or no from
  setup does not measure it.
- **The workflow (item 6).**
  - Triggers are `schedule` and `workflow_dispatch`, with no inputs. `permissions: {}` is at the top.
  - There is one job, on a GitHub-hosted runner, with `id-token: write` only and `timeout-minutes: 10`.
  - Its steps are exactly a credential-less `git` fetch of `$GITHUB_SHA`, the auth action (pinned by full SHA) and the script. No artifact, no cache, no other action and no
    `continue-on-error`.
- **The auth inputs (item 7).** `token_format: access_token`; `access_token_scopes: https://www.googleapis.com/auth/webmasters.readonly`; `access_token_lifetime: 300s`;
  **`create_credentials_file: false` and `export_environment_variables: false`** (m1). The action's defaults write a credentials file that embeds the OIDC request token in a header, and export
  `GOOGLE_APPLICATION_CREDENTIALS` to every later step. The token reaches the script through the step's `env:` only, never through `${{ }}` inside `run:`.
- **300 s is the service-account token's lifetime and nothing more (m2).** The federated token the action also holds has Google's own lifetime, and while the job runs any step can mint again,
  because the OIDC request URL and bearer are set for the job and not for one step. **No ADR, runbook or summary text gives five minutes as
  the exposure.**
- **The action (the CTO).** `google-github-actions/auth` v3.0.0, pinned at `7c6bc770dae815cd3e89ee6cdf493a5fab2cc093` (the latest release, 2025-08-28, measured 2026-10-18). It is acceptable as a
  dependency: it is Google's own token exchange, published under the `google-github-actions` organisation, it is the path Google Cloud's WIF page names, and it masks the token. "Not an officially
  supported Google product" is a statement about support and not a defect. BUILD.md §3.1 asks for a justification, and this is one.
- **The identifiers (item 8).** The four non-secret identifiers, which are the project number, the pool, the provider and the service-account address, live in the workflow file and not in a
  repository variable, because a variable can change without a diff. They are not secrets. The security rests on the attribute condition.
- **If Search Console refuses the service account as a user (item 10): STOPP** back to `senior-cto-advisor` and `security-auditor`. Never a fallback to an OAuth refresh token, which would be Klas's
  Owner account and can remove URLs, and never to a key.
- **The optional quota (item 11).** At setup, if the project's Quotas page lets Klas lower the URL Inspection quota to 0, he sets it and the yes or no is recorded. Nothing may call the quota a
  barrier unless that reading is a yes.

### D4 — The barrier is ours: a lapse, a review and an exemption from auto-merge (`security-auditor` §4 item 9, §5 and M1; Klas's answer 3)

- **Why the CTO's barrier was not enough as written.** The CTO named the reviewed code of one workflow plus the `workflow_ref` binding. `security-auditor` measured four reasons it falls short:
  `workflow_ref` binds the workflow file and not the script and samples it executes; every step in the job can mint the token; the binding's Google-side parts change without a diff; and a Dependabot
  bump of the auth action merges with no reader (M1).
- **Two mechanisms, because one would fire on every pin bump.** Read literally, "a change to that file is a D10 trigger" would put Klas's re-take in front of every bump of a pin.
  - **Lapse.** ADR 0166 D10 triggers 13 and 14, read by Klas. They are the single home of the list, and this ADR does not restate them.
  - **Review.** `security-auditor` reads, before merge, any change to the file set (the workflow, the script, its suite and its samples) and any bump of an action the job runs.
- **The exemption from auto-merge (M1; Klas's answer 3, "Ja (Rek.)").** `google-github-actions/auth` is exempted from Dependabot auto-merge by name in `dependabot-automerge.yml`, in the form
  `github/codeql-action` has had since #1239. A bump then becomes an ordinary PR that `security-auditor` reads. The cost is that it is no longer merged by itself. Without the exemption she could not
  sign, because the amendment would claim a review that does not happen (Art. 5(2)).
- **The header pointer.** The workflow, the script and the suite each carry a `GOVERNANCE` header that points to this review rule and to the ADR 0166 amendment (the workflow and the script name
  triggers 13 and 14), so the trigger sits where the change is made.
- **The allow-list test (her §5).** The suite asserts that the complete set of `https://` literals in the workflow and the script equals an allow-list, that every method is a literal `GET` or
  `POST`, that there is one curl call site and one jq call site, that the jq call discards stderr, that no verbose or trace flag and no `::debug::` is present, and the workflow's form (D3, item 6).
  It is an allow-list of form over two files and not a deny-list of spellings. A new endpoint fails it until the list changes in the same diff, which the review rule then puts in front of her.
- **CODEOWNERS is not a gate.** It gates nothing: 0 approvals and no code-owner review were measured on 2026-10-18, and the file is `* @klasolsson81`. It is not used.
- **Revocation** is in `docs/runbooks/search-console-read.md` §4.
- **The residuals** are written in the ADR 0166 amendment, point 4, and not here.

### D5 — The data-protection determination: nothing personal goes in either direction (`security-auditor` §2 and §3)

The determination rests on one invariant: **no dimension, and no page or query value, on the path.** This ADR is its single home, and the ADR 0166 amendment points here.

- **Outbound.** The property id, the filter of each page-type request, a date window, and the index address. In addition, the request's source IP, which is a GitHub runner's, and the OIDC token's
  claims, which name the personal account that initiated the run (`actor`, `actor_id`). Today that account is Klas's, which CLAUDE.md §9.2 (c) admits. It stays that way only while no one but Klas's
  account dispatches or edits the workflow. None of this is data about a user, a searcher or a recruiter.
- **Inbound.** Per request, one row with `clicks`, `impressions`, `ctr` and `position`, no `keys`, and `responseAggregationType`; and sitemap entries, which are our own paths, dates, booleans and
  counts. The aggregates are anonymous (Recital 26). They are monthly totals per page type with no query, page, date, country or device, so no searcher can be singled out by any means reasonably
  likely to be used, and that holds even for counts of 1. The per-part `submitted` counts add nothing to the public sitemap, so they add no W5 oracle.
- **Chapter V, recipients and the register.** No transfer: Chapter V needs a data subject's personal data, and none leaves or arrives. The GitHub runner (US) and Google (US) see only the aggregates
  and Klas's own identifiers. No Art. 28 relationship: neither party processes personal data for us here, and Search Console's statistics are Google's own processing of its searchers, so we receive
  anonymous output. No Art. 30 row, because there is no processing. ADR 0166 D7, the recruiter notice, the privacy policy and the register are unchanged.
- **The policy's own sentences stay true only while the invariant holds.** `web/jobbliggaren-web/messages/sv/content-legal.json:113` says "Loggar du inte in med Google eller GitHub sker inga
  överföringar av dina personuppgifter till länder utanför EU/EES", and `:382` says "inga verktyg som Google Analytics". A widening would put searcher data on a US runner and into a log that any
  signed-in GitHub user can read. ADR 0166 trigger 13 is what fires before that.
- **CLAUDE.md §9.2 (c) and (f) are respected** by ruling 10 together with D1. (f) is not engaged, because no agent processes user data.
- **No agent session opens the Search Console or Google Cloud Console UI,** through the browser pane, Claude in Chrome or Codex. The UI shows query rows and the users list, so it is the same
  channel as the API. A session may dispatch the workflow and read its summary and logs, and no response body is written anywhere it could read.

### D6 — What stays manual, what is never automated, and what is out for now (CTO rulings 7, 8 and 10)

- **The rule.** Automate what is deterministic, aggregate and push-free. "As much as possible" is not a scope rule (ruling 10).
- **Manual, because no read API meets the rule.**
  - **Page indexing** is the phase-2 gate, and its weekly readings stay Klas's, in the Search Console UI.
  - **Crawl stats** (ADR 0174 D5) and **the generative-AI report.**
  - **Bing** stays Klas's monthly reading in the UI (ruling 8). Its API key travels in the query string and is account-wide: the same key that reads `GetCrawlStats` can also submit URLs and read
    `GetPageStats` and `GetQueryStats`, which makes one credential both a push channel and a CLAUDE.md §9.2 (c) channel. A read-only OAuth scope exists but needs a long-lived refresh token.
- **Out for now.**
  - **URL Inspection sampling** is not proposed (ruling 7). B14 fixed the gate in advance on the Page indexing report's counts, filtered to the job sitemap, and swapping in a sampled instrument is the
    re-litigation that fixing it in advance was meant to prevent. A sample cannot decide "the largest single status" near a tie; `coverageState` is a free string and not the report's taxonomy of
    reasons; `richResultsResult` carries the ad's own text back into our process; and the call sends posting URLs, which is trigger 5 by S4's signed criterion. It would save four weekly readings.
  - **CrUX** (ruling 8): the stage-B B11 stands. No key until CrUX shows any data, and the reading of 2026-10-18 was "Ingen data". Once it shows data it is the cheapest automation of the set, with the origin
    only, no ad URL and no trigger 5, and it gets its own PR under `security-auditor`'s 6(d).
- **Never.** Automated SERP checks: Google's spam policy (2026-08-28) names scraping results for rank checks as machine-generated traffic, so `seo-measurement.md` §1's SERP row stays manual. The
  push and write channels, and the other credentials and callers, stay inside trigger 5 as the ADR 0166 amendment, point 3, lists them.

### D7 — Two deviations from the rulings, recorded so that the panel sees them

- **(i) The first real response does not join the fixtures.** The CTO's ruling 5 said it would. It does not, because a response body never leaves the runner (`security-auditor` §2 and §7), so no
  real response exists in any place from which it could be copied. The success fixtures follow Google's reference pages instead, as the suite's header states. The first run's accepted outcome is the
  production evidence (ADR 0166 amendment, point 8). A real response whose shape differs from the reference fails closed as `shape`, and is never accepted silently.
- **(ii) The runbook enables two APIs `security-auditor` did not name.** Her item 2 named the Search Console API, STS and IAM Service Account Credentials. The runbook also enables
  `iam.googleapis.com` and `cloudresourcemanager.googleapis.com`, because Google's WIF page (last updated 2026-10-13) requires them to create the pool. This is for her review.

## What is not measured, and the measurements owed

**Not measured by anyone, and not claimed:**
- Whether Search Console accepts a service account as a user. Google documents none. Klas's setup step 9 measures it, and a refusal is a STOPP (D3, item 10).
- The Restricted level of the account. It is measured by the first run's `sites.get`, never inferred from the setup record.
- Whether the project's URL Inspection quota can be lowered. Google's limits page (2025-08-28) does not say. Setup step 10 reads it.
- Whether Google's real responses match the shapes D2 accepts. The first run answers it (D7, (i)).

**Owed:** the first run's reading, which is appended to the ADR 0166 amendment under its own rule, with its date.

## Alternatives considered

### Alt A — The chosen design (D1–D6)
**For:** no key exists that can leak or needs rotating; one workflow on one branch is the only caller; what Google's permission model does not hold, the code, the attribute condition and a reader
for every change hold; the request bodies carry no `dimensions`, so no query or page value can come back; the output has one home that carries no personal data; and a refusal names its class, which
makes the lapse observable. **Against:** a one-time Google Cloud setup for Klas, one more third-party action, and a barrier that is ours and not Google's (*Consequences*).

### Rejected, in one line each
- **A JSON key in a GitHub secret** (ruling 6). It is a bearer secret that by default never expires, any workflow that can read secrets can use it, and it carries a rotation duty that nobody
  performs. That fails least privilege (Saltzer and Schroeder 1975) and goes against the guidance of both Google and the action.
- **A hand-rolled STS and IAM Credentials exchange** (ruling 6). It re-implements a security protocol in bash to save one pinned dependency.
- **An OAuth refresh token for Klas's Owner account** (ruling 6). It can remove URLs and is a push channel.
- **A repository variable for the identifiers** (`security-auditor` item 8). A variable can change without a diff.
- **An encrypted artifact** (ruling 4). It protects nothing that §4 does not already publish, and it adds key management.
- **Mail** (ruling 4). A new processing path and a sender, for an internal metric.
- **A bot commit** (ruling 4). The protected `main` needs a PR, and a PR made with `GITHUB_TOKEN` starts no `ci`.
- **An issue used as a log** (ruling 4). It would be a second home for §4's record, and the backlog is issues and nothing else.
- **`["date"]` as a dimension** (ruling 5). It still returns keyed rows, so it is weaker than no dimension.
- **A list of paths instead of a partition** (ruling 5). It does not maintain itself when a marketing page is added.

## Consequences

### Positive
- No key exists, so none can leak or fall due for rotation. Google issues short-lived tokens to one job, on one branch, from one workflow file.
- No agent ever holds the credential.
- Each run measures the Restricted level with `sites.get` and stops on anything else.
- The month's page-type numbers and the sitemaps' status reach a summary every week with no human or agent holding a credential.
- A refusal names its class and never a value, so the lapse in trigger 14 can be observed from a run.

### Negative and accepted trade-offs (the CTO's list unless marked)
- **PR B costs Klas a one-time Google Cloud setup and adds one third-party action.**
- **The credential can technically inspect a URL, and read query- and page-level data.** The barrier is the code, the workflow binding and a reader for every change, and not Google's permission
  model. The residuals are in the ADR 0166 amendment, point 4.
- **A bump of the auth action is no longer merged by itself** (`security-auditor` M1).
- **The gate's weekly reading stays manual.** The automation does not reach the one reading phase 2 waits on.
- **§4 is still written by hand.**
- **The page-type numbers are aggregated by page, as in the UI,** so they do not sum to the site total.
- **A month's reading arrives about four days after the month ends.**
- **"No key" is a setup record and not an enforced property** (D3, item 4), and the optional quota is no barrier unless the setup reads yes.
- **The summary is visible to any signed-in GitHub user** (D1), and it expires with the run's retention.
- **Nothing detects a lapse automatically.** Triggers 13 and 14 are read by Klas when a change is proposed or a run is reported.

## Implementation

One PR on `ci/seo-search-console-read`, under #2188. **This section states the plan; the PR's diff and CI are the record.** It adds no application code, migration, package, `deploy/` diff or ACL
change.

- **The file set the review rule covers (D4):** `.github/workflows/search-console-read.yml`, `.github/scripts/search-console-read.sh`, `.github/scripts/search-console-read.test.sh` and
  `.github/scripts/samples/search-console/` (the golden request bodies and the response samples). `.gitattributes` pins the samples' line endings to LF, so the byte-for-byte comparison also holds
  on a Windows checkout.
- **Also in the PR:** the `google-github-actions/auth` arm in `dependabot-automerge.yml`; `docs/runbooks/search-console-read.md`; `docs/runbooks/seo-measurement.md` (its rules block points to the
  amendment, and the "How" cells of the Performance and Sitemaps rows point to the summary); this ADR; the ADR 0166 amendment; and the README row. The ADR is promoted with `git add -f`.
- **The suite runs in `build.yml`'s `scripts` job** (`security-auditor` §5), as a step beside the other `.github/scripts` suites.
- **The identifiers.** Klas's setup
  returns the four identifiers (runbook §1, steps 11 and 12), and they go into the workflow file.
- **The panel** (CTO ruling 9): `security-auditor` (an external integration, a credential, and her signature), `code-reviewer`, and `dotnet-architect` (more than five files, and an ADR).

**The decision chain (the CTO), and where it stands on 2026-10-18:**
1. `senior-cto-advisor`: the ruling. Done.
2. `security-auditor`: the plan read of rulings 4–7 and 9. Done.
3. Klas: the three questions, relayed verbatim and recorded in the ADR 0166 amendment. Done.
4. Klas: the Google Cloud setup, and adding the service account as Restricted. The session records only yes or no for each step (runbook §1, step 12). A refusal at step 9 is a STOPP back to steps 1 and 2.
5. #2180 (S5) merges. Done: this branch contains `182e002ff`.
6. PR B: the amendment, this ADR and the code; the panel; `security-auditor` signs the amendment on the final head; then the merge.
7. One `workflow_dispatch` on `main`. Its summary is read, and its outcome is recorded with its date in the ADR 0166 amendment.

## References

- Code and records, as read on 2026-10-18: `.github/workflows/search-console-read.yml` · `.github/scripts/search-console-read.sh` · `.github/scripts/search-console-read.test.sh` ·
  `.github/scripts/samples/search-console/` · `.github/workflows/dependabot-automerge.yml` (the `google-github-actions/auth` arm) · `docs/runbooks/search-console-read.md` ·
  `docs/runbooks/seo-measurement.md` · `web/jobbliggaren-web/messages/sv/content-legal.json` (lines 113 and 382)
- `docs/reviews/2026-10-18-seo-measurement-automation-cto.md` · `docs/reviews/2026-10-18-seo-measurement-automation-security-auditor.md` · `docs/reviews/2026-10-18-seo-audit-stage-b-cto.md` (B11) (all
  transcribed verbatim by the driving session; local)
- ADR 0166 (D7, D10 and the amendments of 2026-10-17 and 2026-10-18, and the amendment for this read path) · ADR 0174 (D5) · ADR 0072 (D2; local)
- AGENTS.md §5 (Tests, Comments) · CLAUDE.md §9.2 (a)–(f), §9.6 · BUILD.md §3.1
- Saltzer and Schroeder, "The Protection of Information in Computer Systems" (1975), least privilege
- Google, read 2026-10-18 (the date on each page is its own last update; these pages change): `urlInspection.index.inspect` (2024-07-23) · `searchAnalytics.query` (2026-08-11) · `sites.get` and the
  Sites resource (2024-07-23) · `sitemaps.list` and the Sitemaps resource (2024-07-23) · *Authorize Requests* (2025-08-28) · *Usage limits* (2025-08-28) · *Workload Identity Federation with
  deployment pipelines* (2026-10-13) · Search Console help, page 7687615 (no date shown) · the spam policy (2026-08-28)
- GitHub, read 2026-10-18: the OIDC reference (no date shown) · the README and source of `google-github-actions/auth` at `7c6bc770dae815cd3e89ee6cdf493a5fab2cc093` (v3.0.0, released 2025-08-28)
- #2090 · #2188 · #1239
