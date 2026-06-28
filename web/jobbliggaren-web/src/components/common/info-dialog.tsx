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
 */
export function InfoDialog({
  title,
  paragraphs,
  triggerClassName,
  showIcon = true,
}: {
  title: string;
  paragraphs: readonly string[];
  triggerClassName?: string;
  showIcon?: boolean;
}) {
  const t = useTranslations("common.dialog");
  const [first, ...rest] = paragraphs;

  const defaultTriggerClassName = showIcon
    ? "inline-flex items-center gap-1.5 text-sm font-medium text-text-secondary hover:text-text-primary hover:underline"
    : "text-sm font-medium text-text-secondary hover:text-text-primary hover:underline";

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
