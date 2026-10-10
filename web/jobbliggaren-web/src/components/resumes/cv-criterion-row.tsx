import { useTranslations } from "next-intl";
import { CircleAlert, CircleCheck, CircleDashed, CircleX } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type {
  CitedEvidenceDto,
  CriterionVerdict,
  CvCriterionVerdictDto,
} from "@/lib/dto/parsed-resume";

/**
 * One criterion as a row in the review ledger (#2083). RSC: the CV text in the evidence is rendered
 * here, on the server, and reaches the ledger's client island only as a rendered node.
 *
 * Every Godkänt, Delvis and Underkänt shows the evidence the engine cited (ADR 0074). Ej bedömt
 * shows the honest reason and nothing that could read as a verdict.
 */

const STATUS_ICON: Record<CriterionVerdict, LucideIcon> = {
  Fail: CircleX,
  Warn: CircleAlert,
  Pass: CircleCheck,
  NotAssessed: CircleDashed,
};

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
  const Icon = STATUS_ICON[verdict.verdict];

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
          verdict.notAssessedReason !== null && (
            <p className="jp-cvledger__reason">{verdict.notAssessedReason}</p>
          )
        ) : (
          <ul className="jp-cvledger__evidence-list">
            {verdict.evidence.map((item, index) => (
              <EvidenceItem
                key={`${item.kind}-${index}`}
                evidence={item}
                excerptLabel={t("review.evidence.excerpt")}
              />
            ))}
          </ul>
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
