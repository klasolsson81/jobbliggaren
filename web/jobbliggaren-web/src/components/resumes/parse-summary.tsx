import { useTranslations } from "next-intl";
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

/** DTO:ns `overall`-värde → nyckel-suffix för förklaringsmeningen i `parse.*`. */
const OVERALL_EXPLANATION_KEY: Record<
  ParseConfidenceDto["overall"],
  "parse.overallConfident" | "parse.overallDegraded" | "parse.overallFailed"
> = {
  Confident: "parse.overallConfident",
  Degraded: "parse.overallDegraded",
  Failed: "parse.overallFailed",
};

export function ParseSummary({
  confidence,
}: {
  confidence: ParseConfidenceDto;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const overall = overallConfidenceLabel(tEnum, confidence.overall);

  return (
    <section className="jp-parse-summary" aria-labelledby="parse-summary-title">
      <div className="jp-parse-summary__head">
        <h2 id="parse-summary-title" className="jp-parse-summary__title">
          {t("parse.title")}
        </h2>
        <StatusPill tone={overall.tone}>{overall.label}</StatusPill>
      </div>

      <p className="jp-parse-summary__lede">
        {t(OVERALL_EXPLANATION_KEY[confidence.overall])}
      </p>

      {confidence.requiresManualReview && (
        <p className="jp-parse-summary__note">
          <span className="jp-parse-summary__note-icon" aria-hidden="true">
            <Info size={16} />
          </span>
          <span>{t("parse.manualReviewNote")}</span>
        </p>
      )}

      {confidence.sections.length > 0 && (
        <ul className="jp-parse-summary__sections">
          {confidence.sections.map((section) => {
            const level = sectionLevelLabel(tEnum, section.level);
            return (
              <li key={section.section} className="jp-parse-summary__section">
                <div className="jp-parse-summary__section-head">
                  <span className="jp-parse-summary__section-name">
                    {sectionKindLabel(tEnum, section.section)}
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
