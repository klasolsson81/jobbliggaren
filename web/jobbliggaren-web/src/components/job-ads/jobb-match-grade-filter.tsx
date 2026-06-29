"use client";

// Client Component: interaktiv filterkontroll (switch + kryssrutor med
// event-handlers + härlett on/av-tillstånd). Ingen egen state — speglar
// URL-staten via `selected`-propen och rapporterar nästa lista uppåt
// (föräldern commit:ar till URL:en, paritet med chip/sort i toolbaren).

import { useId } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { LIST_MATCH_GRADES, type ListMatchGrade } from "@/lib/dto/job-ad-match";
import { OVERSIKT_MATCH_GRADES } from "@/lib/dto/match-count";

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

// #381 — "Visa bara matchade" = Bra + Stark match (Good/Strong). Återbruka
// `OVERSIKT_MATCH_GRADES` (SSOT, drift-guardad mot backend
// `GetMyMatchCountQueryHandler.HeadlineGrades`) i stället för en egen kopia, så
// snabbvalet landar BEVISLIGT på exakt samma grad-set som Översiktens "visa
// matchade jobb"-länk + notis-count (?matchGrades=Good&matchGrades=Strong).
// Exkluderar Grundmatch (Basic) OCH Relaterat yrke (Related) — "matchad" mäter
// grad-STYRKA, inte yrkes-bredd; relaterade yrken styrs av sin egen toggle.
const ONLY_MATCHED_GRADES = OVERSIKT_MATCH_GRADES;

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
  const relatedHelpId = useId();

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

  // #381 — "Visa bara matchade" är aktiv (knappen pressad) EXAKT när det valda
  // grad-setet är {Good, Strong} (oavsett relaterade-toggle:n — Related räknas
  // aldrig som matchad). En custom-delmängd (t.ex. bara Stark, eller med Basic)
  // → ej aktiv (opressad). Ett klick när opressad sätter Good+Strong; ett klick
  // när pressad återställer till alla ([] = alla synliga grader visas).
  const onlyMatched =
    selected.length === ONLY_MATCHED_GRADES.length &&
    ONLY_MATCHED_GRADES.every((g) => selected.includes(g));

  function toggleOnlyMatched() {
    onChange(onlyMatched ? [] : [...ONLY_MATCHED_GRADES]);
  }

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
        // #300 PR-5 — egen full-bredds rad-wrapper: "Visa relaterade också"-
        // toggle:n OVANFÖR grad-kryssrutorna, sedan kryssrute-gruppen. `basis-full`
        // bryter raden så toggle:n + grupperna staplas (den horisontella
        // .jp-gradefilter-raden wrappar annars allt på samma rytm). Ren
        // layout-utility (flex/kolumn/gap) — INGEN ny gradefilter-CSS i
        // globals.css-hotspoten; switch/label/kryssrutor bär de befintliga
        // .jp-gradefilter__*-tokenen.
        <div className="flex basis-full flex-col gap-2">
          {/* "Visa relaterade också" — subordinerad toggle, EGEN rad ovanför
              kryssrutorna, med en hjälprad under. Återanvänder
              .jp-gradefilter__switch-tokenet + aria-labelledby-mönstret (a11y
              §2/§6 — synlig label = namnet); hjälptexten kopplas via
              aria-describedby. */}
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <button
                type="button"
                role="switch"
                aria-checked={includeRelated}
                aria-labelledby={relatedLabelId}
                aria-describedby={relatedHelpId}
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
            {/* Civic hjälprad (ink-2, informationsbärande — aldrig ink-3, ADR
                0038). Förklarar vad toggle:n gör utan att lova per-kort-exakthet
                (samma honesty-linje som gradeFilter.help). */}
            <span
              id={relatedHelpId}
              className="text-body-sm text-text-secondary"
            >
              {t("relatedToggleHelp")}
            </span>
          </div>

          {/* #381 — "Visa bara matchade"-snabbval: ETT klick smalnar till Bra +
              Stark match (Good/Strong), paritet med Översiktens "visa matchade
              jobb"-länk; ett klick till återställer alla nivåer. Toggle-knapp
              (aria-pressed) — pressad = aktiv filtrering. Återanvänder .jp-btn-
              tokenen: sekundär (kontur) opressad, primär (grön accent-fyll
              `--jp-accent-800`, ADR 0068) pressad — accent-fyllen = "på"/aktiv
              selektion i hela design-systemet (switch-`data-checked`/segment-
              `data-active`). Grad-kryssrutorna nedan speglar resultatet (Bra +
              Stark ikryssade), så förhållandet är självförklarande. EGEN rad
              ovanför grupperna; inga nya globals.css-klasser. */}
          <div>
            <button
              type="button"
              aria-pressed={onlyMatched}
              onClick={toggleOnlyMatched}
              className={`jp-btn jp-btn--sm ${
                onlyMatched ? "jp-btn--primary" : "jp-btn--secondary"
              }`}
            >
              {t("onlyMatchedLabel")}
            </button>
          </div>

          <div
            role="group"
            aria-label={t("groupLabel")}
            className="jp-gradefilter__grades"
          >
            {visibleGrades.map((grade) => {
              // issue #292 — PÅ + tom selected = "alla grader visas" → varje
              // synlig kryssruta renderas ikryssad (härlett). Annars: vald
              // delmängd.
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
        </div>
      )}
    </div>
  );
}
