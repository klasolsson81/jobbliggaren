"use client";

import type { ErrorInfo } from "next/error";
import { AdminErrorSurface } from "@/components/admin/admin-error-surface";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

/**
 * (admin)/error — the runtime error boundary for the admin surfaces.
 *
 * Klas's rule is about the FRAME, not about who is looking at it: "man ska
 * alltid se header, alltid se footer" (2026-08-23, #1477). (admin)/layout
 * already renders HeaderStrip + SiteFooter and owns `#main`, so this boundary
 * renders as its children and the chrome survives a throw — where before it
 * bubbled to global-error.tsx, which REPLACES the root layout.
 *
 * Client Component by Next convention. The `error` prop is read by
 * `useReloadOnStaleBuild` (ADR 0148: a page from a previous build reloads once
 * instead of this surface) and the surface's explicit retry control — never
 * shown to the user (no stack trace), never logged here: Next reports uncaught
 * errors on its own, and console output is a §5 anti-pattern.
 */
export default function AdminError({ error, retry }: ErrorInfo) {
  // The surface is its own component so its hooks mount WITH it: when the stamp
  // write fails, the hook flips from reloading to the surface a tick later, and
  // `useFocusOnMount` then runs on the <h1> that now exists (code-reviewer M1 on
  // #1955; the global-error form). Nothing renders while the document is being
  // replaced — no flash of the error surface before the reload.
  const reloading = useReloadOnStaleBuild(error);
  if (reloading) return null;

  return <AdminErrorSurface error={error} retry={retry} />;
}
