import { fetchLandingStats } from "@/lib/api/landing";

/**
 * Live-stats för landing-toppen ("aktiva annonser · nya idag"). Konsumeras
 * av <LandingTopbar /> (RSC).
 *
 * Tidigare hårdkodade konstanter (45 580 / 312, från HANDOVER-v3 målbild 01)
 * är borta — ADR 0056 Beslut 4-utbytespunkt lyft i ADR 0064:
 * `getLandingStats()` är nu en async server-only-helper som anropar
 * `GET /api/v1/landing/stats` (pre-computed Redis-cache via Worker-cron
 * `RefreshLandingStatsJob`).
 *
 * <p>
 * Fallback-floor används vid backend-fail (network, 5xx, 429, shape-mismatch).
 * Konservativa värden — ljuger inte uppåt. Frontend exponerar inte
 * `isStale`-flaggan i UI:t (HANDOVER §6.4 nämner ingen sådan affordans);
 * räkneraden ser identisk ut oavsett ursprung. Backend-disciplin med
 * `IsStale=true` bibehålls för operativ telemetri och framtida
 * partner-integrationer.
 * </p>
 */
export interface LandingStats {
  activeCount: number;
  newToday: number;
}

const FALLBACK_FLOOR: LandingStats = {
  activeCount: 40_000,
  newToday: 0,
};

export async function getLandingStats(): Promise<LandingStats> {
  const dto = await fetchLandingStats();
  if (!dto) return FALLBACK_FLOOR;
  return { activeCount: dto.activeCount, newToday: dto.newToday };
}

/**
 * Formaterar antal enligt svensk locale (non-breaking-space mellan
 * tusentalsgrupper; sv-SE använder mellanslag — `Intl.NumberFormat` med
 * `sv-SE` ger U+00A0 som default). Hårdkodad till sv-SE eftersom SV är
 * enda aktiverade locale just nu (LangToggle: EN disabled).
 */
export function formatLandingNumber(n: number): string {
  return new Intl.NumberFormat("sv-SE").format(n);
}
