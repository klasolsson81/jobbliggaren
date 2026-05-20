/**
 * Live-stats för landing-toppen ("45 580 aktiva annonser · 312 nya idag").
 *
 * Klas pre-F6 Prompt 1 (Landing) verbatim FAS-DEFERRAL: värden hårdkodas
 * mot denna hjälpfunktion tills datakontrakt klart. Pekare till framtida
 * endpoint dokumenteras i ADR 0056 (Landing v3-shell).
 *
 * Källa idag: konstanter från HANDOVER-v3 målbild 01-landing-light.png.
 * Värden är placeholder; ingen `getJobAds()`-aggregation eller cron-snapshot
 * gjord. När backend exponerar `GET /api/v1/job-ads/landing-stats` (eller
 * motsvarande) byter denna funktion till `fetch`-anrop i en RSC-context.
 */

export interface LandingStats {
  activeCount: number;
  newToday: number;
}

export function getLandingStats(): LandingStats {
  return {
    activeCount: 45_580,
    newToday: 312,
  };
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
