"use server";

import { cookies } from "next/headers";
import { SETUP_WELCOMED_COOKIE } from "./setup-welcome";

// Separat fil per Next.js 16-konvention: en fil får ha antingen "use server"
// ELLER "server-only", inte båda (server-only-import är inte tillåten i
// "use server"-filer). Modulen exporterar ENDAST Server Actions. Speglar
// `lib/guest/guest-mode-actions.ts`.

const SETUP_WELCOMED_MAX_AGE = 365 * 24 * 60 * 60;

/**
 * Server Action — sätter setup-welcome-cookien så modalen inte återkommer.
 * Anropas från `<MatchSetupLauncher>`-klient-komponenten (epik #526) när
 * matchnings-setup-modalen stängs (close/skip/Esc eller efter en sparning).
 */
export async function markSetupWelcomeSeen(): Promise<void> {
  const cookieStore = await cookies();
  // httpOnly: true — klienten behöver inte läsa cookien (modal-state styrs av
  // server-prop `showWelcome`); minimera privilegier (Saltzer–Schroeder,
  // paritet med session- och gäst-welcome-cookien).
  // sameSite: "lax" — tillåter cookien att räknas vid cross-site top-level GET
  // (extern länk till /oversikt) så modalen inte återkommer.
  cookieStore.set(SETUP_WELCOMED_COOKIE, "1", {
    httpOnly: true,
    secure: true,
    sameSite: "lax",
    path: "/",
    maxAge: SETUP_WELCOMED_MAX_AGE,
  });
}
