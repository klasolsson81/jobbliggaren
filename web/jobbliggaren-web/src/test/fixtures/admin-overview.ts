import type { AccountOverviewDto, AdminOverviewSnapshot, BackupObservationData } from "@/lib/dto/admin-overview";
import type { HostObservationDto } from "@/lib/dto/admin-host";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import type { EmailAllowanceDto } from "@/lib/dto/admin-email";

export const OVERVIEW_TIME = "2026-10-08T10:00:00Z";

export function accountOverviewFixture(): AccountOverviewDto {
  return {
    sampledAt: OVERVIEW_TIME,
    counts: { total: 12, active: 6, pendingDeletion: 2, profileMissing: 2, suspended: 2 },
    newAccounts: {
      today: { count: 2, from: "2026-10-07T22:00:00Z", before: OVERVIEW_TIME },
      yesterday: { count: 3, from: "2026-10-06T22:00:00Z", before: "2026-10-07T22:00:00Z" },
      last7Days: { count: 6, from: "2026-10-01T22:00:00Z", before: OVERVIEW_TIME },
      last30Days: { count: 6, from: "2026-09-08T22:00:00Z", before: OVERVIEW_TIME },
    },
    days: Array.from({ length: 90 }, (_, index) => ({
      date: new Date(Date.UTC(2026, 6, 11 + index)).toISOString().slice(0, 10),
      newAccounts: index === 89 ? 2 : index === 88 ? 3 : index === 87 ? 1 : 0,
    })),
  };
}

/** A recorded run at 02:19 and a timer armed for the next night, as the host sampler publishes them. */
export function backupFixture(): BackupObservationData {
  return {
    lastSuccess: { state: "recorded", completedAt: "2026-10-08T00:19:07+00:00", overdue: false },
    timer: { state: "scheduled", nextRunAt: "2026-10-09T00:17:55+00:00" },
  };
}

/** The three host readings as the API sends them, sampled 30 s before `OVERVIEW_TIME` (a box that idles, 8 GiB). */
export function hostFixture(): HostObservationDto {
  const sampledAt = "2026-10-08T09:59:30Z";
  return {
    staleAfterSeconds: 120,
    cpu: { state: "Available", sampledAt, value: { percent: 5, windowSeconds: 30 } },
    memory: { state: "Available", sampledAt, value: { percent: 29.4, usedBytes: 2_449_854_464, totalBytes: 8_331_255_808 } },
    disk: { state: "Available", sampledAt, value: { percent: 6.1, freeBytes: 242_287_181_824, totalBytes: 258_154_033_152 } },
  };
}

/**
 * The three service rows as the API sends them, sampled 30 s before `OVERVIEW_TIME`: a Worker heartbeat 10 s before
 * the sample, a store that answers, and an import run at 11:50 Stockholm time without errors.
 */
export function servicesFixture(): ServicesObservationDto {
  const sampledAt = "2026-10-08T09:59:30Z";
  return {
    staleAfterSeconds: 120,
    workerAliveWithinSeconds: 120,
    importOverdueAfterSeconds: 1500,
    backgroundJobs: { state: "Alive", sampledAt, heartbeatAt: "2026-10-08T09:59:20Z" },
    volatileStore: { state: "Reachable", sampledAt },
    jobImport: { state: "Recorded", sampledAt, lastRunAt: "2026-10-08T09:50:00Z", errors: 0 },
  };
}

/**
 * The allowance as the API sends it (#1981 PR 1): 1 301 mails this month under the configured plan, read 30 s before
 * `OVERVIEW_TIME`, so 1 001 are beyond the 300 free and cost 349 öre with Swedish VAT, as the API computes it.
 */
export function emailAllowanceFixture(): EmailAllowanceDto {
  return {
    terms: {
      plan: "Essential", includedMonthlyEmails: 300, pricePerThousandExtra: 0.25, currency: "EUR",
      priceReadOn: "2026-10-01", monthlySendLimit: 10_000, costCurrency: "SEK", sekPerEur: 11.145, exchangeRateOn: "2026-10-16",
    },
    usage: {
      status: "Observed", reason: null, observedAt: "2026-10-08T09:59:30+00:00", month: "2026-10",
      sent: 1301, billable: 1001, estimatedExtraCostOre: 349, aboveHalfOfSendLimit: false, webhooksCount: 0,
    },
    plan: { status: "Observed", reason: null, active: "Essential", matches: true },
  };
}

export function overviewSnapshotFixture(): AdminOverviewSnapshot {
  return {
    accounts: { kind: "loaded", data: accountOverviewFixture(), sampledAt: OVERVIEW_TIME, refreshFailed: false },
    audit: { kind: "loaded", data: [{
      id: "event-1", occurredAt: OVERVIEW_TIME, eventType: "AccountSuspendedEvent",
      aggregateType: "JobSeeker", aggregateId: "00000000-0000-4000-8000-000000000001",
    }], sampledAt: OVERVIEW_TIME, refreshFailed: false },
    jobs: { kind: "loaded", data: { totalCount: 3 }, sampledAt: OVERVIEW_TIME, refreshFailed: false },
    backup: { kind: "loaded", data: backupFixture(), sampledAt: OVERVIEW_TIME, refreshFailed: false },
    host: { kind: "loaded", data: hostFixture(), sampledAt: "2026-10-08T09:59:30Z", refreshFailed: false },
    services: { kind: "loaded", data: servicesFixture(), sampledAt: "2026-10-08T09:59:30Z", refreshFailed: false },
    email: { kind: "loaded", data: emailAllowanceFixture(), sampledAt: "2026-10-08T09:59:30+00:00", refreshFailed: false },
  };
}