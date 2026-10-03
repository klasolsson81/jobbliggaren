"use client";

// "use client": reads sessionStorage after mount and follows the pathname — browser-only.

import { useEffect, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { STALE_BUILD_RELOADED_NOTICE_KEY } from "@/lib/stale-build/stale-build-reload";

/**
 * ReloadedAfterUpdateNotice — the one line a page shows after it reloaded
 * itself into a new build (ADR 0148 D7, design-reviewer Major 3(b) on #1948).
 *
 * A page from a previous build meets the current one at its next Server
 * Action; the boundary reloads the document once and the action never ran.
 * After the reload the user is on the same page in the new build with nothing
 * to tell them why their click did nothing and their input is gone. This line
 * says so, once: the reload left a stamp in `sessionStorage`
 * (`STALE_BUILD_RELOADED_NOTICE_KEY`), the line reads it after mount, removes
 * it, and shows the text on the page it was read on. The next navigation
 * clears it, because the text is about THIS page's reload (ADR 0047).
 *
 * The live region's container is in the DOM from the first paint and is
 * filled after the storage read, so assistive technology announces the
 * insertion; a manual reload shows nothing (the stamp is gone). Mounted in
 * every group layout that has a sibling `error.tsx` — `(admin)`, `(app)`,
 * `(auth)`, `(guest)/gast`, `(marketing)`, `(marketing-inner)` — at the top
 * of the content. It renders nothing while the guest mode is switched off.
 */
export function ReloadedAfterUpdateNotice() {
  const t = useTranslations("common");
  const pathname = usePathname();
  const read = useRef(false);
  // The pathname the line was read on; derived visibility (`shownOn === pathname`)
  // is what clears it on the next navigation — no state change on navigate.
  const [shownOn, setShownOn] = useState<string | null>(null);

  useEffect(() => {
    if (read.current) return;
    read.current = true;
    let stamp: string | null = null;
    try {
      stamp = window.sessionStorage.getItem(STALE_BUILD_RELOADED_NOTICE_KEY);
      if (stamp !== null) window.sessionStorage.removeItem(STALE_BUILD_RELOADED_NOTICE_KEY);
    } catch {
      stamp = null;
    }
    if (stamp === null) return;
    // Scheduled, the house form for a state change an effect must make
    // (react-hooks/set-state-in-effect).
    const id = setTimeout(() => setShownOn(pathname), 0);
    return () => clearTimeout(id);
  }, [pathname]);

  const visible = shownOn !== null && shownOn === pathname;

  return (
    <div role="status" aria-live="polite">
      {visible ? <p className="jp-banner">{t("reloadedAfterUpdate")}</p> : null}
    </div>
  );
}
