import "server-only";
import { cookies } from "next/headers";

// F-Pre Punkt 5 — Gäst-mode cookie-mekanik (CTO-dom 2026-05-24 Beslut 4).
//
// Cookie-mönster följer `__Host-jobbliggaren_session` (lib/auth/session.ts:8) —
// `__Host-`-prefixet kräver Secure + Path=/ + inget Domain. Funktional cookie
// (EDPB Guidelines 2/2023 — UX-state, kräver inte samtycke-banner).
// 365 dagar = engångs-välkomst per webbläsare/enhet.

export const GUEST_WELCOMED_COOKIE = "__Host-jobbliggaren_guest_welcomed";
// MAX_AGE bor i `guest-mode-actions.ts` där set:en sker — code-reviewer m1
// 2026-05-24: undvik DRY-brott genom att inte deklarera konstanten på två
// ställen.

/**
 * Läser welcome-cookien server-side i RSC-context. Returnerar `true` om
 * användaren redan stängt välkomst-modalen i denna webbläsare.
 *
 * Används i `(guest)/gast/layout.tsx` för att SSR:a `showWelcome`-prop till
 * `<GuestWelcomeModal>` utan hydration-flash.
 */
export async function hasSeenGuestWelcome(): Promise<boolean> {
  const cookieStore = await cookies();
  return cookieStore.get(GUEST_WELCOMED_COOKIE)?.value === "1";
}
