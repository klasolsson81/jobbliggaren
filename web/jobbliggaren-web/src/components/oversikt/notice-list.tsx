"use client";

// ─────────────────────────────────────────────────────────────────────────────
// GUEST-ONLY (efter #726 notiscenter-refaktoreringen). Den autentiserade
// `/oversikt` bygger nu notiser per källa via `notice-section.tsx` +
// `notice-toolbar.tsx`. Den här platta "Kräver åtgärd / Information"-listan lever
// kvar ENBART för den publika gäst-förhandsvisningen
// (`components/guest/guest-oversikt-page.tsx`), som är en avsiktlig demo-yta.
// Dismiss-state delas numera via `use-dismissed-notices` (samma store som det
// autentiserade centret) så kunskapen bor på en plats. Observerbart beteende är
// oförändrat — gäst-testerna förblir gröna.
// ─────────────────────────────────────────────────────────────────────────────

import { useCallback, useMemo } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { NoticeRow, type NoticeData } from "./notice-row";
import { useDismissedNotices } from "./use-dismissed-notices";

interface NoticeListProps {
  readonly actionNotices: ReadonlyArray<NoticeData>;
  readonly infoNotices: ReadonlyArray<NoticeData>;
  readonly lastUpdated: string;
}

export function NoticeList({
  actionNotices,
  infoNotices,
  lastUpdated,
}: NoticeListProps) {
  const t = useTranslations("oversikt");
  const { dismissed, dismiss, dismissMany } = useDismissedNotices();

  // En icke-avfärdbar notis är ALLTID synlig (filtreras aldrig av dismissed-
  // mängden); en avfärdbar göms när dess id finns i dismissed.
  const isVisible = useCallback(
    (n: NoticeData) => n.dismissible === false || !dismissed.has(n.id),
    [dismissed],
  );

  const visibleAction = useMemo(
    () => actionNotices.filter(isVisible),
    [actionNotices, isVisible],
  );
  const visibleInfo = useMemo(
    () => infoNotices.filter(isVisible),
    [infoNotices, isVisible],
  );
  const visibleCount = visibleAction.length + visibleInfo.length;

  // "Markera alla som lästa" visas bara när minst en SYNLIG notis faktiskt går
  // att avfärda — annars vore knappen en no-op (t.ex. när bara den persistenta
  // setup-nudgen finns kvar).
  const hasDismissibleVisible =
    visibleAction.some((n) => n.dismissible !== false) ||
    visibleInfo.some((n) => n.dismissible !== false);

  const dismissAll = useCallback(() => {
    // F4-12 PR-B (ADR 0076): icke-avfärdbara notiser (setup-nudgen) exkluderas
    // ur id-insamlingen så "Markera alla som lästa" lämnar dem synliga.
    const ids = [...actionNotices, ...infoNotices]
      .filter((n) => n.dismissible !== false)
      .map((n) => n.id);
    dismissMany(ids);
  }, [actionNotices, infoNotices, dismissMany]);

  return (
    <section className="jp-section" aria-labelledby="oversikt-notiser">
      <div className="jp-section__head">
        <h2 className="jp-section__title" id="oversikt-notiser">
          {t("notices.title")}
        </h2>
        <span className="jp-section__count">
          {t.rich("notices.lastUpdated", {
            date: lastUpdated,
            mono: (chunks) => <span className="jp-mono">{chunks}</span>,
          })}
        </span>
        <span style={{ flex: 1 }} />
        {hasDismissibleVisible && (
          <button
            type="button"
            className="jp-btn jp-btn--ghost jp-btn--sm"
            onClick={dismissAll}
          >
            <Check size={14} aria-hidden="true" /> {t("notices.markAllRead")}
          </button>
        )}
      </div>

      {visibleCount === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">{t("notices.emptyTitle")}</div>
          {t("notices.emptyBody")}
        </div>
      ) : (
        <>
          {visibleAction.length > 0 && (
            <>
              <div className="jp-notice-group">
                <span className="jp-notice-group__title">
                  {t("notices.groupAction")}
                </span>
                <span className="jp-notice-group__count">
                  {visibleAction.length}
                </span>
              </div>
              <ul className="jp-notice-list">
                {visibleAction.map((n) => (
                  <NoticeRow key={n.id} notice={n} onDismiss={dismiss} />
                ))}
              </ul>
            </>
          )}
          {visibleInfo.length > 0 && (
            <>
              <div className="jp-notice-group jp-notice-group--info">
                <span className="jp-notice-group__title">
                  {t("notices.groupInfo")}
                </span>
                <span className="jp-notice-group__count">
                  {visibleInfo.length}
                </span>
              </div>
              <ul className="jp-notice-list">
                {visibleInfo.map((n) => (
                  <NoticeRow key={n.id} notice={n} onDismiss={dismiss} />
                ))}
              </ul>
            </>
          )}
        </>
      )}
    </section>
  );
}
