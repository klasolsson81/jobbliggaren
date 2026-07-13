import "server-only";
import { fetchLandingStats } from "@/lib/api/landing";
import { LANDING_STATS_UNKNOWN_DTO } from "@/lib/dto/landing";
import { type LandingStats } from "./landing-stats-format";

/**
 * Live-stats för landing-toppen ("aktiva annonser · nya idag"). Konsumeras
 * av `<LandingHeader />` (RSC). Klient-komponenter (`<HeaderStats />`)
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
 * Vid backend-fail (network, 5xx, 429, shape-mismatch) returneras det ÄRLIGA icke-svaret: inga tal.
 * Tidigare returnerades ett hårdkodat golv (40 000) och räkneraden såg identisk ut oavsett ursprung —
 * en siffra ingen mätt, renderad som ett faktum för varje anonym besökare. Golvet är borta
 * (CTO-bind 2026-07-13, A′): `null` = vi vet inte, och konsumenten MÅSTE hantera det (typen tvingar
 * det). En mätt nolla är fortfarande `0`.
 * </p>
 */
export async function getLandingStats(): Promise<LandingStats> {
  const dto = (await fetchLandingStats()) ?? LANDING_STATS_UNKNOWN_DTO;
  return { activeCount: dto.activeCount, newToday: dto.newToday };
}
