/** The admin surface's view models (ADR 0150). */

/**
 * A data region is exactly one of these (ADR 0150 D2). `unavailable` means no source exists yet,
 * so it is the only state that says "Kommer snart".
 */
export type AdminRegion<T> =
  | { readonly kind: "unavailable" }
  | { readonly kind: "loading" }
  | { readonly kind: "empty" }
  | { readonly kind: "failed" }
  | { readonly kind: "loaded"; readonly data: T };

export type AdminRegionKind = AdminRegion<unknown>["kind"];

/** A list region holding rows, or the empty region when there are none: the one encoding every loader uses. */
export function listRegion<T>(
  rows: ReadonlyArray<T>,
): Extract<AdminRegion<ReadonlyArray<T>>, { readonly kind: "empty" | "loaded" }> {
  return rows.length === 0 ? { kind: "empty" } : { kind: "loaded", data: rows };
}

export const ADMIN_REGION_KINDS: ReadonlyArray<AdminRegionKind> = [
  "loaded",
  "unavailable",
  "empty",
  "failed",
  "loading",
];

export type AdminAccountRole = "user" | "admin";

/**
 * An account's lifecycle state (ADR 0151); every account is in exactly one. Whether the address is
 * confirmed is a separate fact, since it can go with any state. Only the preview produces `suspended`
 * until #1976 builds suspension.
 */
export type AdminAccountStatus = "active" | "pendingDeletion" | "profileMissing" | "suspended";

/**
 * An account names itself by its address alone: the account stores no name (ADR 0150 D3). Last
 * login and last activity have no source at all, so they are not fields here.
 */
export interface AdminAccountRow {
  readonly id: string;
  /** Null when Identity holds no address for the account. */
  readonly email: string | null;
  readonly role: AdminAccountRole;
  readonly status: AdminAccountStatus;
  readonly emailConfirmed: boolean;
  /** Independent of deletion status; older preview fixtures derive it from their primary status. */
  readonly isSuspended?: boolean;
  /** ISO instant from the account's profile; null when it has none. */
  readonly registeredAt: string | null;
  /** Null when the count is unknown for this account. */
  readonly applicationCount: number | null;
  /** `YYYY-MM-DD`, only while the status is `pendingDeletion`: the earliest permanent deletion. */
  readonly deletionEarliest: string | null;
}

export interface AdminAccountDetail extends AdminAccountRow {
  readonly savedSearchCount: number | null;
  readonly resumeCount: number | null;
}

/** The ledger's two sortable columns and their direction. */
export type AdminAccountSortKey = "email" | "registeredAt";

export interface AdminAccountSort {
  readonly key: AdminAccountSortKey;
  readonly direction: "ascending" | "descending";
}

/** An account the panel can name in a question or a receipt: one Identity holds an address for. */
export type AdminAddressedAccount = AdminAccountDetail & { readonly email: string };

/**
 * The signed-in administrator, as the account page knows them from the session: their own account is told
 * by its id, never by its address, and their own step-up code goes to their own address (#1975).
 */
export interface AdminSelf {
  readonly userId: string;
  readonly email: string;
}

/** The account actions the panel knows; which of them are live is the caller's to say (ADR 0150 D4). */
export type AdminAccountAction =
  | "changeEmail"
  | "cancelEmailChange"
  | "suspend"
  | "reinstate"
  | "scheduleDeletion"
  | "impersonate"
  | "sendLoginLink"
  | "markVerified"
  | "restore"
  | "deletePermanently";

/** A region that holds a value rather than a list: zero is a value, so it has no empty state. */
export type AdminValueRegion<T> = Exclude<AdminRegion<T>, { readonly kind: "empty" }>;

// ── The overview (ADR 0150 D1) ──────────────────────────────────────────────────────────────

export interface AdminNewAccounts {
  readonly today: number;
  readonly yesterday: number;
  readonly last7Days: number;
  readonly last30Days: number;
}

export interface AdminAccountTotals {
  readonly total: number;
  readonly suspended: number;
  readonly pendingDeletion: number;
}

export interface AdminActiveAccounts {
  readonly last30Days: number;
  readonly today: number;
  readonly last7Days: number;
}

export interface AdminLogins {
  readonly today: number;
  readonly yesterday: number;
  readonly failed: number;
  readonly locked: number;
}

/**
 * One calendar day of the trend, oldest first; `date` is `YYYY-MM-DD`. The series is consecutive
 * Europe/Stockholm days, zero-filled, and its last day is today: the card labels it "Idag".
 */
export interface AdminTrendDay {
  readonly date: string;
  readonly newAccounts: number;
  readonly logins: number;
}

export interface AdminServiceStatus {
  readonly id: string;
  readonly label: string;
  readonly state: "ok" | "warning";
  /** What the check measured, such as a response time. */
  readonly detail: string;
}

/** Observed readings in percent of capacity. */
export interface AdminServerReading {
  readonly cpu: number;
  readonly memory: number;
  readonly disk: number;
}

export interface AdminBackupStatus {
  readonly latestAt: string;
  readonly offsiteAt: string;
  readonly nextAt: string;
  readonly retentionDays: number;
}

export interface AdminEmailTotals {
  readonly sent: number;
  readonly failed: number;
  readonly noRecipient: number;
}

export interface AdminEmailOverview extends AdminEmailTotals {
  /** The most sent email types of the last 24 hours, most first. */
  readonly topTypes: ReadonlyArray<{ readonly type: string; readonly sent: number }>;
}

export type AdminAttentionKind = "failedJobs" | "pendingDeletions" | "failedEmails";

export interface AdminAttentionItem {
  readonly kind: AdminAttentionKind;
  readonly count: number;
}

export type AdminEventKind =
  | "accountCreated"
  | "accountSuspended"
  | "accountReinstated"
  | "deletionScheduled"
  | "emailChangeRequested"
  | "jobFailed";

export interface AdminRecentEvent {
  readonly id: string;
  readonly occurredAt: string;
  readonly kind: AdminEventKind;
  /** What the event is about: an account's address or a job's name. */
  readonly subject: string;
}

export interface AdminOverviewRegions {
  readonly newAccounts: AdminValueRegion<AdminNewAccounts>;
  readonly totals: AdminValueRegion<AdminAccountTotals>;
  readonly active: AdminValueRegion<AdminActiveAccounts>;
  readonly logins: AdminValueRegion<AdminLogins>;
  /** Up to 90 days; the period shows its tail. */
  readonly trend: AdminValueRegion<ReadonlyArray<AdminTrendDay>>;
  readonly services: AdminRegion<ReadonlyArray<AdminServiceStatus>>;
  readonly server: AdminValueRegion<AdminServerReading>;
  readonly backup: AdminValueRegion<AdminBackupStatus>;
  readonly email: AdminValueRegion<AdminEmailOverview>;
  readonly attention: AdminRegion<ReadonlyArray<AdminAttentionItem>>;
  readonly events: AdminRegion<ReadonlyArray<AdminRecentEvent>>;
}

const UNAVAILABLE = { kind: "unavailable" } as const;

/** The overview while none of its sources exists. */
export const ADMIN_OVERVIEW_UNAVAILABLE: AdminOverviewRegions = {
  newAccounts: UNAVAILABLE,
  totals: UNAVAILABLE,
  active: UNAVAILABLE,
  logins: UNAVAILABLE,
  trend: UNAVAILABLE,
  services: UNAVAILABLE,
  server: UNAVAILABLE,
  backup: UNAVAILABLE,
  email: UNAVAILABLE,
  attention: UNAVAILABLE,
  events: UNAVAILABLE,
};

// ── Feedback (#1979) ────────────────────────────────────────────────────────────────────────

/** Where the operator is with a submission: Ny, Pågår, Åtgärdad or Avstår. */
export type AdminFeedbackStatus = "new" | "inProgress" | "resolved" | "declined";

/** The notice mail's delivery state. `accepted` is the provider's acceptance, never a claim of arrival. */
export type AdminFeedbackNoticeState = "queued" | "sending" | "accepted" | "failed" | "unknown";

/** Why feedback is open or closed (the backend's gate). */
export type AdminFeedbackAvailability = "open" | "disabled" | "noRecipient" | "cannotDeliver";

/** The summary's window in days. */
export type AdminFeedbackWindow = 7 | 30 | 90;

/**
 * One submission in the list. It carries an excerpt and never the reporter's address: an address is
 * read one submission at a time, in the detail.
 */
export interface AdminFeedbackListItem {
  readonly id: string;
  /** The page's key; a key this web does not know yet is shown as it came. */
  readonly page: string;
  /** 1–5, or null when the reporter gave no rating. */
  readonly rating: number | null;
  /** Null when the reporter wrote no text. */
  readonly excerpt: string | null;
  readonly status: AdminFeedbackStatus;
  readonly submittedAt: string;
  /** Null when the submission has no notice. */
  readonly notice: AdminFeedbackNoticeState | null;
}

export interface AdminFeedbackCounts {
  readonly all: number;
  readonly new: number;
  readonly inProgress: number;
  readonly resolved: number;
  readonly declined: number;
}

/** One answered list read: its page of submissions and the counts per status inside the page filter. */
export interface AdminFeedbackListPage {
  readonly items: ReadonlyArray<AdminFeedbackListItem>;
  readonly page: number;
  readonly totalPages: number;
  readonly totalCount: number;
  readonly counts: AdminFeedbackCounts;
}

export type AdminFeedbackTheme = "light" | "dark";
export type AdminFeedbackDeviceClass = "mobile" | "tablet" | "desktop";
export type AdminFeedbackOs = "windows" | "macOs" | "ios" | "android" | "linux" | "chromeOs" | "other";
export type AdminFeedbackBrowser =
  | "chrome"
  | "edge"
  | "firefox"
  | "safari"
  | "samsungInternet"
  | "opera"
  | "other";

/** What the browser reported about itself, as reported and never verified. Each value is null when unknown. */
export interface AdminFeedbackClient {
  readonly viewportWidth: number | null;
  readonly viewportHeight: number | null;
  readonly screenWidth: number | null;
  readonly screenHeight: number | null;
  readonly pixelRatio: number | null;
  readonly theme: AdminFeedbackTheme | null;
  readonly deviceClass: AdminFeedbackDeviceClass | null;
  readonly os: AdminFeedbackOs | null;
  readonly browser: AdminFeedbackBrowser | null;
}

export interface AdminFeedbackNotice {
  readonly state: AdminFeedbackNoticeState;
  readonly attempts: number;
  /** When the next attempt is due; it says something only while the notice is queued. */
  readonly nextAttemptAt: string;
}

/** One submission as the detail shows it. */
export interface AdminFeedbackItem {
  readonly id: string;
  readonly page: string;
  readonly rating: number | null;
  /** The whole text, or null when the reporter wrote none. */
  readonly comment: string | null;
  readonly status: AdminFeedbackStatus;
  readonly submittedAt: string;
  /** Null until the status is first changed. */
  readonly statusChangedAt: string | null;
  /** The reporter's address, or null when it cannot be read. */
  readonly reporterEmail: string | null;
  readonly client: AdminFeedbackClient;
  /** The commit the web build came from, or null. */
  readonly appVersion: string | null;
  /** Null when the submission has no notice. */
  readonly notice: AdminFeedbackNotice | null;
}

/** One page's ratings over the window: each user's latest rating counts once. */
export interface AdminFeedbackPageSummary {
  readonly page: string;
  /** Every submission in the window, rated or not. */
  readonly submissions: number;
  readonly raters: number;
  /** How many latest ratings were 1, 2, 3, 4 and 5, in that order. */
  readonly ratings: readonly [number, number, number, number, number];
  /** Null when nobody rated the page in the window. */
  readonly mean: number | null;
}

// ── Logs (#1980) ────────────────────────────────────────────────────────────────────────────

export type AdminSecurityEventKind = "loginFailed" | "rateLimited" | "accountLocked" | "adminImpersonation";

export interface AdminSecurityLogRow {
  readonly id: string;
  readonly occurredAt: string;
  readonly kind: AdminSecurityEventKind;
  /** The account's address, masked. */
  readonly account: string;
  /** The client's address with its last part zeroed. */
  readonly ip: string;
  readonly count: number;
  readonly detail: string;
}

export interface AdminErrorLogRow {
  readonly id: string;
  readonly lastSeenAt: string;
  readonly level: "error" | "warning";
  readonly source: string;
  readonly message: string;
  readonly count24h: number;
}

export interface AdminImportLogRow {
  readonly id: string;
  readonly run: string;
  readonly kind: "delta" | "full";
  readonly startedAt: string;
  readonly durationSeconds: number;
  readonly fetched: number;
  readonly added: number;
  readonly updated: number;
  readonly closed: number;
  readonly status: "succeeded" | "failed";
}

// ── Email delivery (#1981) ──────────────────────────────────────────────────────────────────

export type AdminEmailPeriod = "h24" | "d3" | "d7";

export interface AdminEmailTypeRow {
  readonly type: string;
  readonly sent: number;
  readonly failed: number;
  readonly lastError: string | null;
}

export interface AdminEmailFailure {
  readonly id: string;
  readonly occurredAt: string;
  /** The recipient's address, masked. */
  readonly recipient: string;
  readonly outcome: "failed" | "bounced";
  readonly message: string;
  readonly type: string;
}

export interface AdminEmailDelivery {
  readonly totals: AdminEmailTotals;
  readonly types: ReadonlyArray<AdminEmailTypeRow>;
  readonly failures: ReadonlyArray<AdminEmailFailure>;
}
