import type { ReactNode } from "react";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";
import { ReloadedAfterUpdateNotice } from "@/components/site/reloaded-after-update-notice";

/**
 * Layout för den publika jobbsidan och dess lägen (ADR 0165). Gruppen når ingen URL på egen hand: `proxy.ts`
 * skriver om en cookielös `/jobb/<uuid>` hit, och en direkt träff på `/publik/**` är ett 404 därifrån.
 *
 * Samma ram som `(marketing-inner)` (SiteHeader, SiteFooter, uppdateringsnotisen), så en besökare som kommer
 * från en sökträff eller en delad länk aldrig hamnar utanför Jobbliggarens ramverk. Varje sida äger sitt eget
 * `<main id="main">`, som SiteHeaders enda hoppa-över-länk pekar på.
 *
 * Ingen `loading.tsx` i gruppen, och det är avsiktligt: ett Suspense-fall committar 200 innan kroppen, och då kan
 * en `notFound()` eller ett kastat fel inte längre bli en riktig 404 eller 5xx (Next streaming-guiden, "Status
 * codes"). `public-job-group.test.ts` fäster det.
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
