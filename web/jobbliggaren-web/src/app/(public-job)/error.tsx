"use client";

import type { ErrorInfo } from "next/error";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (public-job)/error — felgränsen för den publika jobbsidan (ADR 0165). Samma form som
 * `(marketing-inner)/error.tsx`: layoutens SiteHeader och SiteFooter överlever, eftersom Next renderar den här
 * som layoutens barn, och ytan bär ett eget `<main id="main">` för hoppa-över-länken (#1477).
 *
 * Ett serverfel före första byte blir ett riktigt 500 från Next och når inte hit; den här fångar klientfel.
 * `error` läses bara av `useReloadOnStaleBuild` (ADR 0148) och visas eller loggas aldrig.
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
