"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Check, RotateCw } from "lucide-react";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";
import type { SectionNoticeData } from "./notice-section";

interface NoticeToolbarProps {
  readonly lastUpdated: string;
  /** ALLA sektioners notiser — "Markera alla" avfärdar tvärs över sektionerna. */
  readonly notices: ReadonlyArray<SectionNoticeData>;
}

/**
 * Tunn sid-toolbar över notissektionerna (#726): "senast uppdaterad"-stämpel och en
 * uppdatera-kontroll till vänster, "Markera alla som lästa" till höger. Delar de två
 * store-hookarna med sektionerna så state hålls konsekvent. Markera-alla visas bara när
 * minst en synlig, avfärdbar notis finns (efter inställnings-filtrering) — annars vore den
 * en no-op.
 *
 * Uppdatera-kontrollen finns på Klas-villkor (2026-08-28, #1549): en tidpunkt användaren
 * varken valde eller kan påverka är störande, så antingen får hen påverka den eller så ska
 * raden bort. Sidan är `force-dynamic`, så `router.refresh()` ger en ny render och därmed en
 * ny stämpel — ingen egen hämtväg behövs.
 *
 * Den blir ALDRIG `disabled`. `router.refresh()` är idempotent, och husets form för just den
 * klassen är att inte disabla (`company-follow-button.tsx`, Klas PR5) — en fokuserad knapp
 * som disablas blurras av webbläsaren, så tangentbordsanvändaren kastas till dokumentets
 * början och skärmläsaren hör inget namnbyte. Väntan bärs av `aria-busy` och av knappens
 * eget namn, som förblir fokuserat.
 *
 * Stämpeln har minutupplösning medan en refresh går på under en sekund, så två klick i samma
 * minut lämnar den oförändrad. Utan kvitto ser kontrollen verkningslös ut — precis det
 * tillstånd Klas villkor pekar ut. Knappen bär därför kvittot själv: `Uppdaterad` en kort
 * stund innan den återgår.
 */
export function NoticeToolbar({ lastUpdated, notices }: NoticeToolbarProps) {
  const t = useTranslations("oversikt");
  const router = useRouter();
  const [isRefreshing, startRefresh] = useTransition();
  const [justRefreshed, setJustRefreshed] = useState(false);
  const { dismissed, dismissMany } = useDismissedNotices();
  const { isEnabled } = useNoticePrefs();

  const dismissibleVisible = notices.filter(
    (n) =>
      n.dismissible !== false &&
      isEnabled(n.source, n.type) &&
      !dismissed.has(n.id),
  );

  // WCAG 2.4.3 (design-reviewer Major, #726): "Markera alla" avmonterar sig
  // själv när inget avfärdbart återstår → utan förflyttning faller fokus till
  // <body>. Efter re-rendern flyttas fokus till första sektionens kugghjul
  // (stabilt — sektionerna döljs aldrig). Ref-flagga i stället för state:
  // klicket muterar dismiss-store:n → effekten (keyad på `dismissed`) körs
  // efter re-rendern; ref-nollning där är lint-säker
  // (react-hooks/set-state-in-effect).
  const moveFocusRef = useRef(false);
  useEffect(() => {
    if (!moveFocusRef.current) return;
    moveFocusRef.current = false;
    document.querySelector<HTMLButtonElement>(".jp-section__gear")?.focus();
  }, [dismissed]);

  // Kvittot tänds på flanken pending -> klar. Ref:en, inte state, är villkoret — annars
  // skulle effekten tända kvittot vid mount.
  const wasRefreshingRef = useRef(false);
  useEffect(() => {
    if (isRefreshing) {
      wasRefreshingRef.current = true;
      return;
    }
    if (!wasRefreshingRef.current) return;
    wasRefreshingRef.current = false;
    setJustRefreshed(true);
    const timer = setTimeout(() => setJustRefreshed(false), 2500);
    return () => clearTimeout(timer);
  }, [isRefreshing]);

  return (
    <div className="jp-oversikt-toolbar">
      <div className="jp-oversikt-toolbar__left">
        <span className="jp-oversikt-toolbar__stamp">
          {t.rich("notices.lastUpdated", {
            date: lastUpdated,
            mono: (chunks) => <span className="jp-mono">{chunks}</span>,
          })}
        </span>
        <button
          type="button"
          className="jp-btn jp-btn--ghost jp-btn--sm jp-oversikt-toolbar__refresh"
          aria-busy={isRefreshing || undefined}
          onClick={() => {
            if (isRefreshing) return;
            startRefresh(() => router.refresh());
          }}
        >
          <RotateCw size={14} aria-hidden="true" />{" "}
          {isRefreshing
            ? t("notices.refreshing")
            : justRefreshed
              ? t("notices.refreshed")
              : t("notices.refresh")}
        </button>
      </div>
      {dismissibleVisible.length > 0 && (
        <button
          type="button"
          className="jp-btn jp-btn--ghost jp-btn--sm"
          onClick={() => {
            moveFocusRef.current = true;
            dismissMany(dismissibleVisible.map((n) => n.id));
          }}
        >
          <Check size={14} aria-hidden="true" /> {t("notices.markAllRead")}
        </button>
      )}
    </div>
  );
}
