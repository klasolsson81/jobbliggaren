"use client";

import { useMemo, type ReactNode } from "react";
import { NoticePrefsContext, type NoticePrefsStore } from "./use-notice-prefs";

/**
 * En notis-preferensstore som inte minns någonting: allt är påslaget, och en
 * växling är en no-op (CTO-dom 2026-08-29, #1572).
 *
 * Finns för gäst-demot, som har ett kugghjul mindre än appen. Utan den vore
 * `/gast/oversikt` filtrerad av `jp-oversikt-notice-prefs` — en localStorage-nyckel
 * vars `"<källa>:<typ>"`-form DELAS med den inloggade appen i samma webbläsare — och
 * besökaren hade varken kunnat se eller ångra filtreringen där. Ett konto som stängt
 * av alla fyra demo-typerna hade fått en publik yta som säger "inga olästa" i varje
 * sektion och inte ens renderar bulk-kontrollen (`mark-all-read-row.tsx` returnerar
 * `null` när ingenting är synligt).
 *
 * En store-VARIANT, inte en flagga per konsument: prefs-läsningen är tvingad ut i
 * klient-löven av att `oversikt-page.tsx` är en Server Component, så ett nytt löv som
 * läser prefs blir rätt av sig självt innanför den här providern.
 *
 * Tar bara `children`. Värdet konstrueras HÄR, inne i klient-modulen — anroparen
 * (`guest-oversikt-page.tsx`) är en RSC, och ett funktionsbärande `value` kan inte
 * passera RSC-gränsen. Samma form som `theme-provider.tsx`.
 */
export function InertNoticePrefsProvider({
  children,
}: {
  readonly children: ReactNode;
}) {
  const store = useMemo<NoticePrefsStore>(
    () => ({
      isEnabled: () => true,
      toggle: () => {},
    }),
    [],
  );

  return (
    <NoticePrefsContext.Provider value={store}>
      {children}
    </NoticePrefsContext.Provider>
  );
}
