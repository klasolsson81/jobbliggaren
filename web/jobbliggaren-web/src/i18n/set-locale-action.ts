"use server";

import { cookies } from "next/headers";
import { isLocale, LOCALE_COOKIE, type Locale } from "./routing";

// Separate file per Next.js convention: a module may declare either
// "use server" OR "server-only", not both, and must export only Server Actions.

const LOCALE_MAX_AGE = 365 * 24 * 60 * 60; // 1 year in seconds

/**
 * Server Action — persists the chosen UI locale in the `NEXT_LOCALE` cookie.
 *
 * The app uses next-intl without i18n routing, so the locale is resolved from
 * this cookie in `i18n/request.ts`. The calling client component runs
 * `router.refresh()` afterwards so the server re-renders with the new config.
 *
 * httpOnly: the client never reads the cookie — the current locale is read from
 * the provider via `useLocale()` — so we minimise privilege, mirroring the
 * session and guest cookies (`lib/auth/session.ts`, `lib/guest/`).
 *
 * The runtime `isLocale` guard rejects untrusted client input even though the
 * parameter is typed.
 */
export async function setLocaleAction(next: Locale): Promise<void> {
  if (!isLocale(next)) {
    return;
  }

  const cookieStore = await cookies();
  cookieStore.set(LOCALE_COOKIE, next, {
    httpOnly: true,
    secure: true,
    sameSite: "lax",
    path: "/",
    maxAge: LOCALE_MAX_AGE,
  });
}
