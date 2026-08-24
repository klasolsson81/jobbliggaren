import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { GuestShell } from "@/components/guest/guest-shell";
import { GuestWelcomeModal } from "@/components/guest/guest-welcome-modal";
import { SiteFooter } from "@/components/site/site-footer";
import { SkipLink } from "@/components/site/skip-link";
import { hasSeenGuestWelcome } from "@/lib/guest/guest-mode";

// F-Pre Punkt 5 — Gäst-tree (CTO-dom 2026-05-24 Beslut 1, Variant A).
//
// Ingen `getServerSession()`-grind: gäst-mode är medvetet anonym yta.
// Middleware (web/jobbliggaren-web/src/middleware.ts) listar `/gast/*` INTE i
// `PROTECTED_PREFIXES` så ingen redirect till `/logga-in` triggas.
//
// `hasSeenGuestWelcome()` läses server-side så `<GuestWelcomeModal>` får
// `showWelcome` SSR-bestämd → ingen hydration-flash (CTO Beslut 4).

export default async function GuestLayout({
  children,
  modal,
}: {
  children: React.ReactNode;
  // F-Pre Punkt 5b 2026-05-24 — @modal parallel-route-slot för
  // intercepting-routes-modal-paritet med live (CTO Beslut 1). default.tsx
  // ger null vid omatchad slot.
  modal: React.ReactNode;
}) {
  const welcomed = await hasSeenGuestWelcome();
  const t = await getTranslations("guest");

  // #737 — the guest mirrors run the same client surfaces as the signed-in app
  // (job list, application views, översikt) on mock data, so this boundary
  // needs those namespaces — but NOT the signed-in-only ones (resumes,
  // settings, matchsetup, aktivitetsrapport, pages, validation), which is where
  // the three measured /gast/* documents lose their weight. Verified for
  // EQUALITY against the import graph by client-namespace-payload.test.ts.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), [
    "applications",
    "common",
    "fallback",
    "guest",
    "jobads",
    "landing",
    "oversikt",
  ]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SkipLink label={t("layout.skipToContent")} />
      <GuestShell>
        {children}
        {modal}
      </GuestShell>
      {/* LP-3 (#256): shared deep-green footer at the shell level (LP-5b/#259
          handles in-shell footer chrome). */}
      <SiteFooter />
      <GuestWelcomeModal showWelcome={!welcomed} />
    </NextIntlClientProvider>
  );
}
