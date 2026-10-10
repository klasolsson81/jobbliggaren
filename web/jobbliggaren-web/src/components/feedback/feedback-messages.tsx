"use client";

// "use client": the sign-in link is built from the current route.

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { TEXT_LINK } from "@/components/auth/mail-link";
import { LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import type { FeedbackPageKey } from "@/lib/feedback/page-keys";
import { SCREENSHOT_REFUSAL_MESSAGE } from "./screenshot-field";
import type { FeedbackAnnouncement, SendRefusal } from "./use-feedback-form";

type Translate = ReturnType<typeof useTranslations<"feedback">>;

/**
 * The question the stars answer and the dialog's title. Ratings are kept per page, so both name the page
 * they are about; general feedback names the service instead.
 */
export function useFeedbackHeadings(page: FeedbackPageKey): { readonly title: string; readonly question: string } {
  const t = useTranslations("feedback");
  if (page === "general") return { title: t("general.title"), question: t("general.question") };
  const name = t(`pages.${page}`);
  return { title: t("dialog.title", { page: name }), question: t("question", { page: name }) };
}

function refusalText(t: Translate, refusal: SendRefusal): string {
  switch (refusal.outcome) {
    case "refused":
      return t(`refused.${refusal.reason}`);
    case "rateLimited":
      return t("refused.rateLimited", { minutes: Math.ceil(refusal.retryAfterSeconds / 60) });
    case "signedOut":
      return t.markup("refused.signedOut", { signIn: (chunks) => chunks });
    case "closed":
    case "busy":
    case "tooLarge":
    case "unknown":
      return t(`refused.${refusal.outcome}`);
  }
}

/** What a feedback surface's live region says for its latest announcement. */
export function announcementText(t: Translate, announcement: FeedbackAnnouncement | null): string {
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

/**
 * The visible line for a refused send. A refusal of the content is an error; the others say the send
 * did not go through and are neutral. A signed-out answer links to the login in a new tab, which keeps
 * what was entered here.
 */
export function RefusalMessage({ id, refusal }: { id: string; refusal: SendRefusal }) {
  const t = useTranslations("feedback");
  const pathname = usePathname();
  const error = refusal.outcome === "refused" || refusal.outcome === "tooLarge";
  return (
    <p id={id} className={error ? "jp-feedback__message jp-feedback__message--error" : "jp-feedback__message"}>
      {refusal.outcome === "signedOut"
        ? t.rich("refused.signedOut", {
            signIn: (chunks) => (
              <Link
                href={`${LOGIN_ENTRY_PATH}?next=${encodeURIComponent(pathname)}`}
                target="_blank"
                rel="noopener noreferrer"
                className={TEXT_LINK}
              >
                {chunks}
              </Link>
            ),
          })
        : refusalText(t, refusal)}
    </p>
  );
}

export type SendLabel = "send" | "sending" | "sendAgain";

/**
 * A send button's label. Every label it can take shares one grid cell and only the current one shows,
 * so the button keeps its width when the label changes.
 */
export function SendLabels({ labels, current }: { labels: ReadonlyArray<SendLabel>; current: SendLabel }) {
  const t = useTranslations("feedback");
  return (
    <span className="jp-feedback__sendlabels">
      {labels.map((label) => (
        <span
          key={label}
          className={label === current ? "jp-feedback__sendlabel" : "jp-feedback__sendlabel jp-feedback__sendlabel--off"}
          aria-hidden={label !== current || undefined}
        >
          {t(label)}
        </span>
      ))}
    </span>
  );
}
