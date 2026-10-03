"use client";

import type { ErrorInfo } from "next/error";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (auth)/error — the runtime error boundary for /logga-in and its steps.
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
 * `error` prop is read by `useReloadOnStaleBuild` (ADR 0148: a page from a
 * previous build reloads once instead of this surface) and nowhere else —
 * never shown to the user (no stack trace), never logged here: Next reports
 * uncaught errors on its own, and console output is a §5 anti-pattern.
 * A throw
 * in (auth)/layout.tsx itself still reaches global-error: a segment's error.tsx
 * cannot catch its own layout.
 */
function AuthErrorSurface({ retry }: Pick<ErrorInfo, "retry">) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();

  return (
    <div className="flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div>
        <button
          type="button"
          onClick={() => retry()}
          className="jp-btn jp-btn--primary"
        >
          {t("retry")}
        </button>
      </div>
    </div>
  );
}

export default function AuthError({ error, retry }: ErrorInfo) {
  // The surface is its own component so its hooks mount WITH it: when the stamp
  // write fails, the hook flips from reloading to the surface a tick later, and
  // `useFocusOnMount` then runs on the <h1> that now exists (code-reviewer M1 on
  // #1955; the global-error form). Nothing renders while the document is being
  // replaced — no flash of the error surface before the reload.
  const reloading = useReloadOnStaleBuild(error);
  if (reloading) return null;

  return <AuthErrorSurface retry={retry} />;
}
