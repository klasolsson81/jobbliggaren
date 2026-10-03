"use client";

import type { ErrorInfo } from "next/error";
import { NextIntlClientProvider, useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";
import { documentFontClassName } from "./fonts";
import svFallback from "../../messages/sv/fallback.json";
import svMetadata from "../../messages/sv/metadata.json";
// global-error REPLACES the root layout (it renders its own <html>/<body>), so
// the root layout's globals.css import no longer applies — re-import it here or
// the civic tokens/utilities (jp-container, jp-btn, surface colours) render
// unstyled. The same goes for next/font's variables, which the root layout sets
// on ITS <html>: this document sets them on its own (`./fonts`), or `--font-sans`
// resolves through an unset variable and the whole surface falls to the
// browser's default serif (design-reviewer on PR #1953, measured 2026-10-03).
import "./globals.css";

/**
 * global-error — the site's last-resort boundary (#995 / B3). It fires only
 * when the ROOT layout itself throws (or an error escapes (app)/error.tsx via
 * (app)/layout), so it must render its own document shell. The user sees a calm
 * civic surface (§10) with a retry and a way to the start page — no stack
 * trace, no danger-alarm styling for a generic failure.
 *
 * It is also the surface a Server Action from the header reaches — "Logga ut"
 * sits in AppShell, inside (app)/layout — so a page from a previous build meets
 * this boundary first when the web image has been replaced (#1948, ADR 0148).
 * For that one error class the document is reloaded once instead: the shell
 * below renders with no surface, keeping the language, the body's surface
 * colour and the site's own title, so nothing flashes before the reload.
 *
 * i18n: because this replaces the root layout it renders OUTSIDE
 * NextIntlClientProvider, so it seeds its own provider from the Swedish catalog
 * (the canonical locale — ADR 0078; English is a secondary convenience). Locale
 * is pinned to "sv" rather than read from the NEXT_LOCALE cookie: this boundary
 * can render during SSR of a crashing root layout, and a fixed locale keeps SSR
 * and hydration identical (no mismatch) for a surface that should essentially
 * never appear. Copy still lives in messages/sv (§5 — no hardcoded UI strings).
 *
 * Theme-aware: the app is light-only in the MVP (DARK_MODE_ENABLED = false), so
 * no ThemeScript is needed here; the civic light tokens resolve directly.
 */
function GlobalErrorSurface({ retry }: Pick<ErrorInfo, "retry">) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();

  return (
    // min-h-[60vh] + justify-center mirrors the root not-found: this renders
    // chrome-less (no header/footer), so the copy needs a height floor rather
    // than gluing to the top edge. Layout utility, not a locked design token.
    <main className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        {/* retry() re-fetches and re-renders (the documented Next 16.3+
            recovery for a transient throw); reset() would only re-render without
            re-fetching. */}
        <button
          type="button"
          onClick={() => retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
        {/* A plain <a> (not next/link) on purpose: global-error replaces the
            root layout, so the App Router context next/link needs is not
            guaranteed here, and a full-document navigation is the robust
            recovery from a catastrophic crash (a soft Link nav would stay inside
            the broken client runtime). */}
        {/* eslint-disable-next-line @next/next/no-html-link-for-pages */}
        <a href="/" className="jp-btn jp-btn--secondary">
          {t("notFound.toStart")}
        </a>
      </div>
    </main>
  );
}

const bodyClassName = "min-h-full bg-surface-primary text-text-primary antialiased";

export default function GlobalError({ error, retry }: ErrorInfo) {
  const reloading = useReloadOnStaleBuild(error);

  if (reloading) {
    return (
      <html lang="sv" className={documentFontClassName}>
        <head>
          <title>{svMetadata.titleDefault}</title>
        </head>
        <body className={bodyClassName} />
      </html>
    );
  }

  return (
    <html lang="sv" className={documentFontClassName}>
      {/* global-error replaces the root layout, so Next's metadata /
          generateMetadata does not apply — including the root layout's
          `title.template`. Compose it here from the same sv-pinned catalog so
          the tab carries the site name like every other page, rather than a
          stale title or a title with no site name in it. */}
      <head>
        <title>{svMetadata.titleTemplate.replace("%s", svFallback.errorTitle)}</title>
      </head>
      <body className={bodyClassName}>
        <NextIntlClientProvider locale="sv" messages={{ fallback: svFallback }}>
          <GlobalErrorSurface retry={retry} />
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
