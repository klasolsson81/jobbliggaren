import { redirect } from "next/navigation";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { logoutAction } from "@/lib/auth/actions";
import { Button } from "@/components/ui/button";
import { AdminNav } from "@/components/admin/admin-nav";
import { HeaderStrip } from "@/components/site/header-strip";
import { SiteFooter } from "@/components/site/site-footer";
import { SkipLink } from "@/components/site/skip-link";

export default async function AdminLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("admin");

  // Roll-check (CTO A1-beslut 2026-05-11): roller kommer färska per request
  // via SessionAuthenticationHandler → /api/v1/me. Non-Admin redirectas till
  // start-yta. Vi 404:ar inte avsiktligt (security through obscurity är inte
  // civic-utility-värde — en uppriktig redirect är rakare).
  if (!user.roles.includes(ROLES.Admin)) redirect("/");

  // #737 — this boundary declares the namespaces its own client subtree reads
  // (AdminNav + the admin tables/filters). React context replaces rather than
  // merges, so the set must be complete for the subtree, not a delta on root's
  // (which is empty). timeZone is inherited from the server request config.
  // The declaration is verified for EQUALITY against the import graph by
  // client-namespace-payload.test.ts — do not edit it by hand-reasoning.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["admin", "fallback"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SkipLink label={t("layout.skipToContent")} />
      <div className="min-h-full flex flex-col bg-background">
        {/* LP-5b (#259): admin adopts the shared `.jp-header` strip via
            HeaderStrip — same white sticky chrome + `<BrandLogo>` as the
            app/guest shells, replacing the legacy raw `border-b
            bg-surface-secondary` bar and the literal "Jobbliggaren" text brand.
            AdminNav + account email + logout compose in unchanged. */}
        <HeaderStrip brandHref="/" brandLabel={t("layout.brandAriaLabel")}>
          <AdminNav />
          <span className="jp-header__spacer" />
          <div className="flex items-center gap-4">
            <span className="text-body-sm text-text-secondary">{user.email}</span>
            <form action={logoutAction}>
              <Button type="submit" variant="ghost" size="sm">
                {t("nav.logout")}
              </Button>
            </form>
          </div>
        </HeaderStrip>
        <main
          id="main"
          tabIndex={-1}
          className="flex-1 mx-auto w-full max-w-[1200px] px-5 sm:px-8 py-8 focus:outline-none"
        >
          {children}
        </main>
        {/* LP-3 (#256): shared deep-green footer at the bottom of the admin
            flex column. */}
        <SiteFooter />
      </div>
    </NextIntlClientProvider>
  );
}
