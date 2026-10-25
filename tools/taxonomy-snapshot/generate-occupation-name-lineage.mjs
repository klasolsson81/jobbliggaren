// Generator for occupation-name-lineage.json: every JobTech occupation-name, deprecated ones
// included, with its replaced-by edges and its occupation group (#2199).
//
// Off-build and run by hand, like its siblings (ADR 0043 Beslut B: no build-time fetch, no
// runtime hop). The committed JSON is the source of truth; this script is its reviewable
// reproduction.
//
// Why: about a quarter of active ads carry an occupation-name that JobTech has deprecated, and
// JobTech's own occupation-name filter folds such an ad into the name that replaced it. An exact
// occupation-name filter on /jobb has to do the same, so it needs the replacement edges. Each
// version keeps only some of them: v31 dropped 6 of v30's edges and added 103, so the file holds
// the union over VERSIONS. The rule that reads it (OccupationNameLineage) counts replacements on
// the current names an edge set leads to, not on the raw edges, because a naive union turns a
// re-pointed edge into a false second replacement.
//
// Never the frozen v30 crosswalk (migration-owned) and never the picker snapshot.
//
// Run: node tools/taxonomy-snapshot/generate-occupation-name-lineage.mjs
// Needs Node 18+ (built-in fetch). No npm dependency.

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { gql, byConceptId } from './lib.mjs';

const VERSIONS = ['30', '31'];
const LINEAGE_VERSION = '1';
const FETCHED_AT = '2026-10-24';
const MIN_CONCEPTS_PER_VERSION = 4000;
const CONCEPT_ID = /^[A-Za-z0-9_-]{1,32}$/;

const here = dirname(fileURLToPath(import.meta.url));
const OUTPUT_PATH = resolve(
  here,
  '../../src/Jobbliggaren.Infrastructure/Taxonomy/occupation-name-lineage.json',
);

const fail = (message) => {
  throw new Error(message);
};

async function fetchVersion(version) {
  const concepts = await gql(
    `{ concepts(type: "occupation-name", include_deprecated: true, version: "${version}") ` +
      '{ id preferred_label deprecated replaced_by { id type } broader(type: "ssyk-level-4") { id } } }',
  );
  if (concepts.length < MIN_CONCEPTS_PER_VERSION) {
    fail(`v${version}: ${concepts.length} occupation-names, expected at least ${MIN_CONCEPTS_PER_VERSION}`);
  }
  for (const c of concepts) {
    if (!CONCEPT_ID.test(c.id)) fail(`v${version}: concept id outside the grammar: ${JSON.stringify(c.id)}`);
    if (!c.deprecated && c.broader.length !== 1) {
      fail(`v${version}: current ${c.id} has ${c.broader.length} groups, expected exactly 1`);
    }
    if (c.broader.length > 1) fail(`v${version}: ${c.id} has ${c.broader.length} groups`);
    if (!c.deprecated && c.replaced_by.length > 0) fail(`v${version}: current ${c.id} has replaced_by edges`);
    for (const r of c.replaced_by) {
      if (r.type !== 'occupation-name') fail(`v${version}: ${c.id} is replaced by a ${r.type}`);
      if (r.id === c.id) fail(`v${version}: ${c.id} replaces itself`);
    }
  }
  return concepts;
}

// Depth-first walk of the merged graph; a back edge is a cycle.
function assertAcyclic(edges) {
  const state = new Map(); // id -> 'open' | 'done'
  const visit = (id, path) => {
    if (state.get(id) === 'done') return;
    if (state.get(id) === 'open') fail(`replaced_by cycle: ${[...path, id].join(' -> ')}`);
    state.set(id, 'open');
    for (const next of edges.get(id) ?? []) visit(next, [...path, id]);
    state.set(id, 'done');
  };
  for (const id of edges.keys()) visit(id, []);
}

async function main() {
  const byVersion = new Map();
  for (const version of VERSIONS) {
    console.log(`Fetching occupation-names, include_deprecated, taxonomy v${version}...`);
    byVersion.set(version, new Map((await fetchVersion(version)).map((c) => [c.id, c])));
  }

  const latest = byVersion.get(VERSIONS.at(-1));
  const ids = new Set();
  for (const concepts of byVersion.values()) for (const id of concepts.keys()) ids.add(id);

  const occupationNames = [];
  const edges = new Map();
  let edgesOnlyInEarlier = 0;
  let edgesOnlyInLatest = 0;

  for (const id of ids) {
    const seen = VERSIONS.map((v) => byVersion.get(v).get(id)).filter(Boolean);
    const groups = new Set(seen.flatMap((c) => c.broader.map((b) => b.id)));
    if (groups.size > 1) fail(`${id} changed group between versions: ${[...groups].join(', ')}`);

    const replacedBy = new Set(seen.flatMap((c) => c.replaced_by.map((r) => r.id)));
    for (const target of replacedBy) if (!ids.has(target)) fail(`${id} is replaced by unknown ${target}`);
    if (replacedBy.size > 0) edges.set(id, [...replacedBy]);

    const latestEdges = new Set((latest.get(id)?.replaced_by ?? []).map((r) => r.id));
    for (const target of replacedBy) if (!latestEdges.has(target)) edgesOnlyInEarlier += 1;
    for (const target of latestEdges) {
      const earlier = VERSIONS.slice(0, -1).some((v) =>
        (byVersion.get(v).get(id)?.replaced_by ?? []).some((r) => r.id === target));
      if (!earlier) edgesOnlyInLatest += 1;
    }

    const label = (latest.get(id) ?? seen.at(-1)).preferred_label;
    occupationNames.push({
      conceptId: id,
      label,
      groupConceptId: groups.size === 1 ? [...groups][0] : null,
      currentIn: VERSIONS.filter((v) => byVersion.get(v).get(id)?.deprecated === false),
      replacedByConceptIds: [...replacedBy].sort((a, b) => byConceptId({ conceptId: a }, { conceptId: b })),
    });
  }

  assertAcyclic(edges);

  // The current names each id leads to; diagnostics and the multi-terminal group check only.
  const terminals = new Map();
  const terminalsOf = (id) => {
    if (terminals.has(id)) return terminals.get(id);
    const next = edges.get(id);
    const result = next ? new Set(next.flatMap((t) => [...terminalsOf(t)])) : new Set([id]);
    terminals.set(id, result);
    return result;
  };
  const groupOf = new Map(occupationNames.map((n) => [n.conceptId, n.groupConceptId]));
  let multiTerminal = 0;
  let collapsedByTerminals = 0;
  let resolvableByOwnGroup = 0;
  for (const [id, next] of edges) {
    const ts = [...terminalsOf(id)];
    if (next.length > 1 && ts.length === 1) collapsedByTerminals += 1;
    if (ts.length < 2) continue;
    multiTerminal += 1;
    for (const t of ts) {
      if (!groupOf.get(t)) fail(`${t}, a terminal of multi-replacement ${id}, has no group`);
      if (latest.get(t)?.deprecated !== false) fail(`${t}, a terminal of ${id}, is not current in v${VERSIONS.at(-1)}`);
    }
    if (ts.filter((t) => groupOf.get(t) === groupOf.get(id)).length === 1) resolvableByOwnGroup += 1;
  }

  occupationNames.sort(byConceptId);

  const artifact = {
    source:
      'JobTech Taxonomy GraphQL concepts(type: "occupation-name", include_deprecated: true) at ' +
      `versions ${VERSIONS.join(' and ')}: replaced_by, and broader(type: "ssyk-level-4") as the group`,
    lineageVersion: LINEAGE_VERSION,
    taxonomyVersions: VERSIONS,
    fetchedAt: FETCHED_AT,
    note:
      'replacedByConceptIds is the union of the replaced_by edges over taxonomyVersions; currentIn ' +
      'lists the versions in which the name is not deprecated. Terminals are computed at load by ' +
      'OccupationNameLineage (#2199). Never the frozen v30 crosswalk, never the picker snapshot. ' +
      'Regenerate with tools/taxonomy-snapshot/generate-occupation-name-lineage.mjs.',
    occupationNames,
  };

  // One entry per line: a refresh then diffs as one line per changed name, at half the size of a
  // fully indented file.
  const { occupationNames: entries, ...header } = artifact;
  const headerJson = JSON.stringify(header, null, 2).replace(/\n}$/, '');
  const body = entries.map((e) => `    ${JSON.stringify(e)}`).join(',\n');
  writeFileSync(OUTPUT_PATH, `${headerJson},\n  "occupationNames": [\n${body}\n  ]\n}\n`, 'utf8');
  console.log(
    `Wrote ${OUTPUT_PATH}:\n` +
      `  ${occupationNames.length} occupation-names, ${edges.size} with replaced_by edges.\n` +
      `  Edges absent from v${VERSIONS.at(-1)}: ${edgesOnlyInEarlier}; new in v${VERSIONS.at(-1)}: ${edgesOnlyInLatest}.\n` +
      `  Multi-terminal sources: ${multiTerminal}; of them resolvable by their own group: ${resolvableByOwnGroup}.\n` +
      `  Raw multi-edge sources that lead to one current name: ${collapsedByTerminals}.`,
  );
}

main().catch((err) => {
  console.error('ERROR:', err.message);
  process.exit(1);
});
