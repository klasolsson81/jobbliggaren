// Yrkesgrupp-paritets-audit — Platsbanken sök-paritet (ADR 0067 / TD-100 item 3).
//
// Off-build, MANUELLT körd audit (samma hermetik-kontrakt som generate.mjs —
// ADR 0043 Beslut B: INGEN build-tids-fetch, INGEN runtime-extern-hop, INGEN
// CI-gate). Detta script är inte ett byggsteg eller ett CI-test; det är ett
// granskningsbart bevis på att den committade snapshotens yrkesgrupp-SET
// (ssyk-level-4) är komplett & aktuellt mot JobTech-taxonomin — vilket per
// ADR 0067 Beslut 1 ÄR Platsbankens källa på list-nivå (arbetsformedlingen.se
// filtrerar yrke på ssyk-level-4 ur samma JobTech-taxonomi).
//
// Avgränsning (medveten): detta bevisar LIST-paritet (är yrkesgrupp-mängden
// komplett & aktuell), INTE annons-träff-paritet (ger val X här samma annonser
// som val X på Platsbankens realtids-UI). Hit-count-stickprovet är timing-
// känsligt mot en extern realtidskälla och spåras separat (TD-100b).
//
// Kör: node tools/taxonomy-snapshot/audit-parity.mjs
// Krav: Node 18+ (inbyggd fetch). Ingen npm-dependency.
// Exit: 0 vid full paritet, 1 vid drift (fail-loud, paritet med generate.mjs).
// Skriver alltid en datumstämplad rapport-artefakt (även vid drift = bevis).

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
// DRY (Hunt/Thomas 1999): återanvänd GraphQL-fetchen + fail-loud-regeln ur
// lib.mjs — ingen ny fetch-kod (samma maskineri som generate.mjs konsumerar).
import { fetchChildren, byConceptId, GRAPHQL } from './lib.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const SNAPSHOT_PATH = resolve(
  here,
  '../../src/Jobbliggaren.Infrastructure/Taxonomy/taxonomy-snapshot.json',
);
const REPORT_PATH = resolve(here, 'parity-audit-report.json');

// Plattar snapshotens nestade yrkesgrupper till {conceptId, label, parentConceptId}
// (parent = det yrkesområde gruppen ligger under) — samma form som live-pullen.
// Fail-loud vid duplicerat conceptId: utan denna vakt skulle en grupp nestad
// under två yrkesområden (generate.mjs verifierar parent-existens men inte att
// barnet är unikt) kollapsa last-wins i diff-Map:en och kunna maskera en
// faktisk drift som PARITY. Samma fail-loud-doktrin som lib.mjs parent-kardinalitet.
function snapshotGroups(snap) {
  const out = [];
  const seen = new Set();
  for (const field of snap.occupationFields) {
    for (const g of field.occupationGroups ?? []) {
      if (seen.has(g.conceptId)) {
        throw new Error(
          `Yrkesgrupp ${g.conceptId} (${g.label}) förekommer mer än en gång i snapshoten ` +
            '(nestad under flera yrkesområden). Snapshoten är korrupt — kör generate.mjs och granska diffen.',
        );
      }
      seen.add(g.conceptId);
      out.push({ conceptId: g.conceptId, label: g.label, parentConceptId: field.conceptId });
    }
  }
  return out;
}

// Diffar två set på conceptId; rapporterar saknade/extra/label-drift/parent-drift.
function diff(snapList, liveList) {
  const snapById = new Map(snapList.map((g) => [g.conceptId, g]));
  const liveById = new Map(liveList.map((g) => [g.conceptId, g]));

  const missingInSnapshot = []; // i live, saknas i snapshot
  const labelDrift = [];
  const parentDrift = [];
  for (const live of liveList) {
    const snap = snapById.get(live.conceptId);
    if (!snap) {
      missingInSnapshot.push({ conceptId: live.conceptId, label: live.label });
      continue;
    }
    if (snap.label !== live.label) {
      labelDrift.push({ conceptId: live.conceptId, snapshotLabel: snap.label, liveLabel: live.label });
    }
    if (snap.parentConceptId !== live.parentConceptId) {
      parentDrift.push({
        conceptId: live.conceptId,
        snapshotParent: snap.parentConceptId,
        liveParent: live.parentConceptId,
      });
    }
  }

  const extraInSnapshot = []; // i snapshot, saknas i live (avvecklat i taxonomin)
  for (const snap of snapList) {
    if (!liveById.has(snap.conceptId)) {
      extraInSnapshot.push({ conceptId: snap.conceptId, label: snap.label });
    }
  }

  missingInSnapshot.sort(byConceptId);
  extraInSnapshot.sort(byConceptId);
  labelDrift.sort(byConceptId);
  parentDrift.sort(byConceptId);
  return { missingInSnapshot, extraInSnapshot, labelDrift, parentDrift };
}

// Härleder yrkesområdes-id-mängden ur live-gruppernas parents (gratis sanity-
// check av occupation-field-paritet utan en extra query — label-paritet på fält
// kräver egen pull och är utanför TD-100:s yrkesgrupp-kärna).
// Blindfläck (medveten): ett NYTT yrkesområde i taxonomin som ännu saknar
// ssyk-level-4-barn är osynligt för derivationen (inga grupp-parents pekar på
// det) → fält-id-paritet kan ge falsk PASS för ett barnlöst fält. Att fånga det
// kräver en separat fetchChildren(..., 'occupation-field')-pull; utanför scope
// eftersom filter-dimensionen är yrkesgruppen, inte det tomma fältet.
function fieldIdParity(snap, liveGroups) {
  const snapFieldIds = new Set(snap.occupationFields.map((f) => f.conceptId));
  const liveFieldIds = new Set(liveGroups.map((g) => g.parentConceptId));
  const missingInSnapshot = [...liveFieldIds].filter((id) => !snapFieldIds.has(id)).sort();
  const extraInSnapshot = [...snapFieldIds].filter((id) => !liveFieldIds.has(id)).sort();
  return {
    snapshotCount: snapFieldIds.size,
    liveCount: liveFieldIds.size,
    missingInSnapshot,
    extraInSnapshot,
  };
}

async function main() {
  console.log('Läser committad snapshot:', SNAPSHOT_PATH);
  const snap = JSON.parse(readFileSync(SNAPSHOT_PATH, 'utf8'));
  const snapGroups = snapshotGroups(snap);
  console.log(
    `  snapshot v${snap.taxonomyVersion} (fetchedAt ${snap.fetchedAt}): ` +
      `${snap.occupationFields.length} yrkesområden, ${snapGroups.length} yrkesgrupper`,
  );

  console.log(`Hämtar live yrkesgrupper (ssyk-level-4 broader→occupation-field) från ${GRAPHQL} ...`);
  const liveGroups = await fetchChildren('ssyk-level-4', 'occupation-field');
  console.log(`  ${liveGroups.length} yrkesgrupper live`);

  const groupDiff = diff(snapGroups, liveGroups);
  const fieldDiff = fieldIdParity(snap, liveGroups);

  const groupParity =
    groupDiff.missingInSnapshot.length === 0 &&
    groupDiff.extraInSnapshot.length === 0 &&
    groupDiff.labelDrift.length === 0 &&
    groupDiff.parentDrift.length === 0;
  const fieldParity =
    fieldDiff.missingInSnapshot.length === 0 && fieldDiff.extraInSnapshot.length === 0;
  const parity = groupParity && fieldParity;

  const report = {
    auditedAt: new Date().toISOString().slice(0, 10),
    result: parity ? 'PARITY' : 'DRIFT',
    source: `${GRAPHQL} (ssyk-level-4 broader→occupation-field)`,
    snapshotVersion: snap.taxonomyVersion,
    snapshotFetchedAt: snap.fetchedAt,
    scope:
      'List-paritet på yrkesgrupp-SET (ssyk-level-4) + occupation-field-id-set ' +
      '(fält-id-setet härleds ur grupp-parents → ett barnlöst nytt fält fångas ej). ' +
      'Bevisar INTE annons-träff-paritet mot Platsbankens realtids-UI (TD-100b).',
    occupationGroups: {
      snapshotCount: snapGroups.length,
      liveCount: liveGroups.length,
      ...groupDiff,
    },
    occupationFields: fieldDiff,
  };

  writeFileSync(REPORT_PATH, JSON.stringify(report, null, 2) + '\n', 'utf8');

  console.log('');
  console.log(`Yrkesgrupper: snapshot ${snapGroups.length} / live ${liveGroups.length}`);
  console.log(`  saknas i snapshot: ${groupDiff.missingInSnapshot.length}`);
  console.log(`  extra i snapshot:  ${groupDiff.extraInSnapshot.length}`);
  console.log(`  label-drift:       ${groupDiff.labelDrift.length}`);
  console.log(`  parent-drift:      ${groupDiff.parentDrift.length}`);
  console.log(`Yrkesområden (id-set): snapshot ${fieldDiff.snapshotCount} / live ${fieldDiff.liveCount}`);
  console.log(`  saknas: ${fieldDiff.missingInSnapshot.length}  extra: ${fieldDiff.extraInSnapshot.length}`);
  console.log('');
  console.log(`Rapport skriven: ${REPORT_PATH}`);
  console.log(`RESULTAT: ${report.result}`);

  if (!parity) {
    console.error(
      'DRIFT: snapshoten är inte i synk med JobTech-taxonomin. ' +
        'Kör tools/taxonomy-snapshot/generate.mjs (ny version) och granska diffen.',
    );
    process.exit(1);
  }
}

main().catch((err) => {
  console.error('FEL:', err.message);
  process.exit(1);
});
