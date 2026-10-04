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

export const ADMIN_REGION_KINDS: ReadonlyArray<AdminRegionKind> = [
  "loaded",
  "unavailable",
  "empty",
  "failed",
  "loading",
];

export type AdminAccountRole = "user" | "admin";

export type AdminAccountStatus = "active" | "suspended" | "unverified" | "pendingDeletion";

/**
 * An account names itself by its address alone: the account stores no name (ADR 0150 D3). Last
 * login and last activity have no source at all, so they are not fields here.
 */
export interface AdminAccountRow {
  readonly id: string;
  readonly email: string;
  readonly role: AdminAccountRole;
  readonly status: AdminAccountStatus;
  /** ISO instant; null when the account predates the field. */
  readonly registeredAt: string | null;
  /** Null when the count is unknown for this account. */
  readonly applicationCount: number | null;
  /** ISO instant, only while the status is `pendingDeletion`: the earliest permanent deletion. */
  readonly deletionEarliest: string | null;
}

export interface AdminAccountDetail extends AdminAccountRow {
  readonly savedSearchCount: number | null;
  readonly resumeCount: number | null;
}

/** The account actions the panel knows; which of them are live is the caller's to say (ADR 0150 D4). */
export type AdminAccountAction =
  | "changeEmail"
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

/** One calendar day of the trend, oldest first; `date` is `YYYY-MM-DD`. */
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

export type AdminFeedbackStatus = "new" | "inProgress" | "resolved" | "skipped";
export type AdminFeedbackCategory = "bug" | "suggestion" | "question";

export interface AdminFeedbackReply {
  readonly id: string;
  readonly sentAt: string;
  readonly text: string;
}

export interface AdminFeedbackItem {
  readonly id: string;
  readonly status: AdminFeedbackStatus;
  readonly category: AdminFeedbackCategory;
  readonly receivedAt: string;
  readonly text: string;
  readonly senderEmail: string;
  /** The app path the report was sent from. */
  readonly page: string;
  readonly screen: string;
  readonly device: string;
  readonly version: string;
  readonly replies: ReadonlyArray<AdminFeedbackReply>;
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
