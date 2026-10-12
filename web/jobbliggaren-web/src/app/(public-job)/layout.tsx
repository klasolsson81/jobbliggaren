import type { ReactNode } from "react";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";
import { ReloadedAfterUpdateNotice } from "@/components/site/reloaded-after-update-notice";

/**
 * Layout for the public job page and its states (ADR 0165). The group is no URL of its own: `proxy.ts` rewrites a
 * cookieless `/jobb/<uuid>` into it, and a direct request for `/publik/**` is a 404.
 *
 * The `(marketing-inner)` frame, so a visitor from a search result or a shared link is never outside the site's
 * frame. Each page owns its `<main id="main">`, which the header's skip link points at.
 *
 * No `loading.tsx` in the group, on purpose: a Suspense fallback commits a 200 before the body, and a later
 * `notFound()` or throw can no longer become a real 404 or 5xx. `public-job-group.test.ts` pins it.
 */
export default async function PublicJobLayout({ children }: { children: ReactNode }) {
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["common", "fallback"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
        <SiteHeader />
        <div className="flex-1">
          <ReloadedAfterUpdateNotice placement="rail" />
          {children}
        </div>
        <SiteFooter />
      </div>
    </NextIntlClientProvider>
  );
}
