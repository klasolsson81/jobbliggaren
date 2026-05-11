import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { logoutAction } from "@/lib/auth/actions";
import { Button } from "@/components/ui/button";

export default async function AppLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const user = await getServerSession();
  // Middleware blocks unauthenticated requests via cookie presence, but the
  // session can still be invalid/expired on the backend even with a cookie.
  if (!user) redirect("/logga-in");

  return (
    <div className="min-h-full flex flex-col bg-background">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
      >
        Hoppa till huvudinnehåll
      </a>
      <header className="border-b border-border bg-surface-secondary">
        <div className="mx-auto max-w-4xl px-6 h-14 flex items-center justify-between">
          <Link
            href="/"
            className="text-body font-medium text-text-primary hover:text-brand-600"
          >
            JobbPilot
          </Link>
          <nav aria-label="Huvudnavigation" className="flex items-center gap-1">
            <Link
              href="/ansokningar"
              className="rounded-md px-3 py-1.5 text-body-sm text-text-secondary hover:bg-surface-tertiary hover:text-text-primary"
            >
              Ansökningar
            </Link>
            <Link
              href="/cv"
              className="rounded-md px-3 py-1.5 text-body-sm text-text-secondary hover:bg-surface-tertiary hover:text-text-primary"
            >
              CV
            </Link>
            {user.roles.includes(ROLES.Admin) && (
              <Link
                href="/admin/granskning"
                className="rounded-md px-3 py-1.5 text-body-sm text-text-secondary hover:bg-surface-tertiary hover:text-text-primary"
              >
                Granskning
              </Link>
            )}
          </nav>
          <div className="flex items-center gap-4">
            <span className="text-body-sm text-text-secondary">{user.email}</span>
            <form action={logoutAction}>
              <Button type="submit" variant="ghost" size="sm">
                Logga ut
              </Button>
            </form>
          </div>
        </div>
      </header>
      <main
        id="main"
        tabIndex={-1}
        className="flex-1 mx-auto w-full max-w-4xl px-6 py-8 focus:outline-none"
      >
        {children}
      </main>
    </div>
  );
}
