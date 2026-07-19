"use client";

import { useEffect, useRef } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";
import type { SectionNoticeData } from "./notice-section";

interface NoticeToolbarProps {
  readonly lastUpdated: string;
  /** ALLA sektioners notiser — "Markera alla" avfärdar tvärs över sektionerna. */
  readonly notices: ReadonlyArray<SectionNoticeData>;
}

/**
 * Tunn sid-toolbar över notissektionerna (#726): "senast uppdaterad"-stämpel till
 * vänster, "Markera alla som lästa" till höger. Delar de två store-hookarna med
 * sektionerna så state hålls konsekvent. Knappen visas bara när minst en synlig,
 * avfärdbar notis finns (efter inställnings-filtrering) — annars vore den en no-op.
 */
export function NoticeToolbar({ lastUpdated, notices }: NoticeToolbarProps) {
  const t = useTranslations("oversikt");
  const { dismissed, dismissMany } = useDismissedNotices();
  const { isEnabled } = useNoticePrefs();

  const dismissibleVisible = notices.filter(
    (n) =>
      n.dismissible !== false &&
      isEnabled(n.source, n.type) &&
      !dismissed.has(n.id),
  );

  // WCAG 2.4.3 (design-reviewer Major, #726): "Markera alla" avmonterar sig
  // själv när inget avfärdbart återstår → utan förflyttning faller fokus till
  // <body>. Efter re-rendern flyttas fokus till första sektionens kugghjul
  // (stabilt — sektionerna döljs aldrig). Ref-flagga i stället för state:
  // klicket muterar dismiss-store:n → effekten (keyad på `dismissed`) körs
  // efter re-rendern; ref-nollning där är lint-säker
  // (react-hooks/set-state-in-effect).
  const moveFocusRef = useRef(false);
  useEffect(() => {
    if (!moveFocusRef.current) return;
    moveFocusRef.current = false;
    document.querySelector<HTMLButtonElement>(".jp-section__gear")?.focus();
  }, [dismissed]);

  return (
    <div className="jp-oversikt-toolbar">
      <span className="jp-oversikt-toolbar__stamp">
        {t.rich("notices.lastUpdated", {
          date: lastUpdated,
          mono: (chunks) => <span className="jp-mono">{chunks}</span>,
        })}
      </span>
      {dismissibleVisible.length > 0 && (
        <button
          type="button"
          className="jp-btn jp-btn--ghost jp-btn--sm"
          onClick={() => {
            moveFocusRef.current = true;
            dismissMany(dismissibleVisible.map((n) => n.id));
          }}
        >
          <Check size={14} aria-hidden="true" /> {t("notices.markAllRead")}
        </button>
      )}
    </div>
  );
}
