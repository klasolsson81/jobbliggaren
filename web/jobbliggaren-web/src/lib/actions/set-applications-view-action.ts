"use server";

import { cookies } from "next/headers";
import {
  APPLICATIONS_VIEW_COOKIE,
  isApplicationsView,
  type ApplicationsView,
} from "@/lib/applications/view";

// Separat fil per Next.js-konventionen: en modul deklarerar antingen "use
// server" ELLER "server-only" och får bara exportera Server Actions. MAX_AGE bor
// HÄR (där set:en sker) — inte i view.ts — för att undvika DRY-brott (samma
// split som `set-locale-action.ts` / `lib/guest/guest-mode-actions.ts`).
const APPLICATIONS_VIEW_MAX_AGE = 365 * 24 * 60 * 60; // 1 år i sekunder

/**
 * Server Action — persistar vald /ansokningar-vy i `__Host-jobbliggaren_apps_view`
 * (ADR 0092 D7). SSR-läses i `page.tsx` via `readApplicationsView()` → seedar öns
 * `initialView`, så nästa besök renderar rätt vy utan flash.
 *
 * Anropas fire-and-forget i öns `startTransition` (INTE await:ad, INGEN
 * `router.refresh()`): vy-växlingen är en ren klient-beräkning över redan hämtad
 * data (ADR 0092 D2) — cookien är bara till för nästa-paint-persistensen, aldrig
 * på den synliga växlingens kritiska väg. En misslyckad skrivning är tyst
 * (nästa paint faller på DEFAULT); en vy-preferens är icke-kritisk.
 *
 * `httpOnly`: klienten läser aldrig cookien — den aktiva vyn hålls i React-state
 * seedat från SSR-propen — så vi minimerar privilegier, speglar session-/guest-/
 * locale-cookiesarna. `isApplicationsView`-vakten avvisar otillförlitlig
 * klient-indata trots att parametern är typad.
 */
export async function setApplicationsViewAction(
  view: ApplicationsView,
): Promise<void> {
  if (!isApplicationsView(view)) return;

  const cookieStore = await cookies();
  cookieStore.set(APPLICATIONS_VIEW_COOKIE, view, {
    httpOnly: true,
    secure: true,
    sameSite: "lax",
    path: "/",
    maxAge: APPLICATIONS_VIEW_MAX_AGE,
  });
}
