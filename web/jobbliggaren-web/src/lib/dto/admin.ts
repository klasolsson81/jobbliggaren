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

// --- Hangfire background-job operator surface (TD-83, issue #204, PR1 read-side) ---
//
// Read-only DTOs for the admin /admin/jobb operator page. The backend exposes
// only PII-free, category-level fields (ADR 0079-aligned security posture):
// `errorCategory` is an exception TYPE NAME (e.g. "DbUpdateException"), never a
// stack trace, message, or job argument. These schemas are the anti-corruption
// boundary — any extra backend field is silently dropped, never surfaced.

/**
 * One recurring (scheduled) Hangfire job. `lastJobState` is the raw Hangfire
 * state name ("Succeeded" | "Failed" | "Processing" | null) — the UI maps it to
 * a localized civic label + semantic color, never printing it verbatim. All
 * timestamps are ISO 8601 UTC strings (Date-conversion is a UI concern, ADR 0020).
 */
export const recurringJobStatusDtoSchema = z.object({
  id: z.string(),
  cron: z.string().nullable(),
  lastExecution: z.string().nullable(),
  lastJobState: z.string().nullable(),
  nextExecution: z.string().nullable(),
});
export type RecurringJobStatusDto = z.infer<typeof recurringJobStatusDtoSchema>;

/**
 * One failed Hangfire job. `errorCategory` is a PII-free exception type name —
 * the backend guarantees no message/stack-trace/argument leakage. `failedAt` is
 * ISO 8601 UTC or null.
 */
export const failedJobStatusDtoSchema = z.object({
  jobId: z.string(),
  jobType: z.string(),
  failedAt: z.string().nullable(),
  errorCategory: z.string(),
});
export type FailedJobStatusDto = z.infer<typeof failedJobStatusDtoSchema>;

/**
 * Failed-jobs response. `totalCount` may exceed `returned` (backend caps at the
 * 50 most recent) — the UI surfaces the truncation honestly so a cap never reads
 * as "no failures left".
 */
export const failedJobsResponseSchema = z.object({
  totalCount: z.number().int().nonnegative(),
  returned: z.number().int().nonnegative(),
  items: z.array(failedJobStatusDtoSchema),
});
export type FailedJobsResponse = z.infer<typeof failedJobsResponseSchema>;

/**
 * The recurring endpoint returns a bare JSON array (server-sorted by id), not a
 * paged envelope. Schema kept co-located for use by the API client.
 */
export const recurringJobsResponseSchema = z.array(recurringJobStatusDtoSchema);
