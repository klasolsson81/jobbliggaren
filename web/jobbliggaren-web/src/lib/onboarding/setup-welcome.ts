import "server-only";
import { cookies } from "next/headers";

// ADR 0077 STEG 5 (välkomst-/första-setup-modal). Cookie-mekaniken speglar
// gäst-mode (`lib/guest/guest-mode.ts`) verbatim: `__Host-`-prefixet kräver
// Secure + Path=/ + inget Domain. Funktionell cookie (EDPB Guidelines 2/2023 —
// UX-state, kräver inte samtycke-banner). 365 dagar = engångs-välkomst per
// webbläsare/enhet.
//
// Varför cookie och inte en ny backend-kolumn (ADR 0077 Alt C förkastad):
// ADR 0076 Decision 3 utesluter avsiktligt ett lagrat "skippat"-state. En
// användare som medvetet lämnar yrke tomt får aldrig om-naggas — cookien
// bryter den loopen utan en backend-skrivning (gäst-mode-precedent).

export const SETUP_WELCOMED_COOKIE = "__Host-jobbliggaren_setup_welcomed";
// MAX_AGE bor i `setup-welcome-actions.ts` där set:en sker (DRY: konstanten
// deklareras inte på två ställen — samma uppdelning som guest-mode).

/**
 * Läser setup-welcome-cookien server-side i RSC-context. Returnerar `true` om
 * användaren redan stängt/skippat välkomst-modalen i denna webbläsare.
 *
 * Används i `(app)/oversikt/page.tsx` för att SSR:a auto-open-beslutet till
 * `<MatchSetupLauncher>` (epik #526) utan hydration-flash.
 */
export async function hasSeenSetupWelcome(): Promise<boolean> {
  const cookieStore = await cookies();
  return cookieStore.get(SETUP_WELCOMED_COOKIE)?.value === "1";
}
