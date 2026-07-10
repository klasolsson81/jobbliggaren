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
 * The shared "?" help affordance (#408; Klas 2026-07-01: inline "?" help next
 * to any non-obvious control is the app-wide pattern). Renders a single
 * HelpCircle-glyph trigger that opens an explainer dialog; the accessible name
 * comes from <c>ariaLabel</c>, falling back to the shared
 * <c>common.dialog.whatIsThis</c> string. The first paragraph is the dialog
 * description (a11y describedby); the rest render as the body. Escape /
 * overlay / close button all dismiss (Radix Dialog default).
 *
 * The padded inline-flex keeps the hit-area ≥32px in-app / ≥44px touch even
 * though the glyph is ~15px (a11y skill §5); the icon itself is decorative
 * (aria-hidden).
 *
 * History: the text-only #337 placement mode (`showIcon={false}`) and the
 * icon+text default were retired 2026-07-10 (CTO verdict, PR-1 of the
 * ansokningar audit) — the icon+text default never gained a consumer and the
 * last text-only instance converged on this convention. A future text-bearing
 * mode is reintroduced deliberately with a real consumer, not speculatively.
 */
export function InfoDialog({
  title,
  paragraphs,
  triggerClassName,
  ariaLabel,
}: {
  title: string;
  paragraphs: readonly string[];
  triggerClassName?: string;
  /**
   * Kontext-specifikt tillgängligt namn för triggern. Default =
   * `common.dialog.whatIsThis` ("Vad är detta?"). Sätt detta när flera "?"-
   * triggers samexisterar på samma yta (t.ex. per-kontroll-hjälp i en popover),
   * så skärmläsar-namnen inte kollapsar till samma generiska sträng (#419 pt7,
   * WCAG 2.4.4/2.5.3 — unika, kontext-bärande namn).
   */
  ariaLabel?: string;
}) {
  const t = useTranslations("common.dialog");
  const [first, ...rest] = paragraphs;

  // Padded square hit-area around the ~15px glyph; the accessible name comes
  // from aria-label since there is no visible text. The padded inline-flex
  // grows the hit-area on coarse pointers (≥44px touch) while the in-app
  // target clears the 32px floor (a11y skill §5).
  const defaultTriggerClassName =
    "inline-flex min-h-[36px] min-w-[36px] items-center justify-center rounded-sm p-1.5 text-text-secondary hover:text-text-primary [@media(pointer:coarse)]:min-h-[44px] [@media(pointer:coarse)]:min-w-[44px]";

  return (
    <Dialog>
      <DialogTrigger
        className={triggerClassName ?? defaultTriggerClassName}
        aria-label={ariaLabel ?? t("whatIsThis")}
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
          <div className="flex flex-col gap-3 text-body-sm leading-5 text-text-primary">
            {rest.map((paragraph) => (
              <p key={paragraph}>{paragraph}</p>
            ))}
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
