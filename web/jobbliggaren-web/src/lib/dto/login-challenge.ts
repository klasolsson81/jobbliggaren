import { z } from "zod";

/** `POST /api/v1/auth/challenge` → 202. Uniform for a known, unknown, cooled and over-budget address. */
export const loginChallengeResponseSchema = z.object({
  challengeId: z.string().min(1).max(64),
});

/**
 * The 200 body of `/auth/challenge/verify`, `/auth/link` and `/auth/challenge/complete`
 * (`AuthEndpoints.LoginOutcomeResult`). Every outcome is a 200, so the status says nothing: the arm
 * is chosen on `outcome` before `sessionId` is ever read.
 */
export const loginOutcomeSchema = z.discriminatedUnion("outcome", [
  z.object({ outcome: z.literal("signedIn"), sessionId: z.string().min(1) }),
  z.object({
    outcome: z.literal("pendingDeletion"),
    // A bare ISO date: the backend renders a `DateOnly` as "yyyy-MM-dd", no time and no zone.
    permanentDeletionDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  }),
  z.object({ outcome: z.literal("registrationClosed") }),
  z.object({ outcome: z.literal("consentRequired"), grantToken: z.string().min(1).max(64) }),
  z.object({ outcome: z.literal("accountUnavailable") }),
]);

export type LoginOutcome = z.infer<typeof loginOutcomeSchema>;
