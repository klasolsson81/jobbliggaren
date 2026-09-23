import { z } from "zod";

// The success bodies of re-authentication by code (#1739, ADR 0142 D5). Grants and challenge ids are
// capped at the bound the backend's validators read when the value comes back (`ReauthGrantRules`,
// 64), so a value this schema admits is one the next request can carry.

/** `POST /api/v1/auth/reauth` and `POST /api/v1/auth/change-email` → 202, after a synchronous send. */
export const boundChallengeSchema = z.object({
  challengeId: z.string().min(1).max(64),
});

/** `POST /api/v1/auth/reauth/verify` → 200. Never a session. */
export const reauthGrantSchema = z.object({
  reauthGrant: z.string().min(1).max(64),
});

/** `POST /api/v1/auth/change-email/verify` → 200. Never a session. */
export const changeEmailGrantSchema = z.object({
  changeEmailGrant: z.string().min(1).max(64),
});

/**
 * `POST /api/v1/auth/change-email/confirm` → 200: every earlier session of the account is gone, and
 * this is the confirming device's new one. The backend sets no cookie (ADR 0018).
 */
export const reissuedSessionSchema = z.object({
  sessionId: z.string().min(1),
  persistent: z.boolean(),
});
