"use client";

// "use client": reads the route, the visit's feedback session, and owns a dialog and its form state.

import { useState } from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { Dialog, DialogContent, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { browserCodec } from "@/lib/feedback/image/browser-codec";
import type { ScreenshotCodec } from "@/lib/feedback/image/prepare";
import { feedbackPageKeyFor, type FeedbackPageKey } from "@/lib/feedback/page-keys";
import { FeedbackForm } from "./feedback-form";
import { useFeedbackSession, type FeedbackSession } from "./feedback-session";
import { useFeedbackForm } from "./use-feedback-form";

function FooterFeedback({
  page,
  session,
  codec,
}: {
  page: FeedbackPageKey;
  session: FeedbackSession;
  codec: ScreenshotCodec;
}) {
  const t = useTranslations("feedback");
  const [open, setOpen] = useState(false);
  const controller = useFeedbackForm({
    page,
    renderedVersion: session.renderedVersion,
    codec,
    onSaved: () => session.markAnswered(page),
  });
  const { phase } = controller.state;
  // Like the row, it does not vanish with work in it if the session closes meanwhile.
  const holding = open || controller.hasDraft || phase.kind === "sending" || phase.kind === "saved";
  if (!session.open && !holding) return null;

  return (
    <li>
      <Dialog
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          // A saved answer has done its work; the next opening starts a new one. An unsent draft stays.
          if (!next && phase.kind === "saved") controller.reset();
        }}
      >
        <DialogTrigger asChild>
          <button type="button" className="jp-foot__linkbtn">
            {t("footer.open")}
          </button>
        </DialogTrigger>
        <DialogContent aria-describedby={undefined} className="jp-feedback-dialog">
          <DialogTitle>{t("footer.title")}</DialogTitle>
          <FeedbackForm controller={controller} />
        </DialogContent>
      </Dialog>
    </li>
  );
}

/**
 * The footer's way to give feedback on the current page at any time (#1979 PR3), also after the page's
 * own row has been answered. It renders nothing on a route without a page key or while feedback is
 * closed. The dialog's draft belongs to this component rather than to the dialog, so closing and
 * reopening keeps it; a new page key starts a new one.
 */
export function FeedbackFooterButton({ codec = browserCodec }: { codec?: ScreenshotCodec }) {
  const pathname = usePathname();
  const session = useFeedbackSession();
  const page = feedbackPageKeyFor(pathname);
  if (session === null || page === null) return null;
  return <FooterFeedback key={page} page={page} session={session} codec={codec} />;
}
