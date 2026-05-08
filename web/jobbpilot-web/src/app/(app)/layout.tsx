import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
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
      <main className="flex-1 mx-auto w-full max-w-4xl px-6 py-8">
        {children}
      </main>
    </div>
  );
}
