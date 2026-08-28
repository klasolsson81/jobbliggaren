"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Check, RotateCw } from "lucide-react";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";
import type { SectionNoticeData } from "./notice-section";

interface NoticeToolbarProps {
  /** Klockslag i läsarens tidszon (`formatNoticesStamp`). */
  readonly lastUpdated: string;
  /** Samma tidpunkt som ISO-8601, för `<time dateTime>`. */
  readonly lastUpdatedIso: string;
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
 * början. Väntan bärs av `aria-busy` och av kvitto-regionen.
 *
 * Stämpeln har minutupplösning medan en refresh går på under en sekund, så två klick i samma
 * minut lämnar den oförändrad. Utan kvitto ser kontrollen verkningslös ut — precis det
 * tillstånd Klas villkor pekar ut. #1556 gjorde kontrollen ikon-only, så kvittot kan inte
 * längre bo i knappens etikett; det ligger i en egen `role="status"`-region intill den.
 * Regionen renderas ALLTID, även tom: en live-region som monteras samtidigt som sitt
 * innehåll annonseras opålitligt. `aria-label` är statisk och namnger vad kontrollen gör
 * (DESIGN.md §6 kräver den på en ikon-only-kontroll); tillståndet bärs av `aria-busy` och
 * regionen, inte av namnet.
 *
 * Stämpeln visar bara klockslag (#1556) — datumet är nästan alltid i dag, eftersom sidan
 * räknar om det per request. Hela tidpunkten finns kvar i `<time dateTime>`, så en flik som
 * stått öppen över midnatt fortfarande går att adjudicera.
 */
export function NoticeToolbar({
  lastUpdated,
  lastUpdatedIso,
  notices,
}: NoticeToolbarProps) {
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
          {t.rich("notices.lastUpdatedTime", {
            time: lastUpdated,
            // Taggen heter inte `time`: värdet gör redan det, och next-intl slår
            // ihop värden och taggar i samma namnrymd.
            stamp: (chunks) => <time dateTime={lastUpdatedIso}>{chunks}</time>,
          })}
        </span>
        <button
          type="button"
          className="jp-oversikt-toolbar__refresh"
          aria-label={t("notices.refresh")}
          title={t("notices.refresh")}
          aria-busy={isRefreshing || undefined}
          onClick={() => {
            if (isRefreshing) return;
            startRefresh(() => router.refresh());
          }}
        >
          <RotateCw size={16} aria-hidden="true" />
        </button>
        <span
          className="jp-oversikt-toolbar__receipt"
          role="status"
          aria-live="polite"
        >
          {isRefreshing
            ? t("notices.refreshing")
            : justRefreshed
              ? t("notices.refreshed")
              : ""}
        </span>
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
