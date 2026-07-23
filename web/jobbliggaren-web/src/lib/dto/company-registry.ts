/**
 * Client-side org.nr input normalisers — the FE mirror of the backend `OrganizationNumber` value object.
 * Shared by the unified `/foretag/sok` search island (`ForetagSokSearchbar`) and the `/api/foretag/sok` BFF
 * route: they decide whether a typed value is an org.nr (10 digits) and whether it is personnummer-shaped
 * (the highest-priority guard). No wire schema lives here — the #454 lookup DTO was retired with the
 * lookup surface (#997); the org.nr search result shape is `orgNrSearchResultSchema` in `company-search.ts`.
 */

/**
 * Strip spaces + hyphens, then require exactly 10 digits (the backend validator's form). Returns the
 * normalised 10-digit value or null. The LABEL carries the instruction (no placeholder examples — Klas
 * hard rule); this helper decides the name-vs-org.nr dispatch + what is POSTed.
 */
export function normalizeOrgNrInput(raw: string): string | null {
  const stripped = raw.replace(/[\s-]/g, "");
  return /^\d{10}$/.test(stripped) ? stripped : null;
}

/**
 * #454 (ADR 0088 D4) — FE mirror of the backend heuristic `OrganizationNumber.IsPersonnummerShaped()`
 * (a legal-entity org.nr always has 3rd digit >= 2; a personnummer has 0/1). DISPLAY GATE ONLY: the
 * unified field uses it to render the refuse state locally WITHOUT transmitting a potential personnummer
 * anywhere (not even to our own BFF); the backend handler remains the enforcing authority (refuses
 * pre-registry, transmission-fail-closed pinned by arch tests). A #456-sanctioned posture flip updates
 * both sides in one PR. Expects an already-normalised 10-digit value.
 */
export function isPersonnummerShapedOrgNr(orgNr: string): boolean {
  const third = orgNr[2];
  // Fail-safe: the unexpected is sensitive (parity the backend heuristic).
  return !/^\d{10}$/.test(orgNr) || third === undefined || third < "2";
}
