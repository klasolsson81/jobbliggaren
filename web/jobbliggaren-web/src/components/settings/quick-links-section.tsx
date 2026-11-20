"use client";

// "use client": the pending write, the error and the focus moves after a removal are state in the browser.

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildRecentSearchLabel, recentSearchLabelCopy } from "@/lib/job-ads/recent-search-label";
import { useCodedTaxonomyName } from "@/lib/i18n/use-coded-taxonomy-name";
import { QUICK_LINK_MAX, hasQuickLinkRoom } from "@/lib/job-ads/quick-links";
import { RemovalStatus, useRowRemoval } from "@/components/common/row-removal";
import { setJuniorQuickLinkAction, setRecentSearchQuickLinkAction } from "@/lib/actions/quick-links";

const JUNIOR_KEY = "junior";
const SECTION_TITLE_ID = "snabblankar-title";
const ADD_TITLE_ID = "snabblankar-add-title";

interface QuickLinksSectionProps {
  juniorEnabled: boolean;
  searches: ReadonlyArray<RecentJobSearchDto>;
  /** The page's h1, which takes focus when a list empties. It carries `tabIndex={-1}`. */
  headingId: string;
}

interface Entry {
  key: string;
  name: string;
}

/**
 * ADR 0188 (#2265) — the quick-link settings: the links the /jobb hero shows, each with a remove button, then what can
 * be added while there is room, the junior link and the unmarked recent searches. Every change revalidates this page,
 * so a row leaves one list and joins the other; each list follows DESIGN.md's removal rule for focus, and one status
 * region announces both directions.
 */
export function QuickLinksSection({ juniorEnabled, searches, headingId }: QuickLinksSectionProps) {
  const t = useTranslations("jobads.quickLinks");
  const tPages = useTranslations("pages");
  const tRecent = useTranslations("jobads.recent");
  const coded = useCodedTaxonomyName();
  const labelCopy = recentSearchLabelCopy(tRecent, coded);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const junior: Entry = { key: JUNIOR_KEY, name: t("junior") };
  const named = (search: RecentJobSearchDto): Entry => ({
    key: search.id,
    name: buildRecentSearchLabel(search.label, labelCopy),
  });
  const marked = searches.filter((search) => search.isQuickLink);
  const current: Entry[] = [...(juniorEnabled ? [junior] : []), ...marked.map(named)];
  const addable: Entry[] = [
    ...(juniorEnabled ? [] : [junior]),
    ...searches.filter((search) => !search.isQuickLink).map(named),
  ];
  const room = hasQuickLinkRoom(juniorEnabled, marked.length);

  const currentRemoval = useRowRemoval(
    current.map((entry) => entry.key),
    () => document.getElementById(headingId)
  );
  const addRemoval = useRowRemoval(
    room ? addable.map((entry) => entry.key) : [],
    () => document.getElementById(headingId)
  );

  // Enabled while pending, as the hero's ×: a `disabled` control with focus drops it to <body>.
  function change(entry: Entry, on: boolean) {
    if (pending) return;
    const removal = on ? addRemoval : currentRemoval;
    removal.started(entry.key);
    setError(null);
    startTransition(async () => {
      const result =
        entry.key === JUNIOR_KEY
          ? await setJuniorQuickLinkAction(on)
          : await setRecentSearchQuickLinkAction(entry.key, on);
      if (result.success) {
        currentRemoval.announce(t(on ? "added" : "removed", { label: entry.name }));
      } else {
        removal.failed(entry.key);
        setError(result.error);
      }
    });
  }

  return (
    <section className="jp-card" aria-labelledby={SECTION_TITLE_ID}>
      <h2 id={SECTION_TITLE_ID} className="jp-card__title">
        {tPages("minaSidor.sections.snabblankar")}
      </h2>
      <p className="text-body text-text-primary">{t("settings.lede")}</p>
      <RemovalStatus receipt={currentRemoval.receipt} />
      {error && (
        <div
          role="alert"
          className="mt-3 rounded-md border border-danger-600/30 bg-danger-50 px-4 py-3 text-body-sm text-danger-700"
        >
          {error}
        </div>
      )}

      <section className="jp-settings-group mt-4" aria-labelledby="snabblankar-current-title">
        <h3 id="snabblankar-current-title" className="jp-settings-group__title">
          {t("settings.currentHeading")}
        </h3>
        {current.length === 0 ? (
          <p className="text-body-sm text-text-primary">{t("settings.none")}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {current.map((entry) => (
              <li key={entry.key} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
                <span className="text-body text-text-primary">{entry.name}</span>
                <button
                  ref={currentRemoval.firstStopRef(entry.key)}
                  type="button"
                  className="jp-btn jp-btn--sm jp-btn--ghost"
                  aria-label={t("remove", { label: entry.name })}
                  onClick={() => change(entry, false)}
                  aria-disabled={pending || undefined}
                >
                  {t("settings.remove")}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="jp-settings-group" aria-labelledby={ADD_TITLE_ID}>
        <h3 id={ADD_TITLE_ID} className="jp-settings-group__title">
          {t("settings.addHeading")}
        </h3>
        {!room ? (
          <p className="text-body-sm text-text-primary">{t("full", { max: QUICK_LINK_MAX })}</p>
        ) : addable.length === 0 ? (
          <p className="text-body-sm text-text-primary">
            {searches.length === 0 ? t("settings.noSearches") : t("settings.allSearchesAdded")}
          </p>
        ) : (
          <ul className="flex flex-col gap-2">
            {addable.map((entry) => (
              <li key={entry.key} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
                <span className="text-body text-text-primary">{entry.name}</span>
                <button
                  ref={addRemoval.firstStopRef(entry.key)}
                  type="button"
                  className="jp-btn jp-btn--sm jp-btn--ghost"
                  aria-label={t("addNamed", { label: entry.name })}
                  onClick={() => change(entry, true)}
                  aria-disabled={pending || undefined}
                >
                  {t("settings.add")}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </section>
  );
}
