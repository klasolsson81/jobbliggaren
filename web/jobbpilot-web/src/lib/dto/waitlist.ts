import { z } from "zod";

/**
 * Backend-svar från `POST /api/v1/waitlist` vid lyckad signup.
 * Per ADR 0005 amendment 2026-05-12 (invitations + waitlist).
 */
export const waitlistEntryResponseSchema = z.object({
  waitlistEntryId: z.string().uuid(),
  email: z.string(),
});

export type WaitlistEntryResponse = z.infer<typeof waitlistEntryResponseSchema>;
