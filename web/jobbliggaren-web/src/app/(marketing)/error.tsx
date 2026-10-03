"use client";

import type { ErrorInfo } from "next/error";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (marketing)/error — the runtime error boundary for the landing route `/`.
 *
 * Without it a throw on the product's front door bubbled past every boundary to
 * global-error.tsx, which by Next convention REPLACES the root layout — no
 * header, no footer, no way back (#1477). The chrome lives in
 * (marketing)/layout.tsx, so this boundary renders as that layout's children
 * and the frame survives.
 *
 * It carries its OWN `<main id="main">`: the landmark is the PAGE's on this
 * surface, so without one here SiteHeader's skip link would point at nothing.
 *
 * It offers only a retry. `(marketing)` holds exactly one route, so a "to the
 * start page" control here would point at the URL the visitor is already on.
 *
 * Client Component by Next convention. The `error` prop is read by
 * `useReloadOnStaleBuild` (ADR 0148: a page from a previous build reloads once
 * instead of this surface) and nowhere else — never shown to the user (no
 * stack trace), never logged here: Next reports uncaught errors on its own,
 * and console output is a §5 anti-pattern.
 */
export default function MarketingError({ error, retry }: ErrorInfo) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();
  const reloading = useReloadOnStaleBuild(error);

  // After every hook (rules of hooks); the document is being replaced, so
  // nothing renders — no flash of the error surface before the reload.
  if (reloading) return null;

  return (
    // `w-full` below is load-bearing. <main> is a flex item, and a flex item with
    // auto inline margins — which is what `.jp-container` centres with — gets no
    // `stretch`, so without a definite width it collapses to fit-content and never
    // lands on the content rail (DESIGN.md). Same shape as the root not-found.
    <div className="flex flex-1 flex-col justify-center">
      <main
        id="main"
        tabIndex={-1}
        className="jp-container jp-page flex w-full flex-col gap-4"
      >
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
      </main>
    </div>
  );
}
