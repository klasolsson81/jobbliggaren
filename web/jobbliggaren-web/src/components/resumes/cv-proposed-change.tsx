import { StatusPill } from "@/components/ui/status-pill";
import {
  proposedChangeKindLabel,
  structuralTransformLabel,
  changeKindPillLabel,
} from "@/lib/resumes/review-labels";
import type {
  ProposedChangeDto,
  CitedEvidenceDto,
} from "@/lib/dto/parsed-resume";

/**
 * Ett enskilt förbättringsförslag (F4-10, propose-and-approve). RSC, display-only:
 * förslaget visas read-only som vägledning — ingen tillämpa/godkänn-interaktion,
 * ingen klient-ö som muterar (CLAUDE.md §5 — en regelmotor skriver aldrig om tyst).
 *
 * Förklarbarhets-invarianten (ADR 0074): varje förslag VISAR sin citerade evidens
 * + sin proveniens — aldrig ett naket förslag. Ett ersättnings-förslag visar
 * före→efter som ett märkt par (Nuvarande/Förslag, aldrig enbart färg — WCAG 1.4.1);
 * ett rent strukturellt förslag visar operationen som en observations-mening utan
 * före/efter. Evidensen renderas med samma logik som granska-vyns kriterie-verdikt
 * (TextSpan→citat; Structural→observation) — citaten är redan pnr-redigerade vid
 * motorns choke point och säkra att rendera verbatim.
 */

function Evidence({ evidence }: { evidence: CitedEvidenceDto }) {
  if (evidence.kind === "TextSpan") {
    return (
      <div className="jp-improve__evidence">
        {evidence.quote !== null && (
          <blockquote className="jp-criterion__quote">
            {evidence.quote}
          </blockquote>
        )}
        {evidence.note !== null && (
          <p className="jp-criterion__note">{evidence.note}</p>
        )}
      </div>
    );
  }

  // Structural: observation om frånvaro/struktur (ingen citerbar textrad).
  if (evidence.observation === null) return null;
  return (
    <div className="jp-improve__evidence">
      <p className="jp-criterion__note">{evidence.observation}</p>
    </div>
  );
}

/** Proveniens-fot: KnowledgeBank → "Källa: {source} {version}" (key utelämnas —
 * brus för slutanvändaren); StructuralTransform → "Källa: strukturell regel
 * ({transform})". Detta är förklarbarhets-kontraktet, alltid synligt. */
function provenanceText(change: ProposedChangeDto): string {
  const { provenance } = change;
  if (provenance.kind === "KnowledgeBank") {
    const parts = [provenance.source, provenance.version].filter(
      (part): part is string => part !== null && part.length > 0,
    );
    return parts.length > 0 ? `Källa: ${parts.join(" ")}` : "Källa: kunskapsbank";
  }
  // StructuralTransform — visa den faktiska transform-regelns namn (svensk
  // etikett ur `provenance.transform`, inte change.kind).
  const transform =
    provenance.transform !== null
      ? structuralTransformLabel(provenance.transform)
      : null;
  return transform !== null
    ? `Källa: strukturell regel (${transform})`
    : "Källa: strukturell regel";
}

export function CvProposedChange({ change }: { change: ProposedChangeDto }) {
  const hasReplacement = change.replacement !== null;
  const pillLabel = changeKindPillLabel(hasReplacement);

  return (
    <div className="jp-improve__change">
      {/* targetId surfacas medvetet INTE i v1: det är en opak intern apply-adress
          (för det framtida godkänn-steget), inte användarnyttig info i en display-
          only-vy — till skillnad från granskas uppslagbara criterionId ("A7"). Den
          lever kvar i DTO:n/zod för forward-compat. CTO-beslut 2026-06-17
          (docs/reviews/2026-06-17-f4-b2-forbattra-cto.md, Q1). */}
      <div className="jp-improve__change-head">
        <StatusPill tone="neutral">{pillLabel}</StatusPill>
      </div>

      {change.replacement !== null ? (
        <div className="jp-improve__diff">
          <div className="jp-improve__diff-side">
            <span className="jp-improve__diff-label">Nuvarande</span>
            <blockquote className="jp-criterion__quote jp-improve__diff-before">
              {change.replacement.before}
            </blockquote>
          </div>
          <div className="jp-improve__diff-side">
            <span className="jp-improve__diff-label">Förslag</span>
            <blockquote className="jp-criterion__quote jp-improve__diff-after">
              {change.replacement.after}
            </blockquote>
          </div>
        </div>
      ) : change.operation !== null ? (
        <p className="jp-improve__operation">
          Föreslagen ändring: {proposedChangeKindLabel(change.kind)} på{" "}
          <code className="jp-criterion__id">{change.operation.target}</code>
        </p>
      ) : null}

      <Evidence evidence={change.evidence} />

      <p className="jp-criterion__note">{change.rationale}</p>

      <p className="jp-improve__provenance">{provenanceText(change)}</p>
    </div>
  );
}
