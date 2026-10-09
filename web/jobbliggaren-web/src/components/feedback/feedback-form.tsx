"use client";

// "use client": a controlled form with paste handling, a live region and focus on the receipt.

import { useEffect, useId, useRef, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { CircleCheck } from "lucide-react";
import { useTranslations } from "next-intl";
import { Textarea } from "@/components/ui/textarea";
import { STANDALONE_LINK, TEXT_LINK } from "@/components/auth/mail-link";
import { LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import { ScreenshotField, SCREENSHOT_REFUSAL_MESSAGE, pastedScreenshot } from "./screenshot-field";
import { StarRating } from "./star-rating";
import {
  FEEDBACK_COMMENT_MAX,
  type FeedbackAnnouncement,
  type FeedbackFormController,
  type SendRefusal,
} from "./use-feedback-form";

type Translate = ReturnType<typeof useTranslations<"feedback">>;

function refusalText(t: Translate, refusal: SendRefusal): string {
  switch (refusal.outcome) {
    case "refused":
      return t(`refused.${refusal.reason}`);
    case "rateLimited":
      return t("refused.rateLimited", { minutes: Math.ceil(refusal.retryAfterSeconds / 60) });
    case "closed":
    case "busy":
    case "tooLarge":
    case "signedOut":
    case "unknown":
      return t(`refused.${refusal.outcome}`);
  }
}

function announcementText(t: Translate, announcement: FeedbackAnnouncement | null): string {
  if (announcement === null) return "";
  switch (announcement.kind) {
    case "sending":
      return t("sending");
    case "preparing":
      return t("screenshot.preparing");
    case "screenshot":
      return t(SCREENSHOT_REFUSAL_MESSAGE[announcement.reason]);
    case "refused":
      return refusalText(t, announcement.refusal);
  }
}

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
 * The feedback form (#1979 PR3), shared by the rating row at the end of a page and the footer's dialog.
 * Its state lives in the owner's `useFeedbackForm`. A rating or a comment is required; the image and
 * the device context are optional, and the device-context box starts unticked.
 *
 * `collapsible` is the row's form: only the question and the stars until a star is chosen, then the
 * rest unfolds below without moving focus, and stays unfolded.
 *
 * Send is never `disabled` while it waits, because a disabled control drops its focus to the page
 * (DESIGN.md §6); it is `aria-disabled` and its handler refuses a second press. The live region is
 * mounted from the first render, so its first message is announced.
 */
export function FeedbackForm({
  controller,
  collapsible = false,
}: {
  controller: FeedbackFormController;
  collapsible?: boolean;
}) {
  const t = useTranslations("feedback");
  const pathname = usePathname();
  const commentId = useId();
  const messageId = useId();
  const consentHintId = useId();
  const [unfolded, setUnfolded] = useState(!collapsible);
  const { state } = controller;

  if (state.phase.kind === "saved") return <FeedbackReceipt />;

  const expanded = unfolded || controller.hasDraft;
  const waiting = state.phase.kind === "sending" || state.screenshot.kind === "preparing";
  const refusal = state.phase.kind === "refused" ? state.phase.refusal : null;
  const sendLabel =
    state.phase.kind === "sending" ? t("sending") : refusal?.outcome === "unknown" ? t("sendAgain") : t("send");

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
      <StarRating
        value={state.rating}
        onChange={(rating) => {
          if (rating !== null) setUnfolded(true);
          controller.setRating(rating);
        }}
      />

      {expanded && (
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
                checkbox's legally bound hint. The policy opens in a new tab, which the hint says, because
                following it in this one leaves the signed-in layout and drops the draft. */}
            <p id={consentHintId} className="text-body-sm text-text-primary">
              {t.rich("consentHint", {
                privacy: (chunks) => (
                  <Link href="/integritet" target="_blank" rel="noopener noreferrer" className={TEXT_LINK}>
                    {chunks}
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
              {sendLabel}
            </button>
            {refusal !== null && (
              <p id={messageId} className="jp-feedback__message">
                {refusalText(t, refusal)}
                {refusal.outcome === "signedOut" && (
                  <>
                    {" "}
                    <Link
                      href={`${LOGIN_ENTRY_PATH}?next=${encodeURIComponent(pathname)}`}
                      className={STANDALONE_LINK}
                    >
                      {t("refused.signIn")}
                    </Link>
                  </>
                )}
              </p>
            )}
          </div>
        </div>
      )}

      <p role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {announcementText(t, state.announcement)}
      </p>
    </form>
  );
}
