import { z } from "zod";

/**
 * ADR 0064 — Zod-mirror av backend `LandingStatsDto`
 * (`Jobbliggaren.Application.Landing.Common`). Single-source per ADR 0020.
 *
 * Värdet hämtas från `GET /api/v1/landing/stats` (publik anonym endpoint).
 * `isStale=true` betyder antingen cold-start (Worker har inte refreshat än)
 * eller Redis-cache-miss; floor-värden returneras av backend i båda fallen.
 * `refreshedAt` är `null` när floor returneras, annars Worker:s UTC-tidpunkt.
 */
export const landingStatsDtoSchema = z.object({
  activeCount: z.number().int().nonnegative(),
  newToday: z.number().int().nonnegative(),
  isStale: z.boolean(),
  refreshedAt: z.string().nullable(),
});
export type LandingStatsDto = z.infer<typeof landingStatsDtoSchema>;

/**
 * Single-source floor — speglar backendens GetLandingStatsQueryHandler.Floor
 * (ADR 0064). Återanvänds av AppLayout (`(app)/layout.tsx`) vid backend-fail
 * och av `getLandingStats()` (`landing-stats.ts`) som klient-format. Att hålla
 * konstanten på ett ställe undviker silent drift mellan kod-paths.
 */
export const LANDING_STATS_FLOOR_DTO: LandingStatsDto = {
  activeCount: 40_000,
  newToday: 0,
  isStale: true,
  refreshedAt: null,
};
