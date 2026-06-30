"use client";

import { useTranslations } from "next-intl";
import { HelpCircle } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";

/**
 * Reusable "Vad är detta?" explainer dialog (Klas 2026-06-28: the same affordance
 * is reused across the app with different text). The trigger and close labels are
 * the shared <c>common.dialog</c> strings; the <paramref name="title"/> and body
 * <paramref name="paragraphs"/> are passed per feature. The first paragraph is the
 * dialog description (a11y describedby); the rest render as the body. Escape /
 * overlay / close button all dismiss (Radix Dialog default).
 *
 * Placement convention (Klas #337, 2026-06-28): the canonical placement is a
 * small, text-only link sitting directly under the control it explains. Callers
 * pass <c>showIcon={false}</c> for that convention; the default keeps the
 * HelpCircle icon for standalone/inline uses. The trigger stays an accessible
 * button either way (the icon is decorative — aria-hidden).
 *
 * Icon-only variant (#408): on a dense control row the literal "Vad är detta?"
 * text would be noise, so <c>iconOnly</c> renders just the HelpCircle glyph and
 * moves the trigger label to <c>aria-label</c> (the same shared
 * <c>common.dialog.whatIsThis</c> string, so the accessible name is unchanged).
 * The padded inline-flex keeps the hit-area ≥32px in-app / ≥44px touch even
 * though the glyph is ~15px. Mutually exclusive with the visible-text variants;
 * <c>showIcon</c> is ignored when <c>iconOnly</c> is set.
 */
export function InfoDialog({
  title,
  paragraphs,
  triggerClassName,
  showIcon = true,
  iconOnly = false,
}: {
  title: string;
  paragraphs: readonly string[];
  triggerClassName?: string;
  showIcon?: boolean;
  iconOnly?: boolean;
}) {
  const t = useTranslations("common.dialog");
  const [first, ...rest] = paragraphs;

  // Icon-only: padded square hit-area around the ~15px glyph; the accessible
  // name comes from aria-label since there is no visible text. The padded
  // inline-flex grows the hit-area on coarse pointers (≥44px touch) while the
  // in-app target clears the 32px floor (a11y skill §5). Default variants keep
  // the text-bearing trigger.
  const iconOnlyTriggerClassName =
    "inline-flex min-h-[36px] min-w-[36px] items-center justify-center rounded-sm p-1.5 text-text-secondary hover:text-text-primary [@media(pointer:coarse)]:min-h-[44px] [@media(pointer:coarse)]:min-w-[44px]";
  const defaultTriggerClassName = showIcon
    ? "inline-flex items-center gap-1.5 text-sm font-medium text-text-secondary hover:text-text-primary hover:underline"
    : "text-sm font-medium text-text-secondary hover:text-text-primary hover:underline";

  if (iconOnly) {
    return (
      <Dialog>
        <DialogTrigger
          className={triggerClassName ?? iconOnlyTriggerClassName}
          aria-label={t("whatIsThis")}
        >
          <HelpCircle size={15} aria-hidden="true" />
        </DialogTrigger>

        <DialogContent>
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            {first ? (
              <DialogDescription className="text-text-primary">
                {first}
              </DialogDescription>
            ) : null}
          </DialogHeader>
          {rest.length > 0 ? (
            <div className="flex flex-col gap-3 text-sm text-text-primary">
              {rest.map((paragraph) => (
                <p key={paragraph}>{paragraph}</p>
              ))}
            </div>
          ) : null}
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Dialog>
      <DialogTrigger className={triggerClassName ?? defaultTriggerClassName}>
        {showIcon ? <HelpCircle size={15} aria-hidden="true" /> : null}
        {t("whatIsThis")}
      </DialogTrigger>

      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {first ? (
            <DialogDescription className="text-text-primary">
              {first}
            </DialogDescription>
          ) : null}
        </DialogHeader>
        {rest.length > 0 ? (
          <div className="flex flex-col gap-3 text-sm text-text-primary">
            {rest.map((paragraph) => (
              <p key={paragraph}>{paragraph}</p>
            ))}
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
