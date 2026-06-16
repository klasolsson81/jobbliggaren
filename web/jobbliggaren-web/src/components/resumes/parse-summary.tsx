import { Info } from "lucide-react";
import { StatusPill } from "@/components/ui/status-pill";
import {
  overallConfidenceLabel,
  sectionKindLabel,
  sectionLevelLabel,
} from "@/lib/resumes/review-labels";
import type { ParseConfidenceDto } from "@/lib/dto/parsed-resume";

/**
 * Parse-sammanfattning (F4-8/OQ5). RSC. Övergripande konfidens + per-sektions-
 * lista. Förklarbar, inte opak: varje sektion visar nivå (StatusPill) och sin
 * citerade, PII-fria evidens. Ärlighet (§5): `Hittades inte` skiljs från
 * `Delvis`; `requiresManualReview` ger en civic kompletteringsnotis utan att
 * skuldbelägga.
 */

const OVERALL_EXPLANATION: Record<ParseConfidenceDto["overall"], string> = {
  Confident:
    "Vi kunde läsa ditt CV och dela upp det i tydliga avsnitt. Granska gärna att allt stämmer.",
  Degraded:
    "Vi kunde läsa ditt CV men en del avsnitt blev ofullständiga. Du kan komplettera dem i nästa steg.",
  Failed:
    "Vi kunde inte läsa någon användbar text ur filen. Du kan fylla i uppgifterna för hand i stället.",
};

export function ParseSummary({
  confidence,
}: {
  confidence: ParseConfidenceDto;
}) {
  const overall = overallConfidenceLabel(confidence.overall);

  return (
    <section className="jp-parse-summary" aria-labelledby="parse-summary-title">
      <div className="jp-parse-summary__head">
        <h2 id="parse-summary-title" className="jp-parse-summary__title">
          Så tolkades ditt CV
        </h2>
        <StatusPill tone={overall.tone}>{overall.label}</StatusPill>
      </div>

      <p className="jp-parse-summary__lede">
        {OVERALL_EXPLANATION[confidence.overall]}
      </p>

      {confidence.requiresManualReview && (
        <p className="jp-parse-summary__note">
          <span className="jp-parse-summary__note-icon" aria-hidden="true">
            <Info size={16} />
          </span>
          <span>
            Några avsnitt behöver kompletteras innan ditt CV är klart. Det gör
            du i nästa steg.
          </span>
        </p>
      )}

      {confidence.sections.length > 0 && (
        <ul className="jp-parse-summary__sections">
          {confidence.sections.map((section) => {
            const level = sectionLevelLabel(section.level);
            return (
              <li key={section.section} className="jp-parse-summary__section">
                <div className="jp-parse-summary__section-head">
                  <span className="jp-parse-summary__section-name">
                    {sectionKindLabel(section.section)}
                  </span>
                  <StatusPill tone={level.tone}>{level.label}</StatusPill>
                </div>
                {section.evidence.length > 0 && (
                  <ul className="jp-parse-summary__evidence">
                    {section.evidence.map((line, index) => (
                      <li
                        key={`${section.section}-${index}`}
                        className="jp-parse-summary__evidence-item"
                      >
                        {line}
                      </li>
                    ))}
                  </ul>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
