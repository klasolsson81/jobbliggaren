import { FEEDBACK_PAGE_KEYS } from "@/lib/admin/feedback";
import { toServerReading } from "@/lib/admin/host-observation";
import { toServicesReading } from "@/lib/admin/service-observation";
import type {
  AdminAccountDetail,
  AdminAccountTotals,
  AdminActiveAccounts,
  AdminAttentionItem,
  AdminBackupStatus,
  AdminEmailAllowance,
  AdminEmailDelivery,
  AdminEmailFailures,
  AdminEmailPeriod,
  AdminErrorLogRow,
  AdminFeedbackAvailability,
  AdminFeedbackClient,
  AdminFeedbackItem,
  AdminFeedbackPageSummary,
  AdminFeedbackWindow,
  AdminImportLogRow,
  AdminLogins,
  AdminNewAccounts,
  AdminRecentEvent,
  AdminSecurityLogRow,
  AdminServerReading,
  AdminServicesReading,
  AdminSelf,
  AdminTrendDay,
} from "@/lib/admin/view-models";
import type { AdminPendingEmailChange } from "@/lib/admin/account-email-change";
import type { AuditLogEntryDto, FailedJobsResponse, RecurringJobStatusDto } from "@/lib/dto/admin";
import { EMAIL_FAILURES_LIMIT, type EmailAllowanceFailure, type EmailFailureReason } from "@/lib/dto/admin-email";
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

/** A deletion scheduled at the fixed clock is permanent no earlier than this date. */
export const PREVIEW_DELETION_EARLIEST = daysAhead(30).slice(0, 10);

function account(row: AdminAccountDetail): AdminAccountDetail {
  return marked(row);
}

/**
 * The preview's start of recording (ADR 0172 D1): earlier than every activity value below, after some registrations.
 * An account registered before it and not seen since has neither value, and one whose session predates it has a last
 * active minute and no login.
 */
export const PREVIEW_ACTIVITY_RECORDED_FROM = daysAgo(20, 6, 30);

export const PREVIEW_ACCOUNTS: ReadonlyArray<AdminAccountDetail> = [
  account({ id: id(1), email: PREVIEW_ADMIN_EMAIL, role: "admin", status: "active", emailConfirmed: true, registeredAt: daysAgo(150, 7, 12), applicationCount: 2, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1, lastLoginAt: daysAgo(0, 7, 2), lastActiveAt: daysAgo(0, 7, 58) }),
  account({ id: id(2), email: address("konto.a"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(6, 12, 2), applicationCount: 4, deletionEarliest: null, savedSearchCount: 3, resumeCount: 1, lastLoginAt: daysAgo(1, 18, 12), lastActiveAt: daysAgo(0, 7, 41) }),
  account({ id: id(3), email: address("konto.b"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(4, 7, 15), applicationCount: 3, deletionEarliest: null, savedSearchCount: 2, resumeCount: 2, lastLoginAt: daysAgo(4, 7, 16), lastActiveAt: daysAgo(2, 20, 5) }),
  account({ id: id(4), email: address("konto.c"), role: "user", status: "suspended", emailConfirmed: true, registeredAt: daysAgo(44, 9, 40), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 1, lastLoginAt: daysAgo(12, 8, 30), lastActiveAt: daysAgo(12, 9, 14) }),
  account({ id: id(5), email: address("konto.d"), role: "user", status: "active", emailConfirmed: false, registeredAt: daysAgo(2, 17, 33), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 0, lastLoginAt: daysAgo(2, 17, 33), lastActiveAt: daysAgo(1, 10, 3) }),
  account({ id: id(6), email: address("konto.e"), role: "user", status: "pendingDeletion", emailConfirmed: true, registeredAt: daysAgo(82, 8, 10), applicationCount: null, deletionEarliest: daysAhead(24).slice(0, 10), savedSearchCount: null, resumeCount: null, lastLoginAt: daysAgo(6, 19, 40), lastActiveAt: daysAgo(6, 19, 52) }),
  account({ id: id(7), email: address("konto.f"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(22, 10, 5), applicationCount: 11, deletionEarliest: null, savedSearchCount: 5, resumeCount: 3, lastLoginAt: daysAgo(3, 9, 0), lastActiveAt: daysAgo(0, 6, 15) }),
  account({ id: id(8), email: address("konto.g"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(9, 19, 1), applicationCount: 7, deletionEarliest: null, savedSearchCount: 2, resumeCount: 1, lastLoginAt: daysAgo(9, 19, 2), lastActiveAt: daysAgo(5, 12, 30) }),
  account({ id: id(9), email: address("konto.h"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(1, 13, 47), applicationCount: 1, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1, lastLoginAt: daysAgo(1, 13, 48), lastActiveAt: daysAgo(1, 14, 20) }),
  account({ id: id(10), email: address("konto.i"), role: "user", status: "active", emailConfirmed: false, registeredAt: daysAgo(0, 6, 58), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 0, lastLoginAt: daysAgo(0, 6, 58), lastActiveAt: daysAgo(0, 7, 10) }),
  account({ id: id(11), email: address("konto.j"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(27, 11, 22), applicationCount: 6, deletionEarliest: null, savedSearchCount: 2, resumeCount: 1, lastLoginAt: null, lastActiveAt: null }),
  account({ id: id(12), email: address("konto.k"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(15, 20, 14), applicationCount: 2, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1, lastLoginAt: daysAgo(15, 20, 15), lastActiveAt: daysAgo(11, 8, 45) }),
  account({ id: id(13), email: address("konto.l"), role: "user", status: "suspended", emailConfirmed: true, registeredAt: daysAgo(61, 15, 3), applicationCount: 3, deletionEarliest: null, savedSearchCount: 1, resumeCount: 2, lastLoginAt: daysAgo(19, 10, 0), lastActiveAt: daysAgo(19, 10, 7) }),
  account({ id: id(14), email: address("konto.m"), role: "user", status: "profileMissing", emailConfirmed: true, registeredAt: null, applicationCount: null, deletionEarliest: null, savedSearchCount: null, resumeCount: null, lastLoginAt: null, lastActiveAt: null }),
  account({ id: id(15), email: address("konto.n.med.en.mycket.lang.adress.for.smala.skarmar"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(33, 10, 44), applicationCount: 1, deletionEarliest: null, savedSearchCount: 0, resumeCount: 1, lastLoginAt: null, lastActiveAt: daysAgo(2, 9, 30) }),
];

/** The administrator the preview acts as: the first account, told by its id as the real page tells it (#1975). */
export const PREVIEW_SELF: AdminSelf = { userId: id(1), email: PREVIEW_ADMIN_EMAIL };

const HOUR_MS = 3_600_000;

function hoursAhead(hours: number): string {
  return new Date(Date.parse(FIXTURE_NOW) + hours * HOUR_MS).toISOString();
}

/**
 * Address changes started before the fixed clock, one in each state the panel names (#1975): one still waiting for
 * its owner, and one whose code was entered wrongly too many times.
 */
export const PREVIEW_EMAIL_CHANGES: ReadonlyArray<{
  readonly accountId: string;
  readonly change: AdminPendingEmailChange;
}> = [
  marked({ accountId: id(7), change: { state: "pending", completableFrom: hoursAhead(50), expiresAt: hoursAhead(74) } }),
  marked({ accountId: id(11), change: { state: "codeBurned", completableFrom: hoursAhead(-6), expiresAt: hoursAhead(18) } }),
];

/** The two instants of a change the preview starts at the fixed clock. */
export const PREVIEW_EMAIL_CHANGE_STARTED = marked({ completableFrom: hoursAhead(72), expiresAt: hoursAhead(96) });

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

// ── Part 3: the overview, feedback, logs and email delivery ─────────────────────────────────

const mask = (local: string, domain: string = SENTINEL) => `${local.slice(0, 1)}•••@${domain}`;

/** Calendar day `days` days before the fixed clock, `YYYY-MM-DD` (UTC; the clock sits mid-morning in Sweden). */
function dayKey(days: number): string {
  return daysAgo(days).slice(0, 10);
}

const TREND_DAYS = 90;

/** New accounts per day come from the accounts above, so the overview and the list agree. */
function newAccountsOn(days: number): number {
  return PREVIEW_ACCOUNTS.filter((row) => row.registeredAt?.slice(0, 10) === dayKey(days)).length;
}

/** A fixed, uneven weekly rhythm of logins: busier on weekdays, never random. */
function loginsOn(days: number): number {
  const weekday = new Date(daysAgo(days)).getUTCDay();
  const base = weekday === 0 || weekday === 6 ? 3 : 8;
  return base + ((days * 7) % 5);
}

const trend: ReadonlyArray<AdminTrendDay> = Array.from({ length: TREND_DAYS }, (_, index) => {
  const days = TREND_DAYS - 1 - index;
  return marked({ date: dayKey(days), newAccounts: newAccountsOn(days), logins: loginsOn(days) });
});

const registeredWithin = (days: number) =>
  PREVIEW_ACCOUNTS.filter(
    (row) => row.registeredAt !== null && Date.parse(FIXTURE_NOW) - Date.parse(row.registeredAt) < days * DAY_MS,
  ).length;

// ── The email allowance (#1981 PR 1, ADR 0178) ──────────────────────────────────────────────────

/** The configured terms as the API sends them; the price list "read" a week before the fixed clock. */
const ALLOWANCE_TERMS = marked({
  plan: "Essential" as const,
  includedMonthlyEmails: 300,
  pricePerThousandExtra: 0.25,
  currency: "EUR" as const,
  priceReadOn: dayKey(6),
  monthlySendLimit: 10_000,
  costCurrency: "SEK" as const,
  sekPerEur: 11.145,
  exchangeRateOn: "2026-10-16",
});

const FIXTURE_MONTH = FIXTURE_NOW.slice(0, 7);

/** An observed month. The figures are written out per scenario, never computed here: the backend owns the arithmetic. */
function observedUsage(figures: {
  readonly sent: number;
  readonly billable: number | null;
  readonly ore: number | null;
  readonly aboveHalf?: boolean;
  readonly month?: string | null;
  readonly webhooks?: number | null;
}): AdminEmailAllowance["usage"] {
  return marked({
    status: "Observed" as const,
    reason: null,
    observedAt: daysAgo(0, 7, 59),
    month: figures.month === undefined ? FIXTURE_MONTH : figures.month,
    sent: figures.sent,
    billable: figures.billable,
    estimatedExtraCostOre: figures.ore,
    aboveHalfOfSendLimit: figures.aboveHalf ?? false,
    webhooksCount: figures.webhooks === undefined ? 0 : figures.webhooks,
  });
}

const NO_USAGE = {
  observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostOre: null,
  aboveHalfOfSendLimit: null, webhooksCount: null,
} as const;

function failedUsage(reason: "Timeout" | "RateLimited" | "Unauthorized" | "Forbidden" | "Malformed"): AdminEmailAllowance["usage"] {
  return marked({ status: "Failed" as const, reason, ...NO_USAGE });
}

const PLAN_IN_FORCE = marked({ status: "Observed" as const, reason: null, active: "Essential" as const, matches: true });

function allowance(usage: AdminEmailAllowance["usage"], plan: AdminEmailAllowance["plan"] = PLAN_IN_FORCE): AdminEmailAllowance {
  return marked({ terms: ALLOWANCE_TERMS, usage, plan });
}

/** Every state the allowance region has, for the e-post preview's scenario choice (ADR 0150 D5). */
export const PREVIEW_EMAIL_ALLOWANCE = marked({
  withinQuota: allowance(observedUsage({ sent: 142, billable: 0, ore: 0 })),
  nothingSent: allowance(observedUsage({ sent: 0, billable: 0, ore: 0 })),
  atQuota: allowance(observedUsage({ sent: 300, billable: 0, ore: 0 })),
  overQuota: allowance(observedUsage({ sent: 1301, billable: 1001, ore: 349 })),
  aboveHalf: allowance(observedUsage({ sent: 5200, billable: 4900, ore: 1707, aboveHalf: true })),
  planDiffers: allowance(
    observedUsage({ sent: 412, billable: null, ore: null }),
    marked({ status: "Observed" as const, reason: null, active: "Scale" as const, matches: false }),
  ),
  planUnknown: allowance(
    observedUsage({ sent: 412, billable: null, ore: null }),
    marked({ status: "Observed" as const, reason: null, active: "Unknown" as const, matches: false }),
  ),
  planFailed: allowance(
    observedUsage({ sent: 412, billable: null, ore: null }),
    marked({ status: "Failed" as const, reason: "ProviderError" as const, active: null, matches: null }),
  ),
  notConfigured: allowance(
    marked({ status: "NotObserved" as const, reason: "NotConfigured" as const, ...NO_USAGE }),
    marked({ status: "NotObserved" as const, reason: "NotConfigured" as const, active: null, matches: null }),
  ),
  readKeyMissing: allowance(
    marked({ status: "ReadKeyMissing" as const, reason: null, ...NO_USAGE }),
    marked({ status: "ReadKeyMissing" as const, reason: null, active: null, matches: null }),
  ),
  noAnswer: allowance(failedUsage("Timeout")),
  rateLimited: allowance(
    failedUsage("RateLimited"),
    marked({ status: "Failed" as const, reason: "RateLimited" as const, active: null, matches: null }),
  ),
  keyRefused: allowance(
    failedUsage("Unauthorized"),
    marked({ status: "Failed" as const, reason: "Unauthorized" as const, active: null, matches: null }),
  ),
  refused: allowance(
    failedUsage("Forbidden"),
    marked({ status: "Failed" as const, reason: "Forbidden" as const, active: null, matches: null }),
  ),
  malformed: allowance(failedUsage("Malformed")),
  noMonth: allowance(observedUsage({ sent: 3, billable: 0, ore: 0, month: null })),
  webhooks: allowance(observedUsage({ sent: 142, billable: 0, ore: 0, webhooks: 1 })),
  webhooksUnreadable: allowance(observedUsage({ sent: 142, billable: 0, ore: 0, webhooks: null })),
});

export type PreviewAllowanceScenario = keyof typeof PREVIEW_EMAIL_ALLOWANCE;

/** Every overview region with data, in the view models' shapes. */
export interface PreviewOverviewData {
  readonly newAccounts: AdminNewAccounts;
  readonly totals: AdminAccountTotals;
  readonly active: AdminActiveAccounts;
  readonly logins: AdminLogins;
  readonly trend: ReadonlyArray<AdminTrendDay>;
  readonly services: AdminServicesReading;
  readonly server: AdminServerReading;
  readonly backup: AdminBackupStatus;
  readonly email: AdminEmailAllowance;
  readonly attention: ReadonlyArray<AdminAttentionItem>;
  readonly events: ReadonlyArray<AdminRecentEvent>;
}

export const PREVIEW_OVERVIEW: PreviewOverviewData = marked({
  newAccounts: marked({
    today: newAccountsOn(0),
    yesterday: newAccountsOn(1),
    last7Days: registeredWithin(7),
    last30Days: registeredWithin(30),
  }),
  totals: marked({
    total: PREVIEW_ACCOUNTS.length,
    suspended: PREVIEW_ACCOUNTS.filter((row) => row.status === "suspended").length,
    pendingDeletion: PREVIEW_ACCOUNTS.filter((row) => row.status === "pendingDeletion").length,
  }),
  active: marked({ last30Days: 9, today: 4, last7Days: 7 }),
  logins: marked({ today: loginsOn(0), yesterday: loginsOn(1), failed: 2, locked: 0 }),
  trend,
  // A live Worker, a store that answers and an import run that recorded two errors, so the warning form is seen.
  services: marked(toServicesReading({
    staleAfterSeconds: 120,
    workerAliveWithinSeconds: 120,
    importOverdueAfterSeconds: 1500,
    backgroundJobs: { state: "Alive", sampledAt: daysAgo(0, 7, 59), heartbeatAt: daysAgo(0, 7, 58) },
    volatileStore: { state: "Reachable", sampledAt: daysAgo(0, 7, 59) },
    jobImport: { state: "Recorded", sampledAt: daysAgo(0, 7, 59), lastRunAt: daysAgo(0, 7, 50), errors: 2 },
  })),
  server: marked(toServerReading({
    staleAfterSeconds: 120,
    cpu: { state: "Available", sampledAt: daysAgo(0, 7, 59), value: { percent: 23.4, windowSeconds: 30 } },
    memory: { state: "Available", sampledAt: daysAgo(0, 7, 59), value: { percent: 61.1, usedBytes: 5_244_620_800, totalBytes: 8_589_934_592 } },
    disk: { state: "Available", sampledAt: daysAgo(0, 7, 59), value: { percent: 38, freeBytes: 133_143_986_176, totalBytes: 214_748_364_800 } },
  })),
  backup: marked({
    lastSuccess: marked({ state: "recorded" as const, completedAt: daysAgo(0, 1, 30), overdue: false }),
    timer: marked({ state: "scheduled" as const, nextRunAt: daysAgo(-1, 1, 30) }),
  }),
  email: PREVIEW_EMAIL_ALLOWANCE.withinQuota,
  attention: [
    marked({ kind: "failedJobs" as const, count: 1 }),
    marked({ kind: "pendingDeletions" as const, count: 1 }),
    marked({ kind: "failedEmails" as const, count: 2 }),
  ],
  events: [
    marked({ id: id(401), occurredAt: daysAgo(0, 6, 58), kind: "accountCreated" as const, subject: address("konto.i") }),
    marked({ id: id(402), occurredAt: daysAgo(0, 2, 4), kind: "jobFailed" as const, subject: "sync-platsbanken-snapshot" }),
    marked({ id: id(403), occurredAt: daysAgo(1, 13, 47), kind: "accountCreated" as const, subject: address("konto.h") }),
    marked({ id: id(404), occurredAt: daysAgo(3, 10, 12), kind: "emailChangeRequested" as const, subject: address("konto.f") }),
    marked({ id: id(405), occurredAt: daysAgo(6, 15, 30), kind: "deletionScheduled" as const, subject: address("konto.e") }),
    marked({ id: id(406), occurredAt: daysAgo(9, 8, 20), kind: "accountReinstated" as const, subject: address("konto.g") }),
    marked({ id: id(407), occurredAt: daysAgo(12, 16, 5), kind: "accountSuspended" as const, subject: address("konto.l") }),
    marked({ id: id(408), occurredAt: daysAgo(33, 10, 44), kind: "accountCreated" as const, subject: address("konto.n.med.en.mycket.lang.adress.for.smala.skarmar") }),
  ],
});

/** The overview's "Tom" state: every count zero, every list empty; backup keeps its values. */
export const PREVIEW_OVERVIEW_ZERO: PreviewOverviewData = marked({
  ...PREVIEW_OVERVIEW,
  newAccounts: marked({ today: 0, yesterday: 0, last7Days: 0, last30Days: 0 }),
  totals: marked({ total: 0, suspended: 0, pendingDeletion: 0 }),
  active: marked({ last30Days: 0, today: 0, last7Days: 0 }),
  logins: marked({ today: 0, yesterday: 0, failed: 0, locked: 0 }),
  trend: trend.map((day) => marked({ date: day.date, newAccounts: 0, logins: 0 })),
  email: PREVIEW_EMAIL_ALLOWANCE.nothingSent,
  // "Tom" shows the rows with nothing observed: no Worker has reported, the store could not be judged and no import
  // run is recorded. None of them is a zero, and none of them is an all-clear (ADR 0150 D2).
  services: marked(toServicesReading({
    staleAfterSeconds: 120,
    workerAliveWithinSeconds: 120,
    importOverdueAfterSeconds: 1500,
    backgroundJobs: { state: "NoServer", sampledAt: daysAgo(0, 7, 59), heartbeatAt: null },
    volatileStore: { state: "Unknown", sampledAt: null },
    jobImport: { state: "NoneRecorded", sampledAt: daysAgo(0, 7, 59), lastRunAt: null, errors: null },
  })),
  attention: [],
  events: [],
  // "Tom" shows the three readings that are not values: the CPU's first window, a host that cannot be measured
  // here, and a failed read. Zero is a value, but an unmeasured reading is not zero (ADR 0150 D2).
  server: marked(toServerReading({
    staleAfterSeconds: 120,
    cpu: { state: "Collecting", sampledAt: null, value: null },
    memory: { state: "NotObservable", sampledAt: null, value: null },
    disk: { state: "Failed", sampledAt: null, value: null },
  })),
});

// ── Feedback (#1979) ──────────────────────────────────────────────────────────────────────────

/** What a browser reports when it reports nothing: every value unknown. */
const NOTHING_REPORTED: AdminFeedbackClient = {
  viewportWidth: null,
  viewportHeight: null,
  screenWidth: null,
  screenHeight: null,
  pixelRatio: null,
  theme: null,
  deviceClass: null,
  os: null,
  browser: null,
};

/**
 * Submissions over the last two months, one with each status and each notice state, a rating without a
 * text and a text without a rating, a reporter whose address cannot be read, a browser that reported
 * nothing, and one reporter who rated the same page twice, so the summary counts only the latest rating.
 * The newest notice has been refused twice and waits a minute for its third attempt. The app versions
 * are commit-shaped, as the web stamps them.
 */
export const PREVIEW_FEEDBACK: ReadonlyArray<AdminFeedbackItem> = [
  marked({
    id: id(501),
    page: "applications",
    rating: 2,
    comment: "När jag sparar en ansökan och går tillbaka till listan visas den gamla statusen tills jag laddar om sidan.",
    status: "new" as const,
    submittedAt: daysAgo(0, 7, 55),
    statusChangedAt: null,
    reporterEmail: address("konto.b"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 1440, viewportHeight: 789, screenWidth: 1440, screenHeight: 900, pixelRatio: 1, theme: "light" as const, deviceClass: "desktop" as const, os: "windows" as const, browser: "firefox" as const },
    appVersion: "4f2a91c",
    notice: { state: "queued" as const, attempts: 2, nextAttemptAt: daysAgo(0, 8, 1) },
    replies: [],
  }),
  marked({
    id: id(502),
    page: "saved-ads",
    rating: 4,
    comment: "Det vore bra att kunna sortera sparade annonser på sista ansökningsdag.",
    status: "inProgress" as const,
    submittedAt: daysAgo(1, 18, 40),
    statusChangedAt: daysAgo(0, 7, 55),
    reporterEmail: address("konto.f"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 390, viewportHeight: 664, screenWidth: 390, screenHeight: 844, pixelRatio: 3, theme: "dark" as const, deviceClass: "mobile" as const, os: "ios" as const, browser: "safari" as const },
    appVersion: "4f2a91c",
    notice: { state: "accepted" as const, attempts: 1, nextAttemptAt: daysAgo(1, 18, 40) },
    replies: [
      { id: id(602), text: "Tack för förslaget. Vi tittar på sortering efter sista ansökningsdag.", state: "accepted" as const, createdAt: daysAgo(0, 7, 58), completedAt: daysAgo(0, 7, 58) },
    ],
  }),
  marked({
    id: id(503),
    page: "cv-review",
    rating: null,
    comment: "Hur länge sparas mitt uppladdade CV om jag inte loggar in på ett tag?",
    status: "new" as const,
    submittedAt: daysAgo(2, 11, 3),
    statusChangedAt: null,
    reporterEmail: address("konto.g"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 1280, viewportHeight: 720, screenWidth: 1280, screenHeight: 800, pixelRatio: 2, theme: "light" as const, deviceClass: "desktop" as const, os: "macOs" as const, browser: "chrome" as const },
    appVersion: "9c03e7b",
    notice: { state: "failed" as const, attempts: 5, nextAttemptAt: daysAgo(2, 12, 24) },
    replies: [],
  }),
  marked({
    id: id(504),
    page: "job-ad",
    rating: 5,
    comment: null,
    status: "resolved" as const,
    submittedAt: daysAgo(5, 9, 27),
    statusChangedAt: daysAgo(4, 10, 0),
    reporterEmail: address("konto.j"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 412, viewportHeight: 839, screenWidth: 412, screenHeight: 915, pixelRatio: 2.63, theme: "light" as const, deviceClass: "mobile" as const, os: "android" as const, browser: "samsungInternet" as const },
    appVersion: "9c03e7b",
    notice: { state: "unknown" as const, attempts: 1, nextAttemptAt: daysAgo(5, 9, 27) },
    replies: [],
  }),
  marked({
    id: id(505),
    page: "my-pages",
    rating: 3,
    comment: "Kan ni lägga till ett sätt att exportera mina ansökningar?",
    status: "declined" as const,
    submittedAt: daysAgo(8, 20, 15),
    statusChangedAt: daysAgo(7, 9, 0),
    reporterEmail: address("konto.k"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 1920, viewportHeight: 969, screenWidth: 1920, screenHeight: 1080, pixelRatio: 1, theme: "dark" as const, deviceClass: "desktop" as const, os: "windows" as const, browser: "edge" as const },
    appVersion: "1b7d0e4",
    notice: { state: "accepted" as const, attempts: 2, nextAttemptAt: daysAgo(8, 20, 16) },
    replies: [],
  }),
  marked({
    id: id(506),
    page: "jobs",
    rating: 4,
    comment: "Sökningen på kommun ger träffar från hela länet.\nJag sökte på Alingsås och fick annonser från Göteborg och Borås.\nDet gör det svårt att hitta jobb nära hemmet.",
    status: "inProgress" as const,
    submittedAt: daysAgo(12, 14, 30),
    statusChangedAt: daysAgo(11, 8, 45),
    reporterEmail: null,
    screenshot: null,
    client: NOTHING_REPORTED,
    appVersion: null,
    notice: { state: "accepted" as const, attempts: 1, nextAttemptAt: daysAgo(12, 14, 30) },
    replies: [],
  }),
  marked({
    id: id(507),
    page: "applications",
    rating: 1,
    comment: "Knappen för att flytta en ansökan till Intervju syns inte på min telefon.",
    status: "new" as const,
    submittedAt: daysAgo(20, 8, 5),
    statusChangedAt: null,
    reporterEmail: address("konto.b"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 820, viewportHeight: 1106, screenWidth: 820, screenHeight: 1180, pixelRatio: 2, theme: "light" as const, deviceClass: "tablet" as const, os: "ios" as const, browser: "safari" as const },
    appVersion: "1b7d0e4",
    notice: { state: "sending" as const, attempts: 1, nextAttemptAt: daysAgo(20, 8, 5) },
    replies: [],
  }),
  marked({
    id: id(508),
    page: "overview",
    rating: 5,
    comment: null,
    status: "resolved" as const,
    submittedAt: daysAgo(45, 10, 0),
    statusChangedAt: daysAgo(44, 9, 30),
    reporterEmail: address("konto.h"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 1366, viewportHeight: 657, screenWidth: 1366, screenHeight: 768, pixelRatio: 1, theme: "light" as const, deviceClass: "desktop" as const, os: "linux" as const, browser: "firefox" as const },
    appVersion: "e05c2aa",
    notice: { state: "accepted" as const, attempts: 1, nextAttemptAt: daysAgo(45, 10, 0) },
    replies: [],
  }),
  marked({
    id: id(509),
    page: "statistics",
    rating: 4,
    comment: "Statistiken per månad är tydlig.",
    status: "new" as const,
    submittedAt: daysAgo(61, 16, 20),
    statusChangedAt: null,
    reporterEmail: address("konto.a"),
    screenshot: null,
    client: { ...NOTHING_REPORTED, viewportWidth: 1536, viewportHeight: 730, screenWidth: 1536, screenHeight: 864, pixelRatio: 1.25, theme: "light" as const, deviceClass: "desktop" as const, os: "windows" as const, browser: "chrome" as const },
    appVersion: "e05c2aa",
    notice: null,
    replies: [],
  }),
];

/** Feedback's gate as the preview shows it: closed for want of a recipient, so its line is seen. */
export const PREVIEW_FEEDBACK_AVAILABILITY: { readonly availability: AdminFeedbackAvailability } = marked({
  availability: "noRecipient" as const,
});

const PAGE_ORDER: ReadonlyMap<string, number> = new Map(FEEDBACK_PAGE_KEYS.map((key, index) => [key, index]));

/** The summary the backend would answer for the window: each reporter's latest rating per page counts once. */
function summaryFor(days: number): ReadonlyArray<AdminFeedbackPageSummary> {
  const since = Date.parse(FIXTURE_NOW) - days * DAY_MS;
  const inWindow = PREVIEW_FEEDBACK.filter((item) => Date.parse(item.submittedAt) >= since);
  const pages = [...new Set(inWindow.map((item) => item.page))].sort(
    (left, right) => (PAGE_ORDER.get(left) ?? PAGE_ORDER.size) - (PAGE_ORDER.get(right) ?? PAGE_ORDER.size),
  );
  return pages.map((page) => {
    const submissions = inWindow
      .filter((item) => item.page === page)
      .sort((left, right) => left.submittedAt.localeCompare(right.submittedAt));
    const latest = new Map<string, number>();
    for (const item of submissions) {
      if (item.rating !== null) latest.set(item.reporterEmail ?? item.id, item.rating);
    }
    const ratings = [...latest.values()];
    const rated = (value: number) => ratings.filter((rating) => rating === value).length;
    const mean = ratings.length === 0 ? null : ratings.reduce((sum, rating) => sum + rating, 0) / ratings.length;
    return marked({
      page,
      submissions: submissions.length,
      raters: ratings.length,
      ratings: [rated(1), rated(2), rated(3), rated(4), rated(5)] as const,
      mean: mean === null ? null : Math.round(mean * 100) / 100,
    });
  });
}

/** The summary per window, from the submissions above, so the list and the summary agree. */
export const PREVIEW_FEEDBACK_SUMMARY: Readonly<Record<AdminFeedbackWindow, ReadonlyArray<AdminFeedbackPageSummary>>> =
  marked({ 7: summaryFor(7), 30: summaryFor(30), 90: summaryFor(90) });

export const PREVIEW_SECURITY_LOG: ReadonlyArray<AdminSecurityLogRow> = [
  marked({ id: id(701), occurredAt: daysAgo(0, 7, 31), kind: "loginFailed" as const, account: mask("konto.d"), ip: "192.0.2.0", count: 3, detail: "Fel kod" }),
  marked({ id: id(702), occurredAt: daysAgo(0, 6, 2), kind: "rateLimited" as const, account: mask("konto.m"), ip: "198.51.100.0", count: 12, detail: "För många kodförsök" }),
  marked({ id: id(703), occurredAt: daysAgo(1, 22, 47), kind: "loginFailed" as const, account: mask("konto.a"), ip: "203.0.113.0", count: 1, detail: "Utgången länk" }),
  marked({ id: id(704), occurredAt: daysAgo(2, 9, 15), kind: "accountLocked" as const, account: mask("konto.c"), ip: "192.0.2.0", count: 1, detail: "Suspenderat konto försökte logga in" }),
  marked({ id: id(705), occurredAt: daysAgo(3, 14, 0), kind: "adminImpersonation" as const, account: mask("konto.b"), ip: "198.51.100.0", count: 1, detail: "Felsökning av ansökningslistan" }),
];

export const PREVIEW_ERROR_LOG: ReadonlyArray<AdminErrorLogRow> = [
  marked({ id: id(801), lastSeenAt: daysAgo(0, 2, 4), level: "error" as const, source: "SyncPlatsbankenSnapshotWorker", message: "Platsbanken svarade inte inom tidsgränsen.", count24h: 3 }),
  marked({ id: id(802), lastSeenAt: daysAgo(0, 7, 40), level: "warning" as const, source: "SyncPlatsbankenStreamWorker", message: "En annons saknade arbetsgivare och hoppades över.", count24h: 1 }),
  marked({ id: id(803), lastSeenAt: daysAgo(1, 16, 22), level: "warning" as const, source: "PdfPigOpenXmlCvTextExtractor", message: "En PDF hade ingen text att läsa.", count24h: 2 }),
];

export const PREVIEW_IMPORT_LOG: ReadonlyArray<AdminImportLogRow> = [
  marked({ id: id(901), run: "#18240", kind: "delta" as const, startedAt: daysAgo(0, 7, 50), durationSeconds: 42, fetched: 318, added: 41, updated: 260, closed: 17, status: "succeeded" as const }),
  marked({ id: id(902), run: "#18239", kind: "delta" as const, startedAt: daysAgo(0, 7, 40), durationSeconds: 39, fetched: 297, added: 36, updated: 248, closed: 13, status: "succeeded" as const }),
  marked({ id: id(903), run: "#18234", kind: "full" as const, startedAt: daysAgo(0, 2, 0), durationSeconds: 244, fetched: 0, added: 0, updated: 0, closed: 0, status: "failed" as const }),
  marked({ id: id(904), run: "#18201", kind: "full" as const, startedAt: daysAgo(1, 2, 0), durationSeconds: 1212, fetched: 46_120, added: 512, updated: 45_390, closed: 218, status: "succeeded" as const }),
];


// ── Leverans (#1981 PR 2) ─────────────────────────────────────────────────────────────────────

/** One Leverans answer: the summary read and the failures read for the same period, as the API sends them. */
export interface PreviewDelivery {
  readonly summary: AdminEmailDelivery;
  readonly failures: AdminEmailFailures;
}

/** Every state Leverans has (ADR 0150 D5); the e-post preview's second choice lists them. */
export type PreviewDeliveryScenario =
  | "observed"
  | "capped"
  | "noneFailed"
  | "nothingSent"
  | "listUnjudged"
  | "notConfigured"
  | "notObservable"
  | "readKeyMissing"
  | "noAnswer"
  | "rateLimited"
  | "keyRefused"
  | "refused"
  | "malformed";

/** A subdomain as long as a real one can run, on the preview's reserved domain, so the column is seen to hold it. */
const LONG_DOMAIN = `postavdelningen.ett-mycket-langt-underdomannamn-for-smala-skarmar.${SENTINEL}`;

type FailureRow = Extract<AdminEmailFailures, { readonly status: "Observed" }>["failures"][number];

function failure(createdAt: string, recipient: string, reason: EmailFailureReason): FailureRow {
  return marked({ createdAt, recipient, reason });
}

/** The latest failed sends, newest first: two in the last 24 hours, three in 3 days and five in 7 days. */
const FAILED_SENDS = [
  failure(daysAgo(0, 6, 41), mask("konto.d"), "MailboxNotFound"),
  failure(daysAgo(0, 5, 3), mask("konto.k"), "SoftBounce"),
  failure(daysAgo(2, 19, 12), mask("konto.a"), "HardBounce"),
  failure(daysAgo(5, 8, 30), mask("konto.h"), "MailboxFull"),
  failure(daysAgo(6, 11, 20), mask("konto.j"), "Spam"),
];

const PERIOD_ANSWERS: Readonly<Record<AdminEmailPeriod, {
  readonly sent: number;
  readonly failed: number;
  readonly rows: ReadonlyArray<FailureRow>;
}>> = {
  h24: { sent: 57, failed: 2, rows: FAILED_SENDS.slice(0, 2) },
  d3: { sent: 163, failed: 3, rows: FAILED_SENDS.slice(0, 3) },
  d7: { sent: 384, failed: 5, rows: FAILED_SENDS },
};

/**
 * As many failed sends in the last 24 hours as one read carries, newest first: one with each of the seven reasons,
 * one at a long domain and one whose address had nothing to mask. The provider counts more than the list shows.
 */
const CAPPED_SENDS = [
  failure(daysAgo(0, 7, 52), mask("konto.b"), "MailboxNotFound"),
  failure(daysAgo(0, 7, 40), mask("konto.n", LONG_DOMAIN), "HardBounce"),
  failure(daysAgo(0, 7, 12), mask("konto.c"), "MailboxFull"),
  failure(daysAgo(0, 6, 58), mask("konto.e"), "SoftBounce"),
  failure(daysAgo(0, 6, 30), mask("konto.f"), "Spam"),
  failure(daysAgo(0, 5, 47), mask("konto.g"), "Blocklisted"),
  failure(daysAgo(0, 4, 15), mask("konto.i"), "Other"),
  failure(daysAgo(0, 2, 2), "•••", "HardBounce"),
  failure(daysAgo(0, 1, 20), mask("konto.l"), "MailboxNotFound"),
  failure(daysAgo(0, 0, 5), mask("konto.m"), "SoftBounce"),
];

/** When the preview "asked" the provider: a minute before the fixed clock. */
const ASKED_AT = daysAgo(0, 7, 59);

function answered(
  period: AdminEmailPeriod,
  figures: { readonly sent: number; readonly failed: number },
  rows: ReadonlyArray<FailureRow> | "unjudged",
): PreviewDelivery {
  return marked({
    summary: marked({ period, status: "Observed" as const, reason: null, observedAt: ASKED_AT, ...figures }),
    failures: rows === "unjudged"
      ? marked({ period, status: "NotObservable" as const, reason: null, observedAt: null, failures: null, limit: EMAIL_FAILURES_LIMIT })
      : marked({ period, status: "Observed" as const, reason: null, observedAt: ASKED_AT, failures: [...rows], limit: EMAIL_FAILURES_LIMIT }),
  });
}

const NO_FIGURES = { observedAt: null, sent: null, failed: null } as const;
const NO_ROWS = { observedAt: null, failures: null, limit: EMAIL_FAILURES_LIMIT } as const;

/** Both reads in the same unanswered state, as one cause gives both. */
function unanswered(period: AdminEmailPeriod, status: "NotObservable" | "ReadKeyMissing"): PreviewDelivery {
  return marked({
    summary: marked({ period, status, reason: null, ...NO_FIGURES }),
    failures: marked({ period, status, reason: null, ...NO_ROWS }),
  });
}

function notConfigured(period: AdminEmailPeriod): PreviewDelivery {
  return marked({
    summary: marked({ period, status: "NotObserved" as const, reason: "NotConfigured" as const, ...NO_FIGURES }),
    failures: marked({ period, status: "NotObserved" as const, reason: "NotConfigured" as const, ...NO_ROWS }),
  });
}

function refused(period: AdminEmailPeriod, reason: EmailAllowanceFailure): PreviewDelivery {
  return marked({
    summary: marked({ period, status: "Failed" as const, reason, ...NO_FIGURES }),
    failures: marked({ period, status: "Failed" as const, reason, ...NO_ROWS }),
  });
}

function scenario(name: PreviewDeliveryScenario, period: AdminEmailPeriod): PreviewDelivery {
  const { sent, failed, rows } = PERIOD_ANSWERS[period];
  switch (name) {
    case "observed": return answered(period, { sent, failed }, rows);
    case "capped": return answered(period, { sent: sent + 12, failed: 14 }, CAPPED_SENDS);
    case "noneFailed": return answered(period, { sent, failed: 0 }, []);
    case "nothingSent": return answered(period, { sent: 0, failed: 0 }, []);
    // The provider counts failed sends the list cannot show, so the list is unknown, never empty.
    case "listUnjudged": return answered(period, { sent, failed }, "unjudged");
    case "notConfigured": return notConfigured(period);
    case "notObservable": return unanswered(period, "NotObservable");
    case "readKeyMissing": return unanswered(period, "ReadKeyMissing");
    case "noAnswer": return refused(period, "Timeout");
    case "rateLimited": return refused(period, "RateLimited");
    case "keyRefused": return refused(period, "Unauthorized");
    case "refused": return refused(period, "Forbidden");
    case "malformed": return refused(period, "Malformed");
  }
}

function byPeriod(name: PreviewDeliveryScenario): Readonly<Record<AdminEmailPeriod, PreviewDelivery>> {
  return marked({ h24: scenario(name, "h24"), d3: scenario(name, "d3"), d7: scenario(name, "d7") });
}

/** Each scenario's answers for each of the three periods; the preview reads them by the period in its URL. */
export const PREVIEW_EMAIL_DELIVERY: Readonly<Record<PreviewDeliveryScenario, Readonly<Record<AdminEmailPeriod, PreviewDelivery>>>> =
  marked({
    observed: byPeriod("observed"),
    capped: byPeriod("capped"),
    noneFailed: byPeriod("noneFailed"),
    nothingSent: byPeriod("nothingSent"),
    listUnjudged: byPeriod("listUnjudged"),
    notConfigured: byPeriod("notConfigured"),
    notObservable: byPeriod("notObservable"),
    readKeyMissing: byPeriod("readKeyMissing"),
    noAnswer: byPeriod("noAnswer"),
    rateLimited: byPeriod("rateLimited"),
    keyRefused: byPeriod("keyRefused"),
    refused: byPeriod("refused"),
    malformed: byPeriod("malformed"),
  });
