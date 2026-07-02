/**
 * #454 PR-0 — shared org.nr display formatter (extracted from
 * `company-watch-row.tsx` so the /jobb employer-filter chip and future
 * company surfaces reuse ONE formatter instead of copying it).
 *
 * Formats a 10-digit org.nr as NNNNNN-NNNN. Any other length is shown
 * verbatim rather than mis-split. Provenance differs per callsite: the
 * company-watch row only ever receives a backend-MASKED value (a sole-prop
 * org.nr arrives as null + IsProtectedIdentity, ADR 0087 D8(c)); the /jobb
 * employer chip formats the URL param the user themselves navigated with
 * (format-gated `^\d{10}$`, not pnr-discriminated — echoing the user's own
 * typed URL state is not a surfacing of third-party data). Pure display —
 * never logged, never transmitted.
 */
export function formatOrgNr(orgNr: string): string {
  return orgNr.length === 10 ? `${orgNr.slice(0, 6)}-${orgNr.slice(6)}` : orgNr;
}
