/**
 * /ansokningar vy-preferens — SSOT för de tre vyerna och deras cookie (ADR 0092
 * D1/D7). Klient+server-säker modul (ingen `server-only`/`"use server"`) så både
 * ön (`useState<ApplicationsView>`), växlaren (`Segment`-optioner), servern
 * (`view-preference.ts`-läsningen) och setter-actionen delar EN deklaration av
 * typ, valid-set och cookie-namn — ingen drift (CLAUDE.md §9.1 DRY).
 *
 * Namnet lever HÄR (inte i server-only-läsmodulen som guest-cookien, vars typ
 * ingen klient behöver) eftersom klienten behöver typen/valid-set:en; `MAX_AGE`
 * lever i setter-modulen (`set-applications-view-action.ts`) för att undvika
 * DRY-brott — samma split-princip som `lib/guest/`.
 *
 * `__Host-`-prefixet kräver Secure + Path=/ + inget Domain (subdomän-injektion-
 * resistent), speglar `__Host-jobbliggaren_session`/`_guest_welcomed`. Funktionell
 * UX-state-cookie (EDPB 2/2023 — kräver ingen samtyckesbanner).
 */
export const APPLICATIONS_VIEW_COOKIE = "__Host-jobbliggaren_apps_view";

// Lista / Tavla / Tabell (ADR 0092 D1). Tabell tillkom i PR 10 (2026-07-10) —
// växlaren, cookie-valid-set:en och serverläsningen följer denna tuple.
export const APPLICATIONS_VIEWS = ["lista", "tavla", "tabell"] as const;

export type ApplicationsView = (typeof APPLICATIONS_VIEWS)[number];

export const DEFAULT_APPLICATIONS_VIEW: ApplicationsView = "lista";

/**
 * Runtime-vakt mot otillförlitlig indata (cookie-värde, klient-parameter): en
 * okänd/frånvarande sträng är aldrig en giltig vy. Servern faller tillbaka på
 * DEFAULT, setter-actionen no-op:ar tyst.
 */
export function isApplicationsView(
  value: string | undefined | null,
): value is ApplicationsView {
  return (
    value != null &&
    (APPLICATIONS_VIEWS as readonly string[]).includes(value)
  );
}
