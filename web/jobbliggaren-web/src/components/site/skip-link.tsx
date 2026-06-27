/**
 * SkipLink — the shared "skip to content" link rendered as the first focusable
 * element of every shell/surface (WCAG 2.4.1 Bypass Blocks). LP-5b / #259;
 * landing page folded in by #284, epic #267.
 *
 * Before #259 the identical `sr-only focus:not-sr-only …` className block was
 * copy-pasted across the public surfaces — the (app)/(guest)/(admin) layouts,
 * the public `site-header.tsx`, and the landing page (`(marketing)/page.tsx`,
 * folded in by #284) — each reading its own namespaced copy key. That className
 * is a single piece of knowledge (DRY) — it lives here once. The label stays a
 * prop: each surface owns its own i18n key (`pages.layout.skipToContent`,
 * `guest.layout.skipToContent`, `admin.layout.skipToContent`,
 * `landing.common.skipToContent` — the last shared by `site-header.tsx` and the
 * landing page) and resolves it itself, so the component takes no `next-intl`
 * dependency and renders safely in both Server and Client Components without
 * forcing a `"use client"` boundary.
 *
 * The target is always `#main` — the real `<main>` landmark each surface exposes
 * (on the marketing-inner pages #284 moved `id="main"` onto each page's own
 * `<main>`, which wraps the page-hero region + content).
 */
export function SkipLink({ label }: { label: string }) {
  return (
    <a
      href="#main"
      className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
    >
      {label}
    </a>
  );
}
