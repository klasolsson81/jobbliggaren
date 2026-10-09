import "server-only";
import { cache } from "react";
import { getSessionId } from "@/lib/auth/session";
import { feedbackPromptStateSchema, toFeedbackPromptState, type FeedbackPromptState } from "@/lib/dto/feedback";
import { authedFetch } from "@/lib/http/authed-fetch";

const PROMPT_STATE_TIMEOUT_MS = 1_000;

/**
 * Whether feedback is open for the signed-in user, and which pages they have already answered (#1979 PR3).
 * Read once per render of the signed-in layout; `React.cache` shares it within the request.
 *
 * It never throws and never blocks the page for long: no session means no request at all (the layout
 * redirects on its own), and a refusal, a timeout or an unexpected body all read as `unavailable`, which
 * shows no feedback surface. The rating row is optional; the page it sits on is not.
 */
export const getFeedbackPromptState = cache(async (): Promise<FeedbackPromptState> => {
  const sessionId = await getSessionId();
  if (sessionId === null) return { kind: "unavailable" };
  try {
    const response = await authedFetch(sessionId, "/api/v1/me/feedback/prompt-state", {
      signal: AbortSignal.timeout(PROMPT_STATE_TIMEOUT_MS),
    });
    if (!response.ok) {
      await response.body?.cancel();
      return { kind: "unavailable" };
    }
    const parsed = feedbackPromptStateSchema.safeParse(await response.json());
    return parsed.success ? toFeedbackPromptState(parsed.data) : { kind: "unavailable" };
  } catch {
    return { kind: "unavailable" };
  }
});
