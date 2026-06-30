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
 * #408 — kontrollen är nu PRESENTATIONS-kroppen i `[Matchning ▾]`-popovern
 * (`JobbToolbarPopover` → `.jp-popover.jp-panel`). Den läses som en ren kolumn:
 * switch-raden överst, sedan (när PÅ) "Visa relaterade också"-raden och
 * grad-kryssrutorna staplade i panel-rytm. Hjälptexten lever inte längre inline
 * — den bor i popoverns "?"-InfoDialog (verbatim `gradeFilter.help` +
 * `relatedToggleHelp`). "Visa bara matchade"-snabbvalet är borttaget (#408
 * DECISION 1): {Good,Strong} nås via Bra+Stark-kryssrutorna och Översikt-
 * djuplänken. #292-master-gaten och #300 PR-5-related-divergensen är OFÖRÄNDRADE.
 *
 * #300 PR-5 (ADR 0084, design-reviewer bind) — "Visa relaterade också"-toggle:n:
 * - En SEPARAT kontroll UNDERORDNAD "Matchning"-switchen (aldrig hopslagen med
 *   den). Renderas INNE i `{isOn && ...}`-blocket, som EGEN rad OVANFÖR
 *   grad-kryssrutorna. Den existerar bara när Matchning är PÅ.
 * - PÅ → tar med `Related`-graderade annonser (`?relaterade=on`, off by default).
 *   De märks "Relaterat yrke" (neutral chip) och rankas under exakta träffar.
 * - `Related`-kryssrutan renderas ENBART när toggle:n är på, placerad MELLAN
 *   Grund och Bra (driven av `LIST_MATCH_GRADES`-ordningen).
 *
 * STATE-MODEL FLOW-TRAP (design-reviewer-flaggad): "alla ikryssade = normalisera
 * till []" får INTE räknas mot en fast längd — med `Related` villkorligt i det
 * synliga setet (bara när toggle:n är på) måste "alla visade" räknas mot det
 * AKTUELLT SYNLIGA grad-setet. `LIST_MATCH_GRADES` är SPOT för ordinaliteten;
 * `visibleGrades` härleds = inkluderar `Related` iff toggle:n är på. När toggle:n
 * slås AV droppar föräldern `Related` ur den valda listan (ett filter på en grad
 * vars kontroll är dold = state/URL-divergens).
 *
 * Honesty (CLAUDE.md §5 / ADR 0076): kontrollen erbjuder Grund/Bra/Stark (+
 * Related bakom toggle:n) med labels IDENTISKA med kort-badgen (issue #291).
 * ALDRIG Toppmatch: listfiltret är Fast-bandet och kan inte beräkna Topp (kräver
 * CV mot annonsens krav på den Fulla per-kort-vägen); Topp visas bara som badge
 * på kort/modal. Toolbar-hjälptexten förklarar det och lovar aldrig exakt göm
 * (#298-beslut (iii)). Ingen magnitud-visualisering (ingen stapel/mätare/fyllnad)
 * — graderna är namngivna kategorier (Goodhart). En prick/kryssruta + namn,
 * inget annat. Grad-taxonomins SSOT-doc: `lib/dto/job-ad-match.ts`
 * (LIST_MATCH_GRADES).
 *
 * Renderas BARA när `hasStatedDesiredOccupation` är true (föräldern gatar) —
 * graden kan inte beräknas utan angivet yrke (paritet med match-sort-
 * disclosuren). Detta är en ren presentationskomponent: ingen URL-kunskap,
 * inget commit — föräldern (`JobbResultsToolbar`) översätter `onChange`/
 * `onTurnOff`/`onTurnOn`/`onRelatedToggle` till ett URL-commit.
 *
 * a11y (jobbliggaren-design-a11y §2/§5/§6): switcharna är `<button
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
   * Valda grader (enum-namn, delmängd av Basic/Related/Good/Strong). Tom lista
   * NÄR `active` = "alla grader visas" (renderas ALLA ikryssade) — INTE "av"
   * (av styrs av `active`/huvudbrytaren).
   */
  selected: ReadonlyArray<string>;
  /**
   * #300 PR-5 — "Visa relaterade också"-toggle:ns på/av (SSOT i föräldern:
   * `?relaterade=on`). PÅ → `Related`-kryssrutan renderas + related-graderade
   * annonser tas med i listan. Bara meningsfull när `active` (toggle:n renderas
   * inne i PÅ-blocket).
   */
  includeRelated: boolean;
  /** Rapportera nästa grad-lista uppåt (föräldern commit:ar till URL:en). */
  onChange: (next: string[]) => void;
  /** issue #292 — switch PÅ → AV (föräldern skriver `?matchning=off` + tömmer). */
  onTurnOff: () => void;
  /** issue #292 — switch AV → PÅ (föräldern tar bort off + lämnar grader tomma). */
  onTurnOn: () => void;
  /**
   * #300 PR-5 — "Visa relaterade också"-toggle:n växlas. `next` = nästa läge.
   * Föräldern skriver/tar bort `?relaterade=on` och — vid AV — droppar `Related`
   * ur den valda grad-listan (kontroll dold ⇒ inget kvarvarande filter).
   */
  onRelatedToggle: (next: boolean) => void;
}

// LIST_MATCH_GRADES är SPOT för de filtrerbara graderna OCH deras ordinala
// ordning (job-ad-match.ts) — [Basic, Related, Good, Strong]. `Top` finns inte
// här. Labels resolveras via `gradeFilter.grade.*`, identiska med `match.grade.*`
// (badge) per issue #291 / #300 (drift-guard-pinnat i parity-testet).
const GRADES: ReadonlyArray<ListMatchGrade> = LIST_MATCH_GRADES;

export function JobbMatchGradeFilter({
  active,
  selected,
  includeRelated,
  onChange,
  onTurnOff,
  onTurnOn,
  onRelatedToggle,
}: JobbMatchGradeFilterProps) {
  const t = useTranslations("jobads.ui.gradeFilter");
  // Den synliga "Matchning"-labeln ÄR det programmatiska namnet (a11y §2/§6):
  // switch-knappen pekar på syskon-spanen via aria-labelledby i stället för att
  // duplicera namnet i ett aria-label + dölja labeln för SR. Samma mönster för
  // "Visa relaterade också"-toggle:n (#300 PR-5).
  const labelId = useId();
  const relatedLabelId = useId();

  // issue #292 — switchen speglar matchnings-axeln (`active`), INTE
  // selected.length. PÅ + tom selected = "alla grader visas" (ej "av").
  const isOn = active;

  // #300 PR-5 STATE-MODEL FLOW-TRAP — det AKTUELLT SYNLIGA grad-setet: `Related`
  // ingår iff "Visa relaterade också"-toggle:n är på. ALL härledning nedan
  // (allShown-normalisering, "alla ikryssade") räknas mot DETTA set, aldrig mot
  // en fast `GRADES.length` (annars blir "alla checkade" inkonsekvent när
  // toggle:n flippas). LIST_MATCH_GRADES förblir SPOT för ordinaliteten.
  const visibleGrades = includeRelated
    ? GRADES
    : GRADES.filter((g) => g !== "Related");

  // Härledd effektiv mängd: PÅ + tom lista = alla SYNLIGA grader visas
  // (kryssrutorna renderas ALLA ikryssade). PÅ + delmängd = bara de graderna.
  const allShown = selected.length === 0;

  function toggleSwitch() {
    // PÅ → AV: föräldern skriver `?matchning=off` + tömmer grader. AV → PÅ:
    // föräldern tar bort off-flaggan + lämnar grader tomma (= alla visas).
    if (isOn) onTurnOff();
    else onTurnOn();
  }

  function toggleGrade(grade: ListMatchGrade) {
    // Operera på den EFFEKTIVA mängden (tom selected = alla SYNLIGA visas).
    // Bevarar ordinal ordning. issue #292: en mängd som blir tom ELLER full (mot
    // det SYNLIGA setet) normaliseras till [] ("alla grader visas", ren URL) —
    // switchen förblir PÅ i bägge fall. #300 PR-5: full = lika med
    // visibleGrades (ej GRADES.length) så "alla synliga checkade" normaliserar
    // korrekt med/utan Related.
    const effective = allShown
      ? [...visibleGrades]
      : visibleGrades.filter((g) => selected.includes(g));
    const next = effective.includes(grade)
      ? effective.filter((g) => g !== grade)
      : visibleGrades.filter((g) => effective.includes(g) || g === grade);
    onChange(next.length === visibleGrades.length ? [] : next);
  }

  return (
    // #408 — ren kolumn inne i popovern: switch-raden överst, sedan (när PÅ)
    // related-raden + grad-kryssrutorna staplade i panel-rytm. Ren layout-utility
    // (flex/kolumn/gap) — INGEN ny globals.css; switch/label/kryssrutor bär de
    // befintliga .jp-gradefilter__*-/.jp-checkitem-tokenen.
    <div className="flex flex-col gap-3 px-2 py-1">
      <div className="flex items-center gap-3">
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
      </div>

      {isOn && (
        // PÅ-blocket: "Visa relaterade också"-toggle:n (#300 PR-5) OVANFÖR
        // grad-kryssrutorna, sedan kryssrute-gruppen i panel-rytm.
        <div className="flex flex-col gap-3">
          {/* "Visa relaterade också" — subordinerad toggle, EGEN rad ovanför
              kryssrutorna. Återanvänder .jp-gradefilter__switch-tokenet +
              aria-labelledby-mönstret (a11y §2/§6 — synlig label = namnet).
              #408 — hjälptexten bor nu i popoverns "?"-InfoDialog (verbatim
              relatedToggleHelp), inte längre som inline-rad här. */}
          <div className="flex items-center gap-3">
            <button
              type="button"
              role="switch"
              aria-checked={includeRelated}
              aria-labelledby={relatedLabelId}
              onClick={() => onRelatedToggle(!includeRelated)}
              className="jp-gradefilter__switch"
              data-checked={includeRelated}
            >
              <span className="jp-gradefilter__thumb" aria-hidden="true" />
            </button>
            <span id={relatedLabelId} className="jp-gradefilter__label">
              {t("relatedToggleLabel")}
            </span>
          </div>

          {/* Grad-kryssrutorna i panel-rytm (vertikal .jp-panel__group +
              .jp-checkitem-rader), samma kontroll-rad som Status-popovern.
              Fokus-ring kommer från .jp-panel .jp-checkitem:focus-visible. */}
          <div
            role="group"
            aria-label={t("groupLabel")}
            className="jp-panel__group"
          >
            {visibleGrades.map((grade) => {
              // issue #292 — PÅ + tom selected = "alla grader visas" → varje
              // synlig kryssruta renderas ikryssad (härlett). Annars: vald
              // delmängd.
              const checked = allShown || selected.includes(grade);
              return (
                <div
                  key={grade}
                  className="jp-checkitem"
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
        </div>
      )}
    </div>
  );
}
