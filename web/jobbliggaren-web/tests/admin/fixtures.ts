/**
 * Fictional data for the admin harness (#1973, ADR 0150 D5): reserved domains (RFC 2606/6761) and
 * documentation IP ranges (RFC 5737) only. The shapes follow the app's own zod schemas in
 * `src/lib/dto/me.ts`, `src/lib/dto/admin.ts`, `src/lib/dto/admin-accounts.ts` and
 * `src/lib/dto/admin-feedback.ts`, so a page that parses them renders exactly what it would render from the
 * backend.
 */

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

export const ADMIN = { userId: id(901), email: "admin@example.test", roles: ["Admin"] };
export const MEMBER = { userId: id(902), email: "medlem@example.test", roles: [] };

/** The administrator's own step-up (#1975): the challenge the code answers, and the grant it buys. */
export const STEP_UP_CHALLENGE = "admin-harness-step-up";
export const STEP_UP_GRANT = "admin-harness-grant";

export const DELETION_TIMING = {
  deletedAt: "2026-10-08T12:00:00Z",
  eligibleAt: "2026-11-07T12:00:00Z",
  scheduledRunAt: "2026-11-08T04:00:00Z",
};

export type AccountDeletionState = typeof DELETION_TIMING;

/**
 * The injected server clock crosses 04:00 UTC. AccountDeletionTiming.From produces these snapshots;
 * AccountRestoreWindowTests.From_ShouldProduceTheServerPreviewAndReceiptSnapshots_WhenTheClockCrossesFourUtc
 * pins these exact triples; AccountDeletionSchedulerTests pins the single-clock basis.
 */
export const DELETION_BEFORE_04: AccountDeletionState = {
  deletedAt: "2026-10-08T03:59:00Z",
  eligibleAt: "2026-11-07T03:59:00Z",
  scheduledRunAt: "2026-11-07T04:00:00Z",
};

export const DELETION_AFTER_04: AccountDeletionState = {
  deletedAt: "2026-10-08T04:01:00Z",
  eligibleAt: "2026-11-07T04:01:00Z",
  scheduledRunAt: "2026-11-08T04:00:00Z",
};

/** A pending address change's two instants, as the request answers them and the read reports them. */
export const EMAIL_CHANGE_INSTANTS = {
  completableFrom: "2026-10-08T12:00:00+00:00",
  expiresAt: "2026-10-09T12:00:00+00:00",
};

export const AUDIT_PAGE = {
  items: [
    {
      id: id(1),
      occurredAt: "2026-10-03T19:05:12Z",
      correlationId: id(11),
      userId: id(902),
      impersonatedBy: null,
      eventType: "Application.StatusTransitioned",
      aggregateType: "Application",
      aggregateId: id(21),
      ipAddress: "192.0.2.0",
      userAgent: "Mozilla/5.0 (harness)",
    },
    {
      id: id(2),
      occurredAt: "2026-10-03T07:40:17Z",
      correlationId: id(12),
      userId: id(902),
      impersonatedBy: null,
      eventType: "SavedSearch.Created",
      aggregateType: "SavedSearch",
      aggregateId: id(22),
      ipAddress: "198.51.100.0",
      userAgent: "Mozilla/5.0 (harness)",
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
  totalPages: 1,
};

export const RECURRING_JOBS = [
  {
    id: "sync-platsbanken-stream",
    cron: "*/10 * * * *",
    lastExecution: "2026-10-03T19:30:00Z",
    lastJobState: "Succeeded",
    nextExecution: "2026-10-03T19:40:00Z",
  },
  {
    id: "hard-delete-accounts",
    cron: "0 4 * * *",
    lastExecution: "2026-10-03T04:00:00Z",
    lastJobState: "Succeeded",
    nextExecution: "2026-10-04T04:00:00Z",
  },
];

export const FAILED_JOBS = {
  totalCount: 1,
  returned: 50,
  items: [
    {
      jobId: "18234",
      jobType: "SyncPlatsbankenStreamWorker",
      failedAt: "2026-10-03T04:15:00Z",
      errorCategory: "HttpRequestException",
    },
  ],
};

const account = (n: number, local: string, status: string, extra: Record<string, unknown> = {}) => ({
  id: id(n),
  email: `${local}@example.test`,
  role: "User",
  status,
  emailConfirmed: true,
  isSuspended: status === "Suspended",
  registeredAt: `2026-09-${String(10 + n).padStart(2, "0")}T09:00:00Z`,
  deletionEarliest: null,
  deletion: null as AccountDeletionState | null,
  applicationCount: status === "Active" ? n : null,
  ...extra,
});

/** The account directory's answers (#1974): one search page with its counts, and one account's details. */
export const ACCOUNTS = [
  account(5, "konto.e", "Active"),
  account(4, "konto.d", "PendingDeletion", { deletionEarliest: "2026-10-30" }),
  account(3, "konto.c", "ProfileMissing", { registeredAt: null }),
  account(2, "konto.b", "Active", { emailConfirmed: false }),
  { ...account(1, "admin", "Active"), role: "Admin" },
];

/** Thirty more accounts, for a listing with a second page. */
const MORE = Array.from({ length: 30 }, (_, index) =>
  account(100 + index, `konto.extra${String(index).padStart(2, "0")}`, "Active", {
    registeredAt: "2026-08-01T09:00:00Z",
    applicationCount: 1,
  })
);

export interface AccountsQuery {
  readonly page?: number;
  readonly pageSize?: number;
  readonly registeredFrom?: string;
  readonly registeredBefore?: string;
  /** Thirty more accounts, so the listing has a second page. */
  readonly many?: boolean;
  /** Accounts removed since the page was read: they no longer list, and their details answer 404. */
  readonly gone?: ReadonlySet<string>;
  readonly status?: string;
  readonly access?: ReadonlyMap<string, AccountAccessState>;
  readonly deletions?: ReadonlyMap<string, AccountDeletionState>;
}

export interface AccountAccessState {
  readonly isSuspended: boolean;
  readonly accessRevision: number;
}

function withAccess(row: (typeof ACCOUNTS)[number], access: ReadonlyMap<string, AccountAccessState>) {
  const isSuspended = access.get(row.id)?.isSuspended ?? row.isSuspended;
  const status = row.status === "ProfileMissing" || row.status === "PendingDeletion"
    ? row.status : isSuspended ? "Suspended" : "Active";
  return { ...row, status, isSuspended };
}

function withDeletion(row: ReturnType<typeof withAccess>, deletions: ReadonlyMap<string, AccountDeletionState>) {
  const deletion = deletions.get(row.id) ?? row.deletion;
  return deletion === null || row.status === "ProfileMissing" ? row : {
    ...row, status: "PendingDeletion", deletion, deletionEarliest: deletion.eligibleAt.slice(0, 10), applicationCount: null,
  };
}

export function accountsPage(
  term: string | undefined,
  { page = 1, pageSize = 25, many = false, gone = new Set<string>(), status, registeredFrom, registeredBefore,
    access = new Map<string, AccountAccessState>(), deletions = new Map<string, AccountDeletionState>() }: AccountsQuery = {}
) {
  const all = (many ? [...ACCOUNTS, ...MORE] : ACCOUNTS)
    .filter((row) => !gone.has(row.id)).map((row) => withDeletion(withAccess(row, access), deletions))
    .filter((row) => registeredFrom === undefined || (row.registeredAt !== null
      && Date.parse(row.registeredAt) >= Date.parse(registeredFrom)
      && Date.parse(row.registeredAt) < Date.parse(registeredBefore ?? registeredFrom)));
  const matching = term === undefined ? all : all.filter((row) => row.email.includes(term.toLowerCase()));
  const items = status === undefined ? matching : matching.filter((row) => row.status === status);
  const count = (status: string) => matching.filter((row) => row.status === status).length;
  return {
    accounts: {
      items: items.slice((page - 1) * pageSize, page * pageSize),
      totalCount: items.length,
      page,
      pageSize,
      totalPages: Math.ceil(items.length / pageSize),
    },
    counts: {
      total: matching.length,
      active: count("Active"),
      pendingDeletion: count("PendingDeletion"),
      profileMissing: count("ProfileMissing"),
      suspended: count("Suspended"),
    },
  };
}

export function accountDetails(accountId: string, gone: ReadonlySet<string> = new Set(),
  access: ReadonlyMap<string, AccountAccessState> = new Map(), deletions: ReadonlyMap<string, AccountDeletionState> = new Map(),
  previewTiming: AccountDeletionState = DELETION_TIMING) {
  const row = gone.has(accountId) ? undefined : [...ACCOUNTS, ...MORE].find((candidate) => candidate.id === accountId);
  if (row === undefined) return undefined;
  const current = withDeletion(withAccess(row, access), deletions);
  const live = current.status === "Active" || current.status === "Suspended";
  return { ...current, deletionPreview: live ? previewTiming : null, resumeCount: live ? 2 : null, savedSearchCount: live ? 1 : null };
}

// ── Feedback (#1979) ──────────────────────────────────────────────────────────────────────────
//
// The submissions the feedback endpoints answer, in the backend's own JSON: camelCase, enums by their
// .NET names, instants as ISO strings (`AdminFeedbackEndpoints`, `ListFeedbackQuery`,
// `GetFeedbackDetailQuery`, `GetFeedbackSummaryQuery`). Every notice state is here once, and so are a
// submission with no rating, one with no text, and one whose reporter and browser are unknown.

/** The backend's clock: the summary's window is counted back from it. No submission is under 8 days old. */
export const FEEDBACK_NOW = "2026-10-07T08:00:00Z";

const DAY_MS = 86_400_000;

export type FeedbackStatus = "New" | "InProgress" | "Resolved" | "Declined";
export type FeedbackNoticeState = "Queued" | "Sending" | "Accepted" | "Failed" | "Unknown";
export type FeedbackAvailability = "Open" | "Disabled" | "NoRecipient" | "CannotDeliver";

export interface FeedbackNotice {
  readonly state: FeedbackNoticeState;
  readonly attempts: number;
  readonly nextAttemptAt: string;
  readonly acceptedAt: string | null;
  readonly stateChangedAt: string | null;
}

export interface FeedbackRecord {
  readonly id: string;
  readonly pageKey: string;
  readonly rating: number | null;
  readonly comment: string | null;
  readonly status: FeedbackStatus;
  readonly submittedAt: string;
  readonly statusChangedAt: string | null;
  readonly reporterEmail: string | null;
  readonly client: {
    readonly viewportWidth: number | null;
    readonly viewportHeight: number | null;
    readonly screenWidth: number | null;
    readonly screenHeight: number | null;
    readonly pixelRatio: number | null;
    readonly theme: "Light" | "Dark" | null;
    readonly deviceClass: "Mobile" | "Tablet" | "Desktop" | null;
    readonly osFamily: string | null;
    readonly browserFamily: string | null;
  };
  readonly appVersion: string | null;
  readonly notification: FeedbackNotice | null;
  readonly screenshot: { readonly width: number; readonly height: number } | null;
}

/** Each submission named by its notice's state, which is what the tests open it for. */
export const FEEDBACK_IDS = {
  queued: id(701),
  failed: id(702),
  unknown: id(703),
  accepted: id(704),
  sending: id(705),
  unreported: id(706),
} as const;

const NOTHING_REPORTED: FeedbackRecord["client"] = {
  viewportWidth: null,
  viewportHeight: null,
  screenWidth: null,
  screenHeight: null,
  pixelRatio: null,
  theme: null,
  deviceClass: null,
  osFamily: null,
  browserFamily: null,
};

const notice = (state: FeedbackNoticeState, attempts: number, at: string): FeedbackNotice => ({
  state,
  attempts,
  nextAttemptAt: at,
  acceptedAt: state === "Accepted" ? at : null,
  stateChangedAt: at,
});

export const FEEDBACK: ReadonlyArray<FeedbackRecord> = [
  {
    id: FEEDBACK_IDS.queued,
    pageKey: "applications",
    rating: 2,
    comment: "När jag sparar en ansökan och går tillbaka till listan visas den gamla statusen tills jag laddar om sidan.",
    status: "New",
    submittedAt: "2026-09-29T07:10:00Z",
    statusChangedAt: null,
    reporterEmail: "konto.b@example.test",
    client: { viewportWidth: 1440, viewportHeight: 789, screenWidth: 1440, screenHeight: 900, pixelRatio: 1,
      theme: "Light", deviceClass: "Desktop", osFamily: "Windows", browserFamily: "Firefox" },
    appVersion: "4f2a91c",
    notification: notice("Queued", 2, "2026-09-29T07:16:00Z"),
    screenshot: null,
  },
  {
    id: FEEDBACK_IDS.failed,
    pageKey: "cv-review",
    rating: null,
    comment: "Hur länge sparas mitt uppladdade CV om jag inte loggar in på ett tag?",
    status: "New",
    submittedAt: "2026-09-26T13:03:00Z",
    statusChangedAt: null,
    reporterEmail: "konto.g@example.test",
    client: { viewportWidth: 1280, viewportHeight: 720, screenWidth: 1280, screenHeight: 800, pixelRatio: 2,
      theme: "Light", deviceClass: "Desktop", osFamily: "MacOs", browserFamily: "Chrome" },
    appVersion: "9c03e7b",
    notification: notice("Failed", 5, "2026-09-26T14:24:00Z"),
    screenshot: null,
  },
  {
    id: FEEDBACK_IDS.unknown,
    pageKey: "job-ad",
    rating: 5,
    comment: null,
    status: "Resolved",
    submittedAt: "2026-09-24T09:27:00Z",
    statusChangedAt: "2026-09-25T10:00:00Z",
    reporterEmail: "konto.j@example.test",
    client: { viewportWidth: 412, viewportHeight: 839, screenWidth: 412, screenHeight: 915, pixelRatio: 2.63,
      theme: "Light", deviceClass: "Mobile", osFamily: "Android", browserFamily: "SamsungInternet" },
    appVersion: "9c03e7b",
    notification: notice("Unknown", 1, "2026-09-24T09:37:00Z"),
    screenshot: null,
  },
  {
    id: FEEDBACK_IDS.accepted,
    pageKey: "saved-ads",
    rating: 4,
    comment: "Det vore bra att kunna sortera sparade annonser på sista ansökningsdag.",
    status: "InProgress",
    submittedAt: "2026-09-20T18:40:00Z",
    statusChangedAt: "2026-09-21T07:55:00Z",
    reporterEmail: "konto.f@example.test",
    client: { viewportWidth: 390, viewportHeight: 664, screenWidth: 390, screenHeight: 844, pixelRatio: 3,
      theme: "Dark", deviceClass: "Mobile", osFamily: "Ios", browserFamily: "Safari" },
    appVersion: "1b7d0e4",
    notification: notice("Accepted", 1, "2026-09-20T18:41:00Z"),
    screenshot: null,
  },
  {
    id: FEEDBACK_IDS.sending,
    pageKey: "my-pages",
    rating: 3,
    comment: "Kan jag exportera mina ansökningar?",
    status: "Declined",
    submittedAt: "2026-09-15T20:15:00Z",
    statusChangedAt: "2026-09-16T09:00:00Z",
    reporterEmail: "konto.k@example.test",
    client: { viewportWidth: 1920, viewportHeight: 969, screenWidth: 1920, screenHeight: 1080, pixelRatio: 1,
      theme: "Dark", deviceClass: "Desktop", osFamily: "Windows", browserFamily: "Edge" },
    appVersion: "1b7d0e4",
    notification: notice("Sending", 1, "2026-09-15T20:16:00Z"),
    screenshot: null,
  },
  {
    id: FEEDBACK_IDS.unreported,
    pageKey: "jobs",
    rating: 4,
    comment: "Sökningen på kommun ger träffar från hela länet.\nJag sökte på Alingsås och fick annonser från Göteborg.",
    status: "InProgress",
    submittedAt: "2026-09-12T14:30:00Z",
    statusChangedAt: "2026-09-13T08:45:00Z",
    reporterEmail: null,
    client: NOTHING_REPORTED,
    appVersion: null,
    notification: notice("Accepted", 1, "2026-09-12T14:31:00Z"),
    screenshot: null,
  },
];

/**
 * `count` more submissions about /jobb, each older than every one above, that fill the list past one page
 * of 25: the pager's bounds, and a submission the notice mail opens that is not on the list's first page.
 */
export function manyFeedback(count: number): ReadonlyArray<FeedbackRecord> {
  return Array.from({ length: count }, (_, index) => {
    const at = new Date(Date.parse("2026-09-01T08:00:00Z") - index * 3_600_000).toISOString();
    return {
      id: id(800 + index),
      pageKey: "jobs",
      rating: (index % 5) + 1,
      comment: `Inskick ${index + 1}: sökningen på yrke visar annonser från fel län.`,
      status: "New",
      submittedAt: at,
      statusChangedAt: null,
      reporterEmail: `konto.m${index + 1}@example.test`,
      client: NOTHING_REPORTED,
      appVersion: null,
      notification: notice("Accepted", 1, at),
      screenshot: null,
    };
  });
}

const EXCERPT_LENGTH = 90;

const excerpt = (comment: string | null) =>
  comment === null || comment.length <= EXCERPT_LENGTH ? comment : `${comment.slice(0, EXCERPT_LENGTH).trimEnd()}…`;

const newestFirst = (left: FeedbackRecord, right: FeedbackRecord) =>
  right.submittedAt.localeCompare(left.submittedAt) || left.id.localeCompare(right.id);

/** `GET /api/v1/admin/feedback`: newest first, with the counts per status inside the page filter. */
export function feedbackList(
  records: ReadonlyArray<FeedbackRecord>,
  { status, pageKey, pageNumber, pageSize }: {
    readonly status?: string; readonly pageKey?: string; readonly pageNumber: number; readonly pageSize: number;
  },
) {
  const onPage = pageKey === undefined ? records : records.filter((record) => record.pageKey === pageKey);
  const shown = (status === undefined ? onPage : onPage.filter((record) => record.status === status))
    .slice().sort(newestFirst);
  const count = (value: FeedbackStatus) => onPage.filter((record) => record.status === value).length;
  return {
    items: {
      items: shown.slice((pageNumber - 1) * pageSize, pageNumber * pageSize).map((record) => ({
        id: record.id,
        pageKey: record.pageKey,
        rating: record.rating,
        excerpt: excerpt(record.comment),
        status: record.status,
        submittedAt: record.submittedAt,
        notificationState: record.notification?.state ?? null,
      })),
      totalCount: shown.length,
      page: pageNumber,
      pageSize,
      totalPages: Math.ceil(shown.length / pageSize),
    },
    counts: {
      all: onPage.length,
      new: count("New"),
      inProgress: count("InProgress"),
      resolved: count("Resolved"),
      declined: count("Declined"),
    },
  };
}

/** `GET /api/v1/admin/feedback/{id}`. */
export function feedbackDetail(record: FeedbackRecord) {
  return {
    id: record.id,
    pageKey: record.pageKey,
    rating: record.rating,
    comment: record.comment,
    status: record.status,
    submittedAt: record.submittedAt,
    statusChangedAt: record.statusChangedAt,
    reporterEmail: record.reporterEmail,
    client: record.client,
    appVersion: record.appVersion,
    notification: record.notification,
    screenshot: record.screenshot,
  };
}

/**
 * `GET /api/v1/admin/feedback/summary?days=`: per page, ordered by key as the SQL orders it, each reporter's
 * latest rating counted once and every submission counted as a submission.
 */
export function feedbackSummary(records: ReadonlyArray<FeedbackRecord>, days: number) {
  const since = Date.parse(FEEDBACK_NOW) - days * DAY_MS;
  const inWindow = records.filter((record) => Date.parse(record.submittedAt) >= since);
  const pages = [...new Set(inWindow.map((record) => record.pageKey))].sort();
  return {
    days,
    pages: pages.map((pageKey) => {
      const submissions = inWindow
        .filter((record) => record.pageKey === pageKey)
        .sort((left, right) => left.submittedAt.localeCompare(right.submittedAt));
      const latest = new Map<string, number>();
      for (const record of submissions) {
        if (record.rating !== null) latest.set(record.reporterEmail ?? record.id, record.rating);
      }
      const ratings = [...latest.values()];
      const rated = (value: number) => ratings.filter((rating) => rating === value).length;
      return {
        pageKey,
        submissions: submissions.length,
        raters: ratings.length,
        rated1: rated(1),
        rated2: rated(2),
        rated3: rated(3),
        rated4: rated(4),
        rated5: rated(5),
        mean:
          ratings.length === 0
            ? null
            : Math.round((ratings.reduce((sum, rating) => sum + rating, 0) / ratings.length) * 100) / 100,
      };
    }),
  };
}

/** Uses the same fictional retained population as the directory and Swedish calendar windows. */
export function accountOverview(query: AccountsQuery = {}) {
  const sampledAt = new Date().toISOString();
  const today = new Intl.DateTimeFormat("sv-SE", { timeZone: "Europe/Stockholm",
    year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(sampledAt));
  const midnight = (date: string) => {
    const utc = Date.parse(`${date}T00:00:00Z`);
    const offset = new Intl.DateTimeFormat("en", { timeZone: "Europe/Stockholm",
      timeZoneName: "longOffset" }).formatToParts(new Date(utc)).find(part => part.type === "timeZoneName")?.value;
    const hours = Number(offset?.match(/GMT\+(\d\d):00/)?.[1]);
    if (!Number.isFinite(hours)) throw new Error("Expected a Stockholm UTC offset");
    return new Date(utc - hours * 3_600_000).toISOString();
  };
  const dates = Array.from({ length: 91 }, (_, index) =>
    new Date(Date.parse(`${today}T12:00:00Z`) + (index - 89) * 86_400_000).toISOString().slice(0, 10));
  const dateAt = (index: number) => {
    const date = dates[index];
    if (date === undefined) throw new Error("Date outside the overview window");
    return date;
  };
  const rows = accountsPage(undefined, { ...query, pageSize: 100 }).accounts.items;
  const count = (from: string, before: string) => rows.filter(row => row.registeredAt !== null
    && Date.parse(row.registeredAt) >= Date.parse(from) && Date.parse(row.registeredAt) < Date.parse(before)).length;
  const period = (index: number, before = sampledAt) => {
    const from = midnight(dateAt(index));
    return { from, before, count: count(from, before) };
  };
  return {
    sampledAt,
    counts: accountsPage(undefined, query).counts,
    newAccounts: { today: period(89), yesterday: period(88, midnight(today)), last7Days: period(83), last30Days: period(60) },
    days: dates.slice(0, 90).map((date, index) => ({
      date, newAccounts: count(midnight(date), index === 89 ? sampledAt : midnight(dateAt(index + 1))),
    })),
  };
}