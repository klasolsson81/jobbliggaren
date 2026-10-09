import { z } from "zod";
import { FEEDBACK_COMMENT_MAX } from "@/lib/feedback/limits";
import { FEEDBACK_PAGE_KEYS, isFeedbackPageKey, type FeedbackPageKey } from "@/lib/feedback/page-keys";

/**
 * The user-facing feedback wire shapes (#1979 PR3, ADR 0156 D2).
 */

/** `GET /api/v1/me/feedback/prompt-state`. */
export const feedbackPromptStateSchema = z.object({
  open: z.boolean(),
  answeredPages: z.array(z.string().max(64)).max(64),
});

/**
 * What the signed-in layout knows about feedback for this visit. `unavailable` is a read that failed or
 * was skipped; the surface shows nothing then, the same as `closed`, and the two stay apart only so a
 * test can tell which one happened.
 */
export type FeedbackPromptState =
  | { readonly kind: "open"; readonly answered: ReadonlyArray<FeedbackPageKey> }
  | { readonly kind: "closed" }
  | { readonly kind: "unavailable" };

export function toFeedbackPromptState(dto: z.infer<typeof feedbackPromptStateSchema>): FeedbackPromptState {
  return dto.open ? { kind: "open", answered: dto.answeredPages.filter(isFeedbackPageKey) } : { kind: "closed" };
}

/** `POST /api/v1/me/feedback`, 201 or 200. */
export const feedbackSubmittedSchema = z.object({ id: z.uuid(), replayed: z.boolean() });

const dimension = z.number().int().min(1).max(20_000);

/** The device context the opt-in box sends, with the backend's own bounds and names. */
export const feedbackClientContextSchema = z
  .object({
    viewportWidth: dimension.optional(),
    viewportHeight: dimension.optional(),
    screenWidth: dimension.optional(),
    screenHeight: dimension.optional(),
    pixelRatio: z.number().min(0.25).max(10).optional(),
    deviceClass: z.enum(["mobile", "tablet", "desktop"]).optional(),
    osFamily: z.enum(["windows", "macOs", "ios", "android", "linux", "chromeOs", "other"]).optional(),
    browserFamily: z.enum(["chrome", "edge", "firefox", "safari", "samsungInternet", "opera", "other"]).optional(),
  })
  .strict();

/**
 * The payload the browser posts to the BFF route. Strict: an unknown key, including an `appVersion` the
 * browser tries to set itself, refuses the request — the version is the web server's to stamp.
 * `renderedVersion` is the version the page was rendered with; the route compares it and never sends it on.
 */
export const feedbackSubmissionPayloadSchema = z
  .object({
    submissionKey: z.uuid(),
    page: z.enum(FEEDBACK_PAGE_KEYS),
    rating: z.number().int().min(1).max(5).optional(),
    comment: z.string().max(FEEDBACK_COMMENT_MAX).optional(),
    client: feedbackClientContextSchema.optional(),
    renderedVersion: z.string().max(40).optional(),
  })
  .strict();

export type FeedbackSubmissionPayload = z.infer<typeof feedbackSubmissionPayloadSchema>;

/**
 * Every outcome the BFF route answers the browser with. The backend's body never travels; only the
 * code in its ProblemDetails title is compared, against this closed list.
 */
export type FeedbackSubmitOutcome =
  | { readonly outcome: "saved" }
  | { readonly outcome: "refused"; readonly reason: "empty" | "screenshot" | "invalid" }
  | { readonly outcome: "closed" }
  | { readonly outcome: "busy" }
  | { readonly outcome: "tooLarge" }
  | { readonly outcome: "rateLimited"; readonly retryAfterSeconds: number }
  | { readonly outcome: "signedOut" }
  | { readonly outcome: "unknown" };

export const feedbackSubmitOutcomeSchema: z.ZodType<FeedbackSubmitOutcome> = z.discriminatedUnion("outcome", [
  z.object({ outcome: z.literal("saved") }),
  z.object({ outcome: z.literal("refused"), reason: z.enum(["empty", "screenshot", "invalid"]) }),
  z.object({ outcome: z.literal("closed") }),
  z.object({ outcome: z.literal("busy") }),
  z.object({ outcome: z.literal("tooLarge") }),
  z.object({ outcome: z.literal("rateLimited"), retryAfterSeconds: z.number().int().positive() }),
  z.object({ outcome: z.literal("signedOut") }),
  z.object({ outcome: z.literal("unknown") }),
]);

export const FEEDBACK_SUBMIT_ERRORS = {
  empty: "Feedback.Empty",
  screenshotInvalid: "Feedback.ScreenshotInvalid",
} as const;
