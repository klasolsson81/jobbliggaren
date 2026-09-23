// State types of re-authentication by code (#1740, ADR 0142 D5). A module of their own: the actions
// that return them are "use server" and may export only async functions (`_action-result.ts`, #1059).

import type { MessageChannel } from "@/lib/auth/challenge-action-state";

/** A code and the challenge it answers; the only thing a consumer's operation receives from the dialog. */
export type CodeProof = { challengeId: string; code: string };

/** `requestReauthCode`. The challenge id goes back to the dialog that asked for it, and only there. */
export type ReauthRequestResult =
  | { ok: true; challengeId: string }
  /**
   * Nothing to correct: a cooldown, a throttle, an outage. The step stays, and so does its button.
   * `cooldown`: the account asked inside the server's window, so a resend countdown starts over.
   */
  | { ok: false; kind: "status"; error: string; cooldown?: true }
  /** No request can succeed today: the account's daily code budget is spent. */
  | { ok: false; kind: "terminal"; error: string }
  /** The session is gone; the way on is the login page. */
  | { ok: false; kind: "notLoggedIn" }
  /** No mail can be delivered on this deployment. What that means is the consumer's to say. */
  | { ok: false; kind: "refused" };

/**
 * An operation that spends a bound code: the code is verified and the operation performed in ONE
 * Server Action, so the grant between them never leaves the server (security-auditor, #1740 S1 (d)).
 */
export type ReauthOutcome<T> =
  | { ok: true; value: T }
  /** The code was wrong; the field takes the message (the last-attempt warning rides in it). */
  | { ok: false; kind: "wrongCode"; error: string }
  /** The code can no longer be used; a new one is the way on. */
  | { ok: false; kind: "deadCode"; reason: "expired" | "burned" }
  /** Nothing the user typed, and nothing spent. */
  | { ok: false; kind: "status"; error: string }
  /** The session is gone before the code was checked. */
  | { ok: false; kind: "notLoggedIn" }
  /**
   * The action refused its own input before the code was presented, so nothing was spent. Only a
   * crafted request or a race reaches it (the dialog validates the same input first); the flow starts
   * again from the request step.
   */
  | { ok: false; kind: "inputRefused"; error: string }
  /**
   * The operation's own refusal, documented by the backend, after the code was accepted and spent (the
   * message says so). `channel` says whether it belongs to a field the consumer owns or is a status;
   * `terminal`, that no retry in this flow can succeed, so the consumer replaces its form with the message.
   */
  | { ok: false; kind: "operationRefused"; error: string; channel: MessageChannel; terminal?: true }
  /** The operation's request left and nothing readable came back: it may or may not have happened. */
  | { ok: false; kind: "outcomeUnknown"; error: string }
  /** No mail can be delivered on this deployment. */
  | { ok: false; kind: "refused" };
