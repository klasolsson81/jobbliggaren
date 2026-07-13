import { z } from "zod";

/**
 * ADR 0064 (amenderad 2026-07-13) — Zod-mirror av backend `LandingStatsDto`
 * (`Jobbliggaren.Application.Landing.Common`). Single-source per ADR 0020.
 *
 * Värdet hämtas från `GET /api/v1/landing/stats` (publik anonym endpoint).
 * `isStale=true` betyder antingen cold-start (Worker har inte refreshat än) eller Redis-cache-miss.
 *
 * **Talen är NULLABLE, och det är hela poängen (CTO-bind 2026-07-13, A′).** Tidigare returnerade
 * backend ett hårdkodat golv (`activeCount: 40 000`) vid cache-miss, och landningssidan renderade det
 * som ett faktum — en siffra ingen mätt, visad för varje anonym besökare utan brasklapp. En flagga kan
 * inte tvinga en konsument att titta på den, och den allra första konsumenten gjorde det inte. `null`
 * kan: under `strict` blir det ett *kompileringsfel* att rendera ett omätt tal.
 *
 * **`0` och `null` är olika svar.** En MÄTT nolla ("inget publicerat än idag", sant kl. 00:05 UTC)
 * renderas fortfarande som 0. Bara "vi vet inte" är `null`.
 */
export const landingStatsDtoSchema = z.object({
  activeCount: z.number().int().nonnegative().nullable(),
  newToday: z.number().int().nonnegative().nullable(),
  isStale: z.boolean(),
  refreshedAt: z.string().nullable(),
});
export type LandingStatsDto = z.infer<typeof landingStatsDtoSchema>;

/**
 * Det ärliga icke-svaret när backend inte kan nås (network, 5xx, 429, shape-mismatch) — speglar
 * backendens `LandingStatsDto.Unknown`. Ersätter den tidigare `LANDING_STATS_FLOOR_DTO`: vi renderar
 * aldrig en storhet vi inte mätt, så ett fetch-fel ger inga tal, inte påhittade tal.
 */
export const LANDING_STATS_UNKNOWN_DTO: LandingStatsDto = {
  activeCount: null,
  newToday: null,
  isStale: true,
  refreshedAt: null,
};
