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
