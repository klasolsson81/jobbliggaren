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

function observation<T extends z.ZodType>(data: T) {
  return z.discriminatedUnion("kind", [
    z.object({ kind: z.literal("loaded"), data, sampledAt: instant, refreshFailed: z.boolean() }),
    z.object({ kind: z.literal("empty"), data, sampledAt: instant, refreshFailed: z.boolean() }),
    z.object({ kind: z.literal("failed") }),
    z.object({ kind: z.literal("loading") }),
  ]);
}

export const overviewSnapshotSchema = z.object({
  accounts: observation(accountOverviewSchema),
  audit: observation(z.array(overviewAuditEventSchema).max(5)),
  jobs: observation(overviewJobsSchema),
});
export type AdminOverviewSnapshot = z.infer<typeof overviewSnapshotSchema>;