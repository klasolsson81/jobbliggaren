import { FEEDBACK_PAGE_KEYS } from "@/lib/admin/feedback";
import type {
  AdminAccountDetail,
  AdminAccountTotals,
  AdminActiveAccounts,
  AdminAttentionItem,
  AdminBackupStatus,
  AdminEmailDelivery,
  AdminEmailOverview,
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
  AdminServiceStatus,
  AdminSelf,
  AdminTrendDay,
} from "@/lib/admin/view-models";
import type { AdminPendingEmailChange } from "@/lib/admin/account-email-change";
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

/** A deletion scheduled at the fixed clock is permanent no earlier than this date. */
export const PREVIEW_DELETION_EARLIEST = daysAhead(30).slice(0, 10);

function account(row: AdminAccountDetail): AdminAccountDetail {
  return marked(row);
}

export const PREVIEW_ACCOUNTS: ReadonlyArray<AdminAccountDetail> = [
  account({ id: id(1), email: PREVIEW_ADMIN_EMAIL, role: "admin", status: "active", emailConfirmed: true, registeredAt: daysAgo(150, 7, 12), applicationCount: 2, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1 }),
  account({ id: id(2), email: address("konto.a"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(6, 12, 2), applicationCount: 4, deletionEarliest: null, savedSearchCount: 3, resumeCount: 1 }),
  account({ id: id(3), email: address("konto.b"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(4, 7, 15), applicationCount: 3, deletionEarliest: null, savedSearchCount: 2, resumeCount: 2 }),
  account({ id: id(4), email: address("konto.c"), role: "user", status: "suspended", emailConfirmed: true, registeredAt: daysAgo(44, 9, 40), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 1 }),
  account({ id: id(5), email: address("konto.d"), role: "user", status: "active", emailConfirmed: false, registeredAt: daysAgo(2, 17, 33), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 0 }),
  account({ id: id(6), email: address("konto.e"), role: "user", status: "pendingDeletion", emailConfirmed: true, registeredAt: daysAgo(82, 8, 10), applicationCount: null, deletionEarliest: daysAhead(24).slice(0, 10), savedSearchCount: null, resumeCount: null }),
  account({ id: id(7), email: address("konto.f"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(22, 10, 5), applicationCount: 11, deletionEarliest: null, savedSearchCount: 5, resumeCount: 3 }),
  account({ id: id(8), email: address("konto.g"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(9, 19, 1), applicationCount: 7, deletionEarliest: null, savedSearchCount: 2, resumeCount: 1 }),
  account({ id: id(9), email: address("konto.h"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(1, 13, 47), applicationCount: 1, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1 }),
  account({ id: id(10), email: address("konto.i"), role: "user", status: "active", emailConfirmed: false, registeredAt: daysAgo(0, 6, 58), applicationCount: 0, deletionEarliest: null, savedSearchCount: 0, resumeCount: 0 }),
  account({ id: id(11), email: address("konto.j"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(27, 11, 22), applicationCount: 6, deletionEarliest: null, savedSearchCount: 2, resumeCount: 1 }),
  account({ id: id(12), email: address("konto.k"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(15, 20, 14), applicationCount: 2, deletionEarliest: null, savedSearchCount: 1, resumeCount: 1 }),
  account({ id: id(13), email: address("konto.l"), role: "user", status: "suspended", emailConfirmed: true, registeredAt: daysAgo(61, 15, 3), applicationCount: 3, deletionEarliest: null, savedSearchCount: 1, resumeCount: 2 }),
  account({ id: id(14), email: address("konto.m"), role: "user", status: "profileMissing", emailConfirmed: true, registeredAt: null, applicationCount: null, deletionEarliest: null, savedSearchCount: null, resumeCount: null }),
  account({ id: id(15), email: address("konto.n.med.en.mycket.lang.adress.for.smala.skarmar"), role: "user", status: "active", emailConfirmed: true, registeredAt: daysAgo(33, 10, 44), applicationCount: 1, deletionEarliest: null, savedSearchCount: 0, resumeCount: 1 }),
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

const mask = (local: string) => `${local.slice(0, 1)}•••@${SENTINEL}`;

/** A provider's reason as long as real ones run, so the failure list is seen to wrap rather than cut it. */
const LONG_PROVIDER_MESSAGE = "Mottagarens server avvisade meddelandet: postlådan är full och tar inte emot nya meddelanden just nu. Servern föreslog ett nytt försök senare, och tre försök under en timme gav samma svar innan utskicket gavs upp.";

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

/** Every overview region with data, in the view models' shapes. */
export interface PreviewOverviewData {
  readonly newAccounts: AdminNewAccounts;
  readonly totals: AdminAccountTotals;
  readonly active: AdminActiveAccounts;
  readonly logins: AdminLogins;
  readonly trend: ReadonlyArray<AdminTrendDay>;
  readonly services: ReadonlyArray<AdminServiceStatus>;
  readonly server: AdminServerReading;
  readonly backup: AdminBackupStatus;
  readonly email: AdminEmailOverview;
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
  services: [
    marked({ id: "database", label: "Databas", state: "ok" as const, detail: "4 ms" }),
    marked({ id: "jobs", label: "Bakgrundsjobb", state: "ok" as const, detail: "4 återkommande" }),
    marked({ id: "platsbanken", label: "Platsbanken", state: "warning" as const, detail: "Senaste hämtningen misslyckades" }),
    marked({ id: "email", label: "E-postutskick", state: "ok" as const, detail: "212 ms" }),
  ],
  server: marked({ cpu: 23, memory: 61, disk: 38 }),
  backup: marked({
    latestAt: daysAgo(0, 1, 30),
    offsiteAt: daysAgo(0, 1, 45),
    nextAt: daysAgo(-1, 1, 30),
    retentionDays: 30,
  }),
  email: marked({
    sent: 57,
    failed: 2,
    noRecipient: 0,
    topTypes: [
      marked({ type: "login-challenge", sent: 31 }),
      marked({ type: "match-notification", sent: 18 }),
      marked({ type: "email-changed-notification", sent: 4 }),
    ],
  }),
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

/** The overview's "Tom" state: every count zero, every list empty; readings and backup keep their values. */
export const PREVIEW_OVERVIEW_ZERO: PreviewOverviewData = marked({
  ...PREVIEW_OVERVIEW,
  newAccounts: marked({ today: 0, yesterday: 0, last7Days: 0, last30Days: 0 }),
  totals: marked({ total: 0, suspended: 0, pendingDeletion: 0 }),
  active: marked({ last30Days: 0, today: 0, last7Days: 0 }),
  logins: marked({ today: 0, yesterday: 0, failed: 0, locked: 0 }),
  trend: trend.map((day) => marked({ date: day.date, newAccounts: 0, logins: 0 })),
  email: marked({ sent: 0, failed: 0, noRecipient: 0, topTypes: [] }),
  services: [],
  attention: [],
  events: [],
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
    client: { ...NOTHING_REPORTED, viewportWidth: 1440, viewportHeight: 789, screenWidth: 1440, screenHeight: 900, pixelRatio: 1, theme: "light" as const, deviceClass: "desktop" as const, os: "windows" as const, browser: "firefox" as const },
    appVersion: "4f2a91c",
    notice: { state: "queued" as const, attempts: 2, nextAttemptAt: daysAgo(0, 8, 1) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 390, viewportHeight: 664, screenWidth: 390, screenHeight: 844, pixelRatio: 3, theme: "dark" as const, deviceClass: "mobile" as const, os: "ios" as const, browser: "safari" as const },
    appVersion: "4f2a91c",
    notice: { state: "accepted" as const, attempts: 1, nextAttemptAt: daysAgo(1, 18, 40) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 1280, viewportHeight: 720, screenWidth: 1280, screenHeight: 800, pixelRatio: 2, theme: "light" as const, deviceClass: "desktop" as const, os: "macOs" as const, browser: "chrome" as const },
    appVersion: "9c03e7b",
    notice: { state: "failed" as const, attempts: 5, nextAttemptAt: daysAgo(2, 12, 24) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 412, viewportHeight: 839, screenWidth: 412, screenHeight: 915, pixelRatio: 2.63, theme: "light" as const, deviceClass: "mobile" as const, os: "android" as const, browser: "samsungInternet" as const },
    appVersion: "9c03e7b",
    notice: { state: "unknown" as const, attempts: 1, nextAttemptAt: daysAgo(5, 9, 27) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 1920, viewportHeight: 969, screenWidth: 1920, screenHeight: 1080, pixelRatio: 1, theme: "dark" as const, deviceClass: "desktop" as const, os: "windows" as const, browser: "edge" as const },
    appVersion: "1b7d0e4",
    notice: { state: "accepted" as const, attempts: 2, nextAttemptAt: daysAgo(8, 20, 16) },
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
    client: NOTHING_REPORTED,
    appVersion: null,
    notice: { state: "accepted" as const, attempts: 1, nextAttemptAt: daysAgo(12, 14, 30) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 820, viewportHeight: 1106, screenWidth: 820, screenHeight: 1180, pixelRatio: 2, theme: "light" as const, deviceClass: "tablet" as const, os: "ios" as const, browser: "safari" as const },
    appVersion: "1b7d0e4",
    notice: { state: "sending" as const, attempts: 1, nextAttemptAt: daysAgo(20, 8, 5) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 1366, viewportHeight: 657, screenWidth: 1366, screenHeight: 768, pixelRatio: 1, theme: "light" as const, deviceClass: "desktop" as const, os: "linux" as const, browser: "firefox" as const },
    appVersion: "e05c2aa",
    notice: { state: "accepted" as const, attempts: 1, nextAttemptAt: daysAgo(45, 10, 0) },
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
    client: { ...NOTHING_REPORTED, viewportWidth: 1536, viewportHeight: 730, screenWidth: 1536, screenHeight: 864, pixelRatio: 1.25, theme: "light" as const, deviceClass: "desktop" as const, os: "windows" as const, browser: "chrome" as const },
    appVersion: "e05c2aa",
    notice: null,
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

/** Email delivery per period: one fictional set each for 24 hours, 3 days and 7 days. */
export const PREVIEW_EMAIL_DELIVERY: Readonly<Record<AdminEmailPeriod, AdminEmailDelivery>> = marked({
  h24: marked({
    totals: marked({ sent: 57, failed: 2, noRecipient: 0 }),
    types: [
      marked({ type: "login-challenge", sent: 31, failed: 1, lastError: "Mottagarens server svarade inte." }),
      marked({ type: "match-notification", sent: 18, failed: 1, lastError: "Adressen finns inte." }),
      marked({ type: "email-changed-notification", sent: 4, failed: 0, lastError: null }),
      marked({ type: "followed-company-notification", sent: 4, failed: 0, lastError: null }),
    ],
    failures: [
      marked({ id: id(1001), occurredAt: daysAgo(0, 6, 41), recipient: mask("konto.d"), outcome: "failed" as const, message: "Mottagarens server svarade inte.", type: "login-challenge" }),
      marked({ id: id(1002), occurredAt: daysAgo(0, 5, 3), recipient: mask("konto.k"), outcome: "bounced" as const, message: "Adressen finns inte.", type: "match-notification" }),
    ],
  }),
  d3: marked({
    totals: marked({ sent: 163, failed: 3, noRecipient: 1 }),
    types: [
      marked({ type: "login-challenge", sent: 92, failed: 2, lastError: "Mottagarens server svarade inte." }),
      marked({ type: "match-notification", sent: 54, failed: 1, lastError: "Adressen finns inte." }),
      marked({ type: "email-changed-notification", sent: 9, failed: 0, lastError: null }),
      marked({ type: "followed-company-notification", sent: 8, failed: 0, lastError: null }),
    ],
    failures: [
      marked({ id: id(1001), occurredAt: daysAgo(0, 6, 41), recipient: mask("konto.d"), outcome: "failed" as const, message: "Mottagarens server svarade inte.", type: "login-challenge" }),
      marked({ id: id(1002), occurredAt: daysAgo(0, 5, 3), recipient: mask("konto.k"), outcome: "bounced" as const, message: "Adressen finns inte.", type: "match-notification" }),
      marked({ id: id(1003), occurredAt: daysAgo(2, 19, 12), recipient: mask("konto.a"), outcome: "failed" as const, message: "Mottagarens server svarade inte.", type: "login-challenge" }),
    ],
  }),
  d7: marked({
    totals: marked({ sent: 384, failed: 5, noRecipient: 1 }),
    types: [
      marked({ type: "login-challenge", sent: 215, failed: 3, lastError: "Mottagarens server svarade inte." }),
      marked({ type: "match-notification", sent: 126, failed: 1, lastError: "Adressen finns inte." }),
      marked({ type: "email-changed-notification", sent: 23, failed: 1, lastError: LONG_PROVIDER_MESSAGE }),
      marked({ type: "followed-company-notification", sent: 20, failed: 0, lastError: null }),
    ],
    failures: [
      marked({ id: id(1001), occurredAt: daysAgo(0, 6, 41), recipient: mask("konto.d"), outcome: "failed" as const, message: "Mottagarens server svarade inte.", type: "login-challenge" }),
      marked({ id: id(1002), occurredAt: daysAgo(0, 5, 3), recipient: mask("konto.k"), outcome: "bounced" as const, message: "Adressen finns inte.", type: "match-notification" }),
      marked({ id: id(1003), occurredAt: daysAgo(2, 19, 12), recipient: mask("konto.a"), outcome: "failed" as const, message: "Mottagarens server svarade inte.", type: "login-challenge" }),
      marked({ id: id(1004), occurredAt: daysAgo(5, 8, 30), recipient: mask("konto.h"), outcome: "failed" as const, message: "Anslutningen bröts.", type: "login-challenge" }),
      marked({ id: id(1005), occurredAt: daysAgo(6, 11, 20), recipient: mask("konto.j"), outcome: "bounced" as const, message: LONG_PROVIDER_MESSAGE, type: "email-changed-notification" }),
    ],
  }),
});

/** The email page's "Tom" state: nothing sent in the period. */
export const PREVIEW_EMAIL_DELIVERY_ZERO: AdminEmailDelivery = marked({
  totals: marked({ sent: 0, failed: 0, noRecipient: 0 }),
  types: [],
  failures: [],
});
