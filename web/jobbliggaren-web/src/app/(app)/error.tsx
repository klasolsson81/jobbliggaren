"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";

/**
 * (app)/error — the signed-in app's runtime error boundary (#995 / B3). A
 * transient throw in any (app) page (/foretag, /jobb, /oversikt, …) is caught
 * here and rendered as the layout's children, so the shell, navigation and
 * theme stay intact and the user sees a calm civic surface (§10) instead of the
 * raw near-black Next overlay (dev) or an ungraceful blank (prod). No stack
 * trace is shown; `reset()` re-renders the segment to retry, and there is
 * always a way back to the overview.
 *
 * Client Component by Next convention (error boundaries run on the client). The
 * `error` prop is accepted to match Next's boundary contract but deliberately
 * neither surfaced to the user (acceptance: no stack trace) nor logged here —
 * Next reports uncaught errors on its own, and console output is a §5
 * anti-pattern. Errors thrown in (app)/layout.tsx itself bubble PAST this
 * boundary to global-error.tsx (a segment's error.tsx cannot catch its own
 * layout).
 */
export default function AppError({
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  const t = useTranslations("pages");

  return (
    // Mirrors (app)/not-found.tsx: jp-container jp-page assumes the errored
    // route is v3-native (/jobb, /foretag, … own their own width). A
    // non-v3-native route (/installningar, /matchningar, …) that throws
    // double-wraps (AppShell's transitional container + this one) — the same
    // accepted Minor not-found already carries; re-evaluate when the
    // transitional container is retired (ADR 0052).
    <div className="jp-container jp-page flex flex-col gap-4">
      <h1 className="jp-h1">{t("common.errorTitle")}</h1>
      <p className="jp-lede">{t("common.errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        <button type="button" onClick={reset} className="jp-btn jp-btn--primary">
          {t("common.retry")}
        </button>
        <Link href="/oversikt" className="jp-btn jp-btn--secondary">
          {t("common.notFound.toOverview")}
        </Link>
      </div>
    </div>
  );
}
