import "server-only";
import { cookies } from "next/headers";
import {
  APPLICATIONS_VIEW_COOKIE,
  DEFAULT_APPLICATIONS_VIEW,
  isApplicationsView,
  type ApplicationsView,
} from "@/lib/applications/view";

/**
 * Läser vy-preferensen server-side i RSC-context (`page.tsx`), speglar
 * `guest-mode.ts:hasSeenGuestWelcome`. Trådas som `initialView`-prop till
 * `<ApplicationsPipeline>` så första-paint renderar rätt vy utan flash (ADR 0092
 * D7 / ADR 0078-precedent — cookie, INTE localStorage).
 *
 * Ett okänt eller frånvarande värde (aldrig-satt, deploy-skew där PR 10 skrivit
 * "tabell" innan detta bygge kan rendera den) faller tillbaka på DEFAULT — aldrig
 * en throw.
 */
export async function readApplicationsView(): Promise<ApplicationsView> {
  const value = (await cookies()).get(APPLICATIONS_VIEW_COOKIE)?.value;
  return isApplicationsView(value) ? value : DEFAULT_APPLICATIONS_VIEW;
}
