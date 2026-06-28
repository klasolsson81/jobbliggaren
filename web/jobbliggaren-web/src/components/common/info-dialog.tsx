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
 */
export function InfoDialog({
  title,
  paragraphs,
  triggerClassName,
}: {
  title: string;
  paragraphs: readonly string[];
  triggerClassName?: string;
}) {
  const t = useTranslations("common.dialog");
  const [first, ...rest] = paragraphs;

  return (
    <Dialog>
      <DialogTrigger
        className={
          triggerClassName ??
          "inline-flex items-center gap-1.5 text-sm font-medium text-text-secondary hover:text-text-primary hover:underline"
        }
      >
        <HelpCircle size={15} aria-hidden="true" />
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
