import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/status-pill";
import { verdictLabel } from "@/lib/resumes/review-labels";
import type {
  CvCriterionVerdictDto,
  CitedEvidenceDto,
} from "@/lib/dto/parsed-resume";

/**
 * Ett enskilt kriterie-verdikt (F4-9). RSC. Förklarbarhets-invarianten (ADR
 * 0074): varje PASS/WARN/FAIL VISAR sin citerade evidens — aldrig ett naket
 * verdikt. `TextSpan` renderas som citat (redan pnr-redigerat vid motorns
 * choke point); `Structural` som en strukturell observation. `NotAssessed`
 * visar den ärliga orsaken (aldrig ett påhittat utfall).
 *
 * Raden leds av den läsbara rubriken (`verdict.name`, t.ex. "Mätbara resultat")
 * — inte koden. `criterionId` ("A1") behålls som en dämpad sekundär mono-
 * referens för support-spårbarhet (B.3), aldrig som primär etikett.
 * `categoryLabel` visas som en rad-kontext-tagg när verdiktet lyfts ut ur sitt
 * kategori-kort (t.ex. i "Att åtgärda"-aggregatet).
 */

function EvidenceItem({ evidence }: { evidence: CitedEvidenceDto }) {
  if (evidence.kind === "TextSpan") {
    return (
      <li className="jp-criterion__evidence-item">
        {evidence.quote !== null && (
          <blockquote className="jp-criterion__quote">
            {evidence.quote}
          </blockquote>
        )}
        {evidence.note !== null && (
          <p className="jp-criterion__note">{evidence.note}</p>
        )}
      </li>
    );
  }

  // Structural: observation om frånvaro/struktur (ingen citerbar textrad).
  return (
    <li className="jp-criterion__evidence-item">
      {evidence.observation !== null && (
        <p className="jp-criterion__note">{evidence.observation}</p>
      )}
    </li>
  );
}

export function CvCriterionVerdict({
  verdict,
  categoryLabel,
}: {
  verdict: CvCriterionVerdictDto;
  /** Kategori-etikett som rad-kontext (visas när verdiktet är utlyft ur sitt
   * kategori-kort, t.ex. i "Att åtgärda"). Utelämnas inne i kategori-korten. */
  categoryLabel?: string;
}) {
  const tEnum = useTranslations("resumes.enums");
  const { label, tone } = verdictLabel(tEnum, verdict.verdict);
  const hasEvidence = verdict.evidence.length > 0;

  return (
    <div className="jp-criterion">
      <div className="jp-criterion__head">
        <StatusPill tone={tone}>{label}</StatusPill>
        <span className="jp-criterion__name">{verdict.name}</span>
        {categoryLabel !== undefined && (
          <span className="jp-criterion__category">{categoryLabel}</span>
        )}
        <code className="jp-criterion__id">{verdict.criterionId}</code>
      </div>

      {verdict.verdict === "NotAssessed" && verdict.notAssessedReason !== null && (
        <p className="jp-criterion__note">{verdict.notAssessedReason}</p>
      )}

      {hasEvidence && (
        <ul className="jp-criterion__evidence">
          {verdict.evidence.map((item, index) => (
            <EvidenceItem key={`${item.kind}-${index}`} evidence={item} />
          ))}
        </ul>
      )}
    </div>
  );
}
