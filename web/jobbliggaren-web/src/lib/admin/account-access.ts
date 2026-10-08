import type { ReauthOutcome } from "@/lib/auth/reauth-action-state";

export type AdminAccessOperation = "suspend" | "reinstate";

export interface AdminAccessReceipt {
  readonly userId: string;
  readonly isSuspended: boolean;
  readonly accessRevision: number;
  readonly pendingDeletion: boolean;
}

export type AdminAccessOutcome = ReauthOutcome<AdminAccessReceipt>;

export const ADMIN_ACCESS_ERRORS = {
  alreadySuspended: "Admin.AccountAlreadySuspended",
  alreadyReinstated: "Admin.AccountAlreadyReinstated",
  selfSuspension: "Admin.SelfSuspension",
  lastAdministrator: "Admin.LastAdministrator",
  profileUnavailable: "Admin.ProfileUnavailable",
  accountNotFound: "Admin.AccountNotFound",
  alreadyPendingDeletion: "Admin.AccountAlreadyPendingDeletion",
  selfDeletion: "Admin.SelfDeletion",
} as const;
