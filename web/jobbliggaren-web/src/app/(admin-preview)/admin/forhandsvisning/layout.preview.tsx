import "@/app/(admin)/admin.css";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { AdminNav } from "@/components/admin/admin-nav";
import { AdminToastHost } from "@/components/admin/admin-toast-host";
import { HeaderStrip } from "@/components/site/header-strip";
import { SiteFooter } from "@/components/site/site-footer";
import { SkipLink } from "@/components/site/skip-link";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_ACCOUNTS, PREVIEW_ADMIN_EMAIL } from "@/lib/admin-preview/fixtures";
import { PreviewShell } from "./_preview/preview-shell.preview";

export const dynamic = "force-dynamic";

/** Every page whose regions follow the band's state; Bakgrundsjobb and Granskning show their data. */
const STATEFUL_PATHS = [
  "",
  "/anvandare",
  "/feedback",
  "/loggar",
  "/loggar/applikationsfel",
  "/loggar/platsbanken-import",
  "/e-post",
];

/**
 * The admin preview's layout (ADR 0150 D5): the admin chrome over fictional data, outside the
 * `(admin)` gate because it reads no backend. It is a route only in a build made with the flag, and
 * it sends a visitor to /admin when the running server lacks the flag.
 */
export default async function AdminPreviewLayout({ children }: { children: React.ReactNode }) {
  requireAdminPreview();
  const t = await getTranslations("admin");
  const locale = await getLocale();
  // `common`, `pages` and `settings` are the shared re-authentication dialog's (#1975).
  const messages = pickClientMessages(await getMessages(), ["admin", "admin-preview", "common", "pages", "settings"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SkipLink label={t("layout.skipToContent")} />
      <div className="min-h-full flex flex-col bg-background">
        <HeaderStrip brandHref="/" brandLabel={t("layout.brandAriaLabel")}>
          <AdminNav basePath={ADMIN_PREVIEW_ROUTE} />
          <div className="jp-adminaccount">
            <span className="jp-adminaccount__email" title={PREVIEW_ADMIN_EMAIL}>
              {PREVIEW_ADMIN_EMAIL}
            </span>
          </div>
        </HeaderStrip>
        <PreviewShell
          impersonatedEmail={PREVIEW_ACCOUNTS[1]?.email ?? PREVIEW_ADMIN_EMAIL}
          statefulPaths={STATEFUL_PATHS.map((path) => ADMIN_PREVIEW_ROUTE + path)}
        >
          <main
            id="main"
            tabIndex={-1}
            className="flex-1 mx-auto w-full max-w-[1200px] px-5 sm:px-8 py-8 focus:outline-none"
          >
            {children}
          </main>
        </PreviewShell>
        <SiteFooter />
      </div>
      <AdminToastHost />
    </NextIntlClientProvider>
  );
}
