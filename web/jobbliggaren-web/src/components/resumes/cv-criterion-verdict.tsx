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
}: {
  verdict: CvCriterionVerdictDto;
}) {
  const { label, tone } = verdictLabel(verdict.verdict);
  const hasEvidence = verdict.evidence.length > 0;

  return (
    <div className="jp-criterion">
      <div className="jp-criterion__head">
        <code className="jp-criterion__id">{verdict.criterionId}</code>
        <StatusPill tone={tone}>{label}</StatusPill>
      </div>

      {verdict.verdict === "NotAssessed" && verdict.notAssessedReason !== null && (
        <p className="jp-criterion__note jp-criterion__note--reason">
          {verdict.notAssessedReason}
        </p>
      )}

      {hasEvidence && (
        <ul className="jp-criterion__evidence">
          {verdict.evidence.map((item, index) => (
            <EvidenceItem key={index} evidence={item} />
          ))}
        </ul>
      )}
    </div>
  );
}
