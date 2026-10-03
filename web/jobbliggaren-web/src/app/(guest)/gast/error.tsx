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
export default function GuestError({ error, retry }: ErrorInfo) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();
  const reloading = useReloadOnStaleBuild(error);

  // After every hook (rules of hooks); the document is being replaced, so
  // nothing renders — no flash of the error surface before the reload.
  if (reloading) return null;

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
