import type { TaxonomyTree } from "@/lib/dto/taxonomy";

/**
 * Spår 3 PR-D (ADR 0076-amendment 2026-06-21, architect NOTE-2) — FE-sidans
 * upplösning av ort-granularitet för match-modalens RegionFit-bevis.
 *
 * Backend unionerar region ∪ municipality till EN ort-dimension och modellerar
 * medvetet inte granulariteten i kontraktet (NOTE-2). Modalen ska ändå ärligt
 * visa VILKEN granularitet som matchade (kommun-träff vs län-träff), så vi
 * härleder den HÄR ur taxonomin som sidan redan har.
 *
 * Nyckeln är postens `conceptId`, inte dess namn: granularitet är en egenskap hos
 * konceptet, och namnet är bara det ord snapshoten gav det. Sedan #1598 bär wire:t
 * `{conceptId, label}` per post, så id:t räcker hela vägen fram till bevis-raden.
 * Två koncept är två id, så kartan behöver ingen kollisionspolicy — en namn-nyckel
 * behövde en. Ett id som saknas i taxonomin (stale snapshot) klassas inte alls.
 */

export type OrtGranularity = "region" | "municipality";

/**
 * Bygger en conceptId → granularitet-karta ur taxonomi-trädet. Serialiserbar
 * (`Record<string, OrtGranularity>`) så en Server Component kan beräkna den och
 * skicka den över RSC-gränsen till matchnings-sektionen.
 *
 * Skrivordningen saknar betydelse — varje koncept skriver sin egen nyckel.
 * Determinism: kartan är ren funktion av trädet.
 */
export function buildOrtGranularityMap(
  taxonomy: TaxonomyTree | null,
): Record<string, OrtGranularity> {
  const map: Record<string, OrtGranularity> = {};
  if (taxonomy === null) return map;

  for (const region of taxonomy.regions) {
    map[region.conceptId] = "region";
    for (const municipality of region.municipalities) {
      map[municipality.conceptId] = "municipality";
    }
  }
  return map;
}

/**
 * Klassar EN register-post via dess concept-id. Okänt id (saknas i taxonomin,
 * t.ex. en stale snapshot) → `null`.
 */
export function classifyOrtConcept(
  conceptId: string,
  granularityByConceptId: Record<string, OrtGranularity>,
): OrtGranularity | null {
  return granularityByConceptId[conceptId] ?? null;
}
