import "server-only";
import { useTranslations } from "next-intl";
import { VERDICT_ICON } from "@/components/resumes/verdict-icon";
import type {
  CitedEvidenceDto,
  CvCriterionVerdictDto,
  NotAssessedKind,
} from "@/lib/dto/parsed-resume";

/**
 * One criterion as a row in the review ledger (#2083). RSC: the CV text in the evidence is rendered
 * here, on the server, and reaches the ledger's client island only as a rendered node.
 *
 * Every Godkänt, Delvis and Underkänt shows the evidence the engine cited (ADR 0074), and Underkänt
 * and Delvis what the rule passes on. Ej bedömt shows nothing that could read as a verdict.
 */

/** The kinds whose reason is one shared sentence that only repeats the tag, so the row shows the
 * tag alone (#2083). The server pins that each of these kinds has one reason sentence. */
const TAG_ONLY_KINDS: ReadonlySet<NotAssessedKind> = new Set(["TextOnly", "RequiresJobAd"]);

/** The id of a row's criterion name, which the row's status control is labelled by. */
export function criterionNameId(criterionId: string): string {
  return `cvledger-${criterionId}-name`;
}

function EvidenceItem({
  evidence,
  excerptLabel,
}: {
  evidence: CitedEvidenceDto;
  excerptLabel: string;
}) {
  if (evidence.kind === "Structural") {
    return evidence.observation === null ? null : (
      <li className="jp-cvledger__evidence-item">
        <p className="jp-cvledger__note">{evidence.observation}</p>
      </li>
    );
  }

  return (
    <li className="jp-cvledger__evidence-item">
      {evidence.note !== null && <p className="jp-cvledger__note">{evidence.note}</p>}
      {/* The engine never writes "…" into a quote: the quote stays a verbatim substring of the CV.
          The ellipsis is drawn here and hidden from screen readers, and the sentence that says it
          is an excerpt sits outside the blockquote, so it is not heard as part of the user's text. */}
      {evidence.quote !== null && (
        <blockquote className="jp-criterion__quote">
          {evidence.quote}
          {evidence.isExcerpt && (
            <span className="jp-criterion__quote-excerpt" aria-hidden="true">
              …
            </span>
          )}
        </blockquote>
      )}
      {evidence.quote !== null && evidence.isExcerpt && (
        <p className="sr-only">{excerptLabel}</p>
      )}
    </li>
  );
}

export function CvCriterionRow({
  verdict,
  hasActionColumn,
  action,
}: {
  verdict: CvCriterionVerdictDto;
  hasActionColumn: boolean;
  /** The status control, on Underkänt and Delvis rows of the canonical review only. */
  action?: React.ReactNode;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const Icon = VERDICT_ICON[verdict.verdict];

  return (
    <tr role="row" className="jp-cvledger__row">
      <td role="cell" className="jp-cvledger__status" data-verdict={verdict.verdict}>
        <Icon size={18} aria-hidden="true" />
        <span>{tEnum(`verdict.${verdict.verdict}`)}</span>
      </td>
      <th role="rowheader" scope="row" className="jp-cvledger__criterion">
        <span id={criterionNameId(verdict.criterionId)} className="jp-cvledger__name">
          {verdict.name}
        </span>
        <span className="jp-cvledger__id">{verdict.criterionId}</span>
      </th>
      <td role="cell" className="jp-cvledger__evidence">
        {verdict.verdict === "NotAssessed" ? (
          <>
            {verdict.notAssessedKind !== null && (
              <span className="jp-cvledger__kind">
                {t(`review.notAssessedKind.${verdict.notAssessedKind}`)}
              </span>
            )}
            {/* Without a known kind the reason is the only explanation, so it always shows. */}
            {verdict.notAssessedReason !== null &&
              (verdict.notAssessedKind === null || !TAG_ONLY_KINDS.has(verdict.notAssessedKind)) && (
                <p className="jp-cvledger__reason">{verdict.notAssessedReason}</p>
              )}
          </>
        ) : (
          <>
            <ul className="jp-cvledger__evidence-list">
              {verdict.evidence.map((item, index) => (
                <EvidenceItem
                  key={`${item.kind}-${index}`}
                  evidence={item}
                  excerptLabel={t("review.evidence.excerpt")}
                />
              ))}
            </ul>
            {verdict.passCriterion !== null && (
              <p className="jp-cvledger__pass">
                {/* A real space after the label, so the words never run together when read aloud
                    or copied; the label's margin is only the visual rest of the gap. */}
                <span className="jp-cvledger__pass-label">{t("review.passCriterionLabel")}</span>{" "}
                {verdict.passCriterion}
              </p>
            )}
          </>
        )}
      </td>
      {hasActionColumn && (
        <td role="cell" className="jp-cvledger__action">
          {action}
        </td>
      )}
    </tr>
  );
}
