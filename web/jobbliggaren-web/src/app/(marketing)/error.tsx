"use client";

import { useTranslations } from "next-intl";

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
 * Client Component by Next convention. The `error` prop is accepted to match
 * the boundary contract but is deliberately neither shown to the user (no stack
 * trace) nor logged here — Next reports uncaught errors on its own, and console
 * output is a §5 anti-pattern.
 */
export default function MarketingError({
  unstable_retry,
}: {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}) {
  const t = useTranslations("fallback");

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex flex-1 flex-col justify-center gap-4 focus:outline-none"
    >
      <h1 className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div>
        <button
          type="button"
          onClick={() => unstable_retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
      </div>
    </main>
  );
}
