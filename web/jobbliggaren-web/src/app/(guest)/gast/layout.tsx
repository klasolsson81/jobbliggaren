import { GuestShell } from "@/components/guest/guest-shell";
import { GuestWelcomeModal } from "@/components/guest/guest-welcome-modal";
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

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
      >
        Hoppa till huvudinnehåll
      </a>
      <GuestShell>
        {children}
        {modal}
      </GuestShell>
      <GuestWelcomeModal showWelcome={!welcomed} />
    </>
  );
}
