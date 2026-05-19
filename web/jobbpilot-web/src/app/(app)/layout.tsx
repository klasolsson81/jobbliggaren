import { redirect } from "next/navigation";
import { getServerSession, ROLES } from "@/lib/auth/session";
import { AppShell } from "@/components/shell/app-shell";

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

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
      >
        Hoppa till huvudinnehåll
      </a>
      <AppShell email={user.email} isAdmin={isAdmin}>
        {children}
        {modal}
      </AppShell>
    </>
  );
}
