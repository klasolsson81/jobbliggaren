#!/usr/bin/env node
// Generates scb-tatorter-2025.v1.json — SCB's tätorter (localities) for the CV place lexicon (#2343).
//
// Off-build and manually run. NEVER a CI gate: it fetches a live external API, which ADR 0043
// Beslut B forbids in the build (the same warning tools/sni-aliases/generate.mjs carries).
//
// Source: SCB PxWeb v1, table MI0810A/LandarealTatortN ("Folkmängd och landareal i tätorter, per
// tätort"), Tid 2025, content code 000003F7 (Folkmängd). A tätort is shipped when SCB publishes a
// population for it in 2025; the table also lists earlier years' tätorter that no longer exist.
// SCB open data in the statistical database is CC0 1.0
// (https://www.scb.se/om-scb/om-scb.se-och-anvandningsvillkor/oppna-data-api/, read 2026-11-27).
//
// Names are reproduced verbatim, stray whitespace and "(preliminär beteckning)" included: matching
// normalises in C# and never changes the stored string.
//
//   node tools/scb-tatorter/generate.mjs            # use the cached API responses, write the asset
//   node tools/scb-tatorter/generate.mjs --refetch  # re-fetch both responses first
//
import fs from "node:fs";
import path from "node:path";

const ROOT = path.resolve(import.meta.dirname, "../..");
const HERE = import.meta.dirname;
const CACHE = path.join(HERE, ".cache");
const STAMP = path.join(CACHE, "fetched-at.txt");
const OUT = path.join(ROOT, "src/Jobbliggaren.Infrastructure/Resumes/Parsing/scb-tatorter-2025.v1.json");
const KOMMUNER = path.join(ROOT, "src/Jobbliggaren.Infrastructure/CompanyRegister/Reference/scb-kommuner-2026.v1.json");
const TABLE = "https://api.scb.se/OV0104/v1/doris/sv/ssd/START/MI/MI0810/MI0810A/LandarealTatortN";
const TABLE_NAME = "MI0810A/LandarealTatortN";
const YEAR = "2025";
const POPULATION = "000003F7";
const VERSION = "2025.v1";
const UA = "jobbliggaren-scb-tatort-generator/1.0 (+https://github.com/klasolsson81/jobbliggaren/issues/2343)";
const CODE = /^(\d{4})T[BC]\d{3}$/;

const die = (m) => {
  console.error(`FEL: ${m}`);
  process.exit(1);
};
const ordinal = (a, b) => (a < b ? -1 : a > b ? 1 : 0);

async function fetchJson(url, init) {
  const res = await fetch(url, { ...init, headers: { "User-Agent": UA, ...(init?.headers ?? {}) } });
  if (!res.ok) die(`${url} svarade ${res.status}`);
  // PxWeb answers with a UTF-8 BOM.
  return JSON.parse((await res.text()).replace(/^﻿/, ""));
}

async function load(refetch) {
  const metaFile = path.join(CACHE, "meta.json");
  const dataFile = path.join(CACHE, `data-${YEAR}.json`);
  if (refetch || !fs.existsSync(metaFile) || !fs.existsSync(dataFile)) {
    fs.mkdirSync(CACHE, { recursive: true });
    const meta = await fetchJson(TABLE);
    const data = await fetchJson(TABLE, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        query: [
          { code: "Region", selection: { filter: "all", values: ["*"] } },
          { code: "ContentsCode", selection: { filter: "item", values: [POPULATION] } },
          { code: "Tid", selection: { filter: "item", values: [YEAR] } },
        ],
        response: { format: "json" },
      }),
    });
    fs.writeFileSync(metaFile, JSON.stringify(meta));
    fs.writeFileSync(dataFile, JSON.stringify(data));
    fs.writeFileSync(STAMP, new Date().toISOString().slice(0, 10));
  }
  if (!fs.existsSync(STAMP)) die(`${path.relative(ROOT, STAMP)} saknas — kör med --refetch.`);
  return {
    meta: JSON.parse(fs.readFileSync(metaFile, "utf8")),
    data: JSON.parse(fs.readFileSync(dataFile, "utf8")),
    fetchedAt: fs.readFileSync(STAMP, "utf8").trim(),
  };
}

const { meta, data, fetchedAt } = await load(process.argv.includes("--refetch"));

const region = meta.variables.find((v) => v.code === "Region");
if (!region || region.values.length !== region.valueTexts.length) die("Region-variabeln saknas eller är ojämn");
const names = new Map(region.values.map((code, i) => [code, region.valueTexts[i]]));

const kommunCodes = new Set(JSON.parse(fs.readFileSync(KOMMUNER, "utf8")).kommuner.map((k) => k.code));

const tatorter = [];
for (const row of data.data) {
  const [code, year] = row.key;
  if (year !== YEAR) die(`rad för ${code} har år ${year}, inte ${YEAR}`);
  if (code === "T00") continue; // "Summa riket"
  const value = row.values[0];
  if (value === ".." || value === "-" || value === "") continue; // no tätort in 2025
  const match = CODE.exec(code);
  if (!match) die(`oväntad tätortskod ${code}`);
  if (!kommunCodes.has(match[1])) die(`${code} pekar på kommunkod ${match[1]}, som saknas i scb-kommuner-2026.v1.json`);
  const name = names.get(code);
  if (!name?.trim()) die(`${code} saknar namn`);
  tatorter.push({ code, name, kommunCode: match[1] });
}

if (new Set(tatorter.map((t) => t.code)).size !== tatorter.length) die("dubblerade tätortskoder");
// Parser-health floor, not a tuned value: SCB published 2,009 tätorter for 2025 (measured 2026-11-27).
if (tatorter.length < 1900) die(`bara ${tatorter.length} tätorter — svaret är trasigt`);
tatorter.sort((a, b) => ordinal(a.code, b.code));

const doc = {
  "//":
    `Swedish tätorter with a population in ${YEAR}. Source: SCB, table ${TABLE_NAME} ` +
    `("Folkmängd och landareal i tätorter, per tätort"), Tid ${YEAR}, content Folkmängd, fetched ${fetchedAt}; ` +
    `SCB open data in the statistical database is CC0 1.0. Names are verbatim; matching normalises them and never ` +
    `changes the stored string. Code = 4-digit kommun code + TB/TC + 3 digits. Generated by ` +
    `tools/scb-tatorter/generate.mjs (#2343).`,
  tatortVersion: VERSION,
  table: TABLE_NAME,
  year: YEAR,
  fetchedAt,
  tatorter,
};
const json = JSON.stringify(doc, null, 1) + "\n";

// The version is hand-set, so a regenerated list with new content must carry a new version.
if (fs.existsSync(OUT)) {
  const old = JSON.parse(fs.readFileSync(OUT, "utf8"));
  const same = JSON.stringify(old.tatorter) === JSON.stringify(tatorter);
  if (!same && old.tatortVersion === VERSION)
    die(`innehållet har ändrats men tatortVersion är fortfarande ${VERSION} — höj versionen`);
}

fs.writeFileSync(OUT, json);
console.log(`${tatorter.length} tätorter → ${path.relative(ROOT, OUT)} (${json.length} B, fetched ${fetchedAt})`);
