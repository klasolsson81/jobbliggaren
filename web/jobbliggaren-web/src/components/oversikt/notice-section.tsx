"use client";

import { useCallback, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Settings } from "lucide-react";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { NoticeRow, type NoticeData, type NoticeKind } from "./notice-row";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";

export type NoticeSource = "applications" | "jobads" | "companies";

/**
 * En notis i en källsektion. Utökar `NoticeData` med `source` + `type` för
 * inställnings-filtrering och "markera alla"-omfattning (#726).
 */
export interface SectionNoticeData extends NoticeData {
  readonly source: NoticeSource;
  readonly type: string;
}

export interface NoticePrefType {
  readonly id: string;
  readonly label: string;
}

interface NoticeSectionProps {
  readonly source: NoticeSource;
  readonly titleId: string;
  readonly title: string;
  readonly notices: ReadonlyArray<SectionNoticeData>;
  /** Underrad i tomt-läget — vad sektionen kommer att visa. */
  readonly emptyBody: string;
  /** Typer som listas i kugghjuls-popovern (inkl. förberedda typer utan notiser). */
  readonly prefTypes: ReadonlyArray<NoticePrefType>;
}

// Åtgärdsnotiser (warning/success) sorteras först, info/brand därefter — övrigt
// bevarar konstruktionsordningen (Array.prototype.sort är stabil sedan ES2019).
const ACTION_KINDS: ReadonlySet<NoticeKind> = new Set<NoticeKind>([
  "warning",
  "success",
]);

function actionFirst(
  notices: ReadonlyArray<SectionNoticeData>,
): SectionNoticeData[] {
  return [...notices].sort(
    (a, b) =>
      (ACTION_KINDS.has(a.kind) ? 0 : 1) - (ACTION_KINDS.has(b.kind) ? 0 : 1),
  );
}

/**
 * En notissektion per källa (Mina ansökningar / Jobbannonser / Företagsbevakning).
 * Client Component — äger läst-läge (dismiss/restore via delad store),
 * inställnings-popover (per-typ på/av) och "visa lästa"-toggeln. Sektionen döljs
 * aldrig: saknar den synliga olästa notiser visas ett tomt-läge i listkortet.
 */
export function NoticeSection({
  source,
  titleId,
  title,
  notices,
  emptyBody,
  prefTypes,
}: NoticeSectionProps) {
  const t = useTranslations("oversikt");
  const { dismissed, dismiss, restore } = useDismissedNotices();
  const { isEnabled, toggle } = useNoticePrefs();

  const [showRead, setShowRead] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const gearRef = useRef<HTMLButtonElement>(null);
  const closeSettings = useCallback(() => setSettingsOpen(false), []);
  const panelRef = useDismissable<HTMLDivElement, HTMLButtonElement>(
    settingsOpen,
    closeSettings,
    gearRef,
  );

  // Avbockad typ filtreras bort helt (räknas inte i "N olästa" heller).
  const enabled = useMemo(
    () => notices.filter((n) => isEnabled(source, n.type)),
    [notices, isEnabled, source],
  );
  const unread = useMemo(
    () => actionFirst(enabled.filter((n) => !dismissed.has(n.id))),
    [enabled, dismissed],
  );
  const read = useMemo(
    () => actionFirst(enabled.filter((n) => dismissed.has(n.id))),
    [enabled, dismissed],
  );

  const resetRead = useCallback(() => {
    for (const n of read) restore(n.id);
  }, [read, restore]);

  return (
    <section className="jp-section" aria-labelledby={titleId}>
      <div className="jp-section__head">
        <h2 className="jp-section__title" id={titleId}>
          {title}
        </h2>
        <span className="jp-section__count">
          {t("notices.unreadCount", { count: unread.length })}
        </span>
        <span style={{ flex: 1 }} />
        <div className="jp-notice-prefs-anchor">
          <button
            ref={gearRef}
            type="button"
            className="jp-section__gear"
            aria-label={t("notices.settingsAria")}
            title={t("notices.settingsAria")}
            aria-haspopup="true"
            aria-expanded={settingsOpen}
            onClick={() => setSettingsOpen((v) => !v)}
          >
            <Settings size={16} aria-hidden="true" />
          </button>
          {settingsOpen && (
            <div
              ref={panelRef}
              className="jp-notice-prefs"
              role="group"
              aria-label={t("notices.settingsAria")}
            >
              <div className="jp-notice-prefs__heading">
                {t("notices.settingsHeading")}
              </div>
              {prefTypes.map((pt) => (
                <label key={pt.id} className="jp-notice-prefs__row">
                  <input
                    type="checkbox"
                    checked={isEnabled(source, pt.id)}
                    onChange={() => toggle(source, pt.id)}
                  />
                  <span>{pt.label}</span>
                </label>
              ))}
              {read.length > 0 && (
                <div className="jp-notice-prefs__foot">
                  <button
                    type="button"
                    className="jp-notice-prefs__reset"
                    onClick={resetRead}
                  >
                    {t("notices.resetRead", { count: read.length })}
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      <ul className="jp-notice-list">
        {unread.length > 0 ? (
          unread.map((n) => (
            <NoticeRow key={n.id} notice={n} onDismiss={dismiss} />
          ))
        ) : (
          <li className="jp-notice-empty">
            <div className="jp-notice-empty__title">
              {t("notices.emptySectionTitle")}
            </div>
            <div className="jp-notice-empty__body">{emptyBody}</div>
          </li>
        )}
        {showRead &&
          read.map((n) => (
            <NoticeRow key={n.id} notice={n} read onRestore={restore} />
          ))}
        {read.length > 0 && (
          <li className="jp-notice-foot">
            <span className="jp-notice-foot__count">
              {t("notices.readCount", { count: read.length })}
            </span>
            <button
              type="button"
              className="jp-notice-foot__toggle"
              aria-expanded={showRead}
              onClick={() => setShowRead((v) => !v)}
            >
              {showRead ? t("notices.hideRead") : t("notices.showRead")}
            </button>
          </li>
        )}
      </ul>
    </section>
  );
}
