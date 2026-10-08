import "./admin.css";
import { redirect } from "next/navigation";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { AppShell } from "@/components/shell/app-shell";
import { fetchLandingStats } from "@/lib/api/landing";
import { LANDING_STATS_UNKNOWN_DTO } from "@/lib/dto/landing";
import { AdminToastHost } from "@/components/admin/admin-toast-host";
import { SiteFooter } from "@/components/site/site-footer";
import { SkipLink } from "@/components/site/skip-link";
import { ReloadedAfterUpdateNotice } from "@/components/site/reloaded-after-update-notice";

export default async function AdminLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const statsPromise = fetchLandingStats();
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("admin");

  // Roll-check (CTO A1-beslut 2026-05-11): roller kommer färska per request
  // via SessionAuthenticationHandler → /api/v1/me. Non-Admin redirectas till
  // start-yta. Vi 404:ar inte avsiktligt (security through obscurity är inte
  // civic-utility-värde — en uppriktig redirect är rakare).
  if (!user.roles.includes(ROLES.Admin)) redirect("/");

  const initialStats = (await statsPromise) ?? LANDING_STATS_UNKNOWN_DTO;

  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["admin", "common", "fallback", "landing", "pages", "settings"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SkipLink label={t("layout.skipToContent")} />
      <div className="min-h-full flex flex-col bg-background">
        <AppShell
          email={user.email}
          isAdmin
          className="flex-1"
          initialStats={initialStats}
          contentClassName="flex-1 mx-auto w-full max-w-[1200px] px-5 sm:px-8 py-8 focus:outline-none"
        >
          <ReloadedAfterUpdateNotice placement="inline" />
          {children}
        </AppShell>
        {/* LP-3 (#256): shared deep-green footer at the bottom of the admin
            flex column. */}
        <SiteFooter />
      </div>
      <AdminToastHost />
    </NextIntlClientProvider>
  );
}
