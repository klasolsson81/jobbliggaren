"use client";

// "use client": the row reads the visit's feedback session and owns a form's state.

import { browserCodec } from "@/lib/feedback/image/browser-codec";
import type { FeedbackPageKey } from "@/lib/feedback/page-keys";
import { FeedbackForm } from "./feedback-form";
import type { FeedbackSession } from "./feedback-session";
import { useFeedbackForm } from "./use-feedback-form";

/**
 * The rating row at the end of a page's content (#1979 PR3): a compact band under a hairline, no card
 * and no shadow. It asks once per page; it renders nothing while feedback is closed or the page has
 * been answered.
 *
 * It never goes away under the user, though: while it holds a draft, a send in flight or the receipt it
 * stays, whatever the session says meanwhile. The receipt replaces the form after a save and the
 * page is marked answered for the rest of the visit.
 *
 * This file owns the page-width container on purpose: `page-width.test.ts` follows one import hop from
 * a page, and the page imports `PageFeedback`, which must not stand in for a page's own container.
 */
export function FeedbackRow({ pageKey, session }: { pageKey: FeedbackPageKey; session: FeedbackSession }) {
  const controller = useFeedbackForm({
    page: pageKey,
    renderedVersion: session.renderedVersion,
    codec: browserCodec,
    onSaved: () => session.markAnswered(pageKey),
  });
  const { phase } = controller.state;
  const holding = controller.hasDraft || phase.kind === "sending" || phase.kind === "saved";
  if (!holding && (!session.open || session.isAnswered(pageKey))) return null;

  return (
    <section className="jp-container jp-feedback">
      <div className="jp-feedback__inner">
        <FeedbackForm controller={controller} collapsible />
      </div>
    </section>
  );
}
