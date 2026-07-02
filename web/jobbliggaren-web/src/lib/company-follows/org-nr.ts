/**
 * #454 PR-0 — shared org.nr display formatter (extracted from
 * `company-watch-row.tsx` so the /jobb employer-filter chip and future
 * company surfaces reuse ONE formatter instead of copying it).
 *
 * Formats a 10-digit legal-entity org.nr as NNNNNN-NNNN. Only ever called
 * with a value the backend guarantees is NOT personnummer-shaped (a
 * sole-prop org.nr arrives masked to null, ADR 0087 D8(c)). Any other
 * length is shown verbatim rather than mis-split.
 */
export function formatOrgNr(orgNr: string): string {
  return orgNr.length === 10 ? `${orgNr.slice(0, 6)}-${orgNr.slice(6)}` : orgNr;
}
