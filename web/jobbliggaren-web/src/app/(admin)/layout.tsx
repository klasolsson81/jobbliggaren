import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { logoutAction } from "@/lib/auth/actions";
import { Button } from "@/components/ui/button";
import { AdminNav } from "@/components/admin/admin-nav";

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

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
      >
        {t("layout.skipToContent")}
      </a>
      <div className="min-h-full flex flex-col bg-background">
        <header className="border-b border-border bg-surface-secondary">
          <div className="mx-auto max-w-6xl px-6 h-14 flex items-center justify-between">
            <Link
              href="/"
              className="text-body font-medium text-text-primary hover:text-brand-600"
            >
              Jobbliggaren
            </Link>
            <AdminNav />
            <div className="flex items-center gap-4">
              <span className="text-body-sm text-text-secondary">{user.email}</span>
              <form action={logoutAction}>
                <Button type="submit" variant="ghost" size="sm">
                  {t("nav.logout")}
                </Button>
              </form>
            </div>
          </div>
        </header>
        <main
          id="main"
          tabIndex={-1}
          className="flex-1 mx-auto w-full max-w-6xl px-6 py-8 focus:outline-none"
        >
          {children}
        </main>
      </div>
    </>
  );
}
