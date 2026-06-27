"use client";

// Client Component: interaktiv filterkontroll (switch + kryssrutor med
// event-handlers + härlett on/av-tillstånd). Ingen egen state — speglar
// URL-staten via `selected`-propen och rapporterar nästa lista uppåt
// (föräldern commit:ar till URL:en, paritet med chip/sort i toolbaren).

import { useId } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { LIST_MATCH_GRADES, type ListMatchGrade } from "@/lib/dto/job-ad-match";

/**
 * STEG 5 (grade-filter, 2026-06-23) — matchningsgrad-filtret på /jobb.
 *
 * Produktmodell (issue #292, Klas + senior-cto-advisor — ERSÄTTER STEG 5:s
 * "av = noll grader"):
 * - En "Matchning"-switch (på/av). Switchen speglar `active` (matchnings-axelns
 *   huvudbrytare i URL:en, `?matchning=off`) — INTE selected.length.
 * - AV → PÅ: föräldern tar bort off-flaggan + lämnar matchGrades tomt (= alla
 *   grader visas). PÅ → AV: föräldern skriver `?matchning=off` + tömmer
 *   matchGrades.
 * - PÅ + tom `selected` = "alla grader visas" → kryssrutorna renderas ALLA
 *   ikryssade (härlett). PÅ + delmängd = bara de graderna ikryssade.
 * - Avmarkera en grad smalnar (t.ex. bara Bra+Stark döljer Grund). Avmarkera
 *   SISTA graden håller switchen PÅ (tom = alla visas igen) — "av" styrs nu av
 *   switchen, inte av att man avmarkerar bort allt (beteendeändring, issue #292).
 *
 * Honesty (CLAUDE.md §5 / ADR 0076): kontrollen erbjuder EXAKT Grund/Bra/Stark
 * — ALDRIG Toppmatch (listfiltret är Fast-bandet, kan inte beräkna Topp).
 * Ingen magnitud-visualisering (ingen stapel/mätare/fyllnad) — graderna är
 * namngivna kategorier (Goodhart). En prick/kryssruta + namn, inget annat.
 *
 * Renderas BARA när `hasStatedDesiredOccupation` är true (föräldern gatar) —
 * graden kan inte beräknas utan angivet yrke (paritet med match-sort-
 * disclosuren). Detta är en ren presentationskomponent: ingen URL-kunskap,
 * inget commit — föräldern (`JobbResultsToolbar`) översätter `onChange`/
 * `onTurnOff`/`onTurnOn` till ett URL-commit.
 *
 * a11y (jobbliggaren-design-a11y §2/§5/§6): switchen är en `<button
 * role="switch">` (delar ToggleRow-mönstret men sitter i kontroll-radens
 * rytm); kryssrute-gruppen har `role="group"` + grupp-label; varje kryssruta
 * är `role="checkbox"` med tangentbords-aktivering (Space/Enter) och synligt
 * fokus (`.jp-checkitem:focus-visible`). Färg bär aldrig betydelse ensam —
 * den synliga labeln ÄR namnet.
 */

interface JobbMatchGradeFilterProps {
  /**
   * issue #292 — matchnings-axelns på/av (SSOT i föräldern: `matchActive`).
   * Switchen speglar detta, INTE selected.length. PÅ → grad-kryssrutorna
   * renderas; AV → bara switchen.
   */
  active: boolean;
  /**
   * Valda grader (enum-namn, delmängd av Basic/Good/Strong). Tom lista NÄR
   * `active` = "alla grader visas" (renderas ALLA ikryssade) — INTE "av"
   * (av styrs av `active`/huvudbrytaren).
   */
  selected: ReadonlyArray<string>;
  /** Rapportera nästa grad-lista uppåt (föräldern commit:ar till URL:en). */
  onChange: (next: string[]) => void;
  /** issue #292 — switch PÅ → AV (föräldern skriver `?matchning=off` + tömmer). */
  onTurnOff: () => void;
  /** issue #292 — switch AV → PÅ (föräldern tar bort off + lämnar grader tomma). */
  onTurnOn: () => void;
}

// Renderas i ordinal ordning (Grund → Bra → Stark). LIST_MATCH_GRADES är SPOT
// för de tre filtrerbara graderna (job-ad-match.ts) — `Top` finns inte här.
const GRADES: ReadonlyArray<ListMatchGrade> = LIST_MATCH_GRADES;

export function JobbMatchGradeFilter({
  active,
  selected,
  onChange,
  onTurnOff,
  onTurnOn,
}: JobbMatchGradeFilterProps) {
  const t = useTranslations("jobads.ui.gradeFilter");
  // Den synliga "Matchning"-labeln ÄR det programmatiska namnet (a11y §2/§6):
  // switch-knappen pekar på syskon-spanen via aria-labelledby i stället för att
  // duplicera namnet i ett aria-label + dölja labeln för SR.
  const labelId = useId();

  // issue #292 — switchen speglar matchnings-axeln (`active`), INTE
  // selected.length. PÅ + tom selected = "alla grader visas" (ej "av").
  const isOn = active;

  // Härledd effektiv mängd: PÅ + tom lista = alla grader visas (kryssrutorna
  // renderas ALLA ikryssade). PÅ + delmängd = bara de graderna.
  const allShown = selected.length === 0;

  function toggleSwitch() {
    // PÅ → AV: föräldern skriver `?matchning=off` + tömmer grader. AV → PÅ:
    // föräldern tar bort off-flaggan + lämnar grader tomma (= alla visas).
    if (isOn) onTurnOff();
    else onTurnOn();
  }

  function toggleGrade(grade: ListMatchGrade) {
    // Operera på den EFFEKTIVA mängden (tom selected = alla visas). Bevarar
    // ordinal ordning. issue #292: en mängd som blir tom ELLER full normaliseras
    // till [] ("alla grader visas", ren URL) — switchen förblir PÅ i bägge fall.
    const effective = allShown ? [...GRADES] : GRADES.filter((g) => selected.includes(g));
    const next = effective.includes(grade)
      ? effective.filter((g) => g !== grade)
      : GRADES.filter((g) => effective.includes(g) || g === grade);
    onChange(next.length === GRADES.length ? [] : next);
  }

  return (
    <div className="jp-gradefilter">
      <button
        type="button"
        role="switch"
        aria-checked={isOn}
        // Den synliga labeln (syskon-spanen nedan) bär det tillgängliga namnet
        // via aria-labelledby — switchen är en knapp, inte en native checkbox,
        // så ingen <label htmlFor>; namnet dupliceras aldrig i ett aria-label.
        aria-labelledby={labelId}
        onClick={toggleSwitch}
        className="jp-gradefilter__switch"
        data-checked={isOn}
      >
        <span className="jp-gradefilter__thumb" aria-hidden="true" />
      </button>
      <span id={labelId} className="jp-gradefilter__label">
        {t("toggleLabel")}
      </span>

      {isOn && (
        <div
          role="group"
          aria-label={t("groupLabel")}
          className="jp-gradefilter__grades"
        >
          {GRADES.map((grade) => {
            // issue #292 — PÅ + tom selected = "alla grader visas" → varje
            // kryssruta renderas ikryssad (härlett). Annars: vald delmängd.
            const checked = allShown || selected.includes(grade);
            return (
              <div
                key={grade}
                className="jp-checkitem jp-gradefilter__grade"
                role="checkbox"
                aria-checked={checked}
                tabIndex={0}
                onClick={() => toggleGrade(grade)}
                onKeyDown={(e) => {
                  if (e.key === " " || e.key === "Enter") {
                    e.preventDefault();
                    toggleGrade(grade);
                  }
                }}
              >
                <span className="jp-checkitem__box">
                  {checked && <Check size={14} aria-hidden="true" />}
                </span>
                {t(`grade.${grade}`)}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
