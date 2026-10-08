import type { ReauthOutcome } from "@/lib/auth/reauth-action-state";

export interface AdminDeletionTiming {
  readonly deletedAt: string;
  readonly eligibleAt: string;
  readonly scheduledRunAt: string;
}

export interface AdminDeletionReceipt extends AdminDeletionTiming {
  readonly userId: string;
}

export type AdminDeletionOutcome = ReauthOutcome<AdminDeletionReceipt>;
