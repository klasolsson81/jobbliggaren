// Generator för occupation-substitutability.json — yrkesmatchnings-breddning
// (#300 / ADR 0084, F1 premiss-korrigering 2026-06-28).
//
// Off-build, manuellt körd generator (ADR 0043 Beslut B — hermetisk build: INGEN
// build-tids-fetch, INGEN runtime-extern-hop). Den committade JSON-filen är
// sanningskällan i repot; detta script är dess granskningsbara reproduktion
// (Variant A — manuell regenerering + commit). Parity-precedens:
// generate-occupation-group-mapping.mjs (separat one-shot via lib.mjs).
//
// VARFÖR rollup: JobTechs `substitutability` är modellerad occupation-name ↔
// occupation-name (live-verifierat 2026-06-28: ssyk-4-grupper har TOMMA
// substitutes; occupation-names har dem). ADR 0084:s grind opererar på ssyk-4.
// Vi rullar därför upp occupation-name-`substitutes` till ssyk-4 via den FRUSNA
// occupation-name-to-ssyk-level-4.v30.json-mappen (LÄSES read-only — rör aldrig
// den migrations-ägda artefakten). Emitterad grain: ssyk-4 → ssyk-4 (CTO-bind
// docs/reviews/2026-06-28-300-f1-premise-correction-cto.md punkt 1/3).
//
// Riktning: `substitutes` (utgående) v1 — "yrken som kan ersätta G på
// arbetsmarknaden" = annonser en G-sökande rimligen är kvalificerad för
// (CTO-bind punkt 2). `substituted_by`→union är en namngiven additiv recall-våg.
// Aggregation: any-member, platt (CTO-bind punkt 4, viks in i svar B).
//
// Determinism: kanterna sorteras byConceptId (käll-grupp + relaterad-mängd) så
// JSON-diffen är stabil oberoende av GraphQL-svarsordning. Käll-occupation-names
// kommer ur v30 (garanterat mappade); substitut-mål utan v30-mappning är
// occupation-names utanför vår ssyk-4-mappade mängd → räknas + hoppas (graceful,
// paritet ACL:s "okänt id → tom, aldrig throw"), aldrig en tyst rollup-distortion.
//
// Kör: node tools/taxonomy-snapshot/generate-substitutability.mjs
// Krav: Node 18+ (inbyggd fetch). Ingen npm-dependency.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { gql, byConceptId } from './lib.mjs';

// v2 (2026-06-29, #359 / ADR 0084 same-field amendment): the rollup now drops
// cross-OccupationField edges (see the same-field guard below). Bumped to force
// the idempotent re-seed.
const SUBSTITUTABILITY_VERSION = '2';
const FETCHED_AT = '2026-06-28';
const RELATION_KIND = 'substitutability';

const here = dirname(fileURLToPath(import.meta.url));
const V30_MAP_PATH = resolve(
  here,
  '../../src/Jobbliggaren.Infrastructure/Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json',
);
// Frozen taxonomy snapshot — the ssyk-4 → occupation-field grain (1:1; every
// ssyk-4 group belongs to exactly one field). Read read-only to build the
// same-field guard without a second live fetch (parity with the v30 map).
const SNAPSHOT_PATH = resolve(
  here,
  '../../src/Jobbliggaren.Infrastructure/Taxonomy/taxonomy-snapshot.json',
);
const OUTPUT_PATH = resolve(
  here,
  '../../src/Jobbliggaren.Infrastructure/Taxonomy/occupation-substitutability.json',
);

// Hämtar alla occupation-name-noder med sina substitut-occupation-names i ETT
// anrop (paritet fetchChildren, men för substitutes-relationen). Filtrerar
// defensivt på type === occupation-name (substitutes ska vara occupation-name).
async function fetchSubstitutes() {
  const raw = await gql(
    '{ concepts(type: "occupation-name") { id substitutes { id type } } }',
  );
  return raw.map((c) => ({
    conceptId: c.id,
    substitutes: (c.substitutes ?? [])
      .filter((s) => s.type === 'occupation-name')
      .map((s) => s.id),
  }));
}

async function main() {
  console.log('Läser frusen v30-mappning (occupation-name → ssyk-level-4):', V30_MAP_PATH);
  const v30 = JSON.parse(readFileSync(V30_MAP_PATH, 'utf8'));
  const occToSsyk = v30.mappings;
  const mappedCount = Object.keys(occToSsyk).length;
  console.log(`  ${mappedCount} occupation-name → ssyk-4-mappningar.`);

  // #359 / ADR 0084 same-field amendment: build ssyk-4 → occupation-field map
  // from the frozen snapshot. Every ssyk-4 group sits under exactly one field;
  // the rollup below only emits an edge when both ends share a field.
  console.log('Läser frusen taxonomy-snapshot (ssyk-4 → occupation-field):', SNAPSHOT_PATH);
  const snapshot = JSON.parse(readFileSync(SNAPSHOT_PATH, 'utf8'));
  const fieldOf = new Map(); // ssyk-4 conceptId → occupation-field conceptId
  for (const f of snapshot.occupationFields) {
    for (const g of f.occupationGroups) fieldOf.set(g.conceptId, f.conceptId);
  }
  console.log(`  ${fieldOf.size} ssyk-4 → occupation-field-mappningar.`);

  console.log('Hämtar occupation-name `substitutes` (GraphQL, ett anrop)...');
  const nodes = await fetchSubstitutes();
  console.log(`  ${nodes.length} occupation-names.`);

  // Rollup: för varje (m → s)-substitut-kant, mappa båda ändar till ssyk-4 och
  // emittera (Gm → Gs) om båda är mappade och Gm != Gs (ett yrke är inte
  // relaterat till sig självt). Dedupliceras via Set per käll-grupp.
  const relatedBySource = new Map(); // ssyk-4 G → Set<ssyk-4>
  let unmappedSourceNodes = 0;
  let unmappedTargetEdges = 0;
  let crossFieldEdges = 0;
  let unknownFieldEdges = 0;
  let edgeCount = 0;

  for (const node of nodes) {
    const gm = occToSsyk[node.conceptId];
    if (!gm) {
      // occupation-name utanför v30 (saknar ssyk-4-mappning) — kan inte rullas upp.
      if (node.substitutes.length > 0) unmappedSourceNodes += 1;
      continue;
    }
    for (const targetId of node.substitutes) {
      const gs = occToSsyk[targetId];
      if (!gs) {
        unmappedTargetEdges += 1;
        continue;
      }
      if (gs === gm) continue; // samma grupp → inte en relaterad-kant
      // #359 / ADR 0084 same-field guard: emittera bara om käll- och mål-ssyk-4
      // ligger i SAMMA occupation-field. Detta tar bort cross-field-bryggor som
      // any-member-rollupen annars genererar (t.ex. mjukvaruutvecklare → handläggare:
      // Data/IT vs Administration/ekonomi/juridik). Okänt fält → droppas konservativt
      // (under-breddning är det trygga felläget för en civic-utility — vi visar hellre
      // för få relaterade än semantiskt fel). Båda fallen räknas (provenance-disciplin).
      const fm = fieldOf.get(gm);
      const fs = fieldOf.get(gs);
      if (fm === undefined || fs === undefined) {
        unknownFieldEdges += 1;
        continue;
      }
      if (fm !== fs) {
        crossFieldEdges += 1;
        continue;
      }
      if (!relatedBySource.has(gm)) relatedBySource.set(gm, new Set());
      relatedBySource.get(gm).add(gs);
      edgeCount += 1;
    }
  }

  // Deterministisk ordning: käll-grupper byConceptId; relaterad-mängd byConceptId.
  // OBS: byConceptId jämför `.conceptId`, så strängarna (relaterad-mängden) och
  // käll-objektens `sourceConceptId` wrappas i { conceptId } innan jämförelse.
  const relations = [...relatedBySource.entries()]
    .map(([sourceConceptId, set]) => ({
      sourceConceptId,
      relatedConceptIds: [...set].sort((a, b) => byConceptId({ conceptId: a }, { conceptId: b })),
    }))
    .sort((a, b) => byConceptId({ conceptId: a.sourceConceptId }, { conceptId: b.sourceConceptId }));

  const totalEdges = relations.reduce((n, r) => n + r.relatedConceptIds.length, 0);

  const artifact = {
    source:
      'JobTech Taxonomy GraphQL `substitutes` (occupation-name) rolled up to ssyk-level-4 ' +
      'via occupation-name-to-ssyk-level-4.v30.json',
    substitutabilityVersion: SUBSTITUTABILITY_VERSION,
    fetchedAt: FETCHED_AT,
    relationKind: RELATION_KIND,
    note:
      'Off-search-path snapshot per ADR 0043 (Variant A) + ADR 0084 (F1 premiss-korrigering ' +
      '2026-06-28; same-field-amendering 2026-06-29 / #359). `substitutability` ar modellerad ' +
      'occupation-name <-> occupation-name i JobTech-taxonomin (live-verifierat: ssyk-4-grupper ' +
      'har tomma substitutes). Vi rullar upp occupation-name-`substitutes` (utgaaende riktning) ' +
      'till ssyk-4 via den frusna v30-mappen (LASES read-only). Aggregation: any-member, platt, ' +
      'SAME-FIELD-CONSTRAINED (v2, #359): en ssyk-4-grupp Y blir relaterad till X om NAGON ' +
      'medlems-occupation-name i X har ett substitut vars ssyk-4 ar Y OCH X och Y ligger i samma ' +
      'occupation-field. Cross-field-bryggor droppas vid generering (any-member-rollupen lankade ' +
      'annars t.ex. mjukvaruutvecklare -> handlaggare). Okant falt droppas konservativt. Emitterad ' +
      'grain: ssyk-4 -> ssyk-4. Determinism: byConceptId-sorterat. Substitut-maal utanfoer v30 ' +
      'hoppas (graceful). Bump substitutabilityVersion for att tvinga re-seed. Se ' +
      'docs/reviews/2026-06-28-300-f1-premise-correction-cto.md + ADR 0084 (same-field-amendering).',
    relations,
  };

  writeFileSync(OUTPUT_PATH, JSON.stringify(artifact, null, 2) + '\n', 'utf8');
  console.log(
    `Skrev ${OUTPUT_PATH}:\n` +
      `  ${relations.length} kall-grupper med relaterade, ${totalEdges} ssyk-4->ssyk-4-kanter ` +
      `(${edgeCount} raa fore dedup).\n` +
      `  Diagnostik: ${unmappedSourceNodes} occupation-names utanfor v30 (med substitutes, ej upprullbara), ` +
      `${unmappedTargetEdges} substitut-kanter med omappat maal (hoppade), ` +
      `${crossFieldEdges} cross-field-kanter (droppade, #359 same-field), ` +
      `${unknownFieldEdges} kanter med okant occupation-field (droppade).`,
  );
}

main().catch((err) => {
  console.error('FEL:', err.message);
  process.exit(1);
});
