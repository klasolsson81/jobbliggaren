"use client";

// Client Component: interaktiv filterkontroll (switch + kryssrutor med
// event-handlers + härlett on/av-tillstånd). Ingen egen state — speglar
// URL-staten via `selected`-propen och rapporterar nästa lista uppåt
// (föräldern commit:ar till URL:en, paritet med chip/sort i toolbaren).

import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { LIST_MATCH_GRADES, type ListMatchGrade } from "@/lib/dto/job-ad-match";

/**
 * STEG 5 (grade-filter, 2026-06-23) — matchningsgrad-filtret på /jobb.
 *
 * Produktmodell (Klas-låst):
 * - En "Matchning"-switch (på/av). PÅ → användaren väljer vilka grader som
 *   visas (kryssrute-grupp Grund/Bra/Stark, MULTI-select).
 * - **"Av = noll grader"**: en tom `selected`-lista ÄR "Matchning av" — då
 *   filtreras ingenting och hela listan returneras.
 * - Slå PÅ → emittera ALLA TRE grader (så "på" visar alla graderade annonser).
 *   Avmarkera smalnar (t.ex. bara Bra+Stark döljer Grund).
 * - Avmarkera ALLA tre (eller slå av switchen) → emittera tom lista → av.
 *
 * Honesty (CLAUDE.md §5 / ADR 0076): kontrollen erbjuder EXAKT Grund/Bra/Stark
 * — ALDRIG Toppmatch (listfiltret är Fast-bandet, kan inte beräkna Topp).
 * Ingen magnitud-visualisering (ingen stapel/mätare/fyllnad) — graderna är
 * namngivna kategorier (Goodhart). En prick/kryssruta + namn, inget annat.
 *
 * Renderas BARA när `hasStatedDesiredOccupation` är true (föräldern gatar) —
 * graden kan inte beräknas utan angivet yrke (paritet med match-sort-
 * disclosuren). Detta är en ren presentationskomponent: ingen URL-kunskap,
 * inget commit — föräldern (`JobbResultsToolbar`) översätter `onChange` till
 * ett URL-commit.
 *
 * a11y (jobbliggaren-design-a11y §2/§5/§6): switchen är en `<button
 * role="switch">` (delar ToggleRow-mönstret men sitter i kontroll-radens
 * rytm); kryssrute-gruppen har `role="group"` + grupp-label; varje kryssruta
 * är `role="checkbox"` med tangentbords-aktivering (Space/Enter) och synligt
 * fokus (`.jp-checkitem:focus-visible`). Färg bär aldrig betydelse ensam —
 * den synliga labeln ÄR namnet.
 */

interface JobbMatchGradeFilterProps {
  /** Valda grader (enum-namn, delmängd av Basic/Good/Strong). Tom = av. */
  selected: ReadonlyArray<string>;
  /** Rapportera nästa grad-lista uppåt (föräldern commit:ar till URL:en). */
  onChange: (next: string[]) => void;
}

// Renderas i ordinal ordning (Grund → Bra → Stark). LIST_MATCH_GRADES är SPOT
// för de tre filtrerbara graderna (job-ad-match.ts) — `Top` finns inte här.
const GRADES: ReadonlyArray<ListMatchGrade> = LIST_MATCH_GRADES;

export function JobbMatchGradeFilter({
  selected,
  onChange,
}: JobbMatchGradeFilterProps) {
  const t = useTranslations("jobads.ui.gradeFilter");

  // "Av = noll grader": switchen är PÅ exakt när minst en grad är vald.
  const isOn = selected.length > 0;

  function toggleSwitch() {
    // Av → på: visa ALLA tre grader (så "på" = alla graderade annonser).
    // På → av: töm listan (hela listan returneras).
    onChange(isOn ? [] : [...GRADES]);
  }

  function toggleGrade(grade: ListMatchGrade) {
    // Avmarkera sista grad → tom lista → switchen slår av (härlett, ingen
    // separat state). Bevarar ordinal ordning vid tillägg.
    onChange(
      selected.includes(grade)
        ? selected.filter((g) => g !== grade)
        : GRADES.filter((g) => selected.includes(g) || g === grade),
    );
  }

  return (
    <div className="jp-gradefilter">
      <button
        type="button"
        role="switch"
        aria-checked={isOn}
        // Synlig label ("Matchning") + switch i samma rad. aria-label bär det
        // tillgängliga namnet eftersom labeln ligger som syskon-span (inte ett
        // <label htmlFor> — switchen är en knapp, inte en native checkbox).
        aria-label={t("toggleLabel")}
        onClick={toggleSwitch}
        className="jp-gradefilter__switch"
        data-checked={isOn}
      >
        <span className="jp-gradefilter__thumb" aria-hidden="true" />
      </button>
      <span className="jp-gradefilter__label" aria-hidden="true">
        {t("toggleLabel")}
      </span>

      {isOn && (
        <div
          role="group"
          aria-label={t("groupLabel")}
          className="jp-gradefilter__grades"
        >
          {GRADES.map((grade) => {
            const checked = selected.includes(grade);
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
