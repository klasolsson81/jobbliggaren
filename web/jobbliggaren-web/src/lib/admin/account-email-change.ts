import type { ReauthOutcome } from "@/lib/auth/reauth-action-state";

// The address change an administrator starts for an account (#1975, ADR 0153), as the panel shows it and as
// its commands answer. Types only, and outside `lib/actions`: the admin preview renders the panel over
// fixture commands and may reach no Server Action module (ADR 0150 D5).

/**
 * An account's pending change: whether its code still works, and two instants. No address, because the panel
 * needs none to name the state.
 */
export interface AdminPendingEmailChange {
  readonly state: "pending" | "codeBurned";
  /** ISO instant: the earliest the account's owner can complete the change. */
  readonly completableFrom: string;
  /** ISO instant: when the code stops working. */
  readonly expiresAt: string;
}

/**
 * What the panel knows of the account's pending change, from a server answer only: a read beside the
 * account's details, a request that went through, or a cancel. `unknown` when the read failed or a command's
 * outcome could not be read.
 */
export type AdminEmailChangeState =
  | { readonly kind: "none" }
  | { readonly kind: "pending"; readonly change: AdminPendingEmailChange }
  | { readonly kind: "unknown" };

/**
 * A request's outcome: what the re-authentication dialog shows or hands over, and what then becomes of the
 * account. `changed` and `gone` follow a refusal that says the account is no longer the one the panel shows;
 * `refetch`, a refusal the account is read again after.
 */
export type AdminEmailChangeRequestOutcome = ReauthOutcome<AdminPendingEmailChange> & {
  readonly after?: "changed" | "gone" | "refetch";
};

/** A cancel's outcome, which the panel words. */
export type AdminEmailChangeCancelOutcome =
  | { readonly kind: "cancelled" }
  /** Nothing was left to cancel: the change expired, was completed or was cancelled elsewhere. */
  | { readonly kind: "nothingPending" }
  /** The cancel did not run. */
  | { readonly kind: "refused"; readonly reason: "rateLimited"; readonly retryAfterSeconds: number }
  | { readonly kind: "refused"; readonly reason: "unauthorized" | "forbidden" }
  /** The cancel left and nothing readable came back: the change may or may not be cancelled. */
  | { readonly kind: "unknown" };
