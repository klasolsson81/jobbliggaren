import { z } from "zod";
import { pagedResultWithTotalPages } from "./_helpers";

export const auditLogEntryDtoSchema = z.object({
  id: z.string(),
  occurredAt: z.string(),
  correlationId: z.string(),
  userId: z.string().nullable(),
  impersonatedBy: z.string().nullable(),
  eventType: z.string(),
  aggregateType: z.string(),
  aggregateId: z.string(),
  ipAddress: z.string().nullable(),
  userAgent: z.string().nullable(),
});
export type AuditLogEntryDto = z.infer<typeof auditLogEntryDtoSchema>;

export const auditLogPagedResultSchema = pagedResultWithTotalPages(
  auditLogEntryDtoSchema
);
export type AuditLogPagedResult = z.infer<typeof auditLogPagedResultSchema>;

/**
 * Filter-input för audit-log-query. Inte ett wire-schema (request-side typ),
 * men co-lokaliseras här för symmetri med `AuditLogPagedResult`.
 */
export interface AuditLogFilter {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  userId?: string;
  eventType?: string;
  aggregateType?: string;
}
