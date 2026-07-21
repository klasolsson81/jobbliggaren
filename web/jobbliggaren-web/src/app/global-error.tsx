"use client";

import { NextIntlClientProvider, useTranslations } from "next-intl";
import svPages from "../../messages/sv/pages.json";
// global-error REPLACES the root layout (it renders its own <html>/<body>), so
// the root layout's globals.css import no longer applies — re-import it here or
// the civic tokens/utilities (jp-container, jp-btn, surface colours) render
// unstyled. Fonts fall back to the system stack baked into --jp-font-sans /
// --font-sans (globals.css) since next/font's variable is only set on the root
// layout's <html>; acceptable for the catastrophic last-resort surface.
import "./globals.css";

/**
 * global-error — the site's last-resort boundary (#995 / B3). It fires only
 * when the ROOT layout itself throws (or an error escapes (app)/error.tsx via
 * (app)/layout), so it must render its own document shell. The user sees a calm
 * civic surface (§10) — "Något gick fel", a retry, a way to the start page — no
 * stack trace, no danger-alarm styling for a generic failure.
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
function GlobalErrorSurface({
  unstable_retry,
}: {
  unstable_retry: () => void;
}) {
  const t = useTranslations("pages");

  return (
    // min-h-[60vh] + justify-center mirrors the root not-found: this renders
    // chrome-less (no header/footer), so the copy needs a height floor rather
    // than gluing to the top edge. Layout utility, not a locked design token.
    <main className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4">
      <h1 className="jp-h1">{t("common.errorTitle")}</h1>
      <p className="jp-lede">{t("common.errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        {/* unstable_retry() re-fetches and re-renders (the documented Next 16.2+
            recovery for a transient throw); reset() would only re-render without
            re-fetching. */}
        <button
          type="button"
          onClick={() => unstable_retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("common.retry")}
        </button>
        {/* A plain <a> (not next/link) on purpose: global-error replaces the
            root layout, so the App Router context next/link needs is not
            guaranteed here, and a full-document navigation is the robust
            recovery from a catastrophic crash (a soft Link nav would stay inside
            the broken client runtime). */}
        {/* eslint-disable-next-line @next/next/no-html-link-for-pages */}
        <a href="/" className="jp-btn jp-btn--secondary">
          {t("common.notFound.toStart")}
        </a>
      </div>
    </main>
  );
}

export default function GlobalError({
  unstable_retry,
}: {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}) {
  return (
    <html lang="sv" data-density="standard" className="h-full font-sans">
      {/* global-error replaces the root layout, so Next's metadata /
          generateMetadata does not apply — set the document title explicitly
          (from the sv-pinned catalog) so the catastrophic surface is not left
          with a stale tab title. */}
      <head>
        <title>{svPages.common.errorTitle}</title>
      </head>
      <body className="min-h-full bg-surface-primary text-text-primary antialiased">
        <NextIntlClientProvider locale="sv" messages={{ pages: svPages }}>
          <GlobalErrorSurface unstable_retry={unstable_retry} />
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
