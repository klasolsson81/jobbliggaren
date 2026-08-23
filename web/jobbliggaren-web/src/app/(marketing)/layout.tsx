import type { ReactNode } from "react";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";

/**
 * Landing-boundary (#737).
 *
 * `(marketing)` holds exactly one route — `/` — and had no layout at all, so
 * the landing page inherited the root provider and its full client payload.
 * That is why `/` shipped a 43 KB document (ADR 0045 budget: 30 KB) carrying
 * `resumes`, `settings`, `applications` and every other namespace no landing
 * client component can reach.
 *
 * This layout exists ONLY to own that payload — it renders no chrome. The
 * landing page mounts the shared SiteHeader and SiteFooter itself (with live
 * stats, #1476), so adding markup here would double it.
 */
export default async function MarketingLayout({ children }: { children: ReactNode }) {
  // Declared set, verified for EQUALITY against the import graph by
  // client-namespace-payload.test.ts — never hand-reasoned. Root carries
  // nothing and a nested provider replaces context rather than merging, so this
  // set must be complete for the landing subtree.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["common", "landing", "pages"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      {children}
    </NextIntlClientProvider>
  );
}
