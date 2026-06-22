import type { TaxonomyTree } from "@/lib/dto/taxonomy";

/**
 * Spår 3 PR-D (ADR 0076-amendment 2026-06-21, architect NOTE-2) — FE-sidans
 * upplösning av ort-granularitet för match-modalens RegionFit-bevis.
 *
 * Backend unionerar region ∪ municipality till EN ort-dimension och emitterar
 * matched/missing som rena DISPLAY-labels (län- och/eller kommun-namn). Modalen
 * ska ärligt visa VILKEN granularitet som matchade (kommun-träff vs län-träff),
 * men backend modellerar medvetet inte granulariteten i kontraktet (NOTE-2). Vi
 * härleder den HÄR ur taxonomin som sidan redan har: varje läns-label är "län",
 * varje kommun-label är "kommun".
 *
 * Tvetydighet (ett namn som är BÅDE län och kommun, t.ex. Gotland): vi klassar
 * som det COARSER (`"region"`). Det är den säkra defaulten eftersom ett läns-id
 * i `preferredRegions` representerar HELA länet (kommun-träffen ingår), och
 * copyn för en län-träff ("hela länet") aldrig över-påstår en exakt kommun. En
 * ren plain-label-fallback (ingen kategori-prefix) gäller för labels som inte
 * finns i taxonomin alls (stale snapshot) — då visas namnet rakt av.
 */

export type OrtGranularity = "region" | "municipality";

/**
 * Bygger en label → granularitet-karta ur taxonomi-trädet. Serialiserbar
 * (`Record<string, OrtGranularity>`) så en Server Component kan beräkna den och
 * skicka den över RSC-gränsen till matchnings-sektionen.
 *
 * Län skrivs FÖRST och kommuner SEDAN, så en kollision (samma namn som både län
 * och kommun) landar på "region" (coarser) — kommun-skrivningen hoppas över för
 * en redan satt nyckel. Determinism: kartan är ren funktion av trädet.
 */
export function buildOrtGranularityMap(
  taxonomy: TaxonomyTree | null,
): Record<string, OrtGranularity> {
  const map: Record<string, OrtGranularity> = {};
  if (taxonomy === null) return map;

  for (const region of taxonomy.regions) {
    // Län först → vinner vid kollision (Gotland-fallet → "region", coarser).
    map[region.label] = "region";
  }
  for (const region of taxonomy.regions) {
    for (const municipality of region.municipalities) {
      // Skriv bara om namnet inte redan är ett län (annars vore Gotland-kommunen
      // omklassad till "municipality" och län-träffen skulle felrapporteras).
      if (map[municipality.label] === undefined) {
        map[municipality.label] = "municipality";
      }
    }
  }
  return map;
}

/**
 * Klassar EN matched/missing-label. Okänd label (saknas i taxonomin, t.ex. en
 * stale snapshot) → `null`: anroparen visar då namnet rakt av utan kategori.
 */
export function classifyOrtLabel(
  label: string,
  granularityByLabel: Record<string, OrtGranularity>,
): OrtGranularity | null {
  return granularityByLabel[label] ?? null;
}
