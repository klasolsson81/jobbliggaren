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
 */
export function computeFreshnessLabel(
  publishedAtIso: string,
  nowMs: number = Date.now(),
): string | null {
  const publishedMs = Date.parse(publishedAtIso);
  if (!Number.isFinite(publishedMs)) return null;
  const ageDays = Math.floor((nowMs - publishedMs) / (24 * 60 * 60 * 1000));
  if (ageDays < 0 || ageDays > 7) return null;
  if (ageDays === 0) return "Idag";
  if (ageDays === 1) return "1 dag";
  return `${ageDays} dagar`;
}
