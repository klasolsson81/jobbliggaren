import { z } from "zod";

// Input schemas of the login flow's Server Actions. A Server Action is reachable by an ordinary POST
// that never passed through the form, so every field is parsed here whatever the markup promises.

/**
 * Non-empty and within the backend's length bound, and nothing more. What counts as an address is
 * the backend's to say (`StorableAddress`, #1781): it admits `björn@…`, which the HTML email
 * production and Zod's default email pattern both refuse. A stricter rule here would be a second,
 * narrower definition in front of the real one. A malformed address comes back as a 400.
 */
export const emailInputSchema = z.string().trim().min(1).max(256);

/** MIRROR of the backend `LoginChallengePolicy.CodeLength`. A malformed code spends no attempt. */
export const codeInputSchema = z
  .string()
  .trim()
  .regex(/^[0-9]{6}$/);

/** A bound challenge's id, re-authentication's and change-email's; the backend validators' bound (64). */
export const challengeIdInputSchema = z.string().min(1).max(64);

/** The bound is the backend validator's (`ConsumeLoginLinkCommandValidator`, 128). */
export const linkTokenInputSchema = z.string().trim().min(1).max(128);

/** A checked native checkbox posts "on"; an unchecked one posts nothing. */
export const acceptTermsInputSchema = z.literal("on");

/** Posted by the link landing's two-control arm only: the press that may replace a session. */
export const replaceSessionInputSchema = z.literal("on");
