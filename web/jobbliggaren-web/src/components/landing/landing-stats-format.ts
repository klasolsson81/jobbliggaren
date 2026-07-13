/**
 * Klient-safe shape för landing-stats. Egen fil (utan `server-only`-tainting)
 * så client-komponenter (`<HeaderStats />` i `(app)`-route-gruppen) kan
 * importera typen utan att dra in `lib/api/landing.ts` server-only fetchen
 * (RSC-boundary-läcka som fångades av `pnpm build` 2026-05-24).
 *
 * `getLandingStats()` (server-only async) bor fortsatt i `landing-stats.ts`.
 * Tal-formateringen gick tidigare via en hårdkodad `formatLandingNumber("sv-SE")`
 * här; den retirerades till den delade locale-medvetna `formatNumber` i
 * `lib/i18n/format.ts` (#214, ADR 0078) så en `en`-läsare ser `1,234` i stället
 * för `1 234`. Konsumenterna hämtar formattern via `useFormatter()`.
 */

/**
 * Talen är NULLABLE (CTO-bind 2026-07-13, A′): `null` = "vi vet inte" (kall cache / backend-fail),
 * och det MÅSTE renderas som frånvaro, aldrig som en siffra. En MÄTT nolla är `0` och renderas som 0.
 * Under `strict` gör nullabiliteten det till ett kompileringsfel att glömma fallet — vilket är precis
 * vad som hände när sanningen bars av en flagga i stället för av typen.
 */
export interface LandingStats {
  activeCount: number | null;
  newToday: number | null;
}
