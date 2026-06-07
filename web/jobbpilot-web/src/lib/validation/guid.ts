/**
 * GUID-validering för id:n som interpoleras i backend-URL:er (BFF-lagret).
 *
 * Allowlist (acceptera endast känd-god form) > denylist — Saltzer/Schroeder
 * "fail-safe defaults". Fungerar som barrier mot CodeQL `js/request-forgery`
 * (query-hjälpens egen remediation = input-restriction, inte encoding) och som
 * path-injektions-skydd: en rå `/`, `..` eller `?` i ett id kan annars subtilt
 * ändra request-path/query. encodeURIComponent på själva segmentet är det
 * kompletterande andra lagret (defense-in-depth, OWASP ASVS V5).
 *
 * Single source of truth för GUID-formatet — konsumeras av både BFF-API/actions
 * och Zod-DTO-scheman (DRY, ett ställe per knowledge piece).
 */
export const GUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** True om värdet är ett välformat GUID (canonical 8-4-4-4-12, case-insensitive). */
export function isValidId(value: string): boolean {
  return GUID_REGEX.test(value);
}
