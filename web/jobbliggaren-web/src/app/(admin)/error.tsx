"use client";

import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

/**
 * (admin)/error — the runtime error boundary for the admin surfaces.
 *
 * Klas's rule is about the FRAME, not about who is looking at it: "man ska
 * alltid se header, alltid se footer" (2026-08-23, #1477). (admin)/layout
 * already renders HeaderStrip + SiteFooter and owns `#main`, so this boundary
 * renders as its children and the chrome survives a throw — where before it
 * bubbled to global-error.tsx, which REPLACES the root layout.
 *
 * Client Component by Next convention. The `error` prop matches the boundary
 * contract but is deliberately neither shown nor logged — no stack trace to the
 * user, and Next reports uncaught errors itself (§5: no console output).
 */
export default function AdminError({
  unstable_retry,
}: {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();

  return (
    <div className="flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
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
    </div>
  );
}
