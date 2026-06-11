/**
 * Per-län-normalisering av Ort-valet (Fas E2b, CTO VAL 1 2026-06-11 —
 * docs/reviews/2026-06-11-sok-paritet-e2b-cto.md).
 *
 * Backend kombinerar region- och kommun-listorna som inkluderande UNION
 * (geografi är EN dimension i två granulariteter, ADR 0067 impl-notat E2b).
 * Normaliseringen här är därför ren UX-kosmetik, ingen korrekthets-bärare:
 * "hela län X" + "kommun i län X" är redundant state under union — vi håller
 * URL:en minimal och chipsen begripliga. Denormaliserat state som ändå
 * anländer (handredigerad URL, gammal recent-sökning) förblir korrekt
 * backend-side.
 *
 * Regler:
 * - Kommun-val i ett län där hela länet är valt → länets region-id tas bort
 *   (kommunvalet ersätter helläns-valet för det länet).
 * - "Välj alla kommuner" (= hela länet, ETT region-id — aldrig
 *   materialiserade kommun-ids; 414-skydd + en chip) → länets enskilda
 *   kommun-val rensas.
 * - Avmarkering tar bara bort det egna id:t.
 */

export interface OrtSelection {
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
}

/**
 * Applicerar popoverns nästa kommun-lista och normaliserar region-axeln:
 * varje NYTILLAGD kommun släcker sitt läns helläns-val.
 * `regionOfMunicipality`: kommun-conceptId → läns-conceptId (ur taxonomin).
 */
export function applyMunicipalityChange(
  current: OrtSelection,
  nextMunicipality: ReadonlyArray<string>,
  regionOfMunicipality: ReadonlyMap<string, string>,
): OrtSelection {
  const previous = new Set(current.municipality);
  const addedParentRegions = new Set<string>();
  for (const id of nextMunicipality) {
    if (!previous.has(id)) {
      const parent = regionOfMunicipality.get(id);
      if (parent) addedParentRegions.add(parent);
    }
  }
  return {
    region:
      addedParentRegions.size > 0
        ? current.region.filter((r) => !addedParentRegions.has(r))
        : current.region,
    municipality: nextMunicipality,
  };
}

/**
 * Togglar "hela länet" (region-id). Vid PÅ: länets enskilda kommun-val
 * rensas (redundanta under union). Vid AV: endast region-id:t tas bort —
 * kommun-val i andra län är orörda.
 */
export function toggleWholeRegion(
  current: OrtSelection,
  regionConceptId: string,
  municipalityIdsOfRegion: ReadonlyArray<string>,
): OrtSelection {
  if (current.region.includes(regionConceptId)) {
    return {
      region: current.region.filter((r) => r !== regionConceptId),
      municipality: current.municipality,
    };
  }
  const ownMunicipalities = new Set(municipalityIdsOfRegion);
  return {
    region: [...current.region, regionConceptId],
    municipality: current.municipality.filter(
      (m) => !ownMunicipalities.has(m),
    ),
  };
}

/**
 * Rensar EN läns-kolumn (höger-kolumnens "Rensa"): länets helläns-val OCH
 * dess enskilda kommun-val. Andra läns val är orörda.
 */
export function clearRegionColumn(
  current: OrtSelection,
  regionConceptId: string,
  municipalityIdsOfRegion: ReadonlyArray<string>,
): OrtSelection {
  const ownMunicipalities = new Set(municipalityIdsOfRegion);
  return {
    region: current.region.filter((r) => r !== regionConceptId),
    municipality: current.municipality.filter(
      (m) => !ownMunicipalities.has(m),
    ),
  };
}
