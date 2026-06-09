// One-shot-generator för den FRUSNA migrations-ägda reverse-lookup-resursen
// occupation-name → ssyk-level-4 (Platsbanken sök-paritet Fas C2, ADR 0067
// Beslut 1 + senior-cto-advisor-dom (b)/(c) 2026-06-09).
//
// KÖRD EN GÅNG 2026-06-09. Output-filen är en frusen migration-ägd artefakt —
// kör ALDRIG om detta script mot samma fil (migrations-immutabilitet,
// Fowler/Sadalage: en applicerad migration ändrar aldrig betydelse). Behöver
// en FRAMTIDA migration en färskare mappning: generera en NY versionerad fil
// (…v31.json) som den nya migrationen äger.
//
// Källa: JobTech Taxonomy GraphQL broader-relationen (occupation-name →
// ssyk-level-4), live-verifierad 2026-06-09: 2179/2179 occupation-names har
// exakt 1 ssyk-level-4-parent. fetchChildren fail-loud:ar vid ≠1 parent.
//
// Kör: node tools/taxonomy-snapshot/generate-occupation-group-mapping.mjs
// Krav: Node 18+ (inbyggd fetch). Ingen npm-dependency.

import { writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { fetchChildren, byConceptId } from './lib.mjs';

const TAXONOMY_VERSION = '30';
const FETCHED_AT = '2026-06-09';

const here = dirname(fileURLToPath(import.meta.url));
const OUTPUT_PATH = resolve(
  here,
  '../../src/JobbPilot.Infrastructure/Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json',
);

async function main() {
  if (existsSync(OUTPUT_PATH)) {
    throw new Error(
      `${OUTPUT_PATH} finns redan. Filen är FRUSEN (migration-ägd) — ` +
        'generera en ny versionerad fil i stället för att skriva över.',
    );
  }

  console.log('Hämtar occupation-name → ssyk-level-4 (broader-relation)...');
  const occupations = await fetchChildren('occupation-name', 'ssyk-level-4');
  console.log(`  ${occupations.length} occupation-names, alla med exakt 1 parent.`);

  occupations.sort(byConceptId);

  const mappings = {};
  for (const o of occupations) mappings[o.conceptId] = o.parentConceptId;

  const artifact = {
    taxonomyVersion: TAXONOMY_VERSION,
    fetchedAt: FETCHED_AT,
    note:
      'FRUSEN migration-aagd artefakt (C2 reverse-lookup, ADR 0067 Beslut 1) - regenereras ALDRIG ' +
      '(senior-cto-advisor-dom (c) 2026-06-09, migrations-immutabilitet). Mappning occupation-name ' +
      '(yrke) -> ssyk-level-4 (yrkesgrupp) via JobTech Taxonomy broader-relationen, deterministisk ' +
      'single-parent (2179/2179 live-verifierat 2026-06-09). Genererad av ' +
      'tools/taxonomy-snapshot/generate-occupation-group-mapping.mjs (one-shot).',
    mappings,
  };

  writeFileSync(OUTPUT_PATH, JSON.stringify(artifact, null, 2) + '\n', 'utf8');
  console.log(`Skrev frusen mappnings-resurs: ${OUTPUT_PATH} (${occupations.length} poster).`);
}

main().catch((err) => {
  console.error('FEL:', err.message);
  process.exit(1);
});
