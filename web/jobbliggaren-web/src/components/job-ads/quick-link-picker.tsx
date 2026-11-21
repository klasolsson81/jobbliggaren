"use client";

// "use client": the ticks shown before the server answers, the pending writes and the error are state in the browser.

import { useId, useOptimistic, useRef, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import type { ActionResult } from "@/lib/actions/_action-result";
import { buildRecentSearchLabel, recentSearchLabelCopy } from "@/lib/job-ads/recent-search-label";
import { useCodedTaxonomyName } from "@/lib/i18n/use-coded-taxonomy-name";
import { QUICK_LINK_MAX, hasQuickLinkRoom } from "@/lib/job-ads/quick-links";
import { setJuniorQuickLinkAction, setRecentSearchQuickLinkAction } from "@/lib/actions/quick-links";

const JUNIOR_KEY = "junior";

interface QuickLinkPickerProps {
  juniorEnabled: boolean;
  /** The user's recent searches, in the list's order. */
  searches: ReadonlyArray<RecentJobSearchDto>;
  /** How many of the searches that are not quick links to offer; all when omitted. Marked ones are always listed. */
  unmarkedLimit?: number;
  /** What the list says when there is no search to pick. */
  emptyNote: string;
  /** Pulls the rows out to the text line of the card around the list. */
  flush?: boolean;
}

interface Row {
  key: string;
  name: string;
}

/**
 * ADR 0188 amendment (#2275) — one tick-box per possible quick link: the junior link, then the searches that are
 * quick links, then the others. A tick adds a link and an untick removes it. The rows are laid out once, as the list
 * mounts: a tick re-renders the page, and a row that moved or left the list would take the focus with it. A refused
 * write unticks the box again and says why. At the cap the unticked boxes are unavailable.
 */
export function QuickLinkPicker({ juniorEnabled, searches, unmarkedLimit, emptyNote, flush }: QuickLinkPickerProps) {
  const t = useTranslations("jobads.quickLinks");
  const tRecent = useTranslations("jobads.recent");
  const coded = useCodedTaxonomyName();
  const fullId = useId();
  const [error, setError] = useState<string | null>(null);
  const [, startTransition] = useTransition();
  const writes = useRef<Promise<ActionResult>>(Promise.resolve({ success: true }));

  const [rows] = useState<ReadonlyArray<Row>>(() => {
    const marked = searches.filter((search) => search.isQuickLink);
    const unmarked = searches.filter((search) => !search.isQuickLink);
    const offered = unmarkedLimit === undefined ? unmarked : unmarked.slice(0, unmarkedLimit);
    const labelCopy = recentSearchLabelCopy(tRecent, coded);
    return [
      { key: JUNIOR_KEY, name: t("junior") },
      ...[...marked, ...offered].map((search) => ({
        key: search.id,
        name: buildRecentSearchLabel(search.label, labelCopy),
      })),
    ];
  });

  // What the server last said, for every search and not only the rows: the cap counts a link marked in another tab.
  const [ticked, tick] = useOptimistic(
    new Map<string, boolean>([
      [JUNIOR_KEY, juniorEnabled],
      ...searches.map((search): [string, boolean] => [search.id, search.isQuickLink]),
    ]),
    (state, change: { key: string; on: boolean }) => new Map(state).set(change.key, change.on),
  );
  const markedCount = [...ticked].filter(([key, on]) => key !== JUNIOR_KEY && on).length;
  const room = hasQuickLinkRoom(ticked.get(JUNIOR_KEY) === true, markedCount);

  function toggle(row: Row) {
    const on = !ticked.get(row.key);
    setError(null);
    startTransition(async () => {
      tick({ key: row.key, on });
      const write = () =>
        row.key === JUNIOR_KEY ? setJuniorQuickLinkAction(on) : setRecentSearchQuickLinkAction(row.key, on);
      const result = writes.current.then(write, write);
      writes.current = result;
      const answer = await result;
      if (!answer.success) setError(answer.error);
    });
  }

  return (
    <fieldset className={flush ? "jp-quicklink-picker jp-quicklink-picker--flush" : "jp-quicklink-picker"}>
      <legend className="sr-only">{t("picker.legend", { max: QUICK_LINK_MAX })}</legend>
      {rows.map((row) => {
        const checked = ticked.get(row.key) === true;
        const unavailable = !checked && !room;
        return (
          <label key={row.key} className="jp-quicklink-picker__row">
            <input
              type="checkbox"
              checked={checked}
              disabled={unavailable}
              aria-describedby={unavailable ? fullId : undefined}
              onChange={() => toggle(row)}
            />
            <span>{row.name}</span>
          </label>
        );
      })}
      {rows.length === 1 && <p className="jp-quicklink-picker__note">{emptyNote}</p>}
      <div role="status">
        {!room && (
          <p id={fullId} className="jp-quicklink-picker__note">
            {t("picker.full", { max: QUICK_LINK_MAX })}
          </p>
        )}
      </div>
      {error && (
        <p role="alert" className="jp-quicklink-picker__error">
          {error}
        </p>
      )}
    </fieldset>
  );
}
