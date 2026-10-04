import type { AdminAccountDetail } from "@/lib/admin/view-models";
import type { AuditLogEntryDto, FailedJobsResponse, RecurringJobStatusDto } from "@/lib/dto/admin";
/**
 * The admin preview's fictional data (ADR 0150 D5). Addresses are on a reserved `.invalid`
 * domain, IP addresses are from the documentation ranges, and every date is counted from one
 * fixed clock. Accounts carry no name field (D3). Every row carries the domain, which is the
 * sentinel the build assertion looks for, so a row that reaches a build output is found there
 * whichever row it is. This module never reaches an image (`.dockerignore`), and nothing outside
 * the preview imports it.
 */
const SENTINEL = "forhandsvisning.invalid";

export const FIXTURE_NOW = "2026-10-04T08:00:00.000Z";

const DAY_MS = 86_400_000;

/** An instant `days` days before the fixed clock, at the given UTC time of day. */
function daysAgo(days: number, hours = 9, minutes = 0): string {
  const midnight = Date.parse(FIXTURE_NOW.slice(0, 10)) - days * DAY_MS;
  return new Date(midnight + (hours * 60 + minutes) * 60_000).toISOString();
}

function daysAhead(days: number): string {
  return new Date(Date.parse(FIXTURE_NOW) + days * DAY_MS).toISOString();
}

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

function marked<T extends object>(row: T): T {
  return { ...row, fixture: SENTINEL };
}

const address = (local: string) => `${local}@${SENTINEL}`;

/** The administrator the preview's header shows; also an account in the list. */
export const PREVIEW_ADMIN_EMAIL = address("admin");

/** A deletion scheduled at the fixed clock is permanent no earlier than this. */
export const PREVIEW_DELETION_EARLIEST = daysAhead(30);

function account(row: AdminAccountDetail): AdminAccountDetail {
  return marked(row);
}

export const PREVIEW_ACCOUNTS: ReadonlyArray<AdminAccountDetail> = [
  account({ id: id(1), email: PREVIEW_ADMIN_EMAIL, role: "admin", status: "active", registeredAt: daysAgo(150, 7, 12), applicationCount: 2, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1 }),
  account({ id: id(2), email: address("konto.a"), role: "user", status: "active", registeredAt: daysAgo(6, 12, 2), applicationCount: 4, deletionEarliest: null, savedSearchCount: 3, resumeCount: 1 }),
  account({ id: id(3), email: address("konto.b"), role: "user", status: "active", registeredAt: daysAgo(4, 7, 15), applicationCount: 3, deletionEarliest: null, savedSearchCount: 2, resumeCount: 2 }),
  account({ id: id(4), email: address("konto.c"), role: "user", status: "suspended", registeredAt: daysAgo(44, 9, 40), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 1 }),
  account({ id: id(5), email: address("konto.d"), role: "user", status: "unverified", registeredAt: daysAgo(2, 17, 33), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 0 }),
  account({ id: id(6), email: address("konto.e"), role: "user", status: "pendingDeletion", registeredAt: daysAgo(82, 8, 10), applicationCount: 9, deletionEarliest: daysAhead(24), savedSearchCount: 4, resumeCount: 2 }),
  account({ id: id(7), email: address("konto.f"), role: "user", status: "active", registeredAt: daysAgo(22, 10, 5), applicationCount: 11, deletionEarliest: null, savedSearchCount: 5, resumeCount: 3 }),
  account({ id: id(8), email: address("konto.g"), role: "user", status: "active", registeredAt: daysAgo(9, 19, 1), applicationCount: 7, deletionEarliest: null, savedSearchCount: 2, resumeCount: 1 }),
  account({ id: id(9), email: address("konto.h"), role: "user", status: "active", registeredAt: daysAgo(1, 13, 47), applicationCount: 1, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1 }),
  account({ id: id(10), email: address("konto.i"), role: "user", status: "unverified", registeredAt: daysAgo(0, 6, 58), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 0 }),
  account({ id: id(11), email: address("konto.j"), role: "user", status: "active", registeredAt: daysAgo(27, 11, 22), applicationCount: 6, deletionEarliest: null, savedSearchCount: 2, resumeCount: 1 }),
  account({ id: id(12), email: address("konto.k"), role: "user", status: "active", registeredAt: daysAgo(15, 20, 14), applicationCount: 2, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1 }),
  account({ id: id(13), email: address("konto.l"), role: "user", status: "suspended", registeredAt: daysAgo(61, 15, 3), applicationCount: 3, deletionEarliest: null, savedSearchCount: 1, resumeCount: 2 }),
  account({ id: id(14), email: address("konto.m"), role: "user", status: "active", registeredAt: null, applicationCount: null, deletionEarliest: null, savedSearchCount: null, resumeCount: null }),
  account({ id: id(15), email: address("konto.n.med.en.mycket.lang.adress.for.smala.skarmar"), role: "user", status: "active", registeredAt: daysAgo(33, 10, 44), applicationCount: 1, deletionEarliest: null, savedSearchCount: 0, resumeCount: 1 }),
];

function recurringJob(row: RecurringJobStatusDto): RecurringJobStatusDto {
  return marked(row);
}

export const PREVIEW_RECURRING_JOBS: ReadonlyArray<RecurringJobStatusDto> = [
  recurringJob({ id: "audit-log-retention", cron: "0 3 * * *", lastExecution: daysAgo(0, 3, 0), lastJobState: "Succeeded", nextExecution: daysAgo(-1, 3, 0) }),
  recurringJob({ id: "hard-delete-accounts", cron: "0 4 * * *", lastExecution: daysAgo(0, 4, 0), lastJobState: "Succeeded", nextExecution: daysAgo(-1, 4, 0) }),
  recurringJob({ id: "sync-platsbanken-snapshot", cron: "0 2 * * *", lastExecution: daysAgo(0, 2, 0), lastJobState: "Failed", nextExecution: daysAgo(-1, 2, 0) }),
  recurringJob({ id: "sync-platsbanken-stream", cron: "*/10 * * * *", lastExecution: daysAgo(0, 7, 50), lastJobState: "Succeeded", nextExecution: daysAgo(0, 8, 0) }),
];

export const PREVIEW_FAILED_JOBS: FailedJobsResponse = marked({
  totalCount: 1,
  returned: 50,
  items: [
    marked({ jobId: "18234", jobType: "SyncPlatsbankenSnapshotWorker", failedAt: daysAgo(0, 2, 4), errorCategory: "HttpRequestException" }),
  ],
});

function auditEntry(row: AuditLogEntryDto): AuditLogEntryDto {
  return marked(row);
}

export const PREVIEW_AUDIT_ENTRIES: ReadonlyArray<AuditLogEntryDto> = [
  auditEntry({ id: id(101), occurredAt: daysAgo(0, 7, 41), correlationId: id(201), userId: id(2), impersonatedBy: null, eventType: "Application.StatusTransitioned", aggregateType: "Application", aggregateId: id(301), ipAddress: "192.0.2.0", userAgent: `Mozilla/5.0 (${SENTINEL})` }),
  auditEntry({ id: id(102), occurredAt: daysAgo(0, 6, 12), correlationId: id(202), userId: id(8), impersonatedBy: null, eventType: "SavedSearch.Created", aggregateType: "SavedSearch", aggregateId: id(302), ipAddress: "198.51.100.0", userAgent: `Mozilla/5.0 (${SENTINEL})` }),
  auditEntry({ id: id(103), occurredAt: daysAgo(1, 19, 3), correlationId: id(203), userId: id(1), impersonatedBy: null, eventType: "Admin.RecurringJobTriggered", aggregateType: "System.BackgroundJob", aggregateId: id(303), ipAddress: "192.0.2.0", userAgent: `Mozilla/5.0 (${SENTINEL})` }),
  auditEntry({ id: id(104), occurredAt: daysAgo(1, 15, 26), correlationId: id(204), userId: id(3), impersonatedBy: null, eventType: "User.InboxProvenByLogin", aggregateType: "User", aggregateId: id(3), ipAddress: "203.0.113.0", userAgent: `Mozilla/5.0 (${SENTINEL})` }),
];
