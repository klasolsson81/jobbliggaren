/**
 * The public job page's, its related section's, the sitemap's and the public catalogue's HTTP contract, measured against a
 * production build (ADR 0165, ADR 0182, ADR 0174 and ADR 0176, SEO epic #2090 S2, S6b, S4 and S5).
 *
 * Why this is a script and not a unit test: the statuses are the contract a crawler reads (a 410 drops the URL from an
 * index, a 503 does not), and the page's are produced by a Next feature with no documented status contract:
 * `NextResponse.rewrite(url, { status })` in the proxy. It carried the status on Next 16.3.8 (`next start`,
 * 2026-10-11), and only a real server shows whether a later Next still does. The sitemap's job parts must be answered per
 * request from the backend, never prerendered at build, which only a production server shows. `e2e.yml` is
 * observe-only, so this runs in the blocking `frontend` job of `build.yml`, right after `pnpm build`.
 *
 * It starts a stub backend on a free port, starts the standalone server the image runs against it, and asserts over
 * HTTP. The stub is `public-job-stub.mjs`, which also serves the pages Lighthouse measures (`lighthouse-server.mjs`);
 * its fixtures, its modes and what it answers for each are described there.
 *
 * Usage (after `pnpm build`):  node scripts/public-job-smoke.mjs
 */
import { spawn } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";
import {
  AD,
  APPLICATION_PAGE,
  ID,
  catalogue,
  catalogueRow,
  freePort,
  listedIds,
  related,
  relatedRow,
  seen,
  startStub,
} from "./public-job-stub.mjs";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const SESSION_COOKIE = "__Host-jobbliggaren_session";
const CLIENT_IP = "203.0.113.7";

async function waitUntilReady(origin, child) {
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`the server exited with code ${child.exitCode}`);
    try {
      const res = await fetch(`${origin}/robots.txt`, { redirect: "manual" });
      if (res.status === 200) return;
    } catch {
      // not listening yet
    }
    await new Promise((done) => setTimeout(done, 500));
  }
  throw new Error("the server did not become ready in 90 s");
}

async function get(origin, path, init = {}) {
  const res = await fetch(`${origin}${path}`, {
    redirect: "manual",
    signal: AbortSignal.timeout(20_000),
    headers: { "x-forwarded-for": CLIENT_IP, "x-forwarded-proto": "https", ...init.headers },
    method: init.method ?? "GET",
    body: init.body,
  });
  const body = init.method === "HEAD" ? "" : await res.text();
  return { status: res.status, headers: res.headers, body };
}

const publicCalls = () => seen.filter((r) => r.path?.startsWith("/api/v1/public/job-ads/"));
const callsFor = (id) => publicCalls().filter((r) => r.path.endsWith(`/${id}`));
const listedIdsCalls = () => seen.filter((r) => r.path?.startsWith("/api/v1/public/job-ads/listed-ids"));
const catalogueCalls = () => seen.filter((r) => r.path?.startsWith("/api/v1/public/job-ads/catalogue"));
const relatedCalls = () => seen.filter((r) => r.path?.startsWith("/api/v1/public/job-ads/") && r.path.endsWith("/related"));
const RELATED = [1, 2, 3, 4, 5].map(relatedRow);

/** The `<head>` of a page and what follows it, so a tag can be placed: metadata must sit in the head (M5). */
function inHead(html, pattern) {
  const end = html.indexOf("</head>");
  assert.ok(end > 0, "the document has a </head>");
  const at = html.search(pattern);
  return at >= 0 && at < end;
}

const GOOGLEBOT = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";
const BROWSER = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";

/** Every `<loc>` of a sitemap file, as written. */
const locsOf = (xml) => [...xml.matchAll(/<loc>([^<]*)<\/loc>/g)].map((m) => m[1]);
/** A `<loc>` names the public host the build was given; only its path is fetched from this server. */
const pathOf = (loc) => new URL(loc).pathname;

/** The job page's data-nosnippet elements in document order, named by what they are (ADR 0182, ADR 0183). */
const nosnippetElements = (html) =>
  [...html.matchAll(/<[a-z]+[^<>]*\sdata-nosnippet[^<>]*>/g)].map(([tag]) => {
    if (tag.includes('aria-labelledby="jp-related-title"')) return "related";
    if (tag.includes('aria-labelledby="jp-share-title"')) return "share";
    if (tag.includes('class="jp-modal__foot')) return "sign-in";
    return `unknown: ${tag.slice(0, 80)}`;
  });
/** Every JSON-LD data block on a page, as text. */
const jsonLdBlocks = (html) => [...html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)].map((m) => m[1]);

/** The part of a page that is the same for an ended and an erased ad, stripped of Next's per-request script ids. */
function mainOf(html) {
  const main = /<main[^>]*>[\s\S]*?<\/main>/.exec(html);
  assert.ok(main, "the response carries a <main> landmark, so it is framed");
  return main[0];
}

const results = [];
async function check(name, fn) {
  try {
    await fn();
    results.push({ name, ok: true });
    console.log(`  ok   ${name}`);
  } catch (error) {
    results.push({ name, ok: false, error });
    console.log(`  FAIL ${name}\n       ${error instanceof Error ? error.message : error}`);
  }
}

const { server: stub, port: stubPort } = await startStub();
const appPort = await freePort();
const origin = `http://127.0.0.1:${appPort}`;

// The server the image runs: `node server.js` from the standalone output, on 0.0.0.0, behind a proxy that sets
// X-Forwarded-Proto. The rewrite's destination is built from the request's own URL, and Next compares its origin with
// the one it derived for itself (hostname, port and that header), so a run on `next start -H 127.0.0.1` does not
// measure the deployment: it would call a same-origin rewrite an external one.
const standaloneDir = resolve(ROOT, ".next", "standalone");
const app = spawn(process.execPath, ["server.js"], {
  cwd: standaloneDir,
  env: {
    ...process.env,
    PORT: String(appPort),
    HOSTNAME: "0.0.0.0",
    BACKEND_URL: `http://127.0.0.1:${stubPort}`,
    NODE_ENV: "production",
  },
  stdio: ["ignore", "pipe", "pipe"],
});
let appLog = "";
app.stdout.on("data", (chunk) => (appLog += chunk));
app.stderr.on("data", (chunk) => (appLog += chunk));

let exitCode = 0;
try {
  await waitUntilReady(origin, app);
  seen.length = 0;
  console.log(`public job page smoke against ${origin} (stub backend on ${stubPort})`);

  await check("a listed ad is a 200, framed, uncacheable, with its text, a clean canonical and no noindex", async () => {
    seen.length = 0;
    const res = await get(origin, `/jobb/${ID.listed}`);
    assert.equal(res.status, 200);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    assert.match(res.body, /<main[^>]*id="main"/);
    assert.match(res.body, /<h1[^>]*>Sjuksköterska till vårdcentral<\/h1>/);
    assert.match(res.body, /Vi söker dig som vill arbeta med primärvård/);
    assert.match(res.body, new RegExp(`<link rel="canonical" href="[^"]*/jobb/${ID.listed}"`));
    assert.doesNotMatch(res.body, /<meta name="robots" content="[^"]*noindex/);
    assert.match(res.body, /<meta property="og:title" content="Sjuksköterska till vårdcentral – Region Exempel"/);
    assert.doesNotMatch(
      res.body,
      /<meta[^>]*(og:description|twitter:title|twitter:description|name="description")[^>]*Vi söker dig/,
      "no free text in a title or description tag"
    );
    assert.equal(res.headers.get("x-robots-tag"), null);
  });

  // ADR 0165 Amendment 2026-10-25: the compact card. The platforms read these tags, so they are asserted in the HTML the
  // server sends, not in the metadata object: the twitter strings are the page's own now, no longer Next's copy.
  await check("a listed ad names the square preview image and the summary card, with the same strings as OpenGraph", async () => {
    const res = await get(origin, `/jobb/${ID.listed}`);
    const meta = (key) =>
      [...res.body.matchAll(new RegExp(`<meta (?:property|name)="${key}" content="([^"]*)"`, "g"))].map((m) => m[1]);
    for (const key of ["og:image", "twitter:image"]) {
      assert.equal(meta(key).length, 1, `one ${key}`);
      assert.match(meta(key)[0], /^https?:\/\/[^"?]+\/delningsbild$/, `${key} is the square, with no query`);
    }
    assert.deepEqual(meta("og:image:width"), ["400"]);
    assert.deepEqual(meta("og:image:height"), ["400"]);
    assert.deepEqual(meta("twitter:card"), ["summary"]);
    assert.ok(inHead(res.body, /<meta property="og:image"/), "og:image in the head");
    assert.ok(inHead(res.body, /<meta name="twitter:card"/), "twitter:card in the head");
    assert.deepEqual(meta("twitter:title"), meta("og:title"));
    assert.equal(meta("og:description").length, 1, "one og:description");
    assert.deepEqual(meta("twitter:description"), meta("og:description"));
    assert.equal(meta("twitter:site").length + meta("twitter:creator").length + meta("fb:app_id").length, 0);
  });

  await check("the preview image is the 400 × 400 brand square, read from nothing and served with no cookie", async () => {
    seen.length = 0;
    const res = await fetch(`${origin}/delningsbild`, { redirect: "manual", signal: AbortSignal.timeout(20_000) });
    assert.equal(res.status, 200);
    assert.equal(res.headers.get("content-type"), "image/png");
    assert.equal(res.headers.get("set-cookie"), null);
    const cacheControl = res.headers.get("cache-control") ?? "";
    assert.doesNotMatch(cacheControl, /immutable/, "the URL carries no content hash");
    const png = Buffer.from(await res.arrayBuffer());
    assert.equal(png.toString("ascii", 12, 16), "IHDR");
    assert.deepEqual([png.readUInt32BE(16), png.readUInt32BE(20)], [400, 400]);
    assert.equal(seen.length, 0, "no backend call");
    console.log(`       /delningsbild cache-control: ${cacheControl}`);
  });

  await check("a listed, complete ad carries exactly one JobPosting data block that parses, matches the page and cannot close itself", async () => {
    const res = await get(origin, `/jobb/${ID.listed}`);
    const blocks = jsonLdBlocks(res.body);
    assert.equal(blocks.length, 1, "one application/ld+json script");
    assert.doesNotMatch(blocks[0], /<|<\/script/, "no literal < inside the data block");
    const posting = JSON.parse(blocks[0]);
    assert.equal(posting["@type"], "JobPosting");
    assert.equal(posting.title, AD.title);
    assert.equal(posting.hiringOrganization.name, AD.companyName);
    assert.equal(posting.jobLocation.address.addressLocality, "Stockholm");
    assert.equal(posting.jobLocation.address.addressCountry, "SE");
    assert.deepEqual(posting.employmentType, ["FULL_TIME", "TEMPORARY"]);
    assert.ok(posting.validThrough, "the ad has a deadline, so validThrough is present");
    assert.equal(posting.url, undefined);
    assert.match(res.body, /Stockholm, Stockholms län/, "the kommun and län are visible on the page");
    assert.match(res.body, /Vikariat · Heltid/, "the employment type and extent are visible on the page");
  });

  await check("a listed ad names its occupation group last in the facts row, marked Swedish, and in no head tag and no JobPosting", async () => {
    const res = await get(origin, `/jobb/${ID.listed}`);
    const facts = /<div class="jp-modal__meta jp-modal__facts">([\s\S]*?)<\/div>/.exec(res.body);
    assert.ok(facts, "the facts row is rendered");
    assert.match(facts[1], new RegExp(`<span lang="sv">${AD.occupationGroupName}</span></span></span>$`), "the group is the last fact");
    const [block] = jsonLdBlocks(res.body);
    assert.doesNotMatch(block, /occupationalCategory/);
    assert.ok(!block.includes(AD.occupationGroupName), "the group is not in the JobPosting");
    assert.ok(inHead(res.body, /<title>/), "the page's metadata sits in the head, so the head is where to look");
    const head = res.body.slice(0, res.body.indexOf("</head>"));
    assert.ok(!head.includes(AD.occupationGroupName), "the group is not in the title, a description or an OpenGraph tag");
  });

  // #2214 PR 1b, ADR 0166 D10 trigger 18: the application page is the head's one link to it and nowhere else. Next writes
  // the anchor once more into the RSC payload; that is the same anchor, so the count is of <a> tags.
  await check("an ad with the employer's application page ends the head with Ansök, its one primary, and carries the page nowhere else", async () => {
    const res = await get(origin, `/jobb/${ID.listedApply}`);
    assert.equal(res.status, 200);
    const anchors = [...res.body.matchAll(/<a\s[^>]*>/g)].map(([tag]) => tag);
    const apply = anchors.filter((tag) => tag.includes(`href="${APPLICATION_PAGE}"`));
    assert.equal(apply.length, 1, "one anchor to the application page");
    assert.match(apply[0], /class="jp-btn jp-btn--primary"/);
    assert.match(apply[0], /target="_blank"/);
    assert.deepEqual(/rel="([^"]*)"/.exec(apply[0])?.[1].split(" ").sort(), ["nofollow", "noopener", "noreferrer"]);
    assert.equal(anchors.filter((tag) => /class="[^"]*jp-btn--primary/.test(tag)).length, 1, "one primary action");
    assert.ok(res.body.includes(`href="${AD.url}"`), "the ad's own page is still linked, as the text link");
    assert.ok(!res.body.includes("Sök jobbet"), "no apply heading: Ansök says it");
    const head = res.body.slice(0, res.body.indexOf("</head>"));
    assert.ok(!head.includes("jobb.example.se"), "not in the title, a description, an OpenGraph or a Twitter tag");
    const blocks = jsonLdBlocks(res.body);
    assert.equal(blocks.length, 1, "one JobPosting");
    for (const block of blocks) assert.ok(!block.includes("jobb.example.se"), "not in the JobPosting");
    const share = anchors.filter((tag) => /facebook\.com|linkedin\.com|wa\.me|mailto:/.test(tag));
    assert.equal(share.length, 4, "the share row's four links");
    for (const tag of share) assert.ok(!tag.includes("jobb.example.se"), "not in a share link");
  });

  await check("a listed ad without an application page keeps the apply heading and the link out as its one primary", async () => {
    const res = await get(origin, `/jobb/${ID.listed}`);
    const anchors = [...res.body.matchAll(/<a\s[^>]*>/g)].map(([tag]) => tag);
    const primary = anchors.filter((tag) => /class="[^"]*jp-btn--primary/.test(tag));
    assert.equal(primary.length, 1, "one primary action");
    assert.ok(primary[0].includes(`href="${AD.url}"`), "the primary is the link out");
    assert.ok(res.body.includes("Sök jobbet"), "the apply heading");
    assert.ok(!res.body.includes(APPLICATION_PAGE), "no application page");
  });

  await check("a listed ad without a resolved kommun has no JobPosting and stays indexable", async () => {
    const res = await get(origin, `/jobb/${ID.listedNoKommun}`);
    assert.equal(res.status, 200);
    assert.equal(jsonLdBlocks(res.body).length, 0);
    assert.doesNotMatch(res.body, /<meta name="robots" content="[^"]*noindex/);
    assert.equal(res.headers.get("x-robots-tag"), null);
    assert.match(res.body, /<h1[^>]*>Sjuksköterska till vårdcentral<\/h1>/);
  });

  await check("a listed ad costs the API two reads (the probe, then the render), both for the visitor and with no credential", async () => {
    seen.length = 0;
    await get(origin, `/jobb/${ID.listed}`, { headers: { cookie: "other=1", authorization: "Bearer x" } });
    const calls = callsFor(ID.listed);
    assert.equal(calls.length, 2);
    for (const call of calls) {
      assert.equal(call.headers["x-forwarded-for"], CLIENT_IP);
      assert.equal(call.headers.cookie, undefined);
      assert.equal(call.headers.authorization, undefined);
    }
  });

  await check("a listed ad costs the API exactly one related read, for the visitor and with no credential", async () => {
    related.mode = "ok";
    seen.length = 0;
    await get(origin, `/jobb/${ID.listed}`, { headers: { cookie: "other=1", authorization: "Bearer x" } });
    const calls = relatedCalls();
    assert.equal(calls.length, 1);
    assert.equal(calls[0].path, `/api/v1/public/job-ads/${ID.listed}/related`);
    assert.equal(calls[0].headers["x-forwarded-for"], CLIENT_IP);
    assert.equal(calls[0].headers.cookie, undefined);
    assert.equal(calls[0].headers.authorization, undefined);
  });

  await check("a listed ad shows its related ads after the article in one data-nosnippet section, before the explainer links, in no head tag and no JobPosting", async () => {
    related.mode = "ok";
    const res = await get(origin, `/jobb/${ID.listed}`);
    assert.equal(res.status, 200);
    assert.deepEqual(nosnippetElements(res.body), ["share", "sign-in", "related"], "the share row, the sign-in footer and the related section");
    const section = /<section[^<>]*aria-labelledby="jp-related-title"[^<>]*\sdata-nosnippet[^<>]*>([\s\S]*?)<\/section>/.exec(res.body);
    assert.ok(section, "the related section is rendered");
    assert.match(section[0], /^<section[^<>]*aria-labelledby="jp-related-title"/);
    assert.match(section[1], /<h2 id="jp-related-title" class="jp-eyebrow">Andra jobb i samma yrkesgrupp<\/h2>/);
    const rows = [...section[1].matchAll(/<h3 class="jp-job__title"><a href="\/jobb\/([0-9a-f-]{36})"/g)].map((m) => m[1]);
    assert.deepEqual(rows, RELATED.map((r) => r.id), "five h3 rows, each a plain link to its job page");
    const articleEnd = res.body.indexOf("</article>");
    const sectionAt = res.body.indexOf(section[0]);
    const explainersAt = res.body.indexOf(">Relaterade sidor<");
    assert.ok(articleEnd > 0 && articleEnd < sectionAt && sectionAt < explainersAt, "the article, then related, then the explainer links");
    const head = res.body.slice(0, res.body.indexOf("</head>"));
    const [block] = jsonLdBlocks(res.body);
    for (const row of RELATED) {
      assert.ok(!head.includes(row.title) && !head.includes(row.companyName), "no related value in the title, a description or an OpenGraph tag");
      assert.ok(!block.includes(row.title) && !block.includes(row.companyName), "no related value in the JobPosting");
    }
  });

  await check("a listed ad with no related rows has no section", async () => {
    related.mode = "ok";
    const res = await get(origin, `/jobb/${ID.listedNoKommun}`);
    assert.equal(res.status, 200);
    assert.ok(!res.body.includes("jp-related-title"));
    assert.deepEqual(nosnippetElements(res.body), ["share", "sign-in"], "only the share row and the sign-in footer");
  });

  for (const mode of ["budget", "outage", "tooWide", "timeout"]) {
    await check(`a related read the backend cannot give (${mode}) leaves the section out, and the ad is still a 200 within the bound`, async () => {
      related.mode = mode;
      try {
        const started = Date.now();
        const res = await get(origin, `/jobb/${ID.listed}`);
        const elapsed = Date.now() - started;
        assert.equal(res.status, 200);
        assert.match(res.body, /<h1[^>]*>Sjuksköterska till vårdcentral<\/h1>/);
        assert.ok(!res.body.includes("jp-related-title"), "no section");
        assert.ok(!res.body.includes("En text som aldrig får visas"), "no member beyond the six reaches the page");
        assert.ok(elapsed < 5_000, `the stub holds its answer 7 s and the web waits 2 s at most; the page took ${elapsed} ms`);
      } finally {
        related.mode = "ok";
      }
    });
  }

  await check("an ad the probe does not answer 200 (404, 410, the budget, an outage) makes no related read", async () => {
    seen.length = 0;
    for (const id of [ID.unknown, ID.ended, ID.erased, ID.budget, ID.outage]) await get(origin, `/jobb/${id}`);
    assert.ok(publicCalls().length >= 5, "non-vacuity: the probe ran for each");
    assert.equal(relatedCalls().length, 0);
  });

  await check("an ended and an erased ad are one 410 page: same status, same headers, same landmark, one read each", async () => {
    seen.length = 0;
    const ended = await get(origin, `/jobb/${ID.ended}`);
    const erased = await get(origin, `/jobb/${ID.erased}`);
    for (const res of [ended, erased]) {
      assert.equal(res.status, 410);
      assert.match(res.headers.get("cache-control") ?? "", /no-store/);
      assert.equal(res.headers.get("x-robots-tag"), "noindex");
      assert.match(res.body, /Annonsen är inte längre tillgänglig/);
      assert.doesNotMatch(res.body, /Sjuksköterska|Region Exempel/);
    }
    assert.equal(mainOf(ended.body), mainOf(erased.body));
    assert.equal(jsonLdBlocks(ended.body).length, 0, "no JobPosting on the 410 page");
    assert.equal(ended.headers.get("content-length") === null, erased.headers.get("content-length") === null);
    assert.equal(callsFor(ID.ended).length, 1);
    assert.equal(callsFor(ID.erased).length, 1);
  });

  await check("a HEAD on an ended ad is a 410 too", async () => {
    const res = await get(origin, `/jobb/${ID.ended}`, { method: "HEAD" });
    assert.equal(res.status, 410);
  });

  await check("an unknown ad is a real 404 in the site frame, after one read", async () => {
    seen.length = 0;
    const res = await get(origin, `/jobb/${ID.unknown}`);
    assert.equal(res.status, 404);
    assert.match(res.body, /<main[^>]*id="main"/);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    assert.equal(callsFor(ID.unknown).length, 1);
  });

  await check("the budget (an upstream 429) is a 503 with a bounded Retry-After, noindexed", async () => {
    const res = await get(origin, `/jobb/${ID.budget}`);
    assert.equal(res.status, 503);
    assert.equal(res.headers.get("retry-after"), "9");
    assert.equal(res.headers.get("x-robots-tag"), "noindex");
    assert.match(res.body, /Annonsen kan inte visas just nu/);
    assert.match(res.body, /<main[^>]*id="main"/);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    assert.equal(jsonLdBlocks(res.body).length, 0, "no JobPosting on the 503 page");
  });

  await check("an outage (an upstream 500) is a 503, never a 404 or a 410", async () => {
    const res = await get(origin, `/jobb/${ID.outage}`);
    assert.equal(res.status, 503);
    assert.ok(res.headers.get("retry-after"));
    assert.match(res.body, /<main[^>]*id="main"/);
  });

  await check("a read that fails after the probe answered 200 is a 5xx, never a 404", async () => {
    const res = await get(origin, `/jobb/${ID.raceOutage}`);
    assert.ok(res.status >= 500 && res.status < 600, `expected a 5xx, got ${res.status}`);
  });

  await check("an ad that ends between the probe and the render is a real 404", async () => {
    const res = await get(origin, `/jobb/${ID.raceEnded}`);
    assert.equal(res.status, 404);
    // Next answers a page's own notFound() here with its error shell, which the browser renders into the frame, so
    // the raw HTML carries no <main>; the contract is the status and the noindex.
    assert.match(res.body, /<meta name="robots" content="noindex"/);
  });

  await check("the internal pages are not URLs: a direct request is a 404", async () => {
    seen.length = 0;
    for (const path of [`/publik/jobb/${ID.listed}`, "/publik/borttagen", "/publik/overbelastad", "/%70ublik/borttagen"]) {
      const res = await get(origin, path);
      assert.equal(res.status, 404, path);
      assert.match(res.body, /<main[^>]*id="main"/, path);
    }
    assert.equal(publicCalls().length, 0);
  });

  await check("a Server Action posted to the page's own address (the language switch) is not sent to the login page", async () => {
    const res = await get(origin, `/jobb/${ID.listed}`, {
      method: "POST",
      headers: { "next-action": "4000deadbeefdeadbeefdeadbeefdeadbeefdead", "content-type": "text/plain;charset=UTF-8" },
      body: "[]",
    });
    assert.notEqual(res.status, 307, "an action POST must not meet the login redirect");
    assert.ok(!(res.headers.get("location") ?? "").includes("/logga-in"));
  });

  await check("a POST to the page's address without next-action still meets the login page", async () => {
    const res = await get(origin, `/jobb/${ID.listed}`, { method: "POST", headers: { "content-type": "text/plain" }, body: "x" });
    assert.equal(res.status, 307);
    assert.match(res.headers.get("location") ?? "", /\/logga-in/);
  });

  await check("a request with a session cookie keeps today's route: the refresh driver, never the public read", async () => {
    seen.length = 0;
    const res = await get(origin, `/jobb/${ID.listed}`, { headers: { cookie: `${SESSION_COOKIE}=stub-session` } });
    assert.equal(res.status, 307);
    assert.match(res.headers.get("location") ?? "", /\/logga-in/);
    assert.equal(publicCalls().length, 0);
    assert.ok(seen.some((r) => r.path === "/api/v1/auth/refresh"), "the signed-in path asked the session endpoint");
  });

  await check("anonymous requests that are not the public detail still meet the login page", async () => {
    seen.length = 0;
    for (const path of ["/jobb", "/jobb/not-a-guid", `/jobb/${ID.listed}/extra`, "/oversikt"]) {
      const res = await get(origin, path);
      assert.equal(res.status, 307, path);
      assert.match(res.headers.get("location") ?? "", /\/logga-in/, path);
    }
    assert.equal(publicCalls().length, 0);
  });

  await check("an upper-case GUID is redirected to its one lower-case URL without a read", async () => {
    seen.length = 0;
    const res = await get(origin, `/jobb/${ID.hexCase.toUpperCase()}`);
    assert.equal(res.status, 308);
    assert.ok((res.headers.get("location") ?? "").endsWith(`/jobb/${ID.hexCase}`));
    assert.equal(publicCalls().length, 0);
  });

  await check("robots.txt blocks the training crawlers, opens /jobb/<uuid>, keeps the list and the app closed", async () => {
    const res = await get(origin, "/robots.txt");
    assert.equal(res.status, 200);
    assert.match(
      res.body,
      /User-Agent: GPTBot\nUser-Agent: CCBot\nUser-Agent: ClaudeBot\nUser-Agent: Applebot-Extended\nUser-Agent: Amazonbot\nUser-Agent: meta-externalagent\nDisallow: \/\n/i,
    );
    assert.match(res.body, /User-Agent: \*\nAllow: \//i);
    assert.match(res.body, /Disallow: \/jobb\$/);
    assert.match(res.body, /Disallow: \/jobb\?/);
    assert.doesNotMatch(res.body, /Disallow: \/jobb\/\s/);
    assert.doesNotMatch(res.body, /publik/);
    assert.doesNotMatch(res.body, /Google-Extended/i);
    assert.match(res.body, /Sitemap: \S+\/sitemap\.xml/);
  });

  await check("the sitemap index lists the marketing part and both job parts at the root, uncacheable, reading no data", async () => {
    seen.length = 0;
    const res = await get(origin, "/sitemap.xml");
    assert.equal(res.status, 200);
    assert.match(res.headers.get("content-type") ?? "", /^application\/xml/);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    assert.match(res.body, /<sitemapindex xmlns="http:\/\/www\.sitemaps\.org\/schemas\/sitemap\/0\.9">/);
    assert.deepEqual(locsOf(res.body).map(pathOf), ["/sitemap-sidor.xml", "/sitemap-jobb-1.xml", "/sitemap-jobb-2.xml"]);
    assert.equal(listedIdsCalls().length, 0);
  });

  await check("a HEAD on the sitemap index is a 200", async () => {
    const res = await get(origin, "/sitemap.xml", { method: "HEAD" });
    assert.equal(res.status, 200);
  });

  await check("the pages part lists the 14 marketing pages and the catalogue's first page, a loc each and nothing else", async () => {
    const res = await get(origin, "/sitemap-sidor.xml");
    assert.equal(res.status, 200);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    const paths = locsOf(res.body).map(pathOf);
    assert.equal(paths.length, 15);
    assert.equal(paths[0], "/");
    assert.equal(paths[14], "/lediga-jobb");
    assert.ok(locsOf(res.body).every((loc) => !loc.includes("sida=")), "no catalogue page beyond the first");
    assert.doesNotMatch(res.body, /<(lastmod|changefreq|priority)>/);
  });

  await check("the catalogue's first page is a 200, uncacheable, read per request for the visitor with no credential", async () => {
    catalogue.mode = "ok";
    seen.length = 0;
    const res = await get(origin, "/lediga-jobb", { headers: { cookie: "other=1", authorization: "Bearer x" } });
    assert.equal(res.status, 200);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    assert.match(res.body, /<main[^>]*id="main"/);
    assert.match(res.body, /<h1[^>]*>Lediga jobb<\/h1>/);
    assert.match(res.body, /<link rel="canonical" href="[^"?]*\/lediga-jobb"/);
    assert.doesNotMatch(res.body, /<meta name="robots" content="[^"]*noindex/);
    assert.equal(res.headers.get("x-robots-tag"), null);
    assert.equal(jsonLdBlocks(res.body).length, 0, "no structured data on a list page");
    const calls = catalogueCalls();
    assert.equal(calls.length, 1, "one read, shared by the metadata and the page");
    assert.equal(new URL(calls[0].path, "http://stub").searchParams.toString(), "page=1");
    assert.equal(calls[0].headers["x-forwarded-for"], CLIENT_IP);
    assert.equal(calls[0].headers.cookie, undefined);
    assert.equal(calls[0].headers.authorization, undefined);
    console.log(`       (document size of /lediga-jobb: ${Buffer.byteLength(res.body)} bytes)`);
  });

  await check("the catalogue keeps the count, the list and the pager in one data-nosnippet block, and its rows link the job pages", async () => {
    catalogue.mode = "ok";
    const res = await get(origin, "/lediga-jobb");
    // The RSC payload repeats the attribute inside a <script>; the rendered markup carries it on one element.
    assert.equal((res.body.match(/<[a-z]+[^<>]*\sdata-nosnippet/g) ?? []).length, 1);
    const block = /<div data-nosnippet[^>]*>([\s\S]*?)<\/main>/.exec(res.body);
    assert.ok(block, "the block is inside main");
    assert.match(block[1], /<b>41<\/b> (<!-- -->)?lediga jobb/, "React's server render may put a text marker between the two nodes");
    assert.match(block[1], /<ol[^>]*class="jp-jobs/);
    assert.match(block[1], /<nav[^>]*aria-label="Paginering"/);
    assert.match(block[1], new RegExp(`<a href="/jobb/${catalogueRow(1).id}" class="jp-job__rowlink"`));
    const head = res.body.slice(0, res.body.indexOf("</head>"));
    assert.doesNotMatch(head, /Undersköterska|Kommun Exempel|\b41\b/, "no row value and no count in the head");
  });

  await check("the header marks /lediga-jobb as the current page there, and nowhere else", async () => {
    catalogue.mode = "ok";
    const catalogueRes = await get(origin, "/lediga-jobb?sida=2");
    assert.match(catalogueRes.body, /<a[^>]*aria-current="page"[^>]*href="\/lediga-jobb"|<a[^>]*href="\/lediga-jobb"[^>]*aria-current="page"/);
    const home = await get(origin, "/om");
    assert.match(home.body, /<a[^>]*href="\/lediga-jobb"/);
    assert.doesNotMatch(home.body, /<a[^>]*href="\/lediga-jobb"[^>]*aria-current|<a[^>]*aria-current="page"[^>]*href="\/lediga-jobb"/);
  });

  await check("a later catalogue page carries its own canonical and reads its own page", async () => {
    catalogue.mode = "ok";
    seen.length = 0;
    const res = await get(origin, "/lediga-jobb?sida=2");
    assert.equal(res.status, 200);
    assert.match(res.body, /<link rel="canonical" href="[^"]*\/lediga-jobb\?sida=2"/);
    assert.match(res.body, /<title>Lediga jobb – sida 2/);
    assert.deepEqual(catalogueCalls().map((c) => new URL(c.path, "http://stub").searchParams.toString()), ["page=2"]);
  });

  await check("an unknown parameter is ignored and kept out of the canonical", async () => {
    catalogue.mode = "ok";
    const res = await get(origin, "/lediga-jobb?utm_source=x");
    assert.equal(res.status, 200);
    assert.match(res.body, /<link rel="canonical" href="[^"?]*\/lediga-jobb"/);
    assert.doesNotMatch(res.body, /<meta name="robots" content="[^"]*noindex/);
  });

  await check("sida=1 is a permanent redirect to the bare address, with no read", async () => {
    seen.length = 0;
    const res = await get(origin, "/lediga-jobb?sida=1&utm_source=x");
    assert.equal(res.status, 308);
    assert.match(res.headers.get("location") ?? "", /\/lediga-jobb$/);
    assert.equal(catalogueCalls().length, 0);
  });

  await check("a sida that is not a page is a real 404 in the site frame, with no read", async () => {
    seen.length = 0;
    for (const query of ["?sida=0", "?sida=abc", "?sida=02", "?sida=2&sida=3"]) {
      const res = await get(origin, `/lediga-jobb${query}`);
      assert.equal(res.status, 404, query);
      assert.match(res.headers.get("cache-control") ?? "", /no-store/, query);
      assert.match(res.body, /<meta name="robots" content="noindex"/, query);
    }
    assert.equal(catalogueCalls().length, 0);
  });

  await check("a page past the end is a real 404", async () => {
    catalogue.mode = "ok";
    const res = await get(origin, "/lediga-jobb?sida=9");
    assert.equal(res.status, 404);
    assert.match(res.headers.get("cache-control") ?? "", /no-store/);
    assert.match(res.body, /<meta name="robots" content="noindex"/);
  });

  for (const mode of ["outage", "budget", "unavailable", "timeout"]) {
    await check(`a catalogue page the backend cannot give (${mode}) is a 5xx, never a 404`, async () => {
      catalogue.mode = mode;
      try {
        const res = await get(origin, "/lediga-jobb");
        assert.ok(res.status >= 500 && res.status < 600, `expected a 5xx, got ${res.status}`);
        assert.match(res.headers.get("cache-control") ?? "", /no-store/);
      } finally {
        catalogue.mode = "ok";
      }
    });
  }

  for (const [agent, ua] of [["a browser", BROWSER], ["Googlebot", GOOGLEBOT]]) {
    await check(`the catalogue's title, description and canonical sit in the head for ${agent} (M5)`, async () => {
      catalogue.mode = "ok";
      const res = await get(origin, "/lediga-jobb?sida=2", { headers: { "user-agent": ua } });
      assert.equal(res.status, 200);
      assert.ok(inHead(res.body, /<title>/), "title in the head");
      assert.ok(inHead(res.body, /<meta name="description"/), "description in the head");
      assert.ok(inHead(res.body, /<link rel="canonical"/), "canonical in the head");
    });
  }

  await check("the job parts list every listed id once, at its page's canonical path, read per request with the crawler's address and no credential", async () => {
    listedIds.mode = "ok";
    seen.length = 0;
    const locs = [];
    for (const part of [1, 2]) {
      const res = await get(origin, `/sitemap-jobb-${part}.xml?part=9`, { headers: { cookie: "other=1", authorization: "Bearer x" } });
      assert.equal(res.status, 200, `part ${part}`);
      assert.match(res.headers.get("cache-control") ?? "", /no-store/);
      assert.match(res.body, /<urlset xmlns="http:\/\/www\.sitemaps\.org\/schemas\/sitemap\/0\.9">/);
      assert.doesNotMatch(res.body, /<(lastmod|changefreq|priority)>/);
      locs.push(...locsOf(res.body));
    }
    assert.deepEqual(locs.map(pathOf).sort(), [`/jobb/${ID.listed}`, `/jobb/${ID.listedNoKommun}`].sort());
    const calls = listedIdsCalls();
    assert.deepEqual(calls.map((c) => new URL(c.path, "http://stub").searchParams.toString()), ["part=1&parts=2", "part=2&parts=2"]);
    for (const call of calls) {
      assert.equal(call.headers["x-forwarded-for"], CLIENT_IP);
      assert.equal(call.headers.cookie, undefined);
      assert.equal(call.headers.authorization, undefined);
    }
  });

  await check("a job part's location is the public page, a 200 with no noindex", async () => {
    listedIds.mode = "ok";
    const part = await get(origin, "/sitemap-jobb-1.xml");
    const [loc] = locsOf(part.body);
    const res = await get(origin, pathOf(loc));
    assert.equal(res.status, 200);
    assert.equal(res.headers.get("x-robots-tag"), null);
    assert.match(res.body, new RegExp(`<link rel="canonical" href="${loc.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}"`));
  });

  for (const [mode, retryAfter] of [["outage", "30"], ["budget", "9"], ["unavailable", "60"], ["upperCase", "30"], ["timeout", "30"]]) {
    await check(`a job part the backend cannot give (${mode}) is a 503 with Retry-After ${retryAfter}, no body, never an empty part`, async () => {
      listedIds.mode = mode;
      try {
        const res = await get(origin, "/sitemap-jobb-2.xml");
        assert.equal(res.status, 503);
        assert.equal(res.headers.get("retry-after"), retryAfter);
        assert.match(res.headers.get("cache-control") ?? "", /no-store/);
        assert.equal(res.body, "");
      } finally {
        listedIds.mode = "ok";
      }
    });
  }

  await check("a job part the sitemap does not declare is a 404", async () => {
    seen.length = 0;
    const res = await get(origin, "/sitemap-jobb-3.xml");
    assert.equal(res.status, 404);
    assert.equal(listedIdsCalls().length, 0);
  });

  const failed = results.filter((r) => !r.ok);
  console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
  if (failed.length > 0) {
    exitCode = 1;
    console.error(`--- server output (tail) ---\n${appLog.split("\n").slice(-40).join("\n")}`);
  }
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  console.error("--- server output (tail) ---\n" + appLog.split("\n").slice(-40).join("\n"));
  exitCode = 1;
} finally {
  app.kill();
  stub.close();
}
process.exit(exitCode);
