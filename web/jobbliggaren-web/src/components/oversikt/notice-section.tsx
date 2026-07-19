"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Settings } from "lucide-react";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { NoticeRow, type NoticeData, type NoticeKind } from "./notice-row";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";

export type NoticeSource = "applications" | "jobads" | "companies";

/**
 * SSOT för notis-typerna per källa (code-reviewer Minor 1, #726): notis-
 * konstruktionen (`SectionNoticeData.type`), kugghjuls-popoverns rader och
 * pref-nycklarna `"<source>:<type>"` läser ALLA denna tabell — en felstavad
 * typ-slug blir ett kompileringsfel i stället för en tyst trasig filtrering.
 * Typer utan notis ännu ("statuschanges", "companyevents") är förberedda
 * popover-val per handoffen.
 */
export const NOTICE_TYPES = {
  applications: ["followup", "interviews", "offers", "statuschanges"],
  jobads: ["deadlines", "matches", "latestsearch"],
  companies: ["followedads", "companyevents"],
} as const satisfies Record<NoticeSource, ReadonlyArray<string>>;

export type NoticeType<S extends NoticeSource = NoticeSource> =
  (typeof NOTICE_TYPES)[S][number];

/**
 * En notis i en källsektion. Utökar `NoticeData` med `source` + `type` för
 * inställnings-filtrering och "markera alla"-omfattning (#726). Mappad union:
 * `type` måste tillhöra just sin `source` (compile-time-länken till
 * {@link NOTICE_TYPES}).
 */
export type SectionNoticeData = {
  [S in NoticeSource]: NoticeData & {
    readonly source: S;
    readonly type: NoticeType<S>;
  };
}[NoticeSource];

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
  const { dismissed, dismiss, restore, restoreMany } = useDismissedNotices();
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

  // WCAG 2.4.3 (design-reviewer Major, #726): dismiss/restore/återställ-alla
  // avmonterar elementet som bar fokus → utan programmatisk förflyttning faller
  // fokus till <body> och en tangentbords-/SR-användare tappar sin plats.
  // Efter re-rendern flyttas fokus till ett stabilt syskon: läst-fotens toggle
  // ("Visa"/"Dölj"), popover-panelens första kryssruta, eller kugghjulet.
  // Ref-flagga (inte state): varje fokus-relevant handling muterar dismiss-
  // store:n, så effekten nedan (keyad på `dismissed`) körs garanterat efter
  // re-rendern — och en ref-nollning i effekten är lint-säker
  // (react-hooks/set-state-in-effect förbjuder setState där).
  const footToggleRef = useRef<HTMLButtonElement>(null);
  const pendingFocusRef = useRef<"foot" | "panel" | "gear" | null>(null);
  useEffect(() => {
    const target = pendingFocusRef.current;
    if (target === null) return;
    pendingFocusRef.current = null;
    if (target === "foot" && footToggleRef.current) {
      footToggleRef.current.focus();
    } else if (target === "panel" && panelRef.current) {
      panelRef.current.querySelector("input")?.focus();
    } else {
      gearRef.current?.focus();
    }
  }, [dismissed, panelRef]);

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

  // Läst-fotens toggle finns alltid direkt efter en dismiss (read ≥ 1).
  const handleDismiss = useCallback(
    (id: string) => {
      pendingFocusRef.current = "foot";
      dismiss(id);
    },
    [dismiss],
  );

  // Efter restore av SISTA lästa raden avmonteras även foten → kugghjulet.
  const handleRestore = useCallback(
    (id: string) => {
      pendingFocusRef.current = read.length > 1 ? "foot" : "gear";
      restore(id);
    },
    [restore, read.length],
  );

  // En skrivning + en notifiering för hela sektionen (code-reviewer Minor 2).
  // Reset-knappen avmonteras när read töms → fokus till panelens första kryssruta.
  const resetRead = useCallback(() => {
    pendingFocusRef.current = "panel";
    restoreMany(read.map((n) => n.id));
  }, [read, restoreMany]);

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
            <NoticeRow key={n.id} notice={n} onDismiss={handleDismiss} />
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
            <NoticeRow key={n.id} notice={n} read onRestore={handleRestore} />
          ))}
        {read.length > 0 && (
          <li className="jp-notice-foot">
            <span className="jp-notice-foot__count">
              {t("notices.readCount", { count: read.length })}
            </span>
            <button
              ref={footToggleRef}
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
