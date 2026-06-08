// Taxonomi-snapshot-generator — Platsbanken sök-paritet (ADR 0067 / ADR 0043-amendment 2026-06-08).
//
// Off-build, manuellt körd generator (ADR 0043 Beslut B — hermetisk build: INGEN
// build-tids-fetch, INGEN runtime-extern-hop). Reproducerbar dokumentation av hur
// `taxonomy-snapshot.json` utökas. JSON-filen är sanningskällan i repot; detta
// script är dess granskningsbara reproduktion (senior-cto-advisor Beslut 1 = Variant C,
// docs/reviews/2026-06-08-sok-paritet-b1-cto.md).
//
// ADDITIVT: läser befintlig snapshot, hämtar (a) kommun-noder (broader→region) och
// (b) ssyk-level-4-noder (broader→occupation-field) från JobTech Taxonomy GraphQL,
// nestar dem under matchande region/occupation-field, bumpar taxonomyVersion, skriver
// tillbaka. Befintliga regions/occupations rörs INTE (occupation-name bevaras per
// ADR 0067 Beslut 1 — byter roll, raderas ej). Kommun→region och ssyk-level-4→
// occupation-field är båda exakt 1:1 (verifierat 2026-06-08) → ingen dedup behövs.
//
// Kör: node tools/taxonomy-snapshot/generate.mjs
// Krav: Node 18+ (inbyggd fetch). Ingen npm-dependency.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const GRAPHQL = 'https://taxonomy.api.jobtechdev.se/v1/taxonomy/graphql';
const NEW_VERSION = '30';
const FETCHED_AT = '2026-06-08';

const here = dirname(fileURLToPath(import.meta.url));
const SNAPSHOT_PATH = resolve(
  here,
  '../../src/JobbPilot.Infrastructure/Taxonomy/taxonomy-snapshot.json',
);

async function gql(query) {
  const res = await fetch(`${GRAPHQL}?query=${encodeURIComponent(query)}`);
  if (!res.ok) throw new Error(`GraphQL ${res.status} ${res.statusText}`);
  const body = await res.json();
  if (!body.data?.concepts) throw new Error(`Oväntat GraphQL-svar: ${JSON.stringify(body)}`);
  return body.data.concepts;
}

// Hämtar barn-noder med exakt en broader-parent. Fail-loud vid 0 eller >1 parent —
// determinism kräver entydig parent (annars PK-konflikt / godtycklig kanonisering).
async function fetchChildren(childType, parentType) {
  const raw = await gql(
    `{ concepts(type: "${childType}") { id preferred_label broader(type: "${parentType}") { id } } }`,
  );
  return raw.map((c) => {
    if (c.broader.length !== 1) {
      throw new Error(
        `${childType} ${c.id} har ${c.broader.length} ${parentType}-parents (förväntat exakt 1). ` +
          'Dedup-regel krävs — avbryter hellre än kanoniserar godtyckligt.',
      );
    }
    return { conceptId: c.id, label: c.preferred_label, parentConceptId: c.broader[0].id };
  });
}

// Sorterar deterministiskt på conceptId (Ordinal/ASCII-stabil → diff-brus undviks
// oberoende av GraphQL-svarsordning och locale).
const byConceptId = (a, b) => (a.conceptId < b.conceptId ? -1 : a.conceptId > b.conceptId ? 1 : 0);

function groupByParent(children) {
  const map = new Map();
  for (const c of children) {
    if (!map.has(c.parentConceptId)) map.set(c.parentConceptId, []);
    map.get(c.parentConceptId).push({ conceptId: c.conceptId, label: c.label });
  }
  for (const arr of map.values()) arr.sort(byConceptId);
  return map;
}

function attach(parents, childrenByParent, key, label) {
  let attached = 0;
  for (const p of parents) {
    const kids = childrenByParent.get(p.conceptId);
    if (kids) {
      p[key] = kids;
      attached += kids.length;
    } else {
      p[key] = [];
      console.warn(`  VARNING: ${label}-parent ${p.conceptId} (${p.label}) saknar barn.`);
    }
  }
  // Fail-loud om GraphQL refererar en parent som inte finns i snapshoten.
  const known = new Set(parents.map((p) => p.conceptId));
  for (const parentId of childrenByParent.keys()) {
    if (!known.has(parentId)) {
      throw new Error(
        `${label}: GraphQL-parent ${parentId} finns ej i snapshoten. ` +
          'Snapshot region/occupation-field-listan är inte i synk med taxonomin.',
      );
    }
  }
  return attached;
}

async function main() {
  console.log('Läser befintlig snapshot:', SNAPSHOT_PATH);
  const snap = JSON.parse(readFileSync(SNAPSHOT_PATH, 'utf8'));
  console.log(`  version ${snap.taxonomyVersion}, ${snap.regions.length} regioner, ${snap.occupationFields.length} yrkesområden`);

  console.log('Hämtar kommuner (municipality broader→region)...');
  const municipalities = await fetchChildren('municipality', 'region');
  console.log(`  ${municipalities.length} kommuner`);

  console.log('Hämtar yrkesgrupper (ssyk-level-4 broader→occupation-field)...');
  const occupationGroups = await fetchChildren('ssyk-level-4', 'occupation-field');
  console.log(`  ${occupationGroups.length} yrkesgrupper`);

  const muniAttached = attach(snap.regions, groupByParent(municipalities), 'municipalities', 'Kommun');
  const groupAttached = attach(snap.occupationFields, groupByParent(occupationGroups), 'occupationGroups', 'Yrkesgrupp');

  snap.taxonomyVersion = NEW_VERSION;
  snap.fetchedAt = FETCHED_AT;
  snap.note =
    'Off-search-path snapshot per ADR 0043 (Variant A) + ADR 0043-amendment 2026-06-08 ' +
    '(kommun + ssyk-level-4 yrkesgrupp, ADR 0067). Genererad av tools/taxonomy-snapshot/generate.mjs. ' +
    'Kanoniskt dedupliserad occupation-name (varje yrke under exakt ETT primaert yrkesomrade). ' +
    'Kommun->region och ssyk-level-4->occupation-field aer baada exakt 1:1 (ingen dedup). ' +
    'Scope: Lan (region) -> Kommun (municipality) + Yrkesomrade (occupation-field) -> Yrke (occupation-name) + Yrkesgrupp (ssyk-level-4).';

  writeFileSync(SNAPSHOT_PATH, JSON.stringify(snap, null, 2) + '\n', 'utf8');
  console.log(
    `Skrev snapshot v${NEW_VERSION}: +${muniAttached} kommuner, +${groupAttached} yrkesgrupper.`,
  );
}

main().catch((err) => {
  console.error('FEL:', err.message);
  process.exit(1);
});
