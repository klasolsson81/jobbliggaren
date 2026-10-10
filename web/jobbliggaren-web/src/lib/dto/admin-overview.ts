import { z } from "zod";
import { accountStatusCountsSchema } from "./admin-accounts";

const instant = z.iso.datetime({ offset: true });
const count = z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const period = z.object({ count, from: instant, before: instant });

export const accountOverviewSchema = z.object({
  sampledAt: instant,
  counts: accountStatusCountsSchema,
  newAccounts: z.object({ today: period, yesterday: period, last7Days: period, last30Days: period }),
  days: z.array(z.object({ date: z.iso.date(), newAccounts: count })).length(90),
});
export type AccountOverviewDto = z.infer<typeof accountOverviewSchema>;
export type RegistrationPeriod = z.infer<typeof period>;

export const overviewAuditEventSchema = z.object({
  id: z.string(),
  occurredAt: instant,
  eventType: z.string(),
  aggregateType: z.string(),
  aggregateId: z.string(),
});
export type OverviewAuditEvent = z.infer<typeof overviewAuditEventSchema>;

export const overviewJobsSchema = z.object({ totalCount: count });

// ── The Backup card (#1982, ADR 0157) ───────────────────────────────────────────────────────

const backupReason = z.enum([
  "NotConfigured",
  "NotSampledYet",
  "Unreadable",
  "NotARegularFile",
  "TooLarge",
  "InvalidFormat",
  "SamplerError",
  "FutureSample",
]);

/**
 * `GET /api/v1/admin/overview/backup`, exactly as the API sends it: a closed union, nulls where a state
 * has no value. A key outside it, a state outside it or a time that is not an instant fails the parse
 * (strict objects, unlike the sources that predate it), and the source reads as failed rather than as a guess.
 */
export const backupStatusResponseSchema = z.discriminatedUnion("status", [
  z.strictObject({
    status: z.literal("Observed"),
    reason: z.null(),
    observedAt: instant,
    stale: z.boolean(),
    lastSuccess: z.discriminatedUnion("state", [
      z.strictObject({ state: z.literal("Recorded"), completedAt: instant, overdue: z.boolean() }),
      z.strictObject({ state: z.enum(["Missing", "Unreadable", "Invalid"]), completedAt: z.null(), overdue: z.null() }),
    ]),
    timer: z.discriminatedUnion("state", [
      z.strictObject({ state: z.literal("Scheduled"), nextRunAt: instant }),
      z.strictObject({ state: z.enum(["Inactive", "NotInstalled", "Unknown"]), nextRunAt: z.null() }),
    ]),
  }),
  z.strictObject({
    status: z.literal("NotObserved"),
    reason: backupReason,
    observedAt: z.null(),
    stale: z.null(),
    lastSuccess: z.null(),
    timer: z.null(),
  }),
  z.strictObject({
    status: z.literal("Failed"),
    reason: backupReason,
    observedAt: instant.nullable(),
    stale: z.null(),
    lastSuccess: z.null(),
    timer: z.null(),
  }),
]);
export type BackupStatusResponse = z.infer<typeof backupStatusResponseSchema>;

/** What crosses the RSC/BFF boundary for the card: the states in the web app's own spelling, and nothing else. */
export const backupDataSchema = z.strictObject({
  lastSuccess: z.discriminatedUnion("state", [
    z.strictObject({ state: z.literal("recorded"), completedAt: instant, overdue: z.boolean() }),
    z.strictObject({ state: z.enum(["missing", "unreadable", "invalid"]) }),
  ]),
  timer: z.discriminatedUnion("state", [
    z.strictObject({ state: z.literal("scheduled"), nextRunAt: instant }),
    z.strictObject({ state: z.enum(["inactive", "notInstalled", "unknown"]) }),
  ]),
});
export type BackupObservationData = z.infer<typeof backupDataSchema>;

function observation<T extends z.ZodType>(data: T) {
  return z.discriminatedUnion("kind", [
    z.object({ kind: z.literal("loaded"), data, sampledAt: instant, refreshFailed: z.boolean() }),
    z.object({ kind: z.literal("empty"), data, sampledAt: instant, refreshFailed: z.boolean() }),
    z.object({ kind: z.literal("failed") }),
    z.object({ kind: z.literal("loading") }),
  ]);
}

/** The same, for a source whose host may not have reported yet: built, so not "Kommer snart", and not an error. */
function awaitingObservation<T extends z.ZodType>(data: T) {
  return z.discriminatedUnion("kind", [...observation(data).options, z.object({ kind: z.literal("awaiting") })]);
}

export const overviewSnapshotSchema = z.object({
  accounts: observation(accountOverviewSchema),
  audit: observation(z.array(overviewAuditEventSchema).max(5)),
  jobs: observation(overviewJobsSchema),
  backup: awaitingObservation(backupDataSchema),
});
export type AdminOverviewSnapshot = z.infer<typeof overviewSnapshotSchema>;