"use client";

// "use client": the row reads the visit's feedback session, owns a send's state, and moves focus to its
// confirmation.

import { useEffect, useId, useRef, useState } from "react";
import { CircleCheck, X } from "lucide-react";
import { useTranslations } from "next-intl";
import { browserCodec } from "@/lib/feedback/image/browser-codec";
import type { FeedbackRoutePageKey } from "@/lib/feedback/page-keys";
import { FeedbackDialog, useFeedbackDialog } from "./feedback-dialog";
import {
  RefusalMessage,
  SendLabels,
  announcementText,
  useFeedbackHeadings,
  type SendLabel,
} from "./feedback-messages";
import type { FeedbackSession } from "./feedback-session";
import { StarButtons } from "./star-rating";
import { useFeedbackForm } from "./use-feedback-form";

const RETRY_LABELS: ReadonlyArray<SendLabel> = ["sendAgain", "sending"];

const TABBABLE =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Moves focus off the row before it goes, so it does not fall to `<body>` (DESIGN.md §6, WCAG 2.4.3):
 * to the next stop in reading order, the footer's first control, else the page's h1.
 */
function moveFocusPast(row: HTMLElement) {
  const next = Array.from(document.querySelectorAll<HTMLElement>(TABBABLE)).find(
    (element) => !row.contains(element) && row.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING,
  );
  const target = next ?? document.querySelector<HTMLElement>("main h1");
  if (target === null) return;
  if (!target.hasAttribute("tabindex")) target.setAttribute("tabindex", "-1");
  target.focus({ preventScroll: true });
}

/**
 * Replaces the stars once the rating is saved and takes focus, so the outcome is read where the user is.
 * "Lämna mer feedback" opens the whole form with the rating already chosen.
 */
function RatingConfirmation({
  page,
  session,
  rating,
  onClose,
}: {
  page: FeedbackRoutePageKey;
  session: FeedbackSession;
  rating: number | null;
  onClose: () => void;
}) {
  const t = useTranslations("feedback");
  const ref = useRef<HTMLParagraphElement>(null);
  const dialog = useFeedbackDialog({ page, session, initialRating: rating });
  useEffect(() => {
    ref.current?.focus();
  }, []);
  return (
    <div className="jp-feedback__done">
      <div className="jp-feedback__donemain">
        <p ref={ref} tabIndex={-1} className="jp-feedback__receipt">
          <CircleCheck size={20} aria-hidden="true" />
          {t("rated")}
        </p>
        <FeedbackDialog dialog={dialog}>
          <button type="button" className="jp-btn jp-btn--secondary jp-btn--sm">
            {t("more")}
          </button>
        </FeedbackDialog>
      </div>
      <button type="button" className="jp-feedback__close" aria-label={t("close")} onClick={onClose}>
        <X size={20} aria-hidden="true" />
      </button>
    </div>
  );
}

/**
 * The rating row at the end of a page's content (#1979 PR3): a compact band under a hairline, no card
 * and no shadow. It asks once per page with the question and the stars, and a chosen star is the answer:
 * it is sent at once, on its own. It renders nothing while feedback is closed or the page has been
 * answered, but it never goes away under the user while a send is in flight or its confirmation shows.
 *
 * A refusal is told under the stars, and choosing a star again sends again. After an answer that may
 * or may not have been saved, "Skicka igen" sends the same rating under the same key, and it stays
 * until a star is chosen, so focus does not drop while it waits. Closing the confirmation removes the
 * row for the rest of the visit: the page is answered by then, so the row does not come back.
 *
 * This file owns the page-width container on purpose: `page-width.test.ts` follows one import hop from
 * a page, and the page imports `PageFeedback`, which must not stand in for a page's own container.
 */
export function FeedbackRow({ pageKey, session }: { pageKey: FeedbackRoutePageKey; session: FeedbackSession }) {
  const t = useTranslations("feedback");
  const { question } = useFeedbackHeadings(pageKey);
  const messageId = useId();
  const controller = useFeedbackForm({
    page: pageKey,
    renderedVersion: session.renderedVersion,
    codec: browserCodec,
    onSaved: () => session.markAnswered(pageKey),
  });
  const rowRef = useRef<HTMLElement>(null);
  const [closed, setClosed] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const { phase, rating } = controller.state;
  const sending = phase.kind === "sending";
  const holding = sending || phase.kind === "saved";
  if (closed || (!holding && (!session.open || session.isAnswered(pageKey)))) return null;

  const refusal = phase.kind === "refused" ? phase.refusal : null;
  const offerRetry = rating !== null && (retrying || refusal?.outcome === "unknown");

  return (
    <section ref={rowRef} className="jp-container jp-feedback">
      <div className="jp-feedback__inner">
        {phase.kind === "saved" ? (
          <RatingConfirmation
            page={pageKey}
            session={session}
            rating={rating}
            onClose={() => {
              if (rowRef.current !== null) moveFocusPast(rowRef.current);
              setClosed(true);
            }}
          />
        ) : (
          <div className="jp-feedback__ask">
            <StarButtons
              question={question}
              value={rating}
              sending={sending}
              onCommit={(next) => {
                if (sending) return;
                setRetrying(false);
                controller.sendRating(next);
              }}
            />
            {(refusal !== null || offerRetry) && (
              <div className="jp-feedback__actions">
                {refusal !== null && <RefusalMessage id={messageId} refusal={refusal} />}
                {offerRetry && (
                  <button
                    type="button"
                    className="jp-btn jp-btn--secondary jp-btn--sm"
                    aria-disabled={sending || undefined}
                    aria-describedby={refusal !== null ? messageId : undefined}
                    onClick={() => {
                      setRetrying(true);
                      controller.sendRating(rating);
                    }}
                  >
                    <SendLabels labels={RETRY_LABELS} current={sending ? "sending" : "sendAgain"} />
                  </button>
                )}
              </div>
            )}
            <p role="status" aria-live="polite" aria-atomic="true" className="sr-only">
              {announcementText(t, controller.state.announcement)}
            </p>
          </div>
        )}
      </div>
    </section>
  );
}
