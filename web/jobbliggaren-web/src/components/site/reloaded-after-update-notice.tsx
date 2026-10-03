"use client";

// "use client": reads sessionStorage after mount and follows the pathname — browser-only.

import { useEffect, useState } from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { isV3Native } from "@/lib/layout/v3-native-routes";
import { STALE_BUILD_RELOADED_NOTICE_KEY } from "@/lib/stale-build/stale-build-reload";

/**
 * Where the layout mounts the line, which decides the rail and the gap (design-reviewer
 * Major 2 on #1955: the line stands on its mounting's own content edge, never on a rail
 * of its own):
 * - `rail`: on the 1136 px content rail, on the canvas band, above a page that starts with
 *   its own band — `.jp-pagehero` paints the same canvas and its 24 px inset is the gap
 *   below; the line brings the 24 px above. `(guest)/gast`, `(marketing-inner)`.
 * - `plate`: the same band and rail above a page that starts with an edge-to-edge plate
 *   with no inset of its own (the landing hero), so the line brings 24 px on both sides.
 *   `(marketing)`.
 * - `inline`: inside a column or `<main>` that already carries the rail and whose content
 *   follows directly, so the line brings the 24 px below. `(admin)`, `(auth)`.
 * - `app`: AppShell's children — `rail` on a v3-native route (full-width children on the
 *   shell's canvas; the page brings its own top inset, `.jp-pagehero` or `.jp-page`) and
 *   `inline` inside the transitional container, the same test AppShell makes (ADR 0052
 *   trail: collapses to `rail` when that container is retired).
 */
export type ReloadedAfterUpdateNoticePlacement = "rail" | "plate" | "inline" | "app";

type Line = { kind: "unread" } | { kind: "shown"; on: string } | { kind: "spent" };

/**
 * ReloadedAfterUpdateNotice — the one line a page shows after it reloaded
 * itself into a new build (ADR 0148 D7, design-reviewer Major 3(b) on #1948).
 *
 * A page from a previous build meets the current one at its next Server
 * Action; the boundary reloads the document once and the action never ran.
 * After the reload the user is on the same page in the new build with nothing
 * to tell them why their click did nothing and their input is gone. This line
 * says so, once: the reload left a stamp in `sessionStorage`
 * (`STALE_BUILD_RELOADED_NOTICE_KEY`), the line reads it once after mount,
 * removes it, and shows the text on the page it was read on. The first
 * navigation to another path retires it for good — a return to that page shows
 * no line, because the line is about THIS page's reload (ADR 0047).
 *
 * The live region's container is in the DOM from the first paint and is
 * filled after the storage read, so assistive technology announces the
 * insertion; a manual reload shows nothing (the stamp is gone). Mounted in
 * every group layout that has a sibling `error.tsx` — `(admin)`, `(app)`,
 * `(auth)`, `(guest)/gast`, `(marketing)`, `(marketing-inner)` — at the top
 * of the content.
 */
export function ReloadedAfterUpdateNotice({ placement }: { placement: ReloadedAfterUpdateNoticePlacement }) {
  const t = useTranslations("common");
  const pathname = usePathname();
  const [line, setLine] = useState<Line>({ kind: "unread" });

  // One-shot: the first navigation to another path retires the line for good, so a
  // return to the page it was shown on cannot announce a reload that did not happen.
  // React's render-phase form for state that follows a changed input (the month
  // picker in `activity-report-view.tsx`), not an effect: no extra commit after paint.
  if (line.kind === "shown" && line.on !== pathname) setLine({ kind: "spent" });

  useEffect(() => {
    if (line.kind !== "unread") return;
    // The whole read — get, remove, show — runs in the scheduled step and nothing in
    // the set-up. Under StrictMode (on in `next dev`) the set-up runs twice with the
    // clean-up between: a stamp consumed by a set-up whose scheduled show was then
    // cancelled is a line that never appears. The scheduled step is also the house
    // form for a state change an effect must make (react-hooks/set-state-in-effect).
    const id = setTimeout(() => {
      let stamp: string | null = null;
      try {
        stamp = window.sessionStorage.getItem(STALE_BUILD_RELOADED_NOTICE_KEY);
        if (stamp !== null) window.sessionStorage.removeItem(STALE_BUILD_RELOADED_NOTICE_KEY);
      } catch {
        stamp = null;
      }
      setLine(stamp === null ? { kind: "spent" } : { kind: "shown", on: pathname });
    }, 0);
    return () => clearTimeout(id);
  }, [line.kind, pathname]);

  const form = placement === "app" ? (isV3Native(pathname) ? "rail" : "inline") : placement;
  const visible = line.kind === "shown";

  return (
    // The live region is in the DOM from the first paint and empty until the read;
    // everything it paints — the canvas band, the rail, the gap — arrives with the
    // line, so the empty region has no height and no colour.
    <div role="status" aria-live="polite" className={visible && form !== "inline" ? "jp-banner-band" : undefined}>
      {visible ? (
        form === "inline" ? (
          <p className="jp-banner">{t("reloadedAfterUpdate")}</p>
        ) : (
          <div className="jp-container">
            <p className={form === "plate" ? "jp-banner jp-banner--before-plate" : "jp-banner jp-banner--before-band"}>
              {t("reloadedAfterUpdate")}
            </p>
          </div>
        )
      ) : null}
    </div>
  );
}
