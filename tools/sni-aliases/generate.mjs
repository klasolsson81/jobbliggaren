#!/usr/bin/env node
// Generates sni-aliases-2025.v1.json — the search-alias asset for the bransch filter (#1115).
//
// Off-build and manually run. NEVER a CI gate: it fetches a live external site, which ADR 0043
// Beslut B forbids in the build (the same warning audit-parity.mjs carries).
//
// Source: SCB SNI-sök (https://snisok.scb.se/<code>), the section "Exempel på vad som ingår i
// denna SNI-kod". SCB open data is CC0 (policy since 2021-07-01); the SNI-sök pages carry no
// separate licence notice — verified 2026-09-05. Same basis on which sni-2025.v1.json ships.
//
// This is NOT a crosswalk. Every code is an SNI 2025 code that exists in sni-2025.v1.json, and no
// SSYK/JobTech concept id appears anywhere in the output. See README.md.
//
//   node tools/sni-aliases/generate.mjs            # use cache, write asset
//   node tools/sni-aliases/generate.mjs --refetch  # re-fetch every page first
//
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

const ROOT = path.resolve(import.meta.dirname, "../..");
const HERE = import.meta.dirname;
const CACHE = path.join(HERE, ".cache");
const REF = path.join(ROOT, "src/Jobbliggaren.Infrastructure/CompanyRegister/Reference");
const OUT = path.join(REF, "sni-aliases-2025.v1.json");
const BASE = "https://snisok.scb.se";
const UA =
  "jobbliggaren-sni-alias-generator/1.0 (+https://github.com/klasolsson81/jobbliggaren/issues/1115)";
const FETCHED_AT = "2026-09-05";

const lc = (s) => s.toLocaleLowerCase("sv-SE");
const ordinal = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
const die = (m) => {
  console.error(`FEL: ${m}`);
  process.exit(1);
};
const readJson = (p) => JSON.parse(fs.readFileSync(p, "utf8"));

const sni = readJson(path.join(REF, "sni-2025.v1.json"));
const demand = readJson(path.join(HERE, "demand-terms.json"));
const authored = readJson(path.join(HERE, "authored-terms.json"));

const nodes = [...sni.sections, ...sni.divisions, ...sni.leaves];
const codeExists = new Set(nodes.map((n) => n.code));

if (demand.measuredAgainst !== sni.sniVersion)
  die(`demand-terms.json är mätt mot ${demand.measuredAgainst}, katalogen är ${sni.sniVersion}`);

// --- fetch -------------------------------------------------------------------
fs.mkdirSync(CACHE, { recursive: true });
const refetch = process.argv.includes("--refetch");
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function page(code) {
  const f = path.join(CACHE, `${code}.html`);
  if (!refetch && fs.existsSync(f)) return fs.readFileSync(f, "utf8");
  const res = await fetch(`${BASE}/${code}`, { headers: { "user-agent": UA } });
  if (!res.ok) die(`${BASE}/${code} svarade ${res.status}`);
  const html = await res.text();
  fs.writeFileSync(f, html);
  await sleep(120); // deliberate: 835 pages against a public authority's site
  return html;
}

// --- extract -----------------------------------------------------------------
const NAMED = {
  "&amp;": "&",
  "&lt;": "<",
  "&gt;": ">",
  "&quot;": String.fromCharCode(34),
  "&apos;": String.fromCharCode(39),
  "&nbsp;": " ",
};
const decode = (s) =>
  s
    .replace(/&#x([0-9A-Fa-f]+);/g, (_, h) => String.fromCodePoint(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(Number(d)))
    .replace(/&[a-zA-Z]+;/g, (m) => NAMED[m] ?? m);

/** The "Exempel på vad som ingår i denna SNI-kod" list — one flat <ul> after that <h2>. */
export function extractPhrases(html) {
  const at = html.indexOf("Exempel p");
  if (at < 0) return [];
  const ul = html.indexOf("<ul>", at);
  const end = html.indexOf("</ul>", ul);
  if (ul < 0 || end < 0) return [];
  return [...html.slice(ul, end).matchAll(/<li>([\s\S]*?)<\/li>/g)]
    .map((m) =>
      decode(m[1])
        .replace(/<[^>]*>/g, " ")
        .replace(/\s+/g, " ")
        .trim()
        .replace(/,$/, ""),
    )
    .filter(Boolean);
}

// --- select ------------------------------------------------------------------
const demandTerms = demand.terms.map(lc);
const short = demandTerms.filter((t) => t.length < 4);
if (short.length) die(`efterfrågetermer kortare än 4 tecken: ${short.join(", ")}`);

const scbByCode = new Map();
const coverage = new Map(demandTerms.map((t) => [t, 0]));

console.log(`Läser ${sni.leaves.length} detaljgrupper...`);
for (const leaf of sni.leaves) {
  const phrases = extractPhrases(await page(leaf.code));
  const own = lc(leaf.name);
  const keep = new Set();
  for (const p of phrases) {
    const lp = lc(p);
    const hits = demandTerms.filter((t) => lp.includes(t));
    if (!hits.length) continue;
    for (const h of hits) coverage.set(h, coverage.get(h) + 1);
    // A phrase the node's own name already contains adds no reach — it matches today.
    if (own.includes(lp)) continue;
    keep.add(p); // verbatim: normalisation is for matching only, never for the stored string
  }
  if (keep.size) scbByCode.set(leaf.code, [...keep].sort(ordinal));
}

// --- authored residue --------------------------------------------------------
// Entry condition (CTO 2026-09-05, decision D): a word may be authored ONLY where it is on the
// demand list AND measured zero SCB coverage. Enforced here so nobody can add a favourite word.
const authoredByCode = new Map();
for (const row of authored.terms) {
  const t = lc(row.term);
  if (!coverage.has(t)) die(`författad term "${row.term}" står inte på efterfrågelistan`);
  if (coverage.get(t) !== 0)
    die(`författad term "${row.term}" har ${coverage.get(t)} SCB-träffar — författas inte (villkor 1)`);
  if (!row.codes?.length) die(`författad term "${row.term}" saknar koder`);
  if (!row.why?.trim()) die(`författad term "${row.term}" saknar motivering`);
  for (const c of row.codes) {
    if (!codeExists.has(c))
      die(`författad term "${row.term}" pekar på ${c}, som inte finns i ${sni.sniVersion}`);
    if (!authoredByCode.has(c)) authoredByCode.set(c, []);
    authoredByCode.get(c).push(row.term);
  }
}

// --- emit --------------------------------------------------------------------
const aliases = [
  ...[...scbByCode].map(([code, terms]) => ({ code, source: "scb", terms })),
  ...[...authoredByCode].map(([code, terms]) => ({
    code,
    source: "authored",
    terms: terms.sort(ordinal),
  })),
].sort((a, b) => ordinal(a.code, b.code) || ordinal(a.source, b.source));

const doc = {
  "//":
    `Search aliases over the SNI 2025 branch names (#1115). Two sources, and the "source" field says which per row. ` +
    `"scb": entries reproduced verbatim from SCB SNI-sök (${BASE}/<code>), section "Exempel på vad som ingår i denna SNI-kod", fetched ${FETCHED_AT}; ` +
    `SCB open data is CC0 (policy since 2021-07-01) and the SNI-sök pages carry no separate licence notice — verified ${FETCHED_AT}. ` +
    `"authored": written by this repo for demand words SCB does not carry at all, enumerated with rationale in tools/sni-aliases/authored-terms.json. ` +
    `NOT A CROSSWALK: every code is an SNI 2025 code present in sni-2025.v1.json, no SSYK/JobTech concept id appears anywhere in this file, ` +
    `and nothing derives or preselects — the user still picks the SNI node. The /jobb occupation synonyms (SearchSynonyms:Occupations in appsettings.json) ` +
    `are a different axis in a different bounded context; neither map is a mapping of the other. ` +
    `Only the terms matching a dated demand list ship, not SCB's whole register — see tools/sni-aliases/README.md for why. ` +
    `Regenerate with: node tools/sni-aliases/generate.mjs`,
  sniVersion: sni.sniVersion,
  aliasVersion: "2025.alias.v1",
  demandVersion: demand.demandVersion,
  fetchedAt: FETCHED_AT,
  aliases,
};

const json = JSON.stringify(doc, null, 1) + "\n";
fs.writeFileSync(OUT, json);

// --- report ------------------------------------------------------------------
const termCount = aliases.reduce((n, a) => n + a.terms.length, 0);
const gz = zlib.gzipSync(Buffer.from(json)).length;
console.log(`\nSkrev ${path.relative(ROOT, OUT).replace(/\\/g, "/")}`);
console.log(
  `  ${aliases.length} rader, ${termCount} termer över ${new Set(aliases.map((a) => a.code)).size} koder`,
);
console.log(`  ${(json.length / 1024).toFixed(1)} kB rå, ${(gz / 1024).toFixed(1)} kB gzip`);

const zero = [...coverage].filter(([, n]) => n === 0).map(([t]) => t);
console.log(`\nEfterfrågetermer utan SCB-täckning (${zero.length}/${coverage.size}): ${zero.join(", ") || "(inga)"}`);
const unauthored = zero.filter((t) => !authored.terms.some((r) => lc(r.term) === t));
if (unauthored.length) console.log(`  varav EJ författade (medvetet utelämnade): ${unauthored.join(", ")}`);
