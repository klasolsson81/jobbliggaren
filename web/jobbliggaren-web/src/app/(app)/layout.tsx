import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { AppShell } from "@/components/shell/app-shell";
import { SiteFooter } from "@/components/site/site-footer";
import { fetchLandingStats } from "@/lib/api/landing";
import { LANDING_STATS_FLOOR_DTO } from "@/lib/dto/landing";

export default async function AppLayout({
  children,
  modal,
}: {
  children: React.ReactNode;
  // @modal parallel-route-slot (ADR 0053). Renderas bredvid children;
  // default.tsx → null när slotten är omatchad (ingen modal aktiv).
  modal: React.ReactNode;
}) {
  const user = await getServerSession();
  // Middleware blocks unauthenticated requests via cookie presence, but the
  // session can still be invalid/expired on the backend even with a cookie.
  if (!user) redirect("/logga-in");

  const isAdmin = user.roles.includes(ROLES.Admin);
  // ADR 0064 — landing-stats server-fetchas en gång per request, samma
  // endpoint som anonyma landing. `<HeaderStats />` i AppShell pollar sedan
  // klient-side var 10:e min (Klas-direktiv 2026-05-24).
  const initialStats = (await fetchLandingStats()) ?? LANDING_STATS_FLOOR_DTO;
  const t = await getTranslations("pages");

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
      >
        {t("layout.skipToContent")}
      </a>
      <AppShell email={user.email} isAdmin={isAdmin} initialStats={initialStats}>
        {children}
        {modal}
      </AppShell>
      {/* LP-3 (#256): the shared deep-green footer mounts at the shell level.
          Moving footer chrome inside AppShell's flex column is LP-5b/#259. */}
      <SiteFooter />
    </>
  );
}
