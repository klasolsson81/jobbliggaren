"use client";

// "use client": a controlled form with paste handling, a live region and focus on the receipt.

import { useEffect, useId, useRef } from "react";
import Link from "next/link";
import { CircleCheck } from "lucide-react";
import { useTranslations } from "next-intl";
import { Textarea } from "@/components/ui/textarea";
import { TEXT_LINK } from "@/components/auth/mail-link";
import { FEEDBACK_COMMENT_MAX } from "@/lib/feedback/limits";
import { RefusalMessage, SendLabels, announcementText, type SendLabel } from "./feedback-messages";
import { ScreenshotField, pastedScreenshot } from "./screenshot-field";
import { StarRating } from "./star-rating";
import type { FeedbackFormController } from "./use-feedback-form";

const SEND_LABELS: ReadonlyArray<SendLabel> = ["send", "sending", "sendAgain"];

/** Replaces the form once a send is saved, and takes focus so the outcome is read where the user is. */
function FeedbackReceipt() {
  const t = useTranslations("feedback");
  const ref = useRef<HTMLParagraphElement>(null);
  useEffect(() => {
    ref.current?.focus();
  }, []);
  return (
    <p ref={ref} tabIndex={-1} className="jp-feedback__receipt">
      <CircleCheck size={20} aria-hidden="true" />
      {t("receipt")}
    </p>
  );
}

/**
 * The feedback dialog's form (#1979 PR3). Its state lives in the owner's `useFeedbackForm`. A rating or
 * a comment is required; the image and the device context are optional, and the device-context box
 * starts unticked.
 *
 * Send is never `disabled` while it waits, because a disabled control drops its focus to the page
 * (DESIGN.md §6); it is `aria-disabled` and its handler refuses a second press. The live region is
 * mounted from the first render, so its first message is announced.
 */
export function FeedbackForm({
  controller,
  question,
  clearable = true,
}: {
  controller: FeedbackFormController;
  question: string;
  clearable?: boolean;
}) {
  const t = useTranslations("feedback");
  const commentId = useId();
  const messageId = useId();
  const consentHintId = useId();
  const { state } = controller;

  if (state.phase.kind === "saved") return <FeedbackReceipt />;

  const waiting = state.phase.kind === "sending" || state.screenshot.kind === "preparing";
  const refusal = state.phase.kind === "refused" ? state.phase.refusal : null;
  const sendLabel: SendLabel =
    state.phase.kind === "sending" ? "sending" : refusal?.outcome === "unknown" ? "sendAgain" : "send";

  return (
    <form
      className="jp-feedback__form"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        controller.send();
      }}
      onPaste={(event) => {
        const file = pastedScreenshot(event.clipboardData);
        if (file === null) return;
        event.preventDefault();
        controller.addScreenshot(file);
      }}
    >
      <StarRating question={question} value={state.rating} onChange={controller.setRating} clearable={clearable} />

      <div className="jp-feedback__fields">
        <div className="jp-field">
          <label htmlFor={commentId} className="jp-label">
            {t("comment.label")}
          </label>
          <Textarea
            id={commentId}
            rows={4}
            maxLength={FEEDBACK_COMMENT_MAX}
            autoComplete="off"
            value={state.comment}
            onChange={(event) => controller.setComment(event.target.value)}
          />
        </div>

        <ScreenshotField
          screenshot={state.screenshot}
          onAdd={controller.addScreenshot}
          onRemove={controller.removeScreenshot}
        />

        <div>
          <label className="jp-feedback__consent">
            <input
              type="checkbox"
              className="jp-feedback__checkbox"
              aria-describedby={consentHintId}
              checked={state.shareDeviceContext}
              onChange={(event) => controller.setShareDeviceContext(event.target.checked)}
            />
            <span>{t("consent")}</span>
          </label>
          {/* The withdrawal route is read before the tick (Art. 7(3)), in primary text like the terms
              checkbox's legally bound hint. The policy opens in a new tab, because following it in this
              one leaves the signed-in layout and drops the draft. */}
          <p id={consentHintId} className="text-body-sm text-text-primary">
            {t.rich("consentHint", {
              privacy: (chunks) => (
                <Link href="/integritet" target="_blank" rel="noopener noreferrer" className={TEXT_LINK}>
                  {chunks} <span className="sr-only">{t("newTab")}</span>
                </Link>
              ),
            })}
          </p>
        </div>

        <div className="jp-feedback__actions">
          <button
            type="submit"
            className="jp-btn jp-btn--secondary"
            aria-disabled={waiting || undefined}
            aria-describedby={refusal !== null ? messageId : undefined}
          >
            <SendLabels labels={SEND_LABELS} current={sendLabel} />
          </button>
          {refusal !== null && <RefusalMessage id={messageId} refusal={refusal} />}
        </div>
      </div>

      <p role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {announcementText(t, state.announcement)}
      </p>
    </form>
  );
}
