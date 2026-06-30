"use client";

// Client Component: interaktiv status-filterkontroll (tre kryssrutor med
// event-handlers). Ingen egen state — speglar URL-staten via props:arna och
// rapporterar nästa läge uppåt (föräldern commit:ar till URL:en, paritet med
// grad-filtret/chip/sort i toolbaren).

import { useId } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";

/**
 * #383 (CTO-bind cto-7f3a9c2e1b4d8a6f, Approach B) — status-filtret på /jobb:
 * "Sparade" / "Ansökta" / "Dölj ansökta".
 *
 * Produktmodell:
 * - Tre fristående kryssrutor. "Sparade" (savedOnly) + "Ansökta" (appliedOnly)
 *   kan båda vara på (backend OR-unionerar dem). "Dölj ansökta" (hideApplied)
 *   döljer annonser man redan sökt.
 * - MUTEX: "Ansökta" (visa ENDAST ansökta) och "Dölj ansökta" är självmotsägande
 *   och kan aldrig vara på samtidigt. Att slå på den ena slår av den andra (UI
 *   förhindrar kombinationen; backend-validatorn 400:ar den som extra skydd).
 *   "Sparade" + "Dölj ansökta" är giltigt ("sparade jag inte sökt ännu").
 *
 * ORTOGONAL mot matchningen: renderas så snart användaren har en seeker
 * (föräldern gatar på `hasSeeker`), oavsett om matchningen är på eller om ett
 * yrke är angett — till skillnad från grad-filtret som kräver angivet yrke.
 *
 * Ren presentationskomponent (ingen URL-kunskap, inget commit): föräldern
 * (`JobbResultsToolbar`) översätter `onChange` till ett URL-commit utan
 * commit-flaggan (runtime-view-state, paritet matchGrades).
 *
 * a11y (jobbliggaren-design-a11y §2/§5/§6): `role="group"` + grupp-label; varje
 * kryssruta är `role="checkbox"` med tangentbords-aktivering (Space/Enter) och
 * synligt fokus (`.jp-panel .jp-checkitem:focus-visible`). Färg bär aldrig
 * betydelse ensam — den synliga labeln ÄR namnet.
 *
 * #408 — kontrollen bor nu i en enkelkolumns toolbar-popover (`[Status ▾]`), så
 * den slutar låna grad-filtrets horisontella `.jp-gradefilter`-rytm och använder
 * popover-/panel-idiomet i stället: `.jp-panel__group` + vertikala
 * `.jp-checkitem`-rader (samma kontroll-rad som Klass-2-panelen). INGEN ny
 * globals.css-CSS — bara befintliga panel-/checkitem-tokens.
 */

export interface StatusFilterState {
  savedOnly: boolean;
  appliedOnly: boolean;
  hideApplied: boolean;
}

interface JobbStatusFilterProps {
  savedOnly: boolean;
  appliedOnly: boolean;
  hideApplied: boolean;
  /** Rapportera nästa status-läge uppåt (föräldern commit:ar till URL:en). */
  onChange: (next: StatusFilterState) => void;
}

export function JobbStatusFilter({
  savedOnly,
  appliedOnly,
  hideApplied,
  onChange,
}: JobbStatusFilterProps) {
  const t = useTranslations("jobads.ui.statusFilter");
  const labelId = useId();

  function toggleSaved() {
    onChange({ savedOnly: !savedOnly, appliedOnly, hideApplied });
  }

  // MUTEX: att slå på "Ansökta" stänger "Dölj ansökta" (självmotsägande —
  // "visa endast ansökta" och "dölj ansökta" kan aldrig samexistera).
  function toggleApplied() {
    const next = !appliedOnly;
    onChange({
      savedOnly,
      appliedOnly: next,
      hideApplied: next ? false : hideApplied,
    });
  }

  function toggleHideApplied() {
    const next = !hideApplied;
    onChange({
      savedOnly,
      appliedOnly: next ? false : appliedOnly,
      hideApplied: next,
    });
  }

  // Nyckeln är den literala message-key-unionen (next-intl typar `t()` mot
  // jobads.ui.statusFilter — en plain `string` skulle inte typecheck:a).
  const items: ReadonlyArray<{
    key: "saved" | "applied" | "hideApplied";
    checked: boolean;
    toggle: () => void;
  }> = [
    { key: "saved", checked: savedOnly, toggle: toggleSaved },
    { key: "applied", checked: appliedOnly, toggle: toggleApplied },
    { key: "hideApplied", checked: hideApplied, toggle: toggleHideApplied },
  ];

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      className="jp-panel__group"
    >
      {/* Visuellt dolt grupp-namn: popover-headern visar redan "Status", men
          gruppen behöver ett programmatiskt namn (a11y §2). sr-only håller det
          tillgängligt utan att dubbla rubriken visuellt. */}
      <span id={labelId} className="sr-only">
        {t("label")}
      </span>
      {items.map((item) => (
        <div
          key={item.key}
          className="jp-checkitem"
          role="checkbox"
          aria-checked={item.checked}
          tabIndex={0}
          onClick={item.toggle}
          onKeyDown={(e) => {
            if (e.key === " " || e.key === "Enter") {
              e.preventDefault();
              item.toggle();
            }
          }}
        >
          <span className="jp-checkitem__box">
            {item.checked && <Check size={14} aria-hidden="true" />}
          </span>
          {t(item.key)}
        </div>
      ))}
    </div>
  );
}
