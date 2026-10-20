/** The admin surface's view models (ADR 0150). */
import type { AdminDeletionTiming } from "./account-deletion";
import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { EmailAllowanceDto, EmailDeliveryDto, EmailDeliveryPeriod, EmailFailuresDto } from "@/lib/dto/admin-email";
import type { AdminServerReading } from "./host-observation";
import type { AdminServicesReading } from "./service-observation";

/**
 * A data region is exactly one of these (ADR 0150 D2). `unavailable` means no source exists yet,
 * so it is the only state that says "Kommer snart".
 */
export type AdminRegion<T> =
  | { readonly kind: "unavailable" }
  | { readonly kind: "loading" }
  | { readonly kind: "empty" }
  | { readonly kind: "failed" }
  | {
      readonly kind: "loaded";
      readonly data: T;
      /** When the source was last sampled, for a region whose source dates itself (ADR 0150 D2). */
      readonly sampledAt?: string;
      /** True when that sample is older than the source's own limit. */
      readonly stale?: boolean;
    };

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
 * confirmed is a separate fact, since it can go with any state.
 */
export type AdminAccountStatus = "active" | "pendingDeletion" | "profileMissing" | "suspended";

/** An account names itself by its address alone: the account stores no name (ADR 0150 D3). */
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
  /** Legacy `YYYY-MM-DD` eligibility date; the actual schedule is carried by `deletion`. */
  readonly deletionEarliest: string | null;
  readonly deletion?: AdminDeletionTiming | null;
  /** ISO minute of the last session the grant issued (ADR 0172); null when none is recorded. */
  readonly lastLoginAt: string | null;
  /** ISO minute of the last request any of the account's sessions made; null when none is recorded. */
  readonly lastActiveAt: string | null;
}

export interface AdminAccountDetail extends AdminAccountRow {
  readonly deletionPreview?: AdminDeletionTiming | null;
  readonly savedSearchCount: number | null;
  readonly resumeCount: number | null;
}

/** The ledger's sortable columns and their direction. */
export type AdminAccountSortKey = "email" | "registeredAt" | "lastActiveAt";

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
  readonly logins: number | null;
}

export type { AdminServerReading } from "./host-observation";
export type { AdminServicesReading } from "./service-observation";

/**
 * What the Backup card holds (#1982, ADR 0157): when the last run succeeded and when the timer next fires,
 * each with a state of its own. There is no offsite time and no retention here because nothing observes
 * them; the card keeps those two rows and says so.
 */
export type AdminBackupStatus = BackupObservationData;

/**
 * This month's mail against the provider's free allowance (#1981 PR 1, ADR 0178): the e-post page's first region and
 * the overview's e-post card. The DTO as the API sends it; both surfaces only format it.
 */
export type AdminEmailAllowance = EmailAllowanceDto;

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
  /** Three rows of their own (#1982): a value region, so a loaded card always holds all three. */
  readonly services: AdminValueRegion<AdminServicesReading>;
  readonly server: AdminValueRegion<AdminServerReading>;
  readonly backup: AdminValueRegion<AdminBackupStatus>;
  readonly email: AdminValueRegion<AdminEmailAllowance>;
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

/** A reply's recorded outcome (#2069). `accepted` is the provider's acceptance, never a claim of arrival. */
export type AdminFeedbackReplyState = "sending" | "accepted" | "failed" | "unknown";

/** One reply an administrator mailed to the reporter, oldest first in the detail. */
export interface AdminFeedbackReply {
  readonly id: string;
  readonly text: string;
  readonly state: AdminFeedbackReplyState;
  readonly createdAt: string;
  /** When the outcome was recorded; null while it is not. */
  readonly completedAt: string | null;
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
  readonly screenshot: { readonly width: number; readonly height: number } | null;
  /** The replies sent to the reporter, oldest first. */
  readonly replies: ReadonlyArray<AdminFeedbackReply>;
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

export type AdminEmailPeriod = EmailDeliveryPeriod;

/** The provider's sent and failed counts for a period (#1981 PR 2), the DTO as the API sends it. */
export type AdminEmailDelivery = EmailDeliveryDto;

/** The latest failed sends in a period (#1981 PR 2), the DTO as the API sends it. */
export type AdminEmailFailures = EmailFailuresDto;

/**
 * One of Leverans's reads as the page holds it. It is built, so it is never "Kommer snart": it is pending, our own
 * read failed (with the line that says why, when trying again does not help), or the API answered with a state.
 */
export type AdminEmailRead<T> =
  | { readonly kind: "loading" }
  | { readonly kind: "failed"; readonly line?: string }
  | { readonly kind: "loaded"; readonly data: T };
