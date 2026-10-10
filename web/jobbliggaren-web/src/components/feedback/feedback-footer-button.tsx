"use client";

// "use client": reads the visit's feedback session and owns a dialog's state.

import { MessageSquare } from "lucide-react";
import { useTranslations } from "next-intl";
import { FeedbackDialog, useFeedbackDialog } from "./feedback-dialog";
import { useFeedbackSession, type FeedbackSession } from "./feedback-session";

function GeneralFeedback({ session }: { session: FeedbackSession }) {
  const t = useTranslations("feedback");
  const dialog = useFeedbackDialog({ page: "general", session });
  // Like the row, it does not vanish with work in it if the session closes meanwhile.
  if (!session.open && !dialog.holding) return null;

  return (
    <li>
      <FeedbackDialog dialog={dialog}>
        <button type="button" className="jp-foot__cta">
          <MessageSquare size={18} aria-hidden="true" />
          {t("footer.open")}
        </button>
      </FeedbackDialog>
    </li>
  );
}

/**
 * The footer's way to give general feedback on the service (#1979 PR3), on every signed-in route and
 * whichever pages have been answered; a page's own rating is its row's. It renders nothing outside the
 * signed-in layout or while feedback is closed. The footer stays mounted across navigation, so one draft
 * lasts the visit.
 */
export function FeedbackFooterButton() {
  const session = useFeedbackSession();
  if (session === null) return null;
  return <GeneralFeedback session={session} />;
}
