"use server";

import { cookies } from "next/headers";
import { GUEST_WELCOMED_COOKIE } from "./guest-mode";

// Separat fil per Next.js 16-konvention: en fil får ha antingen
// "use server" ELLER "server-only", inte båda (server-only-import är inte
// tillåten i "use server"-filer). Modulen exporterar ENDAST Server Actions.

const GUEST_WELCOMED_MAX_AGE = 365 * 24 * 60 * 60;

/**
 * Server Action — sätter welcome-cookien så modalen inte återkommer.
 * Anropas från `<GuestWelcomeModal>`-klient-komponenten på close/dismiss.
 */
export async function markGuestWelcomeSeen(): Promise<void> {
  const cookieStore = await cookies();
  // httpOnly: true (security-auditor M-1 2026-05-24): klienten behöver inte
  // läsa cookien (modal-state styrs av server-prop `showWelcome`) — minimera
  // privilegier per Saltzer–Schroeder defense-in-depth. Paritet med
  // session-cookie (`lib/auth/session.ts:64`).
  // sameSite: "lax" (security-auditor m-1): tillåter cookien att räknas vid
  // cross-site top-level GET (extern länk till /gast/oversikt) så modalen
  // inte återkommer om användaren delar URL:n och kommer tillbaka via
  // extern länk. Session-cookien använder "strict" — annan livscykel.
  cookieStore.set(GUEST_WELCOMED_COOKIE, "1", {
    httpOnly: true,
    secure: true,
    sameSite: "lax",
    path: "/",
    maxAge: GUEST_WELCOMED_MAX_AGE,
  });
}
