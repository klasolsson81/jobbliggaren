import Link from "next/link";
import { useTranslations } from "next-intl";
import { Check, Minus } from "lucide-react";
import { MatchChip } from "./match-chip";
import type {
  JobAdMatchDetail,
  MatchDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";
import {
  classifyOrtLabel,
  type OrtGranularity,
} from "@/lib/job-ads/ort-granularity";

// Scoped to the `match` subtree (next-intl typed-messages instantiate too
// deeply when ICU-arg calls resolve against the whole `jobads.ui` namespace).
type MatchTranslator = ReturnType<typeof useTranslations<"jobads.ui.match">>;

/**
 * JobAdMatchSection — F4-16 (ADR 0076 Amendment (b) §3/§5, design-reviewer
 * 2026-06-20). Ren presentational Server Component (ingen "use client", noll
 * interaktivitet). Renderar matchnings-nedbrytningen mot användarens profil i
 * JobAdDetail (modal + fullsida), ovanför Annonsbeskrivning.
 *
 * Goodhart-vakt (hård, ADR 0053 Beslut 5 + ADR 0076 §5): INGEN
 * siffra/procent/mätare/ring. Varje verdict är en diskret prick + namngivet ord.
 *
 * NotAssessed/Saknas använder NEUTRAL ink — ALDRIG röd (ett saknat meritvärde
 * är inget fel). NotAssessed = hålig prick + "Ej bedömt" + skäl; aldrig
 * förväxlad med NoMatch (CLAUDE.md §5).
 *
 * Saknas hela `match`-propen (anonym / ingen träffdata) renderar anroparen
 * INGEN sektion alls (frånvaro, ej teater — ADR 0053).
 */

// Kanonisk länk — IDENTISK med Översikt setup-nudgen (oversikt-page.tsx:181)
// och /jobb-disclosuren. Texten resolveras via next-intl (`ui.match.settingsCta`,
// SPOT med Översikt-/toolbar-disclosuren — ingen drift mellan ytor).
const MATCH_SETTINGS_HREF = "/installningar#matchning";

// CV-import-länk för signposten "ladda upp CV" (PR-B2). Samma route som
// matchnings-kortets CV-förslag (importCvHref).
const CV_IMPORT_HREF = "/cv/importera";

// Dimensions-ordning (design §2.B). Fast-dimensionerna först (yrke/region/
// anställning), sedan CV-härledda (kompetenser/krav/meriterande). Etiketterna
// resolveras via next-intl (`ui.match.dimension.*`). Titel hålls "Ej bedömd" v1
// (Klas-bind: title-dimensionen är OUT of scope i F4-16 — CTO D7=A).
const DIMENSION_KEYS: ReadonlyArray<keyof Omit<JobAdMatchDetail, "grade">> = [
  "ssykOverlap",
  "titleSimilarity",
  "regionFit",
  "employmentFit",
  "skillOverlap",
  "mustHaveCoverage",
  "niceToHaveCoverage",
];

function MatchSectionHeading({
  t,
  children,
}: {
  t: MatchTranslator;
  children?: React.ReactNode;
}) {
  return (
    <div className="jp-modal__matchsection-head">
      <div
        style={{
          fontSize: 13,
          fontWeight: 700,
          textTransform: "uppercase",
          letterSpacing: "0.06em",
          color: "var(--jp-ink-2)",
        }}
      >
        {/* Normal-case source; the heading element applies
            `text-transform: uppercase` (parity "Annonsbeskrivning"). */}
        {t("heading")}
      </div>
      {children}
    </div>
  );
}

/**
 * "Ej bedömt"-skäl per dimension. Honest, namnger VARFÖR (design §2.C) —
 * aldrig en tyst blank. matched[]/missing[] är tomma för NotAssessed-rader, så
 * skälet härleds ur dimensionen.
 */
function notAssessedReason(
  key: keyof Omit<JobAdMatchDetail, "grade">,
  t: MatchTranslator,
): string {
  switch (key) {
    case "ssykOverlap":
      return t("notAssessedReason.ssykOverlap");
    case "titleSimilarity":
      return t("notAssessedReason.titleSimilarity");
    case "regionFit":
      return t("notAssessedReason.regionFit");
    case "employmentFit":
      return t("notAssessedReason.employmentFit");
    case "skillOverlap":
    case "mustHaveCoverage":
    case "niceToHaveCoverage":
      return t("notAssessedReason.skills");
    default:
      return t("notAssessedReason.default");
  }
}

/**
 * Must-have-sammanfattning (PR-B2, CTO G3.6) — en kort rad som kopplar graden
 * till om annonsens ska-krav uppfylls (graden är nu requirement-aware: Stark/
 * Topp kräver att ska-kraven är mötta). Returnerar `null` för `NotAssessed`
 * (inget CV) → då visas "ladda upp CV"-signposten i stället. `Vacuous` =
 * annonsen anger inga ska-krav (gate-öppen — därför kan den ändå nå Stark/Topp).
 * Ingen siffra (Goodhart-vakt) — ren konstaterande civic-copy.
 */
function mustHaveSummary(
  verdict: MatchVerdict,
  t: MatchTranslator,
): string | null {
  switch (verdict) {
    case "Match":
      return t("mustHaveSummary.Match");
    case "Partial":
      return t("mustHaveSummary.Partial");
    case "NoMatch":
      return t("mustHaveSummary.NoMatch");
    case "Vacuous":
      return t("mustHaveSummary.Vacuous");
    case "NotAssessed":
      return null;
  }
}

/**
 * Titel-sammanfattning (#5a / STEG 2-grannfeature, ADR 0079) — en kort civic rad
 * som konstaterar hur CV:ts roll förhåller sig till annonsens titel. Titel-
 * dimensionen scoras på stammade lexem (Snowball), vars råa stammar ("systemutveckl")
 * vore obegripliga i UI — därför en per-verdict-fras i stället för rå bevis-lista.
 * Titel är EVIDENCE-ONLY (styr aldrig graden), och yrket (SSYK) är den primära
 * signalen — copy:n överklagar därför aldrig en titel-skillnad. `NotAssessed`
 * (ingen roll i CV:t) faller till `notAssessedReason`; `Vacuous` förekommer ej för
 * titel. Ingen siffra (Goodhart).
 */
function titleSummary(verdict: MatchVerdict, t: MatchTranslator): string | null {
  switch (verdict) {
    case "Match":
      return t("titleSummary.Match");
    case "Partial":
      return t("titleSummary.Partial");
    case "NoMatch":
      return t("titleSummary.NoMatch");
    case "Vacuous":
    case "NotAssessed":
      return null;
  }
}

/**
 * Spår 3 PR-D — grupperar en ort-label-lista per granularitet (kommun/län) och
 * formaterar två civic-fraser så bevisraden ärligt visar VILKEN granularitet
 * som matchade (architect NOTE-2; klassningen sker FE-side mot taxonomin).
 * Okänd label (saknas i kartan, t.ex. stale snapshot) faller till `region`-
 * hinken som plain text (visas rakt av, ingen felaktig kategori).
 */
function splitOrtByGranularity(
  labels: ReadonlyArray<string>,
  granularityByLabel: Record<string, OrtGranularity>,
): { municipalities: string[]; regions: string[] } {
  const municipalities: string[] = [];
  const regions: string[] = [];
  for (const label of labels) {
    if (classifyOrtLabel(label, granularityByLabel) === "municipality") {
      municipalities.push(label);
    } else {
      // "region" eller okänd → coarser/plain (Gotland-fallet, stale snapshot).
      regions.push(label);
    }
  }
  return { municipalities, regions };
}

/**
 * RegionFit-bevis med granularitet (kommun-träff vs län-träff). Utan en
 * granularitets-karta (`granularityByLabel` saknas) faller den tillbaka på den
 * generiska "Du har:"/"Annonsen efterfrågar även:"-formen (bakåtkompat).
 */
function RegionFitEvidence({
  detail,
  granularityByLabel,
  t,
}: {
  detail: MatchDimensionDetail;
  granularityByLabel: Record<string, OrtGranularity>;
  t: MatchTranslator;
}) {
  const matched = splitOrtByGranularity(detail.matched, granularityByLabel);
  const missing = splitOrtByGranularity(detail.missing, granularityByLabel);

  return (
    <>
      {matched.municipalities.length > 0 && (
        <span>
          {t("ort.matchedMunicipalities", {
            items: matched.municipalities.join(", "),
          })}
        </span>
      )}
      {matched.regions.length > 0 && (
        <span>
          {t("ort.matchedRegions", { items: matched.regions.join(", ") })}
        </span>
      )}
      {missing.municipalities.length > 0 && (
        <span className="jp-modal__matchrow-missing">
          {t("ort.missingMunicipalities", {
            items: missing.municipalities.join(", "),
          })}
        </span>
      )}
      {missing.regions.length > 0 && (
        <span className="jp-modal__matchrow-missing">
          {t("ort.missingRegions", { items: missing.regions.join(", ") })}
        </span>
      )}
    </>
  );
}

/**
 * Per-ska-krav-checklista (#5b, ADR 0079 / STEG 2) — visar VARJE krav annonsen
 * ställer med en uppfyllt/saknas-indikator per rad, i stället för den generiska
 * "Du har / Annonsen efterfrågar även"-formen. Komponerad FE-side ur de
 * redan-på-tråden `matched`/`missing`-arrayerna (CTO-dom FE-only — ingen
 * backend-ändring; varje matchad Display = uppfyllt krav, varje saknad = ej
 * uppfyllt). `matched` (✓, success-ink) först, sedan `missing` (NEUTRAL ink,
 * ALDRIG röd — ett saknat krav är inget fel, CLAUDE.md §5 / ADR 0053). Ikonen är
 * `aria-hidden`; status bärs av en sr-only-text per rad (skärmläsare hör
 * "uppfyllt/saknas", seende ser ikon + ink).
 */
function RequirementChecklist({
  detail,
  label,
  t,
}: {
  detail: MatchDimensionDetail;
  /** Dimensionens etikett (Ska-krav / Meriterande) — listans aria-label. */
  label: string;
  t: MatchTranslator;
}) {
  return (
    <ul className="flex flex-col gap-1" aria-label={label}>
      {detail.matched.map((item, i) => (
        // Nyckel inkluderar index: Display-labels är dedupade per concept-id
        // uppströms, men två skilda koncept kan dela identisk label → index
        // garanterar unik nyckel (ingen omordning sker, listan byggs en gång).
        <li key={`met-${i}-${item}`} className="flex items-center gap-2">
          <Check
            size={16}
            aria-hidden="true"
            style={{ color: "var(--jp-success)", flexShrink: 0 }}
          />
          <span>{item}</span>
          <span className="sr-only">{t("requirements.met")}</span>
        </li>
      ))}
      {detail.missing.map((item, i) => (
        <li key={`unmet-${i}-${item}`} className="flex items-center gap-2">
          <Minus
            size={16}
            aria-hidden="true"
            style={{ color: "var(--jp-ink-2)", flexShrink: 0 }}
          />
          <span className="jp-modal__matchrow-missing">{item}</span>
          <span className="sr-only">{t("requirements.unmet")}</span>
        </li>
      ))}
    </ul>
  );
}

function MatchRow({
  label,
  dimensionKey,
  detail,
  t,
  granularityByLabel,
  isRelatedYrke,
}: {
  label: string;
  dimensionKey: keyof Omit<JobAdMatchDetail, "grade">;
  detail: MatchDimensionDetail;
  t: MatchTranslator;
  /** Spår 3 PR-D — endast satt för RegionFit-raden (label → kommun/län). */
  granularityByLabel?: Record<string, OrtGranularity>;
  /**
   * #300 PR-5 (ADR 0084, design-reviewer bind) — true PÅ Yrke-raden (ssykOverlap)
   * NÄR hela matchen är `Related`. Då ersätts den generiska bevisformen med en
   * neutral förklaring av VARFÖR annonsen rankas lägre (liknande, inte exakt valt,
   * yrke). Neutral ink, ALDRIG röd (ett relaterat yrke är inget fel).
   */
  isRelatedYrke?: boolean;
}) {
  const word = t(`verdict.${detail.verdict}`);
  const isNotAssessed = detail.verdict === "NotAssessed";
  // Granularitets-uppdelad bevisrad bara för Region OCH bara när kartan finns.
  const useOrtGranularity =
    dimensionKey === "regionFit" && granularityByLabel !== undefined;
  // Per-krav-checklista bara för krav-dimensionerna (Ska-krav / Meriterande) OCH
  // bara när det finns krav att lista. Vacuous (annonsen anger inga) + NotAssessed
  // (inget CV) har tomma matched/missing → faller till den generiska/NotAssessed-
  // grenen (ärligt: ingen tom eller vilseledande checklista; footern bär summan).
  const isRequirementDim =
    dimensionKey === "mustHaveCoverage" ||
    dimensionKey === "niceToHaveCoverage";
  const hasRequirementItems =
    detail.matched.length > 0 || detail.missing.length > 0;
  // Titel-raden (#5a): visa en per-verdict-fras i stället för råa Snowball-stammar
  // (titel scoras på lexem; stammarna vore obegripliga i civic-UI).
  const isTitleDim = dimensionKey === "titleSimilarity";

  return (
    <div className="jp-modal__matchrow">
      <span className="jp-modal__matchrow-label">{label}</span>
      <span
        className="jp-modal__matchrow-verdict"
        data-verdict={detail.verdict}
      >
        <span
          className={
            isNotAssessed
              ? "jp-modal__matchrow-dot jp-modal__matchrow-dot--hollow"
              : "jp-modal__matchrow-dot"
          }
          aria-hidden="true"
        />
        {word}
      </span>
      <span className="jp-modal__matchrow-evidence">
        {isNotAssessed ? (
          <span className="jp-modal__matchrow-missing">
            {notAssessedReason(dimensionKey, t)}
          </span>
        ) : isRelatedYrke ? (
          // #300 PR-5 — neutral "därför lägre"-förklaring på Yrke-raden för en
          // Related-match. Ingen siffra (Goodhart), neutral ink (jp-modal__
          // matchrow-missing = ink-2, ej röd — ett relaterat yrke är inget fel).
          <span className="jp-modal__matchrow-missing">
            {t("relatedYrkeReason")}
          </span>
        ) : useOrtGranularity ? (
          <RegionFitEvidence
            detail={detail}
            granularityByLabel={granularityByLabel}
            t={t}
          />
        ) : isRequirementDim && hasRequirementItems ? (
          <RequirementChecklist detail={detail} label={label} t={t} />
        ) : isTitleDim ? (
          <span>{titleSummary(detail.verdict, t)}</span>
        ) : (
          <>
            {detail.matched.length > 0 && (
              <span>{t("youHave", { items: detail.matched.join(", ") })}</span>
            )}
            {detail.missing.length > 0 && (
              <span className="jp-modal__matchrow-missing">
                {t("alsoRequested", { items: detail.missing.join(", ") })}
              </span>
            )}
          </>
        )}
      </span>
    </div>
  );
}

export interface JobAdMatchSectionProps {
  match: JobAdMatchDetail;
  /**
   * Spår 3 PR-D — label → ort-granularitet (kommun/län), härledd FE-side ur
   * taxonomin (architect NOTE-2). Utelämnad → RegionFit-raden faller till den
   * generiska bevisformen (bakåtkompat / degraderad taxonomi).
   */
  ortGranularityByLabel?: Record<string, OrtGranularity>;
}

export function JobAdMatchSection({
  match,
  ortGranularityByLabel,
}: JobAdMatchSectionProps) {
  // Synchronous next-intl translator — keeps JobAdMatchSection a non-async RSC
  // (shared by the modal + full page as a serialized slot, with sync tests).
  const t = useTranslations("jobads.ui.match");

  // Inloggad användare UTAN angivet yrke (yrket kan inte bedömas): visa EN
  // ärlig signpost-rad i stället för nedbrytningen, med kanonisk Översikt-copy
  // (design §2.E #2 — ingen string-drift mellan ytor).
  const noStatedOccupation =
    match.grade === null && match.ssykOverlap.verdict === "NotAssessed";

  if (noStatedOccupation) {
    return (
      <section className="jp-modal__matchsection" aria-label={t("heading")}>
        <MatchSectionHeading t={t} />
        <p style={{ margin: 0, fontSize: 14, color: "var(--jp-ink-1)" }}>
          {t("noStatedOccupation")}{" "}
          <Link
            href={MATCH_SETTINGS_HREF}
            style={{
              color: "var(--jp-accent-700)",
              fontWeight: 600,
              textDecoration: "underline",
            }}
          >
            {t("settingsCta")}
          </Link>
        </p>
      </section>
    );
  }

  return (
    <section className="jp-modal__matchsection" aria-label={t("heading")}>
      <MatchSectionHeading t={t}>
        {match.grade !== null && <MatchChip grade={match.grade} />}
      </MatchSectionHeading>
      <div className="jp-modal__matchrows">
        {DIMENSION_KEYS.map((key) => (
          <MatchRow
            key={key}
            label={t(`dimension.${key}`)}
            dimensionKey={key}
            detail={match[key]}
            t={t}
            // Granularitets-uppdelning bara för Region-raden (kommun vs län).
            granularityByLabel={
              key === "regionFit" ? ortGranularityByLabel : undefined
            }
            // #300 PR-5 — "därför lägre"-förklaring bara på Yrke-raden NÄR hela
            // matchen är Related (liknande, inte exakt valt, yrke).
            isRelatedYrke={key === "ssykOverlap" && match.grade === "Related"}
          />
        ))}
      </div>

      {/* Foot (PR-B2): kopplar graden till ska-kraven. Utan CV kan man inte nå
          Stark/Topp (kräver kompetens-/krav-bedömning) → en lugn signpost driver
          CV-uppladdning; annars en kort must-have-sammanfattning. Text-only,
          ingen siffra (Goodhart-vakt). */}
      {match.mustHaveCoverage.verdict === "NotAssessed" ? (
        <p
          className="jp-modal__matchfoot"
          style={{
            margin: "12px 0 0",
            paddingTop: 12,
            borderTop: "1px solid var(--jp-border-soft)",
            fontSize: 14,
            color: "var(--jp-ink-2)",
          }}
        >
          {t("uploadCvFoot")}{" "}
          <Link
            href={CV_IMPORT_HREF}
            style={{
              color: "var(--jp-accent-700)",
              fontWeight: 600,
              textDecoration: "underline",
            }}
          >
            {t("uploadCvCta")}
          </Link>
        </p>
      ) : (
        <p
          className="jp-modal__matchfoot"
          style={{
            margin: "12px 0 0",
            paddingTop: 12,
            borderTop: "1px solid var(--jp-border-soft)",
            fontSize: 14,
            color: "var(--jp-ink-2)",
          }}
        >
          {mustHaveSummary(match.mustHaveCoverage.verdict, t)}
        </p>
      )}
    </section>
  );
}
