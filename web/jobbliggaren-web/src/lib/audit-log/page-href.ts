/**
 * The pagination URL builder for `/admin/granskning`.
 *
 * Extracted from the page it serves so it can be CALLED rather than read. Its key inventory is
 * bound to the edge's scrubbing list in `audit-log-edge-log-inventory.test.ts`, and that fact
 * exists because reading a builder's source text binds the local variable name instead of the
 * emitted key — the trap `/jobb`'s `distans` axis demonstrated, where a sweep for `setAxis(`
 * missed the one axis written with a bare `params.set`. A module-private function inside a
 * `page.tsx` cannot be called by a test at all, so extracting it is what makes the inventory
 * measurable instead of asserted.
 *
 * Pure and dependency-free on purpose: no `server-only` import, so the fact can import it without
 * a shim.
 */

/** The raw, uninterpreted search params `/admin/granskning` reads. */
export type AuditLogRawSearchParams = {
  page?: string;
  pageSize?: string;
  from?: string;
  to?: string;
  userId?: string;
  eventType?: string;
  aggregateType?: string;
};

export function buildAuditLogPageHref(
  params: AuditLogRawSearchParams,
  page: number
): string {
  const url = new URLSearchParams();
  if (page !== 1) url.set("page", String(page));
  if (params.pageSize) url.set("pageSize", params.pageSize);
  if (params.from) url.set("from", params.from);
  if (params.to) url.set("to", params.to);
  if (params.userId) url.set("userId", params.userId);
  if (params.eventType) url.set("eventType", params.eventType);
  if (params.aggregateType) url.set("aggregateType", params.aggregateType);
  const q = url.toString();
  return q.length > 0 ? `/admin/granskning?${q}` : "/admin/granskning";
}
