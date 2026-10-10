"use client";

// "use client": owns a dialog's open state and its form's state.

import { useState, type ReactElement } from "react";
import { Dialog, DialogContent, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { browserCodec } from "@/lib/feedback/image/browser-codec";
import type { FeedbackPageKey } from "@/lib/feedback/page-keys";
import { FeedbackForm } from "./feedback-form";
import { useFeedbackHeadings } from "./feedback-messages";
import type { FeedbackSession } from "./feedback-session";
import { useFeedbackForm, type FeedbackFormController } from "./use-feedback-form";

export type FeedbackDialogState = {
  readonly page: FeedbackPageKey;
  readonly controller: FeedbackFormController;
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  /** Open, or holding work that the dialog's owner going away would throw away. */
  readonly holding: boolean;
  /** A rating the form was opened with is already saved, so it is not the form's to take back. */
  readonly clearable: boolean;
};

/**
 * A feedback dialog's state (#1979 PR3). The form's draft belongs to the caller rather than to the
 * dialog, so closing and reopening keeps it. A saved answer has done its work: closing after it starts
 * the next opening empty, also without the initial rating.
 */
export function useFeedbackDialog({
  page,
  session,
  initialRating = null,
}: {
  page: FeedbackPageKey;
  session: FeedbackSession;
  initialRating?: number | null;
}): FeedbackDialogState {
  const [open, setOpen] = useState(false);
  const [seeded, setSeeded] = useState(initialRating !== null);
  const controller = useFeedbackForm({
    page,
    renderedVersion: session.renderedVersion,
    codec: browserCodec,
    onSaved: () => session.markAnswered(page),
    initialRating,
  });
  const { phase } = controller.state;
  return {
    page,
    controller,
    open,
    onOpenChange: (next) => {
      setOpen(next);
      if (!next && phase.kind === "saved") {
        controller.reset();
        setSeeded(false);
      }
    },
    holding: open || controller.hasDraft || phase.kind === "sending" || phase.kind === "saved",
    clearable: !seeded,
  };
}

/**
 * The feedback dialog (#1979 PR3): the whole form, opened by the footer's button for general feedback and
 * by the rating row's "Lämna mer feedback" for its page. `children` is the control that opens it, and
 * focus returns to it on close.
 */
export function FeedbackDialog({ dialog, children }: { dialog: FeedbackDialogState; children: ReactElement }) {
  const { title, question } = useFeedbackHeadings(dialog.page);
  return (
    <Dialog open={dialog.open} onOpenChange={dialog.onOpenChange}>
      <DialogTrigger asChild>{children}</DialogTrigger>
      <DialogContent aria-describedby={undefined} className="jp-feedback-dialog">
        <DialogTitle>{title}</DialogTitle>
        <FeedbackForm controller={dialog.controller} question={question} clearable={dialog.clearable} />
      </DialogContent>
    </Dialog>
  );
}
