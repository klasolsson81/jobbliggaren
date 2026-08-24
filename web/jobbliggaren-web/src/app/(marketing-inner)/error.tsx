"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

/**
 * (marketing-inner)/error — the runtime error boundary for every inner public
 * page, the legal pages the registration consent text links to among them.
 *
 * Without it a throw bubbled to global-error.tsx, which REPLACES the root
 * layout and renders chrome-less — so the legal pages the registration consent
 * text links to could drop the visitor outside the site frame (#1477). Here the
 * layout's SiteHeader and SiteFooter survive, because Next renders this as that
 * layout's children.
 *
 * It carries its OWN `<main id="main">`: in this group the landmark lives on
 * each PAGE (#284), not in the layout, so without one here SiteHeader's skip
 * link would point at nothing on the error surface.
 *
 * Client Component by Next convention. The `error` prop matches the boundary
 * contract but is deliberately neither shown nor logged — no stack trace to the
 * user, and Next reports uncaught errors itself (§5: no console output).
 */
export default function MarketingInnerError({
  unstable_retry,
}: {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();

  return (
    <main
      id="main"
      tabIndex={-1}
      // min-h-[60vh] rather than flex-1: (marketing-inner)/layout wraps children
      // in a plain block, so a flex child has nothing to grow against. Same
      // height-floor idiom as global-error.tsx. Layout utility, not a token.
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
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
        <Link href="/" className="jp-btn jp-btn--secondary">
          {t("notFound.toStart")}
        </Link>
      </div>
    </main>
  );
}
