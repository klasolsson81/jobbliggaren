// Delad GraphQL-mekanik för taxonomi-scripten (extraherad ur generate.mjs,
// Fas C2 2026-06-09 — DRY på knowledge-piece-nivå: fail-loud-regeln vid ≠1
// parent ska finnas i EN kopia). Konsumeras av generate.mjs (levande
// snapshot-regenerator) och generate-occupation-group-mapping.mjs (one-shot,
// frusen migrations-artefakt).

export const GRAPHQL = 'https://taxonomy.api.jobtechdev.se/v1/taxonomy/graphql';

export async function gql(query) {
  const res = await fetch(`${GRAPHQL}?query=${encodeURIComponent(query)}`);
  if (!res.ok) throw new Error(`GraphQL ${res.status} ${res.statusText}`);
  const body = await res.json();
  if (!body.data?.concepts) throw new Error(`Oväntat GraphQL-svar: ${JSON.stringify(body)}`);
  return body.data.concepts;
}

// Hämtar barn-noder med exakt en broader-parent. Fail-loud vid 0 eller >1 parent —
// determinism kräver entydig parent (annars PK-konflikt / godtycklig kanonisering).
export async function fetchChildren(childType, parentType) {
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
export const byConceptId = (a, b) => (a.conceptId < b.conceptId ? -1 : a.conceptId > b.conceptId ? 1 : 0);
