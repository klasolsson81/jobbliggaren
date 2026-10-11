"use client";

import type { ErrorInfo } from "next/error";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (public-job)/error: the error boundary of the public job page (ADR 0165), shaped like
 * `(marketing-inner)/error.tsx`. The layout's header and footer survive, because Next renders this as the layout's
 * children, and the surface carries its own `<main id="main">` for the skip link (#1477). A server error before the
 * first byte is a real 500 from Next and does not reach here; this catches client errors. `error` is read only by
 * `useReloadOnStaleBuild` (ADR 0148) and is never shown or logged.
 */
function PublicJobErrorSurface({ retry }: Pick<ErrorInfo, "retry">) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        <button type="button" onClick={() => retry()} className="jp-btn jp-btn--primary">
          {t("retry")}
        </button>
        <Link href="/" className="jp-btn jp-btn--secondary">
          {t("notFound.toStart")}
        </Link>
      </div>
    </main>
  );
}

export default function PublicJobError({ error, retry }: ErrorInfo) {
  const reloading = useReloadOnStaleBuild(error);
  if (reloading) return null;

  return <PublicJobErrorSurface retry={retry} />;
}
