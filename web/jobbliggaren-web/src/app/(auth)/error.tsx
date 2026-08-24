"use client";

import { useTranslations } from "next-intl";

/**
 * (auth)/error — the runtime error boundary for /logga-in, /registrera,
 * /glomt-losenord, /aterstall-losenord and the two /bekrafta-* surfaces.
 *
 * Without it a throw on any of them bubbled past every boundary to
 * global-error.tsx, which by Next convention REPLACES the root layout — so the
 * visitor landed on a bare document with no header, no footer and no way back
 * (#1477). This file renders as (auth)/layout's children instead, inside the
 * shared SiteHeader/SiteFooter, and the layout's back link stays on screen
 * above it. That link is why this surface offers only a retry: a second
 * control carrying the same label directly under it is the defect, not the
 * missing button.
 *
 * Client Component by Next convention (error boundaries run on the client). The
 * `error` prop is accepted to match the boundary contract but is deliberately
 * neither shown to the user (no stack trace) nor logged here — Next reports
 * uncaught errors on its own, and console output is a §5 anti-pattern. A throw
 * in (auth)/layout.tsx itself still reaches global-error: a segment's error.tsx
 * cannot catch its own layout.
 */
export default function AuthError({
  unstable_retry,
}: {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}) {
  const t = useTranslations("fallback");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div>
        {/* unstable_retry() re-fetches and re-renders the segment (the Next
            16.2+ recovery for a transient throw); reset() would only re-render
            and replay the same failed RSC payload. */}
        <button
          type="button"
          onClick={() => unstable_retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
      </div>
    </div>
  );
}
