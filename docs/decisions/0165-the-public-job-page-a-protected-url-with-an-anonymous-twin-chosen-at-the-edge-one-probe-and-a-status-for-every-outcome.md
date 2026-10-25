# ADR 0165 — The public job page: a protected URL with an anonymous twin chosen at the edge, one probe, and a status for every outcome

**Date:** 2026-10-11
**Status:** Accepted
**Deciders:** Klas Olsson (product owner; the epic directive of 2026-10-10 and the S2 scope in his prompt) ·
`senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; forks F1–F7 and the follow-up on F2, 2026-10-11,
report-only; `docs/reviews/2026-10-11-2090-s2-plan-cto.md`, local) · `security-auditor` and `design-reviewer` (plan reads,
2026-10-11, report-only; `docs/reviews/2026-10-11-2090-s2-plan-*.md`, local)
**Amends:** ADR 0053, ADR 0017 and ADR 0160, each by a dated amendment at the end of that file. Nothing is rewritten.
**Related:** ADR 0160 (the endpoint this page reads; D5, D6, D9) · ADR 0166 (the legal basis for publishing the text; a
go-live condition of this page) · ADR 0053 (the job route and its modal) · ADR 0017 (the proxy as a gate) · ADR 0018
(Amendment 2026-10-10, #2094: the cookie bounce) · ADR 0106 (recruiter erasure) · ADR 0154 (every merge goes live) ·
ADR 0163 (the per-request nonce CSP, PR #2128, open when this was written) ·
[#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the epic; this is S2) ·
[#2091](https://github.com/klasolsson81/jobbliggaren/issues/2091) (S1) · #2094 · #2098 · #2123 · #1477
**Measured against:** the branch `feat/seo-s2-public-job-page`, cut from `8bc108dcf`; `origin/main` at `11750988b`; Next
16.3.8. D1–D7 describe what the branch builds. It is not merged, and the merge is the go-live (ADR 0154).

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief (CLAUDE.md §9.2, §13). The decisions are
> the CTO's rulings, with the changes `security-auditor` and `design-reviewer` made at their plan reads. The measurements
> in D2 are the driving session's, dated, and the pin that keeps them true is `scripts/public-job-smoke.mjs`.

---

## Context

Epic #2090 makes job ads indexable. S1 (ADR 0160) shipped the rule for what is public and one anonymous endpoint,
`GET /api/v1/public/job-ads/{id}`, with no page in front of it. S2 is the page. Today `/jobb` is in `PROTECTED_PREFIXES`
and `(app)/layout.tsx` re-verifies the session, so an anonymous visitor to `/jobb/<uuid>` is sent to `/logga-in`. Three facts
shape the answer.

- **One URL, two audiences.** ADR 0053 gives `/jobb/<uuid>` a signed-in page with an intercepting modal. The public page must
  live at the same address, because that address is the canonical, the shared link and the search result.
- **A crawler reads the status.** A 404 or a 410 takes a URL out of an index. A 429 or a 5xx slows the crawl and, over time,
  can drop URLs too (Google, "HTTP status codes", read 2026-10-11). So an outage must never read as a removal, and an erased
  ad must answer exactly as an ended one (ADR 0160 D3).
- **A Server Component cannot set every status.** It can produce a 404 (`notFound()`), a 3xx and a 5xx. It can not produce a
  410, or a 503 with `Retry-After`, and once a response streams its status is committed (Next 16.3.8, the streaming guide's
  "Status codes" section and the framework source, read 2026-10-11).

Merge is go-live (ADR 0154), and ADR 0160 D9 lists what must hold before it. Items 4–7 are this ADR's. Items 2–3 are
ADR 0166's, the recruiter notice's and the runbook's (see the amendment to ADR 0160).

## Decision

**`/jobb/<uuid>` is answered by one of two pages, chosen at the edge by whether a session cookie is present. The anonymous
page is an internal route group that only `proxy.ts` can reach, by rewrite. Before the first byte the proxy asks ADR 0160's
endpoint once and maps the answer to a page and a status, so every outcome has the status a crawler should see.**

### D1 — The route mechanism

- **The discriminator is cookie presence, nothing else.** `proxy.ts` rewrites a cookieless `GET` or `HEAD` of exactly
  `/jobb/<uuid>` to `(public-job)/publik/jobb/[id]`. The URL in the browser stays `/jobb/<uuid>`.
- **The signed-in route is untouched.** `(app)/jobb/[id]`, the `@modal` slot, `PROTECTED_PREFIXES` and the #2094 bounce do not
  change. A request with a cookie takes today's path byte for byte (pinned). The two pages change for different reasons (SRP,
  CCP), and "the modal and Back/Forward are unchanged" holds by construction (OCP).
- **The branch.** It sits after `strictCookieBounce` and before the login redirect. It handles `GET` and `HEAD` with a
  path that is one segment and a well-formed GUID (`isValidId`, no encoding). An upper-case GUID gets a 308 to its lower-case
  form, so the page has one URL. Everything else (`/jobb`, `/jobb/not-a-guid`, `/jobb/<uuid>/extra`, a `PUT`, a `POST` without
  `next-action`) keeps today's path and meets the login redirect.
- **A Server Action posted to the page's own address** (amended 2026-10-11, CTO ruling, after the browser measurement below).
  The language switch in the shared header is a Server Action, and a browser posts it to the URL of the page it is on. A
  cookieless `POST` to exactly `/jobb/<uuid>` that carries `next-action` is rewritten to `/publik/jobb/<id>`, with no probe
  and the same forwarded headers. Before this, that POST met the login redirect, the browser
  re-posted it to `/logga-in`, and the page ended on its error surface with the language unchanged (measured 2026-10-11 against
  the production build). The safety property is not that the group holds only that action: Next forwards an action id it
  cannot find to a worker that has it. It is that every Server Action authorizes itself, and an anonymous action POST already
  reaches that handler on every public page (`/`, `/om`, the rest), so the branch adds no new kind of reachability.
- **It never calls `next()`**, not even for a prefetch. A cookieless request that reached the `(app)` route could reach its
  intercepting modal with a forged `Next-Url`.
- **The internal prefix is not a URL.** A direct request to `/publik/**` is a 404 from the proxy, for everyone. The match is
  on the decoded path, up to three rounds, and a path that does not decode counts as internal, because Next matches routes
  on the decoded path (`/%70ublik/...` would otherwise get past a literal check). A refusal costs a 404; a miss would
  publish the same content on a second URL.
- **Rejected: moving the full page out of `(app)` into a layout that branches on the session.** It puts two change-reasons
  in one module, drops the ADR 0017 gate for the URL, and relies on interception across route groups, which nobody measured.

### D2 — Status before the first byte: one probe, one mapping

The proxy makes **one** `GET /api/v1/public/job-ads/{id}`, whatever the method of the visitor's request.

- **What the probe sends and keeps.** Only the headers `pickForwardedHeaders` returns (the client address; a test pins the key
  set, so no `Cookie` or `Authorization` can reach it). `redirect: "manual"`, `cache: "no-store"`, a 2.5 s timeout, and the
  body is cancelled unread. The probe never holds an ad's content.
- **The mapping**, a pure unit-tested function (`answerForPublicJobAdProbe`):

  | The API answers | The edge answers |
  |---|---|
  | 200 | rewrite to `/publik/jobb/<id>`, status 200 |
  | 410 (ended and erased alike) | rewrite to `/publik/borttagen`, status **410**, `X-Robots-Tag: noindex` |
  | 404, 400 | rewrite to an unmatched path, so the root's framed not-found answers a real **404** |
  | 429 | rewrite to `/publik/overbelastad`, status **503**, `Retry-After` = the API's integer (default 30 s, ceiling 120 s), `noindex` |
  | timeout, network error, 5xx, a redirect, any other status | the same 503 page, `Retry-After: 30`, `noindex`. **Never a 404 or a 410.** |

  Every answer carries `Cache-Control: no-store`. A 400 is a 404 because it is a deterministic client answer, and a 503 would
  make a crawler retry a URL that will never exist. `Retry-After` is never relayed raw: a date or a huge number from upstream
  must not become our promise.
- **The render side.** After a 200 probe the page reads once more. A 404 or 410 there can only be the race (the ad ended in
  between) and is `notFound()`. Every other outcome (429, 5xx, a timeout, a body that does not parse) **throws**, which is a
  5xx. It is never `notFound()` (ADR 0160 D9.5). Both arms are tested.
- **No `loading.tsx` in the group.** A Suspense fallback commits a 200 before the body, and a later `notFound()` or throw can
  no longer change the status. A filesystem test pins the absence.
- **Measured 2026-10-11 on Next 16.3.8, `next start`.**
  `NextResponse.rewrite(url, { status, headers, request: { headers } })` carries the status on `GET` and on `HEAD`, and the
  forwarded request headers reach the page. A thrown page error in a group with an `error.tsx` is HTTP 500. An unmatched path
  is 404. Next does not document the first of these. The measurement covered `GET` and `HEAD`; a Flight (RSC) request to the
  rewritten URL was not part of it.
- **The pin.** `scripts/public-job-smoke.mjs` runs in the blocking `frontend` job of `build.yml`, right after
  `Build (production)`. (`e2e.yml` is observe-only, so a guard there would not block.) It starts a Node stub as
  `BACKEND_URL`, starts the standalone server the image runs (`HOSTNAME=0.0.0.0`, `X-Forwarded-Proto: https`), and asserts
  over HTTP: 200 with its text and a clean canonical; one 410 page for an ended and an erased ad with byte-identical `<main>`;
  503 with `Retry-After` for a stub 429 and for a stub 5xx; a 5xx (never a 404) when the probe says 200 and the render fails;
  a 404 (with `noindex`) when the ad ends in between; `/publik/**` and its encoded form a 404 with no API call; the cookie path
  unchanged; and a Server Action posted to the page's address not sent to the login page. **The run on `next start -H 127.0.0.1` is not representative:** Next
  compared the rewrite's origin with the one it derived for itself and treated a same-origin rewrite as external, so it hung.
- **If a later Next stops carrying the status,** the build fails. The way out is not a quiet switch to an unframed document:
  that needs Klas's decision (see the 410 and 503 pages in D4).

### D3 — What it costs, against ADR 0160 D5 and D9.4

- **A 200 view makes two API reads** (the probe and the render). A 404, 410 or 503 makes one. The per-IP bucket is 60 per
  60 s (ADR 0160 D5), so a visitor can view about 30 ads a minute.
- **Accepted for phase 1.** There is no sitemap before S4, so nothing crawls at scale, and ADR 0160 D5 says the numbers are
  raised from measured crawl rates, not guessed. S2 raises nothing. S4 measures again, with the probe in the count.
- **The client address is relayed on both calls:** `pickForwardedHeaders` in the probe, `forwardedHeaders()` in the render,
  one test each. Without it every anonymous visitor would share the web container's one bucket (ADR 0160 D5).
- **D9.4 holds for the render.** `getPublicJobAd` is wrapped in `cache()`, so `generateMetadata` and the page share one call,
  and the render is per request, with no ISR and no static rendering. The probe is the additional call that D9.5 and D9.7
  force: the status has to be known before the render begins.

### D4 — The page

- **A composition of its own, not `JobAdDetail`.** `PublicJobAd` takes `PublicJobAdDto` and can render nothing else.
  `JobAdDetail` carries save, follow, applied, match and contacts behind optional props, and a public page that reached it
  would be one forgotten prop from showing them. A mode flag would move the PII boundary from the type system into a boolean.
- **The text.** The whole description is in the first response, with no excerpt, toggle or script. It is plain text, handed
  to React as children by `formatAdDescription`, so it is escaped and never HTML (ADR 0160 D9.6). A listed ad is `Active` by
  the publication predicate, so `status="Active"` is a fact.
- **Metadata comes from structured fields only.** The title is "<title> – <company>". The description and the OpenGraph tags
  are built from title, company and dates, **never from the free text**. The reason is Art. 5(1)(c) and Art. 17(2): a link
  preview is a copy held by a third party that an erasure step may not reach, and the free text can name a person. A test with
  a sentinel name in the description pins it.
- **The canonical** is `absoluteUrl("/jobb/<lower-case uuid>")`. A page that sets its own `openGraph` replaces the root's
  wholesale, the file-based image included (measured in the production build), so the shared `/opengraph-image` is named on
  the page. There is no per-ad image.
- **Left out on purpose:** concept-id labels (ADR 0160 D7; S3 and S6 decide), JSON-LD (S3), a call to action (registration is
  open since #2121, but a CTA is outside S2), and any contact block, ever.
- **The link out** is the ad's `url`, labelled "Öppna annonsen hos Arbetsförmedlingen" only when the host is
  `arbetsformedlingen.se` or a subdomain, and "Öppna annonsen" otherwise. A value that is not `http` or `https` renders no
  link, because it ends up in an `href`.
- **The notice row is a ratified deviation from DESIGN.md §8.** The page carries the `/kontaktperson-i-annons` notice link
  after the description, with no contact beside it. §8's rule is that the notice appears only with a contact. The deviation is
  grounded in ADR 0160 D9.2: this page publishes free text that can name a person, so the notice must be there although no
  contact is. DESIGN.md §8 records the exception, and it makes this the only job-ad surface with a notice and no contact.
- **The non-resting states are framed internal pages,** with the site header and footer and a `main#main`. The 410 page is
  word for word the same for an ended and an erased ad, and has no id, title, company or date in scope. The 503 page claims no
  cause. A person lands on the 410 page every time someone opens an ended ad, so it is a final state, and Klas's #1477
  directive ("Man får aldrig aldrig hamna utanför jobbliggarens ramverk") rules out an unframed document.
- **The group** (`(public-job)`) uses the `(marketing-inner)` frame, its own message provider with the smallest namespace set,
  an `error.tsx` and a `not-found.tsx`. No log line on the front-end path carries a title, company or description.

### D5 — The #2094 bounce is unchanged, and the marker is taken off the address

- **Unchanged:** `strictCookieBounce`, `/jobb` in `PROTECTED_PREFIXES`, and the cookie's `SameSite=Strict`.
- **What follows.** An anonymous visitor who arrives from another site (a search result, a shared link) is a cross-site
  browser navigation with no cookie, so they get the continuation document and land on `/jobb/<id>?vidare=1`. The public page
  is served there directly. **There is no redirect:** a sandboxed popup with an opaque origin can come back cross-site, and a
  redirect would loop (ADR 0018, Amendment 2026-10-10).
- **The defect S2 would create, fixed in the same change.** A visitor copies `/jobb/<id>?vidare=1` from the address bar. A
  signed-in person who opens it from another site carries a marker that makes the bounce stand down, so they would get the
  anonymous page. A small client island, `StripBounceMarker`, removes the marker with `history.replaceState` once the page is
  up (`stripBounceMarker(href)` is pure and imports `BOUNCE_MARKER` from its own module, so the island does not pull the
  bounce's server code into the browser). Without JavaScript the marker stays, which does no harm: the canonical is clean.
- **Crawlers send no `Sec-Fetch-*` headers, so they are not bounced** (pinned in the proxy tests). Whether Google's crawler
  reaches the page without the hop has not been measured against the live site. It is to be checked with the Search Console
  URL Inspection live test after go-live.
- **Rejected:** a server redirect to strip the marker (loops in the sandbox case); a `Lax` has-session cookie (a new auth
  surface that brings back a first document that depends on the session); exempting `/jobb/<uuid>` from the bounce (it breaks
  the signed-in landing from a link in a notification mail).

### D6 — robots.txt

- **An array of rules.** The `*` group is the old one with `/jobb` handled differently: `/jobb/` is **removed** from the
  disallow list, and `/jobb$` stays and `/jobb?` is added. `allow: "/"` covers the detail, because `(app)/jobb/` holds nothing
  but `[id]`. The private list and its query URLs stay closed.
- **`GPTBot` and `CCBot` form a group of their own** with `disallow: "/"` (Klas, 2026-10-10). A crawler uses one group only
  and `*` is not merged into it (Google, robots.txt specification, updated 2026-08-31). `Google-Extended` is a token for
  Gemini training and grounding and has no effect on Search (Google, common crawlers, 2026-07-14), so it is allowed by not
  naming it, like every search crawler. Naming a further bot is Klas's decision.
- **No `Disallow: /jobb/*?`.** robots.txt is not a canonicalisation tool (Google, "consolidate duplicate URLs", 2026-07-10),
  and a shared or tracked link has to keep its canonical readable. The canonical link does that job.
- **The internal prefix is not listed.** A URL blocked in robots.txt cannot show the 404 or the `noindex` the proxy gives it
  (Google, "block indexing", 2025-12-10: `noindex` needs the page to be crawlable), and the line would name the path.
- The sitemap is unchanged (S4).

### D7 — Rollout and overlay

- **Open on merge, no flag.** The merge is the go-live (ADR 0154), and a revert is the rollback.
- **No personal overlay.** A signed-in person gets the unchanged `(app)` page. ADR 0160 D2's recorded window (an ad can be
  Active in signed-in search and Ended publicly until `ExpireJobAdsJob` runs at 03:45) is **closed as accepted**.
- **What must hold before the merge is live,** beyond the code: ADR 0160 D9.1's measurement (S2-P3, a hard gate; see the
  amendment to ADR 0160), ADR 0166 approved by Klas and signed by `security-auditor`, the revised recruiter notice, and the
  runbook's step 2.4a. These ride in the same PR (ADR 0065).

## Alternatives considered

- **One route in `(app)` with a layout that branches on the session.** Rejected in D1.
- **Let the page set the status.** A Server Component gives a 404 and a 5xx and no 410 and no 503 with `Retry-After`, so an
  ended ad would answer 200 or 404 and an outage would have to pick one of them. Rejected in D2.
- **An unframed 410 or 503 document straight from the proxy.** It was the CTO's first ruling on F2 and was reversed in the
  follow-up: a final state a person lands on must be framed (#1477). Rejected in D4.
- **A loopback render of the page for the status.** Its nonce would not match #2128's CSP. Rejected.
- **Passing the ad in a request header from the probe to the render.** It saves the second read and costs header size and a
  forgeable input. Rejected.
- **A static or ISR public page.** An erased ad must not be served stale (ADR 0160 D6, D9.4). Rejected.
- **`Disallow` forms for the detail's query URLs or the internal prefix.** Rejected in D6.

## Consequences

### Positive

- One canonical URL serves both audiences, and the signed-in path is byte for byte what it was.
- Every outcome has the status a crawler should read, and an outage can not read as a removal.
- The PII boundary of the page is the type of its one input. It adds no data: the page shows a subset of what ADR 0160's
  endpoint already answers anyone.
- A Next behaviour that the framework does not document is pinned by a blocking check, not left to be found in production.

### Negative and risks

- **Two API reads per 200 view** (D3), and a network call in the proxy on the hot path of every cookieless view, bounded at 2.5 s.
- **A signed-in person whose cookie has expired** takes the signed-in path and is sent to login, as before. The discriminator
  is the cookie's presence, not a session check.
- **Organic human visitors from a search result pass through the continuation document** (an extra hop) before the page. A
  crawler does not (D5).
- **A render failure after a 200 probe shows Next's own 500 page,** which is unframed. It needs the API to fail between the
  probe and the render, which is rare.
- **An ad that ends between the probe and the render is a real 404 whose HTML is Next's error shell** (`__next_error__`, with
  `noindex`), which the browser renders into the frame (header, `main`, footer; measured). The raw HTML carries no `<main>`.
- **No per-ad OpenGraph image.**
- **The mechanism rests on an undocumented Next feature.** The pin makes a Next upgrade that breaks it fail the build, which
  is the intent, and the fallback needs a decision (D2).
- **`proxy.ts` is a hotspot, and PR #2128 rewrites the same file.** The branch is an isolated module with one call site and
  takes its headers as a parameter, so the rebase is one line; a test pins that the rewrite carries exactly the headers it was
  given. Whichever PR merges second rebases.

## Implementation

Ships under #2090 S2 in one PR with this ADR, ADR 0166 and the three amendments (CLAUDE.md §1.5).

- `web/jobbliggaren-web/src/proxy.ts` (one call site) · `src/lib/auth/public-job-ad-route.ts` ·
  `src/lib/job-ads/public-job-ad-answer.ts` (the pure half: paths, `classifyPublicJobAdProbe`, `answerForPublicJobAdProbe`) ·
  `src/lib/api/public-job-ad-probe.ts` · `src/lib/api/public-job-ad.ts` · `src/lib/dto/public-job-ad.ts`
- `src/app/(public-job)/` (`layout`, `error`, `not-found`, `publik/jobb/[id]`, `publik/borttagen`, `publik/overbelastad`) ·
  `src/components/job-ads/{public-job-ad,strip-bounce-marker}.tsx` · `src/lib/auth/{bounce-marker,strip-bounce-marker}.ts` ·
  `src/lib/job-ads/ad-source-link.ts` · `messages/{sv,en}/publicjob.json`
- `src/app/robots.ts` and its emulation in `seo-metadata-routes.test.ts`
- `scripts/public-job-smoke.mjs` and its step in the `frontend` job of `.github/workflows/build.yml`
- Fitness tests: no `loading.tsx` and no `dangerouslySetInnerHTML` in the group, the group in the client-namespace payload
  boundaries and the error-boundary tables, and a title for the dynamic pages.

## References

- Code and tests: as listed above. ADR 0160 D2, D3, D5, D6, D7, D9; ADR 0166.
- `docs/reviews/2026-10-11-2090-s2-plan-cto.md` (local) and the `security-auditor` and `design-reviewer` plan reads
  (`docs/reviews/2026-10-11-2090-s2-plan-*.md`, local).
- Next 16.3.8, `node_modules/next/dist/docs/01-app/02-guides/streaming.md`, "The HTTP contract" and "Status codes".
- Google Search Central, read 2026-10-11 (the date on each page is its own last update):
  [robots.txt specification](https://developers.google.com/search/docs/crawling-indexing/robots/robots_txt) (2026-08-31) ·
  [common crawlers](https://developers.google.com/search/docs/crawling-indexing/google-common-crawlers) (2026-07-14) ·
  [block indexing](https://developers.google.com/search/docs/crawling-indexing/block-indexing) (2025-12-10) ·
  [HTTP status codes and network errors](https://developers.google.com/search/docs/crawling-indexing/http-network-errors)
  (2026-02-04) ·
  [job posting structured data](https://developers.google.com/search/docs/appearance/structured-data/job-posting)
  (2026-09-08) ·
  [consolidate duplicate URLs](https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls)
  (2026-07-10). These pages change; the dates say what was read.
- ADR 0018 (Amendment 2026-10-10), ADR 0053, ADR 0017, ADR 0106, ADR 0154, ADR 0163 · #2090 · #2091 · #2094 · #2098 · #2123 ·
  #1477

## Amendment 2026-10-17 (#2090 S3, ADR 0171) — JSON-LD now ships; D4's metadata rule is unchanged; the page gains a facts row

D1–D7 stand and nothing above is rewritten. This amendment records what the S3 PR changes in D4; the reasoning is in ADR 0171.

**1. "Left out on purpose: … JSON-LD (S3)" is closed.** The page now carries one `JobPosting` JSON-LD block when the ad is complete enough for Google's schema (ADR 0171
D2–D5). The block is built from the anonymous DTO and from the blocks `parseAdDescription` produces, with every text run HTML-escaped.

**2. D4's metadata rule is UNCHANGED.** "Metadata comes from structured fields only": the title, the description and the OpenGraph tags are built from title, company and
dates, never from the free text. The sentinel-name test on `generateMetadata` still pins it and is not modified. The data block is a different channel with a different
class of holder, which ADR 0166 states in its amendment of the same date.

**3. The page gains a facts row** under the dates: kommun and län, and employment type and extent (ADR 0171 D6). The concept-id labels that D4 left out "on purpose" are
therefore partly shown; occupation group, the explainer links and related ads stay with S6.

ADR 0165 remains **Accepted**.

## Amendment 2026-10-18 (#2090 S4, ADR 0174) — D3: "S4 measures again" is carried out; D6: the sitemap is no longer unchanged

D1–D7 and the earlier amendment stand and nothing above is rewritten. This amendment records two things the S4 PR settles; the reasoning is in ADR 0174.

**1. D3: "S4 measures again" is carried out by ADR 0174 D5.** The page's numbers are unchanged: 60 per 60 s per IP (ADR 0160 D5). ADR 0174 D5 sets what is measured (rate-limit
rejections per day on the job-detail endpoint, which the probe and the render both read, and the 503s per day that the site answers on `/jobb/<uuid>`), a dated baseline before the merge,
two readings after, and the rule for when a raise is warranted. D3's "no sitemap before S4" ends with the S4 merge.

**2. D6: "The sitemap is unchanged (S4)" no longer holds.** `app/sitemap.ts` is deleted. `/sitemap.xml` is now an index over a marketing part and two job parts, which are route handlers at the
site root (ADR 0174 D3, D6). The `Sitemap:` line in `robots.txt` still names `/sitemap.xml`, which is now the index, and it is not changed. Nothing else in D6 changes: the `*` group, the
training-crawler group and the absence of a `Disallow` for the internal prefix stand.

ADR 0165 remains **Accepted**.

## Amendment 2026-10-18 (#2090 S5, ADR 0176) — D3: the catalogue sends crawlers to job pages on the same budget; D6: robots.txt is unchanged

D1–D7 and the earlier amendments stand and nothing above is rewritten. This amendment records two things the S5 PR settles; the reasoning is in ADR 0176.

**1. D3: the catalogue sends crawlers to job pages, on the same budget.** The catalogue is the first public page that links to job pages. A crawler that follows its links reaches
`/jobb/<uuid>` on the page's budget, which is unchanged: 60 per 60 s per IP (ADR 0160 D5), and a 200 view still costs two API reads, the probe and the render. The catalogue's own budget is
separate (ADR 0176 D7). Row links are plain anchors, and nothing that links to `/lediga-jobb` prefetches (ADR 0176 D10). So a catalogue view spends nothing from the page's bucket, and a client
navigation never reaches the twin's Flight path, which D2 records as not measured, or the `@modal` slot of D1. D3's "S4 measures again" is ADR 0174 D5's, now with the catalogue path.

**2. D6: robots.txt is unchanged.** `/lediga-jobb` is matched by no rule: the `*` group's `/jobb$` and `/jobb?` entries do not prefix-match it, so the existing `allow: "/"` covers it. An
unknown query parameter on the catalogue is left out of the canonical and is neither `noindex`ed nor disallowed (ADR 0176 D1), for D6's own reason that a blocked URL hides its canonical. The
`Sitemap:` line is unchanged.

ADR 0165 remains **Accepted**.

## Amendment 2026-10-19 (#2177) — D6: the training-crawler group names six crawlers

D1–D7 and the earlier amendments stand and nothing above is rewritten. This amendment changes D6's training-crawler group only.

**1. The group names `GPTBot`, `CCBot`, `ClaudeBot`, `Applebot-Extended`, `Amazonbot` and `meta-externalagent`**, still with `disallow: "/"` and nothing else. The four added tokens
are each their vendor's training crawler or training opt-out token, and each vendor names a separate search or user crawler that this group does not name (vendor pages read 2026-10-19):

| Token | Vendor's own page | Not named, so allowed under `*` |
|---|---|---|
| `ClaudeBot` | Anthropic, support article 8896518 | `Claude-SearchBot`, `Claude-User` |
| `Applebot-Extended` | Apple, "About Applebot" (support.apple.com/119829): crawls nothing, decides training use of what `Applebot` fetched | `Applebot` |
| `Amazonbot` | Amazon, developer.amazon.com/amazonbot: "may be used to train Amazon AI models" | `Amzn-SearchBot`, `Amzn-User` |
| `meta-externalagent` | Meta, "Meta Web Crawlers" (developers.facebook.com/docs/sharing/webmasters/web-crawlers) | `Meta-WebIndexer`, `Meta-ExternalFetcher` |

**2. D6's "Naming a further bot is Klas's decision" is answered by Klas's rule of 2026-10-10**: a crawler that only collects for training is blocked, and search and grounding crawlers
are allowed. `senior-cto-advisor` applied that rule to these four (SEO/AIO audit stage B, B12, 2026-10-18), and Klas was told and did not object. A later training crawler joins by the same
rule, with its vendor page cited. `Google-Extended` stays allowed, as Klas chose.

**3. No ADR 0166 re-take and no notice change.** `security-auditor` (stage B, §5): blocking a crawler narrows copying and adds no recipient.

ADR 0165 remains **Accepted**.

## Amendment 2026-10-23 (#2090 S6b, ADR 0182) — D3: a 200 view also makes one related read, on its own budget

D1–D7 and the earlier amendments stand and nothing above is rewritten. This amendment records what the S6b PR settles in D3; the reasoning is in ADR 0182.

**D3: a 200 view makes two reads on the page's budget and one on the related budget.** The render also reads the related ads (`getPublicJobAdRelated`), in parallel with its own read and
only in the page body, never in `generateMetadata`. That read is on `public-job-ad-related`, 60 per 60 s per IP (ADR 0182 D4), not on the page's bucket, so D3's "about 30 ads a minute"
stands: each view spends two of the page's tokens and one of the related ones, and the page's budget binds first. A 404, 410 or 503 from the probe still makes one read and no related read,
because the page is never rendered (the smoke pins both counts). The race D2 describes (probe 200, render 404) spends one related read for nothing. The related read has a 2-second timeout,
and any failure leaves its section out with the page still a 200, so the worst case adds at most that bound to the time to first byte. The client address is relayed on the related call
too, through `forwardedHeaders()`, with a test of the key set.

ADR 0165 remains **Accepted**.

## Amendment 2026-10-24 (#2214 PR 1, ADR 0183) — D4: the link out appears once, the page invites sign-in and shares, and the notice label is reworded

D1–D7 and the earlier amendments stand and nothing above is rewritten. This amendment records what the #2214 PR 1 changes in D4; the reasoning is in ADR 0183.

**1. "Left out on purpose: … a call to action" is reversed.** The page now invites a visitor who is not signed in to sign in: in the card footer, a heading, one line and a plain `rel="nofollow"` link to `/logga-in?next=` plus the page's own path, so the visitor returns to the same ad (ADR 0183 D2). "A call to action" leaves D4's list. The rest of that sentence is as the earlier amendments leave it, and "any contact block, ever" stands.

**2. The link out appears once.** D4's rule for the link's target and label is unchanged: the ad's `url`; "Öppna annonsen hos Arbetsförmedlingen" for an `arbetsformedlingen.se` host or subdomain and "Öppna annonsen" otherwise; no link for a value that is not `http` or `https`. The page showed that link twice, as a secondary action under the dates and as the only primary action in the footer. It now shows it once, as the page's one `--primary`, in the head under an `h2` "Sök jobbet", and the footer holds no copy of it. It is not relabelled "Ansök" (ADR 0183 D1).

**3. The notice row's label is reworded; its presence and its place are not.** The ratified deviation from DESIGN.md §8 and the place after the description stand. The `/kontaktperson-i-annons` link on this page now reads "Så behandlar vi uppgifter om kontaktpersoner i annonser" (`publicjob.recruiterNoticeLink`), as bound copy: ADR 0144 Amendment 2026-10-24 (ADR 0183 D3).

**4. The page gains a share row.** "Dela annonsen", `data-nosnippet`, in the card body after the notice row: Facebook, LinkedIn, WhatsApp and E-post as plain anchors, then "Kopiera länken" and "Fler sätt att dela" (ADR 0183 D4, D5). D4's metadata rule is unchanged: the row hands on the canonical address and the page's `og:title` string, which is built from structured fields and never from the free text.

**5. Not changed.** "There is no per-ad image" and the shared `/opengraph-image` stand; #2214's PR 2 amends them. D3 is unchanged, because this PR adds no read and no endpoint. D6 is unchanged: crawlers are kept from the page's login URLs by `rel="nofollow"` on the link, not by a `Disallow`.

ADR 0165 remains **Accepted**.

## Amendment 2026-10-25 (#2214, ADR 0183 Amendment 2026-10-25) — D4: the page names a square brand image and the summary card

D1–D7 and the earlier amendments stand and nothing above is rewritten. This amendment records what PR [#2232](https://github.com/klasolsson81/jobbliggaren/pull/2232) changes in D4.

**1. The image.** The page names `/delningsbild` in `openGraph.images`, with width, height and the alt "Jobbliggaren" (`metadata.applicationName`). It also names it in a `twitter` block of its own, with `card: "summary"` and the same title and description as OpenGraph.
- **What it is:** a 400 × 400 PNG of the brand mark on white, the same for every ad, reading nothing.
- **The root's large card stays for every other page.** A page's own `openGraph` and `twitter` replace the root's wholesale. Measured on the production build by `public-job-smoke.mjs`, 2026-10-25: one `og:image` and one `twitter:image`, both the square, and one `twitter:card`, `summary`.
- **Why 400 px:** it is the widest image LinkedIn still shows as a thumbnail ("less than 401 pixels wide", linkedin.com/help/linkedin/answer/a521928). It is also under the 600 × 315 Facebook needs for its large card (developers.facebook.com/docs/sharing/webmasters/images). Both pages were read 2026-10-25.
- **Not measured:** that Facebook, LinkedIn and WhatsApp show the image as the compact card. Klas checks it from his own device.

**2. "There is no per-ad image" stands, and no PR is now pending against it.** The amendment of 2026-10-24 (point 5) said #2214's PR 2 would amend it. Klas withdrew PR 2 on 2026-10-25 ("Ja, ersätter PR 2 (Rek.)"). The ADR 0166 D10 reading has one home, ADR 0183 Amendment 2026-10-25, and it is not repeated here.

**3. D4's metadata rule is unchanged.** Title, description, OpenGraph and now the Twitter card are built from structured fields only.

ADR 0165 remains **Accepted**.
