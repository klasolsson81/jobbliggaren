// (app)-scoped component CSS (#750): rules whose consumers render only under
// this route group, so public/landing/auth pages no longer parse them. Loads
// after the root globals.css, whose tokens/keyframes it resolves via var().
import "./app.css";
import { redirect } from "next/navigation";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { ApplicationToastHost } from "@/components/applications/application-toast-host";
import { AppShell } from "@/components/shell/app-shell";
import { SiteFooter } from "@/components/site/site-footer";
import { SkipLink } from "@/components/site/skip-link";
import { fetchLandingStats } from "@/lib/api/landing";
import { LANDING_STATS_UNKNOWN_DTO } from "@/lib/dto/landing";

export default async function AppLayout({
  children,
  modal,
}: {
  children: React.ReactNode;
  // @modal parallel-route-slot (ADR 0053). Renderas bredvid children;
  // default.tsx → null när slotten är omatchad (ingen modal aktiv).
  modal: React.ReactNode;
}) {
  // ADR 0064 — landing-stats server-fetchas en gång per request, samma endpoint
  // som anonyma landing. `<HeaderStats />` i AppShell pollar sedan klient-side var
  // 10:e min (Klas-direktiv 2026-05-24). Backend-fail ⇒ det ärliga icke-svaret
  // (inga tal), aldrig ett påhittat golv (CTO-bind 2026-07-13, A′). HeaderStats
  // renderar en en-dash (–) tills en mätt siffra finns.
  //
  // #742 — anonym/publik (ADR 0064), inget session-beroende, och körs på VARJE
  // (app)-sida. Starta den EAGER bredvid session-läsningen istället för seriellt
  // EFTER — layouten kedjar inte längre /me → stats (sparar ~1 backend-RTT per
  // authad sid-laddning). `cache()`-wrappad och returnerar värde/null (kastar
  // aldrig), så på guest-redirect-vägen löser det svävande löftet sig ofarligt.
  const statsPromise = fetchLandingStats();

  const user = await getServerSession();
  // Middleware blocks unauthenticated requests via cookie presence, but the
  // session can still be invalid/expired on the backend even with a cookie.
  if (!user) redirect("/logga-in");

  const isAdmin = user.roles.includes(ROLES.Admin);
  const initialStats = (await statsPromise) ?? LANDING_STATS_UNKNOWN_DTO;
  const t = await getTranslations("pages");

  // #737 — the signed-in surface is the widest boundary: its client subtree
  // (222 files) legitimately reads most of the catalog. It still declares its
  // set explicitly, because the root provider now carries NOTHING — a nested
  // provider replaces context rather than merging, so an omission here is a
  // MISSING_MESSAGE, not a silent fallback. Verified for EQUALITY against the
  // import graph by client-namespace-payload.test.ts.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), [
    "aktivitetsrapport",
    "applications",
    "common",
    "components",
    "fallback",
    "jobads",
    "landing",
    "matchsetup",
    "oversikt",
    "pages",
    "resumes",
    "settings",
    "validation",
  ]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SkipLink label={t("layout.skipToContent")} />
      <AppShell email={user.email} isAdmin={isAdmin} initialStats={initialStats}>
        {children}
        {modal}
      </AppShell>
      {/* LP-3 (#256): the shared deep-green footer mounts at the shell level.
          Moving footer chrome inside AppShell's flex column is LP-5b/#259. */}
      <SiteFooter />
      {/* #630 PR 7 (CTO-bind 2): EN toast-host för hela (app)-ytan — både
          pipeline-ön och den intercept-monterade detaljmodalen publicerar till
          samma modul-store (toast-store.ts). Fixed-positionerad, renderar null
          utan aktiv toast. */}
      <ApplicationToastHost />
    </NextIntlClientProvider>
  );
}
