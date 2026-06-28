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

const SUBSTITUTABILITY_VERSION = '1';
const FETCHED_AT = '2026-06-28';
const RELATION_KIND = 'substitutability';

const here = dirname(fileURLToPath(import.meta.url));
const V30_MAP_PATH = resolve(
  here,
  '../../src/Jobbliggaren.Infrastructure/Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json',
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

  console.log('Hämtar occupation-name `substitutes` (GraphQL, ett anrop)...');
  const nodes = await fetchSubstitutes();
  console.log(`  ${nodes.length} occupation-names.`);

  // Rollup: för varje (m → s)-substitut-kant, mappa båda ändar till ssyk-4 och
  // emittera (Gm → Gs) om båda är mappade och Gm != Gs (ett yrke är inte
  // relaterat till sig självt). Dedupliceras via Set per käll-grupp.
  const relatedBySource = new Map(); // ssyk-4 G → Set<ssyk-4>
  let unmappedSourceNodes = 0;
  let unmappedTargetEdges = 0;
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
      '2026-06-28). `substitutability` ar modellerad occupation-name <-> occupation-name i ' +
      'JobTech-taxonomin (live-verifierat: ssyk-4-grupper har tomma substitutes). Vi rullar upp ' +
      'occupation-name-`substitutes` (utgaaende riktning, v1) till ssyk-4 via den frusna v30-mappen ' +
      '(LASES read-only). Aggregation: any-member, platt (en ssyk-4-grupp Y blir relaterad till X om ' +
      'NAGON medlems-occupation-name i X har ett substitut vars ssyk-4 ar Y). Emitterad grain: ' +
      'ssyk-4 -> ssyk-4. Determinism: byConceptId-sorterat. Substitut-maal utanfoer v30 hoppas ' +
      '(graceful). Bump substitutabilityVersion for att tvinga re-seed. Se ' +
      'docs/reviews/2026-06-28-300-f1-premise-correction-cto.md.',
    relations,
  };

  writeFileSync(OUTPUT_PATH, JSON.stringify(artifact, null, 2) + '\n', 'utf8');
  console.log(
    `Skrev ${OUTPUT_PATH}:\n` +
      `  ${relations.length} kall-grupper med relaterade, ${totalEdges} ssyk-4->ssyk-4-kanter ` +
      `(${edgeCount} raa fore dedup).\n` +
      `  Diagnostik: ${unmappedSourceNodes} occupation-names utanfor v30 (med substitutes, ej upprullbara), ` +
      `${unmappedTargetEdges} substitut-kanter med omappat maal (hoppade).`,
  );
}

main().catch((err) => {
  console.error('FEL:', err.message);
  process.exit(1);
});
