/**
 * Server-säker helper för färskhets-etikett (pre-F6 Prompt 1 tagg-system).
 * Måste leva i en icke-client-modul: `JobAdCard` är RSC och anropar denna
 * vid server-render. Next.js "use client"-moduler exporterar non-component-
 * referenser som Client Reference Proxies — anrop från RSC kastar vid
 * runtime (medvetet kontrakt, inte bug). Denna fil har INGEN "use client"
 * och är därför universellt callbar.
 */

/**
 * Beräknar färskhets-etikett från `publishedAt` (ISO 8601 sträng).
 * Returnerar `null` om annonsen är äldre än 7 dygn eller datumet är
 * oparsbart. Anropas i `JobAdCard` (RSC) så strängvärdet är stabilt
 * mellan server-render och client-hydration.
 *
 * Klas-observation 2026-05-24: jämförelsen är **kalender-dag-baserad**,
 * inte 24-timmars-fönster. En annons publicerad kl 23:37 är "1 dag"
 * dagen efter, inte "Idag". Detta synkar med `formatPublishedAtWithTime`
 * i `job-ad-card.tsx` som också använder kalender-dag (`getDate()`/
 * `getMonth()`/`getYear()`) — utan synk visade copy "igår, kl. 23:37"
 * samtidigt som taggen visade "Idag" på samma annons.
 *
 * Tidzon: server-renderad i UTC (ECS Fargate default-TZ). Klient-side
 * hydration använder samma ISO-värde + samma kalender-jämförelse → ingen
 * hydration-drift. Sverige-användare som besöker sidan kl 00:30 lokal-tid
 * (22:30/23:30 UTC) ser fortfarande server-renderad "Idag"/"1 dag" enligt
 * UTC-dag — samma beteende som `formatPublishedAtWithTime`. Konsistens
 * mellan tagg och copy är viktigare än tidzon-purism för MVP.
 */
export function computeFreshnessLabel(
  publishedAtIso: string,
  nowMs: number = Date.now(),
): string | null {
  const published = new Date(publishedAtIso);
  if (Number.isNaN(published.getTime())) return null;

  const now = new Date(nowMs);

  // Kalender-dag-jämförelse i UTC: anropas server-side (ECS Fargate UTC) av
  // JobAdCard RSC. UTC-metoderna gör funktionen TZ-agnostisk → identiskt
  // beteende i vitest (Sverige-TZ) och produktion (UTC) → deterministiska
  // tester + hydration-safe. Matchar `formatPublishedAtWithTime`-copyns
  // server-side `getDate()`-beteende (vilket på UTC-server är samma som
  // `getUTCDate()`).
  const publishedDay = Date.UTC(
    published.getUTCFullYear(),
    published.getUTCMonth(),
    published.getUTCDate(),
  );
  const nowDay = Date.UTC(
    now.getUTCFullYear(),
    now.getUTCMonth(),
    now.getUTCDate(),
  );
  const dayDiff = Math.round((nowDay - publishedDay) / (24 * 60 * 60 * 1000));

  if (dayDiff < 0 || dayDiff > 7) return null;
  if (dayDiff === 0) return "Idag";
  if (dayDiff === 1) return "1 dag";
  return `${dayDiff} dagar`;
}
