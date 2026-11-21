"use client";

// "use client": the pending removal, the links removed since the last server render, the open settings list and the
// focus moves are state in the browser.

import { useMemo, useRef, useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Settings2, X } from "lucide-react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildRecentSearchHref } from "@/lib/job-ads/recent-search-href";
import { buildRecentSearchLabel, recentSearchLabelCopy } from "@/lib/job-ads/recent-search-label";
import { JUNIOR_ON_VALUE } from "@/lib/job-ads/search-params";
import { useCodedTaxonomyName } from "@/lib/i18n/use-coded-taxonomy-name";
import { RemovalStatus, useRowRemoval } from "@/components/common/row-removal";
import { PopoverBand, PopoverShell } from "@/components/popover/popover-shell";
import { QuickLinkPicker } from "@/components/job-ads/quick-link-picker";
import { setJuniorQuickLinkAction, setRecentSearchQuickLinkAction } from "@/lib/actions/quick-links";

const JUNIOR_KEY = "junior";
const JUNIOR_HREF = `/jobb?junior=${JUNIOR_ON_VALUE}`;
const PICKER_UNMARKED_LIMIT = 6;

interface JobbQuickLinksProps {
  /** The junior link's count, or null when the link is hidden: switched off, at 0, or after a failed read. */
  juniorCount: number | null;
  /** Whether the junior link is on, or null when the profile could not be read; then no settings list is offered. */
  juniorEnabled: boolean | null;
  /** The user's recent searches, in the list's order; the marked ones are the links. */
  searches: ReadonlyArray<RecentJobSearchDto>;
  /** The hero's h1, the last fallback for focus after a removal. It carries `tabIndex={-1}`. */
  headingId: string;
}

interface QuickLink {
  key: string;
  name: string;
  href: string;
  count: number | null;
}

/**
 * ADR 0188 (#2265, amended for #2275) — the quick links under the line in the /jobb plate (DESIGN.md, the band clause
 * for /jobb): the junior IT link first, then the marked recent searches, each with an × that removes it at once, and
 * last the "Anpassa" button that opens the list to pick them from. Every link stays on /jobb, the junior one as its
 * junior filter, so they navigate softly and are never prefetched.
 */
export function JobbQuickLinks({ juniorCount, juniorEnabled, searches, headingId }: JobbQuickLinksProps) {
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
  const [settingsOpen, setSettingsOpen] = useState(false);
  const settingsRef = useRef<HTMLButtonElement>(null);

  const links = useMemo(() => {
    const labelCopy = recentSearchLabelCopy(tRecent, coded);
    const all: QuickLink[] = [
      ...(juniorCount === null
        ? []
        : [{ key: JUNIOR_KEY, name: t("junior"), href: JUNIOR_HREF, count: juniorCount }]),
      ...searches
        .filter((search) => search.isQuickLink)
        .map((search) => ({
          key: search.id,
          name: buildRecentSearchLabel(search.label, labelCopy),
          href: buildRecentSearchHref(search),
          count: null,
        })),
    ];
    return all.filter((link) => !removed.has(link.key));
  }, [juniorCount, searches, removed, t, tRecent, coded]);

  // After the last link, focus goes to the control that adds them (DESIGN.md's removal rule, as for Matchning's chips).
  const removal = useRowRemoval(
    links.map((link) => link.key),
    () => settingsRef.current ?? document.getElementById(headingId)
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
      {(links.length > 0 || juniorEnabled !== null) && (
        <nav aria-label={t("navLabel")}>
          <ul className="jp-hero__quicklinks-list">
            {links.map((link) => (
              <li key={link.key} className="jp-hero__quicklink-item">
                <Link
                  ref={removal.firstStopRef(link.key)}
                  href={link.href}
                  prefetch={false}
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
                </Link>
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
            {juniorEnabled !== null && (
              <li className="jp-hero__quicklink-item">
                <button
                  ref={settingsRef}
                  type="button"
                  className="jp-hero__quicklinks-settings"
                  aria-label={links.length > 0 ? t("settings.openName") : undefined}
                  aria-haspopup="dialog"
                  aria-expanded={settingsOpen}
                  onClick={() => setSettingsOpen((open) => !open)}
                >
                  <Settings2 size={16} aria-hidden="true" />
                  {links.length > 0 ? t("settings.open") : t("settings.openEmpty")}
                </button>
                <PopoverShell
                  open={settingsOpen}
                  onClose={() => setSettingsOpen(false)}
                  triggerRef={settingsRef}
                  preferredWidth={320}
                  label={t("navLabel")}
                  band={({ titleId, closeButton }) => (
                    <PopoverBand titleId={titleId} label={t("navLabel")} closeButton={closeButton} />
                  )}
                >
                  <QuickLinkPicker
                    juniorEnabled={juniorEnabled}
                    searches={searches}
                    unmarkedLimit={PICKER_UNMARKED_LIMIT}
                  />
                  <div className="jp-popover__foot">
                    <Link href="/mina-sidor/snabblankar" prefetch={false} className="jp-popover__footlink">
                      {t("picker.all")}
                    </Link>
                  </div>
                </PopoverShell>
              </li>
            )}
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
