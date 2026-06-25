import "server-only";
import { fetchLandingStats } from "@/lib/api/landing";
import { LANDING_STATS_FLOOR_DTO } from "@/lib/dto/landing";
import { type LandingStats } from "./landing-stats-format";

/**
 * Live-stats för landing-toppen ("aktiva annonser · nya idag"). Konsumeras
 * av `<LandingTopbar />` (RSC). Klient-komponenter (`<HeaderStats />`)
 * importerar `LandingStats`-typen direkt från `./landing-stats-format` —
 * denna fil är server-tainted via `lib/api/landing`. Tal-formateringen går
 * via den delade locale-medvetna `formatNumber` (`lib/i18n/format.ts`, #214).
 *
 * <p>
 * Async server-only-helper som anropar `GET /api/v1/landing/stats`
 * (pre-computed Redis-cache via Worker-cron `RefreshLandingStatsJob` per
 * ADR 0064). ADR 0056 Beslut 4-utbytespunkt lyft i ADR 0064.
 * </p>
 * <p>
 * Fallback-floor används vid backend-fail (network, 5xx, 429, shape-mismatch).
 * Konservativa värden — ljuger inte uppåt. Frontend exponerar inte
 * `isStale`-flaggan i UI:t (HANDOVER §6.4 nämner ingen sådan affordans);
 * räkneraden ser identisk ut oavsett ursprung. Backend-disciplin med
 * `IsStale=true` bibehålls för operativ telemetri och framtida
 * partner-integrationer.
 * </p>
 */
export async function getLandingStats(): Promise<LandingStats> {
  const dto = (await fetchLandingStats()) ?? LANDING_STATS_FLOOR_DTO;
  return { activeCount: dto.activeCount, newToday: dto.newToday };
}
