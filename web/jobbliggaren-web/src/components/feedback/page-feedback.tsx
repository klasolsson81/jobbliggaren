"use client";

// "use client": reads the visit's feedback session from context.

import type { FeedbackRoutePageKey } from "@/lib/feedback/page-keys";
import { FeedbackRow } from "./feedback-row";
import { useFeedbackSession } from "./feedback-session";

/**
 * A page's rating row (#1979 PR3), placed last in the page's own markup with the page's key written as
 * a literal. Outside the signed-in layout there is no session and it renders nothing.
 * `page-feedback-coverage.test.ts` holds every mapped page to exactly one of these.
 */
export function PageFeedback({ pageKey }: { pageKey: FeedbackRoutePageKey }) {
  const session = useFeedbackSession();
  if (session === null) return null;
  return <FeedbackRow pageKey={pageKey} session={session} />;
}
