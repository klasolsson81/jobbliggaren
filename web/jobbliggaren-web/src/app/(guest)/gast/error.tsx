"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

/**
 * (guest)/gast/error — the runtime error boundary for the guest mirrors
 * (/gast/oversikt, /gast/jobb, /gast/ansokningar, /gast/cv and the two
 * intercepting modals).
 *
 * It sits at `gast/`, NOT at the `(guest)` group root, and that placement is
 * the whole point: the shell and the client i18n provider both live on
 * `gast/layout.tsx`, so a boundary one level up would render outside the shell
 * AND under the root layout's deliberately EMPTY payload — every string blank
 * (#737; client-namespace-payload.test.ts spells out the same trap for pages).
 * Here it renders as that layout's children, inside GuestShell's `<main>`, so
 * the shell, its nav and the footer stay intact (#1477).
 *
 * Client Component by Next convention. The `error` prop matches the boundary
 * contract but is deliberately neither shown nor logged — no stack trace to the
 * user, and Next reports uncaught errors itself (§5: no console output).
 */
export default function GuestError({
  unstable_retry,
}: {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();

  return (
    <div className="jp-container jp-page flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        <button
          type="button"
          onClick={() => unstable_retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
        <Link href="/gast/oversikt" className="jp-btn jp-btn--secondary">
          {t("notFound.toOverview")}
        </Link>
      </div>
    </div>
  );
}
