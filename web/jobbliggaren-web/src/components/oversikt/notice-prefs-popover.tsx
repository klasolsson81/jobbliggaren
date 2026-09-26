"use client";

import {
  Fragment,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type Ref,
} from "react";
import { useTranslations } from "next-intl";
import { Settings } from "lucide-react";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import type { NoticeSource } from "./notice-types";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";

export interface NoticePrefType {
  readonly id: string;
  readonly label: string;
}

export interface NoticePrefGroup {
  readonly source: NoticeSource;
  /** Shown above the group's rows. Omitted for a single-group popover, which needs no heading. */
  readonly title?: string;
  /** Types listed in the popover, including prepared types with no notices yet. */
  readonly types: ReadonlyArray<NoticePrefType>;
}

interface NoticePrefsPopoverProps {
  readonly groups: ReadonlyArray<NoticePrefGroup>;
  /**
   * Every notice the popover's "Återställ lästa notiser" foot may restore. The foot counts the
   * read ones among them and restores them all in one store write.
   */
  readonly notices: ReadonlyArray<{
    readonly id: string;
    readonly source: NoticeSource;
    readonly type: string;
  }>;
  /** The gear button — the stable sibling a caller's list moves focus to (see `useNoticeList`). */
  readonly ref?: Ref<HTMLButtonElement>;
}

/**
 * The notice-preferences gear and its popover: per-type on/off, plus a foot that restores the
 * read notices. Lifted out of `NoticeSection` (ADR 0140 Beslut 4) so `/oversikt` can carry ONE
 * gear in its toolbar row, listing all nine types grouped by source, while the section the guest
 * demo renders keeps the same markup with one group.
 *
 * The popover WRITES to the localStorage key `jp-oversikt-notice-prefs`, whose
 * `"<source>:<type>"` keys are shared with every surface that reads it — so a surface that must
 * not write (the guest demo, Klas-direktiv 2026-08-29) does not render this component at all
 * and wraps itself in `<InertNoticePrefsProvider>` instead. Both are needed: hiding the gear
 * alone leaves a surface filtered by a preference the visitor can neither see nor undo (CTO
 * ruling 2026-08-29).
 */
export function NoticePrefsPopover({ groups, notices, ref }: NoticePrefsPopoverProps) {
  const t = useTranslations("oversikt");
  const { dismissed, restoreMany } = useDismissedNotices();
  const { isEnabled, toggle } = useNoticePrefs();

  const [open, setOpen] = useState(false);
  const localGearRef = useRef<HTMLButtonElement>(null);
  const close = useCallback(() => setOpen(false), []);
  const panelRef = useDismissable<HTMLDivElement, HTMLButtonElement>(open, close, localGearRef);

  // The reset button unmounts when the read set empties → focus the panel's first checkbox
  // (WCAG 2.4.3). Ref flag + effect keyed on `dismissed`, for the reason `useNoticeList` gives.
  const pendingFocusRef = useRef(false);
  useEffect(() => {
    if (!pendingFocusRef.current) return;
    pendingFocusRef.current = false;
    panelRef.current?.querySelector("input")?.focus();
  }, [dismissed, panelRef]);

  const readIds = useMemo(
    () =>
      notices
        .filter((n) => isEnabled(n.source, n.type) && dismissed.has(n.id))
        .map((n) => n.id),
    [notices, isEnabled, dismissed],
  );

  // One write + one notification for the whole set (code-reviewer Minor 2, #726).
  const resetRead = useCallback(() => {
    pendingFocusRef.current = true;
    restoreMany(readIds);
  }, [readIds, restoreMany]);

  // Both refs must reach the same button: the caller's (focus target) and the local one
  // (outside-click dismissal).
  const setGearRef = useCallback(
    (el: HTMLButtonElement | null) => {
      localGearRef.current = el;
      if (typeof ref === "function") ref(el);
      else if (ref) ref.current = el;
    },
    [ref],
  );

  return (
    <div className="jp-notice-prefs-anchor">
      <button
        ref={setGearRef}
        type="button"
        className="jp-section__gear"
        aria-label={t("notices.settingsAria")}
        title={t("notices.settingsAria")}
        aria-haspopup="true"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        <Settings size={16} aria-hidden="true" />
      </button>
      {open && (
        <div
          ref={panelRef}
          className="jp-notice-prefs"
          role="group"
          aria-label={t("notices.settingsAria")}
        >
          <div className="jp-notice-prefs__heading">{t("notices.settingsHeading")}</div>
          {/* Rows stay direct children of the panel — one group renders exactly the markup
              NoticeSection rendered before the extraction; only a titled group adds a line. */}
          {groups.map((group) => (
            <Fragment key={group.source}>
              {group.title !== undefined && (
                <div className="jp-notice-prefs__grouptitle">{group.title}</div>
              )}
              {group.types.map((pt) => (
                <label key={pt.id} className="jp-notice-prefs__row">
                  <input
                    type="checkbox"
                    checked={isEnabled(group.source, pt.id)}
                    onChange={() => toggle(group.source, pt.id)}
                  />
                  <span>{pt.label}</span>
                </label>
              ))}
            </Fragment>
          ))}
          {readIds.length > 0 && (
            <div className="jp-notice-prefs__foot">
              <button
                type="button"
                className="jp-notice-prefs__reset"
                onClick={resetRead}
              >
                {t("notices.resetRead", { count: readIds.length })}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
