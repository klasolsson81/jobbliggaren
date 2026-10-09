import type { AccountOverviewDto, AdminOverviewSnapshot } from "@/lib/dto/admin-overview";

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

export function overviewSnapshotFixture(): AdminOverviewSnapshot {
  return {
    accounts: { kind: "loaded", data: accountOverviewFixture(), sampledAt: OVERVIEW_TIME, refreshFailed: false },
    audit: { kind: "loaded", data: [{
      id: "event-1", occurredAt: OVERVIEW_TIME, eventType: "AccountSuspendedEvent",
      aggregateType: "JobSeeker", aggregateId: "00000000-0000-4000-8000-000000000001",
    }], sampledAt: OVERVIEW_TIME, refreshFailed: false },
    jobs: { kind: "loaded", data: { totalCount: 3 }, sampledAt: OVERVIEW_TIME, refreshFailed: false },
  };
}