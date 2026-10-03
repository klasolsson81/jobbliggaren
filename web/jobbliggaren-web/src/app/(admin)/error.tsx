"use client";

import type { ErrorInfo } from "next/error";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (admin)/error — the runtime error boundary for the admin surfaces.
 *
 * Klas's rule is about the FRAME, not about who is looking at it: "man ska
 * alltid se header, alltid se footer" (2026-08-23, #1477). (admin)/layout
 * already renders HeaderStrip + SiteFooter and owns `#main`, so this boundary
 * renders as its children and the chrome survives a throw — where before it
 * bubbled to global-error.tsx, which REPLACES the root layout.
 *
 * Client Component by Next convention. The `error` prop is read by
 * `useReloadOnStaleBuild` (ADR 0148: a page from a previous build reloads once
 * instead of this surface) and nowhere else — never shown to the user (no
 * stack trace), never logged here: Next reports uncaught errors on its own,
 * and console output is a §5 anti-pattern.
 */
export default function AdminError({ error, retry }: ErrorInfo) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();
  const reloading = useReloadOnStaleBuild(error);

  // After every hook (rules of hooks); the document is being replaced, so
  // nothing renders — no flash of the error surface before the reload.
  if (reloading) return null;

  return (
    <div className="flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div>
        <button
          type="button"
          onClick={() => retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
      </div>
    </div>
  );
}
