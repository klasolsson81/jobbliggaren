import Link from "next/link";
import { MatchChip } from "./match-chip";
import type {
  JobAdMatchDetail,
  MatchDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";

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

// Kanonisk länk + text — IDENTISK med Översikt setup-nudgen
// (oversikt-page.tsx:181) och /jobb-disclosuren. Ett koncept, en sträng
// (jobbpilot-design-copy — ingen drift mellan ytor).
const MATCH_SETTINGS_HREF = "/installningar#matchning";
const MATCH_SETTINGS_CTA = "Ställ in matchning";

// Verdict → svenskt civic ord (design-bind §2.B). NoMatch = "Saknas" (neutral,
// ej röd-alarm); NotAssessed = "Ej bedömt"; Vacuous = "Inga angivna" (annonsen
// anger inga krav/kompetenser av den sorten — neutral, ej fel). Vacuous får
// neutral ink + fylld prick (default), aldrig hålig (det är ett definitivt
// "inget krävs", inte ett "kunde ej bedömas").
const VERDICT_WORD: Record<MatchVerdict, string> = {
  Match: "Matchar",
  Partial: "Delvis",
  NoMatch: "Saknas",
  NotAssessed: "Ej bedömt",
  Vacuous: "Inga angivna",
};

// Dimensions-ordning + svenska civic-labels (design §2.B). Fast-dimensionerna
// först (yrke/region/anställning), sedan CV-härledda (kompetenser/krav/
// meriterande). Titel hålls "Ej bedömd" v1 (Klas-bind: title-dimensionen är
// OUT of scope i F4-16 — CTO D7=A).
const DIMENSIONS: ReadonlyArray<{
  key: keyof Omit<JobAdMatchDetail, "grade">;
  label: string;
}> = [
  { key: "ssykOverlap", label: "Yrke" },
  { key: "titleSimilarity", label: "Titel" },
  { key: "regionFit", label: "Region" },
  { key: "employmentFit", label: "Anställningsform" },
  { key: "skillOverlap", label: "Kompetenser" },
  { key: "mustHaveCoverage", label: "Ska-krav" },
  { key: "niceToHaveCoverage", label: "Meriterande" },
];

// Normal-case source; the heading element applies `text-transform: uppercase`
// (parity "Annonsbeskrivning" — the style versalises, not the string).
const HEADING = "Matchning mot din profil";

function MatchSectionHeading({ children }: { children?: React.ReactNode }) {
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
        {HEADING}
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
function notAssessedReason(key: keyof Omit<JobAdMatchDetail, "grade">): string {
  switch (key) {
    case "ssykOverlap":
      return "Du har inte angett vilka yrken du söker inom.";
    case "titleSimilarity":
      return "Titel jämförs inte mot din profil ännu.";
    case "regionFit":
      return "Du har inte angett någon region.";
    case "employmentFit":
      return "Du har inte angett någon anställningsform.";
    case "skillOverlap":
    case "mustHaveCoverage":
    case "niceToHaveCoverage":
      return "Inga kompetenser kunde läsas ur ditt CV. Ladda upp ett CV under Profil.";
    default:
      return "Den här delen kunde inte bedömas.";
  }
}

function MatchRow({
  label,
  dimensionKey,
  detail,
}: {
  label: string;
  dimensionKey: keyof Omit<JobAdMatchDetail, "grade">;
  detail: MatchDimensionDetail;
}) {
  const word = VERDICT_WORD[detail.verdict];
  const isNotAssessed = detail.verdict === "NotAssessed";

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
            {notAssessedReason(dimensionKey)}
          </span>
        ) : (
          <>
            {detail.matched.length > 0 && (
              <span>Du har: {detail.matched.join(", ")}</span>
            )}
            {detail.missing.length > 0 && (
              <span className="jp-modal__matchrow-missing">
                Annonsen efterfrågar även: {detail.missing.join(", ")}
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
}

export function JobAdMatchSection({ match }: JobAdMatchSectionProps) {
  // Inloggad användare UTAN angivet yrke (yrket kan inte bedömas): visa EN
  // ärlig signpost-rad i stället för nedbrytningen, med kanonisk Översikt-copy
  // (design §2.E #2 — ingen string-drift mellan ytor).
  const noStatedOccupation =
    match.grade === null && match.ssykOverlap.verdict === "NotAssessed";

  if (noStatedOccupation) {
    return (
      <section className="jp-modal__matchsection" aria-label="Matchning mot din profil">
        <MatchSectionHeading />
        <p style={{ margin: 0, fontSize: 14, color: "var(--jp-ink-1)" }}>
          Du har inte angett vilka yrken du söker inom. Ställ in det för att se
          hur väl annonser matchar din profil.{" "}
          <Link
            href={MATCH_SETTINGS_HREF}
            style={{
              color: "var(--jp-accent-700)",
              fontWeight: 600,
              textDecoration: "underline",
            }}
          >
            {MATCH_SETTINGS_CTA}
          </Link>
        </p>
      </section>
    );
  }

  return (
    <section className="jp-modal__matchsection" aria-label="Matchning mot din profil">
      <MatchSectionHeading>
        {match.grade !== null && <MatchChip grade={match.grade} />}
      </MatchSectionHeading>
      <div className="jp-modal__matchrows">
        {DIMENSIONS.map(({ key, label }) => (
          <MatchRow
            key={key}
            label={label}
            dimensionKey={key}
            detail={match[key]}
          />
        ))}
      </div>
    </section>
  );
}
