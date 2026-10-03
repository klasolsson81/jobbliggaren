"use client";

import type { ErrorInfo } from "next/error";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

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
 * Client Component by Next convention. The `error` prop is read by
 * `useReloadOnStaleBuild` (ADR 0148: a page from a previous build reloads once
 * instead of this surface) and nowhere else — never shown to the user (no
 * stack trace), never logged here: Next reports uncaught errors on its own,
 * and console output is a §5 anti-pattern.
 */
function GuestErrorSurface({ retry }: Pick<ErrorInfo, "retry">) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();

  return (
    <div className="jp-container jp-page flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div className="flex flex-wrap gap-3">
        <button
          type="button"
          onClick={() => retry()}
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

export default function GuestError({ error, retry }: ErrorInfo) {
  // The surface is its own component so its hooks mount WITH it: when the stamp
  // write fails, the hook flips from reloading to the surface a tick later, and
  // `useFocusOnMount` then runs on the <h1> that now exists (code-reviewer M1 on
  // #1955; the global-error form). Nothing renders while the document is being
  // replaced — no flash of the error surface before the reload.
  const reloading = useReloadOnStaleBuild(error);
  if (reloading) return null;

  return <GuestErrorSurface retry={retry} />;
}
