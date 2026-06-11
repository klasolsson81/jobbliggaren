import type { SuggestionDto } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { applyMunicipalityChange } from "./ort-selection";
import type { JobbUrlState } from "./search-params";

/**
 * Typeahead-chip-komponist (ADR 0067 Beslut 5b, Fas E2d). Ren funktion: tar ett
 * valt typeahead-förslag (`SuggestionDto`) + nuvarande URL-state och returnerar
 * nästa URL-state. Komponenten gör sedan `router.push(buildJobbHref(next))` —
 * chips lever i URL:en (E2g-principen: URL är ENDA sanningen), inte i egen
 * komponent-state.
 *
 * Semantik (Klas-grind 2026-06-11 + ADR 0042 Beslut B + ADR 0067):
 * - chips = AND mellan dimensioner / OR inom dimension. Att lägga till ett
 *   förslag i en dimension är OR-inom (additivt + dedupe).
 * - Ort = EN dimension i två granulariteter (region∪kommun, backend unionerar,
 *   E2b). Per-län-normaliseringen (ort-selection.ts) hålls konsekvent: en ny
 *   kommun släcker sitt läns helläns-val; ett helt län släcker länets enskilda
 *   kommun-val. Ren UX-kosmetik (URL minimal) — ingen korrekthets-bärare.
 * - Title-förslag (occupation-name finns EJ som dimension, VAL 4) = ren
 *   fritext → `q` (residual-q, recall-bevarande FTS-hybrid, ADR 0062/D2;
 *   ALDRIG hårt dimensions-AND).
 *
 * VAL 2a (CTO 2026-06-11): OccupationField (yrkesområde) är suggest-bart men är
 * INGEN egen URL-dimension — det materialiseras till sina barn-yrkesgrupper
 * ("hela yrkesområdet"), exakt som popoverns "Välj alla yrkesgrupper". Bounded
 * av taxonomin (≤ MaxConceptIds=400).
 *
 * VAL 2c (CTO 2026-06-11): EmploymentType/WorktimeExtent ("Heltid") är GATED —
 * NULL-data tills re-ingest Klass 2 (ADR 0067 B2). De finns INTE i
 * `SuggestionKind` ännu. När de adderas till enumen tvingar `assertNever` nedan
 * fram en ny case-gren INNAN koden kompilerar (falsk-klar-skydd, CLAUDE.md
 * §9.6 — ingen tyst odimensionerad gren).
 */
export function composeSuggestionChip(
  suggestion: SuggestionDto,
  current: JobbUrlState,
  taxonomy: TaxonomyTree | null,
): JobbUrlState {
  const conceptId = suggestion.conceptId;

  switch (suggestion.kind) {
    case "Title":
      // Residual fritext → q. Hela råförslags-labeln blir söktermen; backend
      // SearchQueryParser normaliserar och kör recall-bevarande FTS-hybrid.
      return { ...current, q: suggestion.label };

    case "OccupationGroup":
      // Yrkesgrupp (ssyk-level-4) = primärt yrke-filter. OR-inom + dedupe.
      if (conceptId === null) return current;
      return {
        ...current,
        occupationGroup: addUnique(current.occupationGroup, conceptId),
      };

    case "OccupationField": {
      // VAL 2a — materialisera barn-yrkesgrupper. Yrkesområdet self har ingen
      // filter-dimension; "Data/IT" → alla dess ssyk-level-4-ids.
      if (conceptId === null) return current;
      const field = taxonomy?.occupationFields.find(
        (f) => f.conceptId === conceptId,
      );
      if (!field || field.occupationGroups.length === 0) {
        // Degraderad ACL (taxonomy null / okänt id) → fall tillbaka på fritext
        // så klicket aldrig blir en tyst no-op (graceful, ADR 0043 Beslut B).
        return { ...current, q: suggestion.label };
      }
      let occupationGroup = [...current.occupationGroup];
      for (const g of field.occupationGroups)
        occupationGroup = addUnique(occupationGroup, g.conceptId);
      return { ...current, occupationGroup };
    }

    case "Region": {
      // Helt län (OR-inom ort-dimensionen). Per-län-normalisering: släck
      // länets enskilda kommun-val (redundant under union — URL minimal).
      if (conceptId === null) return current;
      const own = municipalityIdsOfRegion(taxonomy, conceptId);
      return {
        ...current,
        region: addUnique(current.region, conceptId),
        municipality:
          own.size > 0
            ? current.municipality.filter((m) => !own.has(m))
            : [...current.municipality],
      };
    }

    case "Municipality": {
      // Kommun (OR-inom ort-dimensionen). Återanvänder applyMunicipalityChange
      // (SPOT mot popoverns normalisering): en ny kommun släcker sitt läns
      // helläns-val. Dedupe: redan vald kommun → no-op.
      if (conceptId === null) return current;
      if (current.municipality.includes(conceptId)) return current;
      const next = applyMunicipalityChange(
        { region: current.region, municipality: current.municipality },
        [...current.municipality, conceptId],
        regionOfMunicipality(taxonomy),
      );
      return {
        ...current,
        region: [...next.region],
        municipality: [...next.municipality],
      };
    }

    default:
      return assertNever(suggestion.kind);
  }
}

function addUnique(list: ReadonlyArray<string>, id: string): string[] {
  return list.includes(id) ? [...list] : [...list, id];
}

/** kommun-conceptId → läns-conceptId (för per-län-normaliseringen). */
function regionOfMunicipality(
  taxonomy: TaxonomyTree | null,
): ReadonlyMap<string, string> {
  const map = new Map<string, string>();
  for (const r of taxonomy?.regions ?? [])
    for (const m of r.municipalities) map.set(m.conceptId, r.conceptId);
  return map;
}

/** Länets egna kommun-conceptIds (släcks när hela länet väljs). */
function municipalityIdsOfRegion(
  taxonomy: TaxonomyTree | null,
  regionConceptId: string,
): ReadonlySet<string> {
  const region = (taxonomy?.regions ?? []).find(
    (r) => r.conceptId === regionConceptId,
  );
  return new Set(region?.municipalities.map((m) => m.conceptId) ?? []);
}

/**
 * Exhaustivitets-vakt (VAL 2c). Om `SuggestionKind` utökas — t.ex.
 * EmploymentType/WorktimeExtent vid re-ingest Klass 2 (ADR 0067 B2) — blir
 * argumentet inte längre `never` och TS vägrar kompilera tills en case-gren
 * lagts till. Det är den re-ingest-redo designen utan död/odimensionerad kod.
 */
function assertNever(kind: never): never {
  throw new Error(`Ohanterad SuggestionKind: ${String(kind)}`);
}
