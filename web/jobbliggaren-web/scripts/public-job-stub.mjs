/**
 * The stub backend of the public job page, its related ads, the sitemap's job parts and the public catalogue (ADR 0165,
 * ADR 0182, ADR 0174 and ADR 0176, SEO epic #2090). One stub, two readers:
 *
 * - `public-job-smoke.mjs` asserts the HTTP contract against it, and sets the modes below to reach every outcome.
 * - `lighthouse-server.mjs` serves the pages Lighthouse measures from it, in the default mode, so a measured page is
 *   the page the smoke asserts on.
 *
 * It answers the detail by id, so those cases are fixed: a listed ad, one with the employer's application page (#2214
 * PR 1b), an ended and an erased one (both 410), an unknown
 * one, the budget (429), an outage (500), and the two races where the render's read disagrees with the probe's. It
 * answers the sitemap's parts in a mode a check sets: the ids, an outage, the budget, its own 503, ids in the wrong case,
 * or an answer held past the web's timeout. It answers the catalogue's pages in a mode too: three pages of rows, a page
 * past the end, the budget, its own 503, an outage, or an answer held past the web's timeout. It answers the related ads
 * in a mode too: five rows under the listed ad and none under the one without a kommun, the budget, an outage, a row
 * with a member beyond the six, or an answer held past the web's 2-second timeout. Any other path is a 401.
 */
import { createServer } from "node:http";

export const ID = {
  listed: "11111111-1111-4111-8111-111111111111",
  ended: "22222222-2222-4222-8222-222222222222",
  erased: "33333333-3333-4333-8333-333333333333",
  unknown: "44444444-4444-4444-8444-444444444444",
  budget: "55555555-5555-4555-8555-555555555555",
  outage: "66666666-6666-4666-8666-666666666666",
  raceOutage: "77777777-7777-4777-8777-777777777777",
  raceEnded: "88888888-8888-4888-8888-888888888888",
  /** Listed, but with no resolved kommun: no JobPosting, still indexable (ADR 0171). */
  listedNoKommun: "99999999-9999-4999-8999-999999999999",
  /** Listed, with the employer's application page as the backend's gate admitted it: the head ends with Ansök. */
  listedApply: "12121212-1212-4212-8212-121212121212",
  /** Hex digits that have a case, for the upper-case redirect; no read is ever made for it. */
  hexCase: "abcdefab-abcd-4abc-8abc-abcdefabcdef",
};

export const AD = {
  id: ID.listed,
  title: "Sjuksköterska till vårdcentral",
  companyName: "Region Exempel",
  description: "Om tjänsten\n\nVi söker dig som vill arbeta med primärvård.",
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00+00:00",
  expiresAt: "2026-11-01T08:00:00+00:00",
  municipalityConceptId: "AvNB_uwa_6n6",
  municipalityName: "Stockholm",
  regionConceptId: "CifL_Rzy_Mku",
  regionName: "Stockholms län",
  occupationGroupConceptId: "Z8ci_bBE_tmx",
  occupationGroupName: "Grundutbildade sjuksköterskor",
  employmentTypeConceptId: "gro4_cWF_6D7", // Vikariat
  worktimeExtentConceptId: "6YE1_gAC_R2G", // Heltid
};

/** The application page `ID.listedApply` carries. */
export const APPLICATION_PAGE = "https://jobb.example.se/ansok/1";

const GONE_BODY = JSON.stringify({ title: "Annonsen är inte längre tillgänglig.", status: 410 });

/** Every request the stub saw, in order: { method, path, headers }. */
export const seen = [];
const hitsByAd = new Map();

/** The sitemap parts' ids, each in the part its first hex digit puts it in (0–7 → 1, 8–f → 2; ADR 0174). */
const LISTED_IDS = { 1: [ID.listed], 2: [ID.listedNoKommun] };
/** How the stub answers a sitemap part; a check sets it, and the checks reset it to "ok". */
export const listedIds = { mode: "ok" };

function answerListedIds(url, res, send) {
  const part = Number(url.searchParams.get("part"));
  const ids = LISTED_IDS[part] ?? [];
  switch (listedIds.mode) {
    case "ok":
      return send(200, JSON.stringify({ ids }));
    case "upperCase":
      // Today's API cannot send this (System.Text.Json writes a Guid in lower case); a later API release could.
      // An id with hex letters, so the upper-case form is a different string.
      return send(200, JSON.stringify({ ids: [ID.hexCase.toUpperCase()] }));
    case "budget":
      return send(429, "{}", { "retry-after": "9" });
    case "unavailable":
      return send(503, "", { "retry-after": "60" });
    case "timeout": {
      // Held past the web's 5-second timeout, so only the web's own clock can end the read.
      const held = setTimeout(() => {
        if (!res.destroyed) send(200, JSON.stringify({ ids }));
      }, 7_000);
      res.on("close", () => clearTimeout(held));
      return undefined;
    }
    default:
      return send(500, "{}");
  }
}

/** The catalogue's rows by page: 41 listed ads, so three pages, the last of them short. */
const CATALOGUE_TOTAL = 41;
export const catalogueRow = (n) => ({
  id: `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, "0")}`,
  title: `Undersköterska ${n}`,
  companyName: "Kommun Exempel",
  municipalityName: n % 2 === 0 ? "Umeå" : null,
  regionName: "Västerbottens län",
  publishedAt: "2026-10-01T08:00:00+00:00",
});
/** How the stub answers a catalogue page; a check sets it, and the checks reset it to "ok". */
export const catalogue = { mode: "ok" };

function answerCatalogue(url, res, send) {
  const page = Number(url.searchParams.get("page"));
  const pages = Math.ceil(CATALOGUE_TOTAL / 20);
  switch (catalogue.mode) {
    case "ok": {
      if (!Number.isInteger(page) || page < 1) return send(400, "{}");
      if (page > pages) return send(404, "");
      const from = (page - 1) * 20 + 1;
      const items = Array.from({ length: Math.min(20, CATALOGUE_TOTAL - from + 1) }, (_, i) => catalogueRow(from + i));
      return send(200, JSON.stringify({ items, totalCount: CATALOGUE_TOTAL, page, pageSize: 20, totalPages: pages }));
    }
    case "budget":
      return send(429, "{}", { "retry-after": "9" });
    case "unavailable":
      return send(503, "", { "retry-after": "60" });
    case "timeout": {
      const held = setTimeout(() => {
        if (!res.destroyed) send(200, "{}");
      }, 7_000);
      res.on("close", () => clearTimeout(held));
      return undefined;
    }
    default:
      return send(500, "{}");
  }
}

/** The related rows under the listed ad: five employers, one row each, the last with no place (ADR 0182). */
export const relatedRow = (n) => ({
  id: `bbbbbbbb-0000-4000-8000-${String(n).padStart(12, "0")}`,
  title: `Distriktssköterska ${n}`,
  companyName: `Vårdgivare ${n} AB`,
  municipalityName: n < 3 ? "Stockholm" : n < 5 ? "Solna" : null,
  regionName: n < 5 ? "Stockholms län" : null,
  publishedAt: `2026-10-0${n}T08:00:00+00:00`,
});
/** How the stub answers the related ads; a check sets it, and the checks reset it to "ok". */
export const related = { mode: "ok" };

function answerRelated(id, res, send) {
  const items = id === ID.listed ? [1, 2, 3, 4, 5].map(relatedRow) : [];
  switch (related.mode) {
    case "ok":
      return id === ID.listed || id === ID.listedNoKommun || id === ID.listedApply
        ? send(200, JSON.stringify({ items }))
        : send(404, "{}");
    case "budget":
      return send(429, "{}", { "retry-after": "9" });
    case "tooWide":
      return send(200, JSON.stringify({ items: [{ ...relatedRow(1), description: "En text som aldrig får visas." }] }));
    case "timeout": {
      // Held past the web's 2-second timeout, so only the web's own clock can end the read.
      const held = setTimeout(() => {
        if (!res.destroyed) send(200, JSON.stringify({ items }));
      }, 7_000);
      res.on("close", () => clearTimeout(held));
      return undefined;
    }
    default:
      return send(500, "{}");
  }
}

export function stubHandler(req, res) {
  seen.push({ method: req.method, path: req.url, headers: { ...req.headers } });

  const send = (status, body = "", headers = {}) =>
    res.writeHead(status, { "content-type": "application/json", "cache-control": "no-store", ...headers }).end(body);

  const url = new URL(req.url ?? "/", "http://stub");
  if (url.pathname === "/api/v1/public/job-ads/listed-ids") {
    return answerListedIds(url, res, send);
  }
  if (url.pathname === "/api/v1/public/job-ads/catalogue") {
    return answerCatalogue(url, res, send);
  }
  const relatedMatch = /^\/api\/v1\/public\/job-ads\/([0-9a-f-]{36})\/related$/.exec(url.pathname);
  if (relatedMatch !== null) {
    return answerRelated(relatedMatch[1], res, send);
  }

  const match = /^\/api\/v1\/public\/job-ads\/([0-9a-f-]{36})$/.exec(req.url ?? "");
  if (match === null) {
    res.writeHead(401, { "content-type": "application/json" }).end("{}");
    return;
  }

  const id = match[1];
  const hit = (hitsByAd.get(id) ?? 0) + 1;
  hitsByAd.set(id, hit);

  switch (id) {
    case ID.listed:
      return send(200, JSON.stringify(AD));
    case ID.listedNoKommun:
      return send(200, JSON.stringify({ ...AD, id, municipalityConceptId: null, municipalityName: null }));
    case ID.listedApply:
      return send(200, JSON.stringify({ ...AD, id, applicationUrl: APPLICATION_PAGE }));
    case ID.ended:
    case ID.erased:
      return send(410, GONE_BODY);
    case ID.budget:
      return send(429, "{}", { "retry-after": "9" });
    case ID.outage:
      return send(500, "{}");
    case ID.raceOutage:
      return hit === 1 ? send(200, JSON.stringify({ ...AD, id })) : send(500, "{}");
    case ID.raceEnded:
      return hit === 1 ? send(200, JSON.stringify({ ...AD, id })) : send(410, GONE_BODY);
    default:
      return send(404, "{}");
  }
}

export function listen(server) {
  return new Promise((resolveListen) => server.listen(0, "127.0.0.1", () => resolveListen(server.address().port)));
}

export async function freePort() {
  const probe = createServer();
  const port = await listen(probe);
  await new Promise((done) => probe.close(done));
  return port;
}

/** Starts the stub on a free loopback port: `{ server, port }`. The caller closes `server`. */
export async function startStub() {
  const server = createServer(stubHandler);
  const port = await listen(server);
  return { server, port };
}
