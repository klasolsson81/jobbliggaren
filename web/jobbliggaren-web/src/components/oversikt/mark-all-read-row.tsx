"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { useDismissedNotices } from "./use-dismissed-notices";
import { useNoticePrefs } from "./use-notice-prefs";
import type { SectionNoticeData } from "./notice-section";

interface MarkAllReadRowProps {
  /** ALLA sektioners notiser — kontrollen avfärdar tvärs över sektionerna. */
  readonly notices: ReadonlyArray<SectionNoticeData>;
  /**
   * Om ytans notis-id ROTERAR, dvs. om en avfärdad notis kommer tillbaka i morgon.
   * Default `true`: app-ytans id:n byggs med `swedishDateSlug(today)`.
   *
   * Gäst-ytan skickar `false` — dess id:n är statiska literaler (`guest-n-offer`, …)
   * utan datumdel, så dygnspåståendet hade varit falskt där (#1572). En sluten
   * boolean och inte en fri hint-sträng: det här är radens enda strängs vars
   * SANNING är bärande, och en boolean kan inte skrivas fel av en anropare.
   */
  readonly noticeIdsRotate?: boolean;
}

const HINT_ID = "jp-markall-hint";

/**
 * "Markera alla som lästa" som en egen rad sist på sidan (#1557). Låg ur
 * `notice-toolbar.tsx` fram till dess.
 *
 * Flytten har två skäl. Efter #1556 är toolbaren en ren STATUSRÄLS — stämpel,
 * uppdatera-ikon, kvitto, allt förankrat i nuläget — och det här var radens enda
 * mutation. Och den låg OVANFÖR det den verkar på, vilket läser som "rensa innan du
 * tittar"; Klas beskrev kontrollen som "farlig" utan att ha tryckt på den. Nu kommer
 * åtgärden efter sitt objekt, och tab-ordningen följer DOM-ordningen utan `tabIndex`.
 *
 * Raden renderas så länge någon notis är synlig — även när knappen inte är det.
 * Villkoret är avsiktligt bredare än knappens: kvitto-regionen får inte monteras
 * samtidigt som sitt innehåll, för då annonseras den opålitligt (#1549/#1556 betalade
 * för den lärdomen). Knappen och hinten är däremot villkorade på att det finns något
 * att avfärda, annars vore de en no-op.
 *
 * Hinten bär konsekvensen, inte etiketten — och den finns i TVÅ former, valda på
 * `noticeIdsRotate`. Etiketten (`notices.markAllRead`) är gemensam; det är bara
 * dygnspåståendet som skiljer ytorna åt, eftersom det är sant precis när id:na bär en
 * datum-slug. Där de inte gör det säger hinten samma sak minus den satsen (#1572).
 * Ingendera formen lovar återkomst: de beskriver handlingens räckvidd, så en passerad
 * deadline gör dem fortfarande sanna — det är döljandet som upphör, inte notisen som
 * utlovas.
 *
 * Ingen bekräftelsedialog. DESIGN.md §6 kräver en för destruktiva åtgärder; den här är
 * mätt icke-destruktiv (sektionens fotband och kugghjulets "Återställ lästa notiser"
 * tar tillbaka allt), så en dialog hade bekräftat feltolkningen i stället för att rätta
 * den.
 *
 * Fokus går till sista `.jp-notice-foot__toggle` i dokumentet, inte till kugghjulet som
 * förr: härifrån hade kugghjulet blivit ett hopp från sidans botten till dess topp.
 * Målet finns garanterat — knappen renderades bara för att något avfärdbart fanns, och
 * varje avfärdat id tillhör en pref-aktiverad typ och landar därför i sin sektions
 * `read`-lista, vilket är precis villkoret som renderar fotbandet.
 */
export function MarkAllReadRow({
  notices,
  noticeIdsRotate = true,
}: MarkAllReadRowProps) {
  const t = useTranslations("oversikt");
  const { dismissed, dismissMany } = useDismissedNotices();
  const { isEnabled } = useNoticePrefs();
  const [justRead, setJustRead] = useState(0);

  const visible = notices.filter((n) => isEnabled(n.source, n.type));
  const dismissibleVisible = visible.filter(
    (n) => n.dismissible !== false && !dismissed.has(n.id),
  );

  // WCAG 2.4.3: knappen avmonterar sig själv när inget avfärdbart återstår, och utan
  // förflyttning faller fokus till <body>. Ref-flagga i stället för state — klicket
  // muterar dismiss-store:n, så effekten (keyad på `dismissed`) körs efter re-rendern
  // och ref-nollningen där är lint-säker (react-hooks/set-state-in-effect).
  const moveFocusRef = useRef(false);
  useEffect(() => {
    if (!moveFocusRef.current) return;
    moveFocusRef.current = false;
    const toggles = document.querySelectorAll<HTMLButtonElement>(
      ".jp-notice-foot__toggle",
    );
    toggles[toggles.length - 1]?.focus();
  }, [dismissed]);

  // WCAG 4.1.3: utan kvitto säger ingenting att något hände — fokus landar på en
  // kontroll vars eget namn ("Visa") inte nämner avfärdandet.
  useEffect(() => {
    if (justRead === 0) return;
    const timer = setTimeout(() => setJustRead(0), 2500);
    return () => clearTimeout(timer);
  }, [justRead]);

  if (visible.length === 0) return null;

  return (
    <div className="jp-notice-bulk">
      {dismissibleVisible.length > 0 && (
        <>
          <button
            type="button"
            className="jp-btn jp-btn--ghost jp-btn--sm jp-btn--flush"
            aria-describedby={HINT_ID}
            onClick={() => {
              moveFocusRef.current = true;
              setJustRead(dismissibleVisible.length);
              dismissMany(dismissibleVisible.map((n) => n.id));
            }}
          >
            <Check size={14} aria-hidden="true" /> {t("notices.markAllRead")}
          </button>
          <span id={HINT_ID} className="jp-notice-bulk__hint">
            {t(
              noticeIdsRotate
                ? "notices.markAllReadHint"
                : "notices.markAllReadHintStatic",
            )}
          </span>
        </>
      )}
      <span
        className="jp-notice-bulk__receipt"
        role="status"
        aria-live="polite"
      >
        {justRead > 0
          ? t("notices.markAllReadReceipt", { count: justRead })
          : ""}
      </span>
    </div>
  );
}
