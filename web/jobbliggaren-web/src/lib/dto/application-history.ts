import { z } from "zod";

/**
 * #311 #448 (ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1/D2) — the caller's OWN application history
 * grouped by employer, for the `/foretag` "Ansökningshistorik" section. A pure consumer of the #444
 * endpoint `GET /api/v1/me/application-history` (`EmployerApplicationHistoryDto[]`): no new backend.
 *
 * The backend is the SINGLE authoritative personnummer guard (ADR 0087 D8(c)): a sole-proprietorship
 * (personnummer-shaped) org.nr arrives with `organizationNumber: null` and `isProtectedIdentity: true` —
 * the raw value NEVER crosses the wire. The FE identifies the employer by `companyName` (resolved
 * server-side from public Platsbanken data) and renders org.nr only when present (a legal-entity number).
 *
 * Each entry is deliberately MINIMAL (ADR 0090 D2 R-A4 firewall): only WHEN the user applied
 * (`appliedAt`, the first-submit stamp) and the application's CURRENT `statusName` — NO application id,
 * NO JobAdId, no title, no free text, no contact-person name. The FE must never invent a link to the
 * individual application from this surface.
 */
export const applicationHistoryEntrySchema = z.object({
  appliedAt: z.string(),
  statusName: z.string(),
});
export type ApplicationHistoryEntry = z.infer<typeof applicationHistoryEntrySchema>;

export const employerApplicationHistorySchema = z.object({
  organizationNumber: z.string().nullable(),
  isProtectedIdentity: z.boolean(),
  companyName: z.string().nullable(),
  applicationCount: z.number().int().nonnegative(),
  applications: z.array(applicationHistoryEntrySchema),
});
export type EmployerApplicationHistory = z.infer<typeof employerApplicationHistorySchema>;

/**
 * `GET /me/application-history` returns a bare array (no pagination — one user's submitted-application
 * history is user-bounded, parity `getCompanyWatches`), most-recently-applied employer first.
 */
export const applicationHistoryResultSchema = z.array(employerApplicationHistorySchema);
export type ApplicationHistoryResult = z.infer<typeof applicationHistoryResultSchema>;
