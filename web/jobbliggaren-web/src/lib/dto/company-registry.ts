import { z } from "zod";

/**
 * #454 (ADR 0088 D5) — the company-lookup result the /foretag lookup card renders. Mirrors backend
 * `CompanyLookupDto`. One wire shape for all three outcomes (`status` discriminator, always HTTP
 * 200 — never-500 civic degradation); a REFUSED personnummer-shaped input is a 400 and never
 * reaches this schema. The backend applies the D8(c) mask+flag before anything crosses the wire —
 * `organizationNumber`/`companyName` are non-null only for a found legal entity.
 *
 * Enrichment semantics (meaningful only when `status === "found"`): `activeAdCount` = public
 * open-role count in our corpus (0 is the honest 0-ad story); `matchingAdCount` = the user's
 * >= Good matching count, `null` = honest not-assessed (no stated occupation — render the nudge,
 * never "0"; parity `companyWatchSchema.matchingAdCount`); `companyWatchId` = the user's existing
 * follow (surrogate id, never an org.nr).
 */
export const companyLookupSchema = z.object({
  status: z.enum(["found", "notFound", "unavailable"]),
  organizationNumber: z.string().nullable(),
  isProtectedIdentity: z.boolean(),
  companyName: z.string().nullable(),
  activeAdCount: z.number().int().nonnegative(),
  matchingAdCount: z.number().int().nullable(),
  companyWatchId: z.string().nullable(),
});
export type CompanyLookup = z.infer<typeof companyLookupSchema>;

/**
 * Client-side org.nr normalisation for the lookup input: strip spaces + hyphens, then require
 * exactly 10 digits (the backend validator's form). Returns the normalised 10-digit value or null.
 * The LABEL carries the instruction (no placeholder examples — Klas hard rule); this helper only
 * decides submit-enablement + what is POSTed.
 */
export function normalizeOrgNrInput(raw: string): string | null {
  const stripped = raw.replace(/[\s-]/g, "");
  return /^\d{10}$/.test(stripped) ? stripped : null;
}

/**
 * #454 (ADR 0088 D4) — FE mirror of the backend heuristic
 * `OrganizationNumber.IsPersonnummerShaped()` (a legal-entity org.nr always has 3rd digit >= 2; a
 * personnummer has 0/1). DISPLAY GATE ONLY: the lookup island uses it to render the refuse state
 * locally WITHOUT transmitting a potential personnummer anywhere (not even to our own BFF); the
 * backend handler remains the enforcing authority (refuses pre-registry, transmission-fail-closed
 * pinned by arch tests). A #456-sanctioned posture flip updates both sides in one PR.
 * Expects an already-normalised 10-digit value.
 */
export function isPersonnummerShapedOrgNr(orgNr: string): boolean {
  const third = orgNr[2];
  // Fail-safe: the unexpected is sensitive (parity the backend heuristic).
  return !/^\d{10}$/.test(orgNr) || third === undefined || third < "2";
}
