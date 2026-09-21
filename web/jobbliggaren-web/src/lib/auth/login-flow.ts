import { z } from "zod";

/**
 * Where a visitor is in the login flow (ADR 0142 "Page form"). The value of the
 * `__Host-jobbliggaren_login` cookie, which the step pages render from.
 *
 * It lives in a cookie and not in action state because a Server Action that writes a cookie makes
 * Next re-render the current route in the same response, and because the grant has to survive the
 * move from the code step to the consent step without ever entering a URL.
 *
 * The cookie is unsigned. Only its holder can forge it, and it is never proof of anything: no
 * branch grants a session, an authorization or account content from it. `.strict()` on every arm
 * is what keeps a forged value from carrying one phase's field into another phase's reader.
 */

/** MIRROR of the backend `LoginChallengePolicy.ChallengeTtl`: the cookie must not outlive the record. */
export const CODE_PHASE_MAX_AGE_SECONDS = 15 * 60;

/** MIRROR of the backend `LoginChallengePolicy.GrantTtl`. */
export const CONSENT_PHASE_MAX_AGE_SECONDS = 10 * 60;

/** A Server Component cannot clear a cookie, so for a panel read once the Max-Age is the only lever. */
export const OUTCOME_PHASE_MAX_AGE_SECONDS = 120;
export const NOTICE_PHASE_MAX_AGE_SECONDS = 120;

/**
 * How long "Skicka ny kod" stays disabled (ADR 0142 "Page form").
 * MIRROR of the backend default `AuthEmailCooldownOptions.LoginChallengeWindowSeconds`: a resend
 * inside the server's window returns a challenge id with no record, which would make the code
 * already mailed unverifiable. If the server window is raised, raise this with it.
 */
export const RESEND_COOLDOWN_SECONDS = 60;

const MAX_NEXT_LENGTH = 512;

const codePhase = z.strictObject({
  phase: z.literal("code"),
  challengeId: z.string().min(1).max(64),
  /** The address as typed. Shown back on the code step and used to request a new code. */
  email: z.string().min(1).max(256),
  next: z.string().max(MAX_NEXT_LENGTH),
  /** Epoch seconds of the mint. The cookie's lifetime and the resend cooldown count from it. */
  sentAt: z.number().int().nonnegative(),
  /** Set once the backend has answered 410 for this challenge; survives a reload. */
  dead: z.enum(["expired", "burned"]).optional(),
});

/** No address and no challenge id: the grant is a bearer, and the account is created on what it proved. */
const consentPhase = z.strictObject({
  phase: z.literal("consent"),
  grantToken: z.string().min(1).max(64),
  next: z.string().max(MAX_NEXT_LENGTH),
});

const outcomeResult = z.discriminatedUnion("outcome", [
  z.strictObject({
    outcome: z.literal("pendingDeletion"),
    permanentDeletionDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  }),
  z.strictObject({ outcome: z.literal("registrationClosed") }),
  z.strictObject({ outcome: z.literal("accountUnavailable") }),
]);

const outcomePhase = z.strictObject({
  phase: z.literal("outcome"),
  result: outcomeResult,
});

const noticePhase = z.strictObject({
  phase: z.literal("notice"),
  notice: z.enum(["grantUnusable", "codeExpired"]),
});

export const loginFlowSchema = z.discriminatedUnion("phase", [
  codePhase,
  consentPhase,
  outcomePhase,
  noticePhase,
]);

export type LoginFlow = z.infer<typeof loginFlowSchema>;
export type LoginFlowCode = z.infer<typeof codePhase>;
export type LoginFlowOutcome = z.infer<typeof outcomeResult>;
export type LoginFlowNotice = z.infer<typeof noticePhase>["notice"];

/** A `next` too long for the cookie is dropped, never clipped into a different path. */
export function cookieSafeNext(next: string): string {
  return next.length <= MAX_NEXT_LENGTH ? next : "";
}

export function maxAgeSecondsFor(flow: LoginFlow, nowEpochSeconds: number): number {
  switch (flow.phase) {
    case "code":
      // Counted from the mint, so rewriting the cookie (a 410 marks it `dead`) never extends it.
      return Math.max(1, flow.sentAt + CODE_PHASE_MAX_AGE_SECONDS - nowEpochSeconds);
    case "consent":
      return CONSENT_PHASE_MAX_AGE_SECONDS;
    case "outcome":
      return OUTCOME_PHASE_MAX_AGE_SECONDS;
    case "notice":
      return NOTICE_PHASE_MAX_AGE_SECONDS;
  }
}

/** Seconds left before "Skicka ny kod" may be pressed, counted from the mint. */
export function resendCooldownRemaining(flow: LoginFlowCode, nowEpochSeconds: number): number {
  return Math.max(0, flow.sentAt + RESEND_COOLDOWN_SECONDS - nowEpochSeconds);
}

export function encodeLoginFlow(flow: LoginFlow): string {
  return Buffer.from(JSON.stringify(flow), "utf8").toString("base64url");
}

/** Any failure, an unknown phase or an extra key reads as "no cookie", never as a partial phase. */
export function decodeLoginFlow(raw: string | undefined): LoginFlow | null {
  if (!raw) return null;
  try {
    const parsed = loginFlowSchema.safeParse(
      JSON.parse(Buffer.from(raw, "base64url").toString("utf8"))
    );
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}
