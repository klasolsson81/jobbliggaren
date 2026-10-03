"use client";

import type { ErrorInfo } from "next/error";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (app)/error — the signed-in app's runtime error boundary (#995 / B3). A
 * transient throw in any (app) page (/foretag, /jobb, /oversikt, …) is caught
 * here and rendered as the layout's children, so the shell, navigation and
 * theme stay intact and the user sees a calm civic surface (§10) instead of the
 * raw near-black Next overlay (dev) or an ungraceful blank (prod). No stack
 * trace is shown; `retry()` re-fetches and re-renders the segment
 * (the documented recovery for a transient throw in Next 16.3+ — `reset()`
 * only re-renders without re-fetching, so it would replay the same failed RSC
 * payload), and there is always a way back to the overview.
 *
 * Client Component by Next convention (error boundaries run on the client). The
 * `error` prop is read by `useReloadOnStaleBuild` (ADR 0148: a page from a
 * previous build reloads once instead of this surface) and nowhere else —
 * never shown to the user (no stack trace), never logged here: Next reports
 * uncaught errors on its own, and console output is a §5 anti-pattern.
 * Errors thrown in (app)/layout.tsx itself bubble PAST this
 * boundary to global-error.tsx (a segment's error.tsx cannot catch its own
 * layout).
 */
export default function AppError({ error, retry }: ErrorInfo) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();
  const reloading = useReloadOnStaleBuild(error);

  // After every hook (rules of hooks); the document is being replaced, so
  // nothing renders — no flash of the error surface before the reload.
  if (reloading) return null;

  return (
    // Mirrors (app)/not-found.tsx: jp-container jp-page assumes the errored
    // route is v3-native (/jobb, /foretag, … own their own width). A
    // non-v3-native route (/matchningar, /sparade, …) that throws
    // double-wraps (AppShell's transitional container + this one) — the same
    // accepted Minor not-found already carries; re-evaluate when the
    // transitional container is retired (ADR 0052).
    <div className="jp-container jp-page flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        <button
          type="button"
          onClick={() => retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
        <Link href="/oversikt" className="jp-btn jp-btn--secondary">
          {t("notFound.toOverview")}
        </Link>
      </div>
    </div>
  );
}
