import type { ReactNode } from "react";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";
import { getLandingStats } from "@/components/landing/landing-stats";

/**
 * Landing boundary (#737) and, since #1477, the landing's chrome.
 *
 * `(marketing)` holds exactly one route — `/` — and had no layout at all, so
 * the landing page inherited the root provider and its full client payload.
 * That is why `/` shipped a 43 KB document (ADR 0045 budget: 30 KB) carrying
 * `resumes`, `settings`, `applications` and every other namespace no landing
 * client component can reach. Owning that payload is still this layout's first
 * job.
 *
 * The header and footer moved here from the page (#1477). They used to sit in
 * `page.tsx`, which meant a throw in the page took the chrome down with it and
 * left the visitor on a bare document — and the only cure from a page-level
 * boundary would have been to mount SiteHeader/SiteFooter from a CLIENT
 * component, pulling both shared RSCs, BrandLogo and the whole footer table
 * into the client bundle of the most CWV-sensitive page in the app. From the
 * layout the chrome survives the throw and stays server-rendered, and
 * `(marketing)/error.tsx` is a leaf. The page keeps its own `<main id="main">`.
 *
 * `getLandingStats()` is safe above the boundary: `fetchLandingStats` wraps its
 * whole body in try/catch and answers `null` on network, 5xx, 429 or a shape
 * mismatch, so it cannot throw past this point — and an unmeasured count is
 * rendered as ABSENCE, never a floor value (CTO-bind 2026-07-13).
 *
 * The header carries its "Logga in" action here since #1480. It was suppressed
 * while the hero mounted AuthCard's own tab with that label, because two
 * controls carrying one label and two behaviours is the defect. The card is
 * gone, so the header is the only place the label appears on this surface.
 */
export default async function MarketingLayout({ children }: { children: ReactNode }) {
  // Declared set, verified for EQUALITY against the import graph by
  // client-namespace-payload.test.ts — never hand-reasoned. Root carries
  // nothing and a nested provider replaces context rather than merging, so this
  // set must be complete for the landing subtree.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["common", "fallback"]);
  const stats = await getLandingStats();

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
        <SiteHeader stats={stats} />
        {children}
        <SiteFooter />
      </div>
    </NextIntlClientProvider>
  );
}
