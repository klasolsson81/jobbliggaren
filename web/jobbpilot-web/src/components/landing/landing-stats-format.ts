/**
 * Klient-safe shape + formatering för landing-stats. Egen fil (utan
 * `server-only`-tainting) så client-komponenter (`<HeaderStats />` i
 * `(app)`-route-gruppen) kan importera typen och format-helpern utan att
 * dra in `lib/api/landing.ts` server-only fetchen (RSC-boundary-läcka som
 * fångades av `pnpm build` 2026-05-24).
 *
 * `getLandingStats()` (server-only async) bor fortsatt i `landing-stats.ts`.
 */

export interface LandingStats {
  activeCount: number;
  newToday: number;
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
