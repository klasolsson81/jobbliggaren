"use client";

// "use client": the pending removal, the links removed since the last server render and the focus moves are state in
// the browser.

import { useMemo, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildRecentSearchHref } from "@/lib/job-ads/recent-search-href";
import { buildRecentSearchLabel, recentSearchLabelCopy } from "@/lib/job-ads/recent-search-label";
import { useCodedTaxonomyName } from "@/lib/i18n/use-coded-taxonomy-name";
import { RemovalStatus, useRowRemoval } from "@/components/common/row-removal";
import { setJuniorQuickLinkAction, setRecentSearchQuickLinkAction } from "@/lib/actions/quick-links";

const JUNIOR_KEY = "junior";

interface JobbQuickLinksProps {
  /** The junior link's count, or null when the link is hidden: switched off, at 0, or after a failed read. */
  juniorCount: number | null;
  /** The recent searches marked as quick links, in the list's order. */
  searches: ReadonlyArray<RecentJobSearchDto>;
  /** The hero's h1, which takes focus when the last link is removed. It carries `tabIndex={-1}`. */
  headingId: string;
}

interface QuickLink {
  key: string;
  name: string;
  href: string;
  count: number | null;
}

/**
 * ADR 0188 (#2265) — the quick links under the line in the /jobb plate (DESIGN.md, the band clause for /jobb): the
 * junior IT link first, then the marked recent searches, each with an × that removes it at once (DESIGN.md's fourth
 * direct-removal exception: it comes back from Inställningar or from the search's row). Plain anchors, never
 * `next/link`, so nothing is prefetched: a prefetched `/junior` would spend the visitor's own budget there.
 */
export function JobbQuickLinks({ juniorCount, searches, headingId }: JobbQuickLinksProps) {
  const t = useTranslations("jobads.quickLinks");
  const tRecent = useTranslations("jobads.recent");
  const coded = useCodedTaxonomyName();
  const [removed, setRemoved] = useState<ReadonlySet<string>>(() => new Set());
  // A new server render is the truth again, so a link added back elsewhere (another tab, Inställningar) shows.
  const [renderedFor, setRenderedFor] = useState({ juniorCount, searches });
  if (renderedFor.juniorCount !== juniorCount || renderedFor.searches !== searches) {
    setRenderedFor({ juniorCount, searches });
    setRemoved(new Set());
  }
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  const links = useMemo(() => {
    const labelCopy = recentSearchLabelCopy(tRecent, coded);
    const all: QuickLink[] = [
      ...(juniorCount === null
        ? []
        : [{ key: JUNIOR_KEY, name: t("junior"), href: "/junior", count: juniorCount }]),
      ...searches.map((search) => ({
        key: search.id,
        name: buildRecentSearchLabel(search.label, labelCopy),
        href: buildRecentSearchHref(search),
        count: null,
      })),
    ];
    return all.filter((link) => !removed.has(link.key));
  }, [juniorCount, searches, removed, t, tRecent, coded]);

  const removal = useRowRemoval(
    links.map((link) => link.key),
    () => document.getElementById(headingId)
  );

  // The × stays enabled while the action runs: a `disabled` control with focus drops it to <body> (DESIGN.md, the
  // loading-state rule). This guard is what stops a second removal.
  function remove(link: QuickLink) {
    if (pending) return;
    removal.started(link.key);
    setError(null);
    startTransition(async () => {
      const result =
        link.key === JUNIOR_KEY
          ? await setJuniorQuickLinkAction(false)
          : await setRecentSearchQuickLinkAction(link.key, false);
      if (result.success) {
        removal.announce(t("removed", { label: link.name }));
        setRemoved((previous) => new Set(previous).add(link.key));
      } else {
        removal.failed(link.key);
        setError(result.error);
      }
    });
  }

  return (
    <div>
      <RemovalStatus receipt={removal.receipt} />
      {links.length > 0 && (
        <nav aria-label={t("navLabel")}>
          <ul className="jp-hero__quicklinks-list">
            {links.map((link) => (
              <li key={link.key} className="jp-hero__quicklink-item">
                <a
                  ref={removal.firstStopRef(link.key)}
                  href={link.href}
                  className="jp-hero__quicklink"
                  // Named in full, since a browser spaces a visually hidden comma apart from the text before it.
                  aria-label={link.count === null ? undefined : t("juniorName", { count: link.count })}
                >
                  {/* One box for the text: at ≤768 px the link is a flex box, which would trim the separator's spaces. */}
                  <span>
                    {link.name}
                    {link.count !== null && (
                      <>
                        <span aria-hidden="true"> · </span>
                        {t("juniorCount", { count: link.count })}
                      </>
                    )}
                  </span>
                </a>
                <button
                  type="button"
                  className="jp-hero__quicklink-remove"
                  aria-label={t("remove", { label: link.name })}
                  onClick={() => remove(link)}
                  aria-disabled={pending || undefined}
                >
                  <X size={16} aria-hidden="true" />
                </button>
              </li>
            ))}
          </ul>
        </nav>
      )}
      {error && (
        <p role="alert" className="jp-hero__quicklinks-error">
          {error}
        </p>
      )}
    </div>
  );
}
