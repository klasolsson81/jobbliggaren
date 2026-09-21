// State types of the login flow's Server Actions. A module of their own: `challenge-actions.ts` is
// "use server" and may export only async functions (`_action-result.ts`, #1059).
//
// Only what must NOT survive a reload lives here (a wrong code, the last-attempt warning, a
// transport failure). Where the visitor is in the flow lives in the cookie (`login-flow.ts`).

import type { LoginFlowOutcome } from "@/lib/auth/login-flow";

/**
 * `field`: the user can correct it, so `role="alert"` + `aria-invalid` + focus to the input.
 * `status`: nothing to correct (a throttle, an outage), so `role="status"`, never danger colour,
 * and focus to the message, since the pressed button is disabled while the action runs.
 */
export type MessageChannel = "field" | "status";

export type EmailStepState = {
  error: string;
  channel: MessageChannel;
  /** Echoed so the form can re-seed its input: React 19 resets an uncontrolled form after an action. */
  values: { email: string };
} | null;

export type CodeStepState = {
  error: string;
  channel: MessageChannel;
  /** The backend said exactly one attempt is left before the code arm burns. */
  lastAttempt?: true;
} | null;

export type ResendState =
  | { status: "sent" }
  | { status: "cooling" }
  | { status: "error"; error: string }
  | null;

export type ConsentStepState = {
  error: string;
  channel: MessageChannel;
} | null;

export type LinkStepState =
  | { kind: "outcome"; result: LoginFlowOutcome }
  | { kind: "unusable" }
  | { kind: "error"; error: string }
  | null;
